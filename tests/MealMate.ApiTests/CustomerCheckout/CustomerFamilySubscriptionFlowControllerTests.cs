using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Serialization;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using FamilyPricingSettings = MealMate.Application.Features.ProgramsSubscriptions.FamilyPricingSettings;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerFamilySubscriptionFlowControllerTests
{
    [Fact]
    public async Task PutGroup_CreatesDraftWithManagerMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Name.Should().Be("عائلة أحمد");
        dto.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager);
        dto.Members[0].DisplayName.Should().Be("أحمد");
        dto.Members[0].RelationshipCode.Should().Be("self");
        dto.Members[0].IsCurrentUser.Should().BeTrue();
        dto.Status.Should().Be(FamilyGroupStatus.Draft);
    }

    [Fact]
    public async Task ConvertFromIndividual_ThenAbandon_UnlinksSubscription()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var sub = new Subscription
        {
            CustomerUserId = userId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(120m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId, users);
        var converted = await controller.ConvertFromIndividual(new ConvertFromIndividualRequest("عائلتي"), CancellationToken.None);
        var screen = converted.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.Status.Should().Be(FamilyGroupStatus.Draft);
        (await db.Subscriptions.SingleAsync(x => x.Id == sub.Id)).FamilyGroupId.Should().Be(screen.FamilyGroupId);

        var abandoned = await controller.AbandonDraft(CancellationToken.None);
        abandoned.Should().BeOfType<NoContentResult>();
        (await db.FamilyGroups.CountAsync()).Should().Be(0);
        var restored = await db.Subscriptions.SingleAsync(x => x.Id == sub.Id);
        restored.FamilyGroupId.Should().BeNull();
        restored.FamilyMemberId.Should().BeNull();
        restored.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Current_WithConvertibleIndividual_DoesNotSeedDraft_ReturnsConvertRequired()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = userId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(120m, "KWD"),
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        var result = await controller.Current(CancellationToken.None);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("conversion.convert_required");
        (await db.FamilyGroups.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetMemberPlan_ConvertedManager_IsLocked()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var sub = new Subscription
        {
            CustomerUserId = userId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(120m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId, users);
        var converted = await controller.ConvertFromIndividual(null, CancellationToken.None);
        var screen = converted.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = screen.Members.Single(m => m.Role == FamilyMemberRole.Manager).MemberId;

        var result = await controller.SetMemberPlan(
            managerId,
            new SetFamilyMemberPlanRequest(program.Id, bundle.Id, RestaurantTier.Elite, 12),
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task GetRelationships_ReturnsKinshipCatalog()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId);

        var result = await controller.Relationships(CancellationToken.None);
        var items = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<FamilyLookupDto>>().Subject;

        items.Select(x => x.Code).Should().Contain(
            ["self", "spouse", "father", "mother", "child", "sibling", "friend", "parent", "other"]);
        items.Should().ContainSingle(x => x.Code == "self").Which.ForDependents.Should().BeFalse();
        items.Should().Contain(x => x.Code == "father" && x.NameAr == "أب" && x.ForDependents);
        items.Should().Contain(x => x.Code == "child" && x.NameAr == "ابن / ابنة");
        items.Should().Contain(x => x.Code == "friend" && x.NameAr == "صديق / صديقة");
    }

    [Fact]
    public async Task Lookup_WhenPhoneHasCustomer_ReturnsDisplayName()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync("+96550000002", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                otherId, null, "+96550000002", "سارة", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, managerId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);

        var result = await controller.LookupMember(new FamilyMemberLookupRequest("+96550000002"), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberLookupDto>().Subject;
        dto.Found.Should().BeTrue();
        dto.DisplayName.Should().Be("سارة");
    }

    [Fact]
    public async Task Lookup_WhenUnknown_ReturnsFoundFalse()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserAccountDto?)null);

        var controller = CreateController(db, managerId, users);
        var result = await controller.LookupMember(new FamilyMemberLookupRequest("+96559999999"), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberLookupDto>().Subject;
        dto.Found.Should().BeFalse();
        dto.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Lookup_WhenPhoneBelongsToAnotherFamily_ReturnsAlreadyInFamily()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherGroup = new FamilyGroup
        {
            ManagerCustomerUserId = otherManagerId,
            Name = "عائلة أخرى",
            Status = FamilyGroupStatus.Active,
        };
        var otherMember = new FamilyMember
        {
            FamilyGroup = otherGroup,
            Role = FamilyMemberRole.Member,
            DisplayName = "سارة",
            RelationshipCode = "spouse",
            NutritionalGoalCode = string.Empty,
            AgeYears = 28,
        };
        otherGroup.Members.Add(otherMember);
        db.FamilyGroups.Add(otherGroup);
        await db.SaveChangesAsync();
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = otherMember.Id,
            HasSeparateAccount = true,
            LinkedCustomerUserId = otherUserId,
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync("+96550000002", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                otherUserId, null, "+96550000002", "سارة", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, managerId, users);
        var result = await controller.LookupMember(new FamilyMemberLookupRequest("+96550000002"), CancellationToken.None);
        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflict.Value.Should().BeOfType<ProblemDetails>().Subject.Extensions["code"].Should().Be("family.already_in_family");
    }

    [Fact]
    public async Task AddMember_UnknownPhone_CreatesUserWithoutReturningPassword()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var controller = CreateController(db, managerId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        var add = await controller.AddMember(
            NewMemberWrite("سارة", Guid.Empty) with { DeliveryAddressId = null, Phone = "+96551111111" },
            CancellationToken.None);

        var dto = add.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Members.Should().Contain(m => m.DisplayName == "سارة" && m.PhoneNumber == "+96551111111");
        await users.Received().CreateAsync(
            Arg.Is<CreateUserRequest>(r =>
                r.PhoneNumber == "+96551111111"
                && r.FullName == "سارة"
                && !string.IsNullOrWhiteSpace(r.Password)),
            Arg.Any<CancellationToken>());
        (await db.FamilyMembers.CountAsync(m => m.MemberSubscriptionId == null && m.DisplayName == "سارة")).Should().Be(1);
    }

    [Fact]
    public async Task AddMember_WhenGroupActive_AddsUnpaidMember()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة نشطة",
            Status = FamilyGroupStatus.Active,
        };
        group.Members.Add(new FamilyMember
        {
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            NutritionalGoalCode = string.Empty,
            AgeYears = 36,
            MemberSubscriptionId = Guid.NewGuid(),
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<CreateUserRequest>();
                return Result<UserAccountDto>.Success(new UserAccountDto(
                    Guid.NewGuid(), null, req.PhoneNumber, req.FullName, UserType.Customer, AccountStatus.Active,
                    null, false, false, DateTime.UtcNow, null));
            });

        var controller = CreateController(db, managerId, users);
        var add = await controller.AddMember(
            NewMemberWrite("سارة", Guid.Empty) with { DeliveryAddressId = null, Phone = "+96552222222" },
            CancellationToken.None);

        var dto = add.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Status.Should().Be(FamilyGroupStatus.Active);
        dto.Members.Should().Contain(m => m.DisplayName == "سارة" && m.PhoneNumber == "+96552222222");
    }

    [Fact]
    public async Task PutGroup_HydratesManagerFromCustomerProfile()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = userId,
            DateOfBirth = new DateOnly(1990, 1, 15),
            DietaryPattern = "weight_loss",
        });
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var manager = dto.Members.Should().ContainSingle(m => m.IsCurrentUser).Subject;
        manager.DisplayName.Should().Be("أحمد");
        manager.AgeYears.Should().Be(36);
        manager.DeliveryAddressId.Should().Be(address.Id);
        manager.RelationshipCode.Should().Be("self");
    }

    [Fact]
    public async Task GetCurrent_WhenNoDraft_SeedsManagerFromCustomerProfile()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = userId,
            DateOfBirth = new DateOnly(1990, 1, 15),
            DietaryPattern = "balanced",
        });
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.Current(CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Name.Should().Be("عائلة أحمد");
        dto.Members.Should().ContainSingle();
        var manager = dto.Members[0];
        manager.IsCurrentUser.Should().BeTrue();
        manager.Role.Should().Be(FamilyMemberRole.Manager);
        manager.DisplayName.Should().Be("أحمد");
        manager.AgeYears.Should().Be(36);
        manager.DeliveryAddressId.Should().Be(address.Id);
    }

    [Fact]
    public async Task AddMember_RejectsFifthMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        created.Should().BeOfType<OkObjectResult>();

        for (var i = 0; i < 3; i++)
        {
            var add = await controller.AddMember(NewMemberWrite($"Member {i + 2}", address.Id), CancellationToken.None);
            add.Should().BeOfType<OkObjectResult>();
        }

        var fifth = await controller.AddMember(NewMemberWrite("Member 5", address.Id), CancellationToken.None);
        var conflict = fifth.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.max_members");
    }

    [Fact]
    public async Task DeleteMember_RejectsManagerWithDistinctCode()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId);

        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        var ok = created.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = dto.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var deleted = await controller.DeleteMember(managerId, CancellationToken.None);
        var conflict = deleted.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.cannot_delete_manager");
    }

    [Fact]
    public async Task PutMemberPlan_WithAddressAndCatalogPlan_SetsSetupStatusReady()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            6,
            IncludeFriday: false), CancellationToken.None);

        var ok = planResult.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.SetupStatus.Should().Be(FamilyMemberSetupStatus.Ready);
        member.ProgramId.Should().Be(program.Id);
        member.BundleId.Should().Be(bundle.Id);
        member.SelectedTier.Should().Be(RestaurantTier.Basic);
        member.DurationDays.Should().Be(6);
        member.DeliveryAddressId.Should().Be(address.Id);
    }

    [Fact]
    public async Task GetSummary_TwoReadyMembers_MatchesQuoteAndAllowsCheckout()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var settings = new FamilyPricingSettings
        {
            MaxMembers = 4,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    NameAr = "باقة عائلية",
                    NameEn = "Family package",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        };
        var controller = CreateController(db, userId, users, new StubFamilyPricingSettingsReader(settings));

        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        var added = await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);
        var afterAdd = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var spouseId = afterAdd.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;
        await controller.SetMemberPlan(spouseId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        await SaveNoneAllergiesAsync(controller, managerId, spouseId);

        var tracked = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var quote = await CreateQuoteService(db, settings).QuoteAsync(tracked, CancellationToken.None);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;

        summary.FinalPrice.Should().Be(quote.FinalPrice);
        summary.Subtotal.Should().Be(quote.Subtotal);
        summary.FamilyDiscountAmount.Should().Be(quote.FamilyDiscountAmount);
        summary.CanCheckout.Should().BeTrue();
        summary.CheckoutBlockReason.Should().BeNull();
        summary.CheckoutEndpoint.Should().Be("POST /api/v1/customer/family-subscription-flow/checkout");
        summary.SelectedMemberId.Should().Be(managerId);
        summary.SelectedMember.MemberId.Should().Be(managerId);
        summary.SelectedMember.IsManager.Should().BeTrue();
        summary.SelectedMember.IsReady.Should().BeTrue();
        summary.SelectedMember.ProgramNameEn.Should().NotBeNullOrWhiteSpace();
        summary.SelectedMember.BundleNameEn.Should().NotBeNullOrWhiteSpace();
        summary.SelectedMember.DurationLabelAr.Should().Contain("يوم");
        summary.SelectedMember.SelectedTier.Should().Be(RestaurantTier.Basic);
        summary.SelectedMember.TierNameEn.Should().Be("Basic");
        summary.MealOverview.Should().HaveCount(2);
        summary.MealOverview.Should().OnlyContain(r =>
            r.IsReady
            && r.Breakfast == false
            && r.Lunch == true
            && r.Snack == false
            && r.Dinner == false
            && r.MealsPerDay == 1);

        var switched = await controller.Summary(memberId: spouseId, CancellationToken.None);
        var switchedSummary = switched.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        switchedSummary.SelectedMemberId.Should().Be(spouseId);
        switchedSummary.SelectedMember.DisplayName.Should().Be("سارة");
        switchedSummary.SelectedMember.IsManager.Should().BeFalse();
        switchedSummary.FinalPrice.Should().Be(summary.FinalPrice);
        switchedSummary.MealOverview.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSummary_UnpaidMemberWithoutPlan_DoesNotBlockCheckout()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);

        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        await SaveNoneAllergiesAsync(controller, managerId);

        await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeTrue();
        summary.CheckoutBlockReason.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_WhenPayableMemberMissingAllergies_BlocksCheckout()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var controller = CreateController(db, userId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerMemberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerMemberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerMemberId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var summary = summaryResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeFalse();
        summary.CheckoutBlockReason.Should().Be("family.allergies_required");
    }

    [Fact]
    public async Task SaveMemberAllergies_None_AllowsCheckoutReadiness()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var controller = CreateController(db, userId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerMemberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerMemberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerMemberId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        await SaveNoneAllergiesAsync(controller, managerMemberId);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var summary = summaryResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeTrue();
        summary.CheckoutBlockReason.Should().BeNull();
    }

    [Fact]
    public async Task CreateMemberAddress_WhenCoordinatesOutsideKuwait_Rejects()
    {
        await using var db = CreateDb();
        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);

        var result = await controller.CreateMemberAddress(
            spouseId,
            new CustomerAddressRequest(
                "Home",
                "Outside",
                null,
                40m,
                30m,
                null,
                true),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(MessageCodes.Validation.InvalidFormat);
        (await db.CustomerAddresses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateMemberAddress_WhenMapPinInsideKuwait_StoresCoordsOnLinkedMember()
    {
        await using var db = CreateDb();
        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var created = await controller.CreateMemberAddress(
            spouseId,
            new CustomerAddressRequest(
                "Home",
                "Pin on map",
                null,
                29.3759m,
                47.9774m,
                null,
                true),
            CancellationToken.None);

        var dto = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<CustomerAddressDto>().Subject;
        dto.Latitude.Should().Be(29.3759m);
        dto.Longitude.Should().Be(47.9774m);

        var stored = await db.CustomerAddresses.SingleAsync(a => a.Id == dto.Id);
        stored.UserId.Should().Be(linkedUserId);
        stored.Latitude.Should().Be(29.3759m);
        stored.Longitude.Should().Be(47.9774m);
        (await db.FamilyMembers.SingleAsync(m => m.Id == spouseId)).DeliveryAddressId.Should().Be(dto.Id);
    }

    [Fact]
    public async Task CreateMemberAddress_WhenManualAreaWithoutCoords_ResolvesCatalogCentroid()
    {
        await using var db = CreateDb();
        var area = await SeedKuwaitAreaAsync(db);
        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var created = await controller.CreateMemberAddress(
            spouseId,
            new CustomerAddressRequest(
                "المنزل",
                "قطعة 4، شارع 12",
                area.Id,
                null,
                null,
                null,
                true,
                Governorate: null,
                Area: null,
                Block: "4"),
            CancellationToken.None);

        var dto = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<CustomerAddressDto>().Subject;
        dto.RegionId.Should().Be(area.Id);
        dto.Governorate.Should().Be("العاصمة");
        dto.Area.Should().Be("شرق");
        dto.Latitude.Should().Be(29.3780m);
        dto.Longitude.Should().Be(47.9900m);

        var stored = await db.CustomerAddresses.SingleAsync(a => a.Id == dto.Id);
        stored.UserId.Should().Be(linkedUserId);
        stored.RegionId.Should().Be(area.Id);
        stored.Latitude.Should().Be(29.3780m);
        stored.Longitude.Should().Be(47.9900m);
        (await db.FamilyMembers.SingleAsync(m => m.Id == spouseId)).DeliveryAddressId.Should().Be(dto.Id);
    }

    [Fact]
    public async Task PreviewMemberAddressCoverage_WhenManualArea_ResolvesCatalogRegion()
    {
        await using var db = CreateDb();
        var area = await SeedKuwaitAreaAsync(db);
        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);

        var previewed = await controller.PreviewMemberAddressCoverage(
            spouseId,
            new PreviewAddressCoverageRequest(
                RegionId: area.Id,
                Governorate: null,
                Area: null,
                Block: "4"),
            CancellationToken.None);

        var dto = previewed.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<AddressCoveragePreviewDto>().Subject;
        dto.ResolvedRegionId.Should().Be(area.Id);
        dto.Governorate.Should().Be("العاصمة");
        dto.Area.Should().Be("شرق");
        dto.Latitude.Should().Be(29.3780m);
        dto.Longitude.Should().Be(47.9900m);
        dto.CanSetAsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GetMemberAddresses_ReturnsAddressesStoredOnLinkedAccount()
    {
        await using var db = CreateDb();
        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var created = await controller.CreateMemberAddress(
            spouseId,
            new CustomerAddressRequest(
                "Home",
                "Pin on map",
                null,
                29.3759m,
                47.9774m,
                null,
                true),
            CancellationToken.None);
        var createdDto = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<CustomerAddressDto>().Subject;

        var listed = await controller.GetMemberAddresses(spouseId, CancellationToken.None);
        var items = listed.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<CustomerAddressDto>>().Subject;
        items.Should().ContainSingle(a => a.Id == createdDto.Id);
        (await db.CustomerAddresses.SingleAsync(a => a.Id == createdDto.Id)).UserId.Should().Be(linkedUserId);
    }

    [Fact]
    public async Task GetCurrent_WhenActive_ReturnsExistingGroup()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة نشطة",
            Status = FamilyGroupStatus.Active,
        };
        group.Members.Add(new FamilyMember
        {
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            NutritionalGoalCode = string.Empty,
            AgeYears = 36,
            MemberSubscriptionId = Guid.NewGuid(),
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        var controller = CreateController(db, managerId, users);

        var result = await controller.Current(CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Status.Should().Be(FamilyGroupStatus.Active);
        dto.Name.Should().Be("عائلة نشطة");
    }

    [Fact]
    public async Task PutMemberPlan_DurationThreeDays_ReturnsFamilyDurationNotAvailable()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            3,
            IncludeFriday: false), CancellationToken.None);

        var badRequest = planResult.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.duration_not_available");
    }

    [Fact]
    public async Task GetPrograms_WhenAvailableForFamilyIsFalse_StillReturnsProgramWithOffer()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        program.AvailableForFamily = false;
        bundle.AvailableForFamily = false;
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));
        var controller = CreateController(db, userId, users);

        var result = await controller.Programs(CancellationToken.None);
        var cards = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<CustomerProgramCardDto>>().Subject;
        cards.Should().Contain(c => c.ProgramId == program.Id);

        var bundlesResult = await controller.Bundles(program.Id, CancellationToken.None);
        var bundles = bundlesResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<CustomerBundleCardDto>>().Subject;
        bundles.Should().Contain(b => b.BundleId == bundle.Id);
    }

    [Fact]
    public async Task PutMemberPlan_WhenProgramNotFlaggedForFamily_StillPersistsPlan()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        program.AvailableForFamily = false;
        bundle.AvailableForFamily = false;
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            6,
            IncludeFriday: false), CancellationToken.None);

        var dto = planResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.ProgramId.Should().Be(program.Id);
        member.BundleId.Should().Be(bundle.Id);
    }

    [Fact]
    public async Task PutMemberPlan_ProgramOnly_PersistsInProgress()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, _) = await SeedProgramBundleOfferAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;
        await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);

        var planResult = await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program.Id) },
            CancellationToken.None);

        var dto = planResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.ProgramId.Should().Be(program.Id);
        member.BundleId.Should().BeNull();
        member.SelectedTier.Should().BeNull();
        member.DurationDays.Should().BeNull();
        member.SetupStatus.Should().Be(FamilyMemberSetupStatus.InProgress);
    }

    [Fact]
    public async Task PutMemberPlan_TwoMembers_EachKeepsOwnProgram()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, _) = await SeedProgramBundleOfferAsync(db);
        var program2 = await SeedSecondFamilyProgramAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;
        await controller.UpdateMember(managerId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);

        await controller.SetMemberPlan(
            managerId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program.Id) },
            CancellationToken.None);

        var added = await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);
        var afterAdd = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var spouseId = afterAdd.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;

        await controller.SetMemberPlan(
            spouseId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program2.Id) },
            CancellationToken.None);

        var reloaded = await controller.Current(CancellationToken.None);
        var screen = reloaded.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.Members.Should().Contain(m => m.MemberId == managerId && m.ProgramId == program.Id);
        screen.Members.Should().Contain(m => m.MemberId == spouseId && m.ProgramId == program2.Id);
    }

    [Fact]
    public async Task PutMemberPlan_ChangeProgram_ClearsIncompatibleBundleAndTier()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        var program2 = await SeedSecondFamilyProgramAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;
        await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);

        await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        var changed = await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program2.Id) },
            CancellationToken.None);

        var dto = changed.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.ProgramId.Should().Be(program2.Id);
        member.BundleId.Should().BeNull();
        member.SelectedTier.Should().BeNull();
        member.SetupStatus.Should().Be(FamilyMemberSetupStatus.InProgress);
    }

    [Fact]
    public async Task PutMemberPlan_EmptyBody_ReturnsPlanEmpty()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        await SeedProgramBundleOfferAsync(db);

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle().Subject.MemberId;

        var planResult = await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest(),
            CancellationToken.None);

        var badRequest = planResult.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.plan_empty");
    }

    [Fact]
    public async Task PutMemberPlan_TierOnly_PersistsSelectedTier()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;
        await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);

        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program.Id) },
            CancellationToken.None);
        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { BundleId = Optional<Guid>.Of(bundle.Id) },
            CancellationToken.None);
        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { DurationDays = Optional<int>.Of(6), IncludeFriday = Optional<bool>.Of(false) },
            CancellationToken.None);

        var tierResult = await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { Tier = Optional<RestaurantTier>.Of(RestaurantTier.Basic) },
            CancellationToken.None);

        var dto = tierResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.SelectedTier.Should().Be(RestaurantTier.Basic);
        member.SetupStatus.Should().Be(FamilyMemberSetupStatus.Ready);
    }

    [Fact]
    public async Task PutMemberPlan_SelectedTierAlias_PersistsLikeTier()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;
        await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);

        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { ProgramId = Optional<Guid>.Of(program.Id) },
            CancellationToken.None);
        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { BundleId = Optional<Guid>.Of(bundle.Id) },
            CancellationToken.None);
        await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { DurationDays = Optional<int>.Of(6), IncludeFriday = Optional<bool>.Of(false) },
            CancellationToken.None);

        var aliasResult = await controller.SetMemberPlan(
            memberId,
            new SetFamilyMemberPlanRequest { SelectedTier = Optional<RestaurantTier>.Of(RestaurantTier.Basic) },
            CancellationToken.None);

        var dto = aliasResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject.SelectedTier.Should().Be(RestaurantTier.Basic);
    }

    private static async Task<MealMate.Domain.ProgramsSubscriptions.Program> SeedSecondFamilyProgramAsync(AppDbContext db)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_KETO",
            ShortCode = "KT",
            NameAr = "كيتو",
            NameEn = "Keto",
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE_KETO",
            NameAr = "باقة كيتو",
            NameEn = "Keto bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();
        return program;
    }

    private static async Task<(MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle)> SeedProgramBundleOfferAsync(AppDbContext db)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_WEIGHT_LOSS",
            ShortCode = "WL",
            NameAr = "خسارة الوزن",
            NameEn = "Weight Loss",
            DescriptionAr = "وجبات محسوبة السعرات.",
            DescriptionEn = "Calorie-controlled meals.",
            KeywordsAr = ["سعرات محسوبة", "نتائج مستدامة"],
            KeywordsEn = ["Calorie controlled", "Sustainable results"],
            ImageStorageKey = "uploads/admin/programs/wl.webp",
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE",
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            IsActive = true,
        });
        // Locked pricing so GetCustomerSubscriptionPricingCatalogQuery yields Basic for the single restaurant.
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(41.6m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        return (program, bundle);
    }

    private static void SeedPricingRules(AppDbContext db)
    {
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromFraction(.3m),
            RMin = Percentage.FromFraction(.05m),
            DShort = 6,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = .1m,
            IsActive = true,
        });
        db.SaveChanges();
    }

    private static IFamilyQuoteService CreateQuoteService(AppDbContext db, FamilyPricingSettings settings)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var pricingHandlers = new PricingAndClassificationHandlers(
            db, Substitute.For<IAuditLogger>(), clock, tiers);
        var catalogSender = new CatalogQuerySender(pricingHandlers);
        return new FamilyQuoteService(
            catalogSender,
            new StubFamilyPricingSettingsReader(settings),
            new SubscriptionScheduleService(clock));
    }

    private static async Task<(CustomerFamilySubscriptionFlowController Controller, Guid SpouseId, Guid LinkedUserId)> CreateLinkedSpouseAsync(
        AppDbContext db)
    {
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var controller = CreateController(db, managerId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        var added = await controller.AddMember(
            NewMemberWrite("سارة", Guid.Empty) with { DeliveryAddressId = null, Phone = "+96553333333" },
            CancellationToken.None);
        var spouseId = added.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject
            .Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;

        var linkedUserId = await db.FamilyMemberAccesses
            .Where(a => a.FamilyMemberId == spouseId)
            .Select(a => a.LinkedCustomerUserId)
            .SingleAsync();
        linkedUserId.Should().NotBeNull();
        return (controller, spouseId, linkedUserId!.Value);
    }

    private static async Task<Region> SeedKuwaitAreaAsync(AppDbContext db)
    {
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var governorate = new Region
        {
            CountryId = country.Id, Type = RegionType.Governorate, Code = "KW-CAP",
            NameAr = "العاصمة", NameEn = "Capital", IsActive = true
        };
        var area = new Region
        {
            CountryId = country.Id, ParentRegionId = governorate.Id, Type = RegionType.Area,
            Code = "KW-CAP-SHARQ", NameAr = "شرق", NameEn = "Sharq", IsActive = true,
            CentroidLatitude = 29.3780m, CentroidLongitude = 47.9900m
        };
        db.AddRange(country, governorate, area);
        await db.SaveChangesAsync();
        return area;
    }

    private static UserAccountDto ManagerAccount(Guid userId, string name = "أحمد")
        => new(userId, null, "+96550000001", name, UserType.Customer, AccountStatus.Active,
            null, false, false, DateTime.UtcNow, null);

    private static FamilyMemberWriteDto NewMemberWrite(string name, Guid addressId) => new(
        name,
        "spouse",
        FamilyMemberGender.Female,
        30,
        addressId == Guid.Empty ? null : addressId,
        $"+9655{Guid.NewGuid():N}"[..12]);

    private static async Task SaveNoneAllergiesAsync(
        CustomerFamilySubscriptionFlowController controller,
        params Guid[] memberIds)
    {
        foreach (var memberId in memberIds)
        {
            var result = await controller.SaveMemberAllergies(
                memberId,
                new SaveFamilyMemberAllergiesRequest(
                    [new FoodPreferenceItemDto(FoodPreferenceNoneCodes.NoAllergies, null, null, null)]),
                CancellationToken.None);
            result.Should().BeOfType<OkObjectResult>();
        }
    }

    private static CustomerFamilySubscriptionFlowController CreateController(
        AppDbContext db,
        Guid userId,
        IUserAccountService? users = null,
        IFamilyPricingSettingsReader? settings = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        IUserAccountService resolvedUsers;
        if (users is null)
        {
            resolvedUsers = Substitute.For<IUserAccountService>();
            resolvedUsers.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var req = ci.Arg<CreateUserRequest>();
                    return Task.FromResult(Result<UserAccountDto>.Success(new UserAccountDto(
                        Guid.NewGuid(), req.Email, req.PhoneNumber, req.FullName, req.UserType, req.AccountStatus,
                        req.RestaurantId, false, false, DateTime.UtcNow, null)));
                });
        }
        else
        {
            resolvedUsers = users;
        }

        resolvedUsers.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<CreateUserRequest>();
                return Task.FromResult(Result<UserAccountDto>.Success(new UserAccountDto(
                    Guid.NewGuid(), req.Email, req.PhoneNumber, req.FullName, req.UserType, req.AccountStatus,
                    req.RestaurantId, false, false, DateTime.UtcNow, null)));
            });
        var pricingSettings = settings ?? new StubFamilyPricingSettingsReader(new FamilyPricingSettings
        {
            MaxMembers = 4,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    NameAr = "باقة عائلية",
                    NameEn = "Family package",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                    SortOrder = 0,
                },
            ],
        });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var pricingHandlers = new PricingAndClassificationHandlers(
            db, Substitute.For<IAuditLogger>(), clock, tiers);
        var catalogSender = new CatalogQuerySender(pricingHandlers);
        var eligibility = new FamilyPlanEligibilityService(
            db,
            new AllowedSubscriptionDurationService(db),
            tiers,
            catalogSender);
        var quote = new FamilyQuoteService(
            catalogSender,
            pricingSettings,
            new SubscriptionScheduleService(clock));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 19));
        var addressBook = new CustomerAddressHandlers(
            db,
            currentUser,
            Substitute.For<IAuditLogger>(),
            new CustomerDeliveryAddressService(db, clock),
            clock);
        var handlers = new FamilyDraftHandlers(db, currentUser, resolvedUsers, pricingSettings, eligibility, quote, clock, addressBook);
        var convertHandler = new ConvertIndividualToFamilyHandler(db, currentUser, resolvedUsers, pricingSettings, clock);
        var sender = new DirectFamilyDraftSender(handlers, convertHandler, db);

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        messages.GetMessage(Arg.Any<string>(), arguments: Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
        var localizer = new ProblemDetailsLocalizer(messages);

        var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .AddSingleton(localizer)
            .BuildServiceProvider();

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        return new CustomerFamilySubscriptionFlowController(
            currentUser,
            db,
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<IFamilyCheckoutService>(),
            configuration,
            CreateTestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static IWebHostEnvironment CreateTestWebHostEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return environment;
    }

    private sealed class StubFamilyPricingSettingsReader(FamilyPricingSettings settings) : IFamilyPricingSettingsReader
    {
        public Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(settings);
    }

    private sealed class CatalogQuerySender(PricingAndClassificationHandlers pricingHandlers) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetCustomerSubscriptionPricingCatalogQuery q)
                return (TResponse)(object)await pricingHandlers.Handle(q, cancellationToken);
            if (request is PreviewCustomerPricingQuery preview)
                return (TResponse)(object)await pricingHandlers.Handle(preview, cancellationToken);
            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Dispatches family draft requests to handlers and persists successful commands (test stand-in for MediatR + UnitOfWork).</summary>
    private sealed class DirectFamilyDraftSender(
        FamilyDraftHandlers handlers,
        ConvertIndividualToFamilyHandler convertHandler,
        AppDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetFamilyDraftMetadataQuery q => await handlers.Handle(q, cancellationToken),
                GetFamilyRelationshipsQuery q => await handlers.Handle(q, cancellationToken),
                GetFamilySizePackagesQuery q => await handlers.Handle(q, cancellationToken),
                GetCurrentFamilyGroupQuery q => await handlers.Handle(q, cancellationToken),
                UpsertFamilyGroupCommand c => await handlers.Handle(c, cancellationToken),
                AddFamilyDraftMemberCommand c => await handlers.Handle(c, cancellationToken),
                UpdateFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                DeleteFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                LookupFamilyMemberByPhoneQuery q => await handlers.Handle(q, cancellationToken),
                SaveFamilyMemberAllergiesCommand c => await handlers.Handle(c, cancellationToken),
                CreateFamilyMemberAddressCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyMemberAddressesQuery q => await handlers.Handle(q, cancellationToken),
                UpdateFamilyMemberAddressCommand c => await handlers.Handle(c, cancellationToken),
                PreviewFamilyMemberAddressCoverageCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberDefaultAddressCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberPlanCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyCheckoutSummaryQuery q => await handlers.Handle(q, cancellationToken),
                AbandonFamilyDraftCommand c => await handlers.Handle(c, cancellationToken),
                ConvertIndividualToFamilyCommand c => await convertHandler.Handle(c, cancellationToken),
                GetSubscriptionConversionOptionsQuery q => await convertHandler.Handle(q, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };

            if (request is IBaseCommand && result is Result { IsSuccess: true })
            {
                await db.SaveChangesAsync(cancellationToken);
                // Mimic request-scoped DbContext: detach after commit so the next call reloads cleanly.
                db.ChangeTracker.Clear();
            }

            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
