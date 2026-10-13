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
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using FamilyPricingSettings = MealMate.Application.Features.ProgramsSubscriptions.FamilyPricingSettings;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Interceptors;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
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
    public async Task ExplicitNonePreferences_PersistCompletionWithoutStoringControlCodes()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition { Code = "MILK", NameAr = "حليب", NameEn = "Milk", IsActive = true });
        db.FoodPreferenceDefinitions.Add(new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true });
        await db.SaveChangesAsync();
        var (controller, memberId, userId) = await CreateLinkedSpouseAsync(db);
        foreach (var none in new[] { false, true, false, true })
        {
            var allergy = none ? "NO_ALLERGIES" : "MILK";
            var dietary = none ? "NO_DIETARY_RESTRICTIONS" : "VEGETARIAN";
            (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: [allergy]), default)).Should().BeOfType<OkObjectResult>();
            (await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: [dietary]), default)).Should().BeOfType<OkObjectResult>();
            db.ChangeTracker.Clear();
            var a = (await controller.GetMemberAllergies(memberId, default)).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberAllergiesDto>().Subject;
            var d = (await controller.GetMemberDietary(memberId, default)).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDietaryDto>().Subject;
            a.Completed.Should().BeTrue(); d.Completed.Should().BeTrue();
            a.HasNoAllergies.Should().Be(none); d.HasNoDietaryRestriction.Should().Be(none);
            var current = (await controller.Current(default)).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
            var card = current.Members.Single(x => x.MemberId == memberId);
            card.SelectedAllergyOptionCodes.Should().Equal(allergy);
            card.SelectedDietaryOptionCodes.Should().Equal(dietary);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(a));
            json.RootElement.GetProperty("SelectedOptionCodes").EnumerateArray().Select(x => x.GetString()).Should().Equal(allergy);
            (await db.CustomerAllergyPreferences.CountAsync(x => x.CustomerUserId == userId && !x.IsDislike)).Should().Be(none ? 0 : 1);
            (await db.CustomerDietaryPreferences.CountAsync(x => x.CustomerUserId == userId)).Should().Be(none ? 0 : 1);
            (await db.CustomerAllergyPreferences.AnyAsync(x => x.AllergenCode == "NO_ALLERGIES")).Should().BeFalse();
            (await db.CustomerDietaryPreferences.AnyAsync(x => x.PreferenceCode == "NO_DIETARY_RESTRICTIONS")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExplicitNoneMixedWithRealPreferences_IsRejectedWithoutClearingSavedData()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition { Code = "MILK", NameAr = "حليب", NameEn = "Milk", IsActive = true });
        db.FoodPreferenceDefinitions.Add(new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true });
        await db.SaveChangesAsync();
        var (controller, memberId, userId) = await CreateLinkedSpouseAsync(db);
        await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["MILK"]), default);
        await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: ["VEGETARIAN"]), default);
        (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["NO_ALLERGIES", "MILK"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(400);
        (await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: ["NO_DIETARY_RESTRICTIONS", "VEGETARIAN"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(400);
        db.ChangeTracker.Clear();
        (await db.CustomerAllergyPreferences.Where(x => x.CustomerUserId == userId && !x.IsDislike).Select(x => x.AllergenCode).ToListAsync()).Should().Equal("MILK");
        (await db.CustomerDietaryPreferences.Where(x => x.CustomerUserId == userId).Select(x => x.PreferenceCode).ToListAsync()).Should().Equal("VEGETARIAN");
    }

    [Theory]
    [InlineData(FamilyCheckoutBatchStatus.PaymentPending)]
    [InlineData(FamilyCheckoutBatchStatus.ReviewRequired)]
    public async Task DeferredMemberPreferences_SaveDuringPendingPaymentButFrozenParticipantIsProtected(FamilyCheckoutBatchStatus status)
    {
        await using var db = CreateDb();
        var (controller, memberId, _) = await CreateLinkedSpouseAsync(db, transactionalLocks: true);
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        group.Status = FamilyGroupStatus.CheckoutPending;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = "pending-preferences", Status = status,
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15) };
        var line = new FamilyCheckoutBatchLine { FamilyMemberId = memberId, CheckoutSelected = false };
        batch.Lines.Add(line); db.FamilyCheckoutBatches.Add(batch); await db.SaveChangesAsync();
        (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["NO_ALLERGIES"]), default)).Should().BeOfType<OkObjectResult>();
        (await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: ["NO_DIETARY_RESTRICTIONS"]), default)).Should().BeOfType<OkObjectResult>();
        line = await db.FamilyCheckoutBatchLines.SingleAsync(x => x.BatchId == batch.Id);
        line.CheckoutSelected = true; await db.SaveChangesAsync();
        (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["NO_ALLERGIES"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(409);
        (await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: ["NO_DIETARY_RESTRICTIONS"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task DeferredCard_SharingFrozenParticipantsProfile_CannotChangePreferences()
    {
        await using var db = CreateDb();
        var (controller, memberId, _) = await CreateLinkedSpouseAsync(db, transactionalLocks: true);
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var manager = group.Members.Single(x => x.Role == FamilyMemberRole.Manager);
        (await db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == memberId)).LinkedCustomerUserId = group.ManagerCustomerUserId;
        group.Status = FamilyGroupStatus.CheckoutPending;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = "shared-preferences", Status = FamilyCheckoutBatchStatus.PaymentPending,
            CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15) };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = manager.Id, CheckoutSelected = true });
        db.FamilyCheckoutBatches.Add(batch); await db.SaveChangesAsync();
        (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["NO_ALLERGIES"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(409);
        (await controller.SaveMemberDietary(memberId, new SaveFamilyMemberDietaryRequest(SelectedCodes: ["NO_DIETARY_RESTRICTIONS"]), default)).Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Allergies_EmptySelectedCodesDoesNotHideNonEmptyCodesAlias()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition { Code = "MILK", NameAr = "حليب", NameEn = "Milk", IsActive = true });
        await db.SaveChangesAsync();
        var (controller, memberId, userId) = await CreateLinkedSpouseAsync(db);
        (await controller.SaveMemberAllergies(memberId, new SaveFamilyMemberAllergiesRequest(SelectedCodes: [], Codes: ["MILK"]), default)).Should().BeOfType<OkObjectResult>();
        db.ChangeTracker.Clear();
        (await db.CustomerAllergyPreferences.Where(x => x.CustomerUserId == userId && !x.IsDislike).Select(x => x.AllergenCode).ToListAsync()).Should().Equal("MILK");
    }

    [Fact]
    public async Task Group_ExpiredReservationIsRecoveredBeforeUpsertAndRetainsRoster()
    {
        await using var db = CreateDb();
        var expiry = Substitute.For<ICapacityHoldExpiryService>();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId, holdExpiry: expiry);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var memberId = group.Members.Single().Id;
        group.Status = FamilyGroupStatus.CheckoutPending;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = userId,
            IdempotencyKey = "expired", Status = FamilyCheckoutBatchStatus.PaymentPending,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        expiry.ExpireDueAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            group.Status = FamilyGroupStatus.Draft;
            batch.Status = FamilyCheckoutBatchStatus.Expired;
            await db.SaveChangesAsync();
            return new CapacityExpiryResult(0, 1);
        });
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var screen = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.FamilyGroupId.Should().Be(group.Id);
        screen.Members.Single().MemberId.Should().Be(memberId);
        screen.Status.Should().Be(FamilyGroupStatus.Draft);
        await expiry.Received(1).ExpireDueAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Current_PendingPaymentReturnsSavedUrlWithoutCreatingAnotherAttempt()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var group = await db.FamilyGroups.SingleAsync();
        group.Status = FamilyGroupStatus.CheckoutPending;
        var anchor = Guid.NewGuid();
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = userId,
            IdempotencyKey = "pending", Status = FamilyCheckoutBatchStatus.PaymentPending, CreatedAtUtc = DateTime.UtcNow,
            PaymentAnchorSubscriptionId = anchor, GatewayPaymentId = "payment-handle", FinalPrice = 40m, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) };
        batch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { AttemptKey = "pending", ResultCode = "Initialized",
            GatewayPaymentId = "payment-handle", GatewayPaymentUrl = "https://pay.example/existing", RequestedAtUtc = DateTime.UtcNow });
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        var result = await controller.Current(CancellationToken.None);
        var screen = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.PendingCheckout.Should().NotBeNull();
        screen.PendingCheckout!.PaymentUrl.Should().Be("https://pay.example/existing");
        screen.PendingCheckout.BatchId.Should().Be(batch.Id);
        screen.PendingCheckout.PaymentAnchorSubscriptionId.Should().Be(anchor);
        (await db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(1);
        group.Status.Should().Be(FamilyGroupStatus.CheckoutPending);
    }

    [Fact]
    public async Task AddedMember_DefaultStartDateIsPersistedForBothMembers()
    {
        await using var db = CreateDb();
        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);
        db.ChangeTracker.Clear();
        var today = new SubscriptionScheduleService(TestPricingClock()).GetKuwaitToday();
        var stored = await db.FamilyMembers.AsNoTracking().ToListAsync();
        stored.Should().HaveCount(2);
        stored.Should().OnlyContain(x => x.StartDate == today);
        var result = await controller.Current(CancellationToken.None);
        var screen = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.Members.Single(x => x.MemberId == spouseId).StartDate.Should().Be(today);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_DefaultStartDoesNotAlterPaidMembersOrPendingCheckout(bool paid)
    {
        await using var db = CreateDb();
        var controller = CreateController(db, Guid.NewGuid());
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var member = group.Members.Single();
        member.StartDate = null;
        if (paid)
        {
            group.Status = FamilyGroupStatus.Active;
            member.MemberSubscriptionId = Guid.NewGuid();
        }
        else group.Status = FamilyGroupStatus.CheckoutPending;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await controller.Current(CancellationToken.None);
        result.Should().BeOfType<OkObjectResult>();
        db.ChangeTracker.Clear();
        (await db.FamilyMembers.SingleAsync()).StartDate.Should().BeNull();
    }

    [Fact]
    public async Task Current_DefaultStartDateIsPersisted_AndExplicitChangeSurvivesReload()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, Guid.NewGuid());
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var screen = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = screen.Members.Single();
        var today = new SubscriptionScheduleService(TestPricingClock()).GetKuwaitToday();
        member.StartDate.Should().Be(today);
        db.ChangeTracker.Clear();
        (await db.FamilyMembers.SingleAsync()).StartDate.Should().Be(today);

        var chosen = today.AddDays(10);
        var changed = await controller.SetMemberStartDate(member.MemberId, new SetFamilyMemberStartDateRequest(chosen), CancellationToken.None);
        changed.Should().BeOfType<OkObjectResult>();
        db.ChangeTracker.Clear();
        var current = await controller.Current(CancellationToken.None);
        var reloaded = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        reloaded.Members.Single().StartDate.Should().Be(chosen);
        (await db.FamilyMembers.SingleAsync()).StartDate.Should().Be(chosen);
    }

    [Fact]
    public async Task Summary_LegacyMissingStartDateIsPersistedBeforeRestaurantEvaluation()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress { UserId = userId, Label = "Home", AddressLine = "Home", RegionId = Guid.NewGuid() };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RestaurantEligibilityDto(Guid.NewGuid(), "مطعم", "Restaurant", true, [], []) });
        var controller = CreateController(db, userId, restaurantEligibility: eligibility);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var member = await db.FamilyMembers.SingleAsync();
        member.ProgramId = program.Id;
        member.BundleId = bundle.Id;
        member.SelectedTier = RestaurantTier.Basic;
        member.DurationDays = 6;
        member.DeliveryAddressId = address.Id;
        member.StartDate = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await controller.Summary(member.Id, CancellationToken.None);
        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.Members.Single().StartDate.Should().NotBeNull();
        summary.MemberSchedules!.Single().FirstDeliveryDate.Should().NotBeNull();
        summary.Restaurants.Should().HaveCount(1);
        db.ChangeTracker.Clear();
        (await db.FamilyMembers.SingleAsync()).StartDate.Should().Be(summary.Members.Single().StartDate);
        await eligibility.Received(1).EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FamilyTierPrices_AreAbsentUntilDuration_AndChangeWithRequestedDuration()
    {
        await using var db = CreateDb();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var region = await SeedKuwaitAreaAsync(db);
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Draft };
        var address = new CustomerAddress { UserId = managerId, RegionId = region.Id, Label = "Home", AddressLine = "Sharq", IsDefault = true };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Manager, DisplayName = "Manager",
            RelationshipCode = "self", NutritionalGoalCode = "maintain", DeliveryAddressId = address.Id };
        group.Members.Add(member);
        var restaurant = await db.RestaurantProfiles.SingleAsync();
        db.AddRange(group, address);
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = region.Id, IsActive = true });
        await db.SaveChangesAsync();
        var service = CreateSharedSubscriptionFlowQueryService(db, realPricing: true);

        var unselected = await service.GetTiersAsync(program.Id, bundle.Id, managerId, null, false, member.Id);
        unselected.IsSuccess.Should().BeTrue();
        unselected.Value.Should().NotBeEmpty();
        unselected.Value.Should().OnlyContain(x => x.DurationDays == null && x.TotalPrice == null && x.DailyPrice == null && x.PriceDifference == null);

        var shortDuration = await service.GetTiersAsync(program.Id, bundle.Id, managerId, 6, false, member.Id);
        var longDuration = await service.GetTiersAsync(program.Id, bundle.Id, managerId, 12, false, member.Id);
        shortDuration.IsSuccess.Should().BeTrue();
        longDuration.IsSuccess.Should().BeTrue();
        var shortTier = shortDuration.Value.Single(x => x.Tier == RestaurantTier.Basic);
        var longTier = longDuration.Value.Single(x => x.Tier == RestaurantTier.Basic);
        shortTier.DurationDays.Should().Be(6);
        longTier.DurationDays.Should().Be(12);
        shortTier.TotalPrice.Should().BeGreaterThan(0);
        longTier.TotalPrice.Should().BeGreaterThan(shortTier.TotalPrice!.Value);
        var catalog = new CatalogQuerySender(new PricingAndClassificationHandlers(db, Substitute.For<IAuditLogger>(),
            TestPricingClock(), new RestaurantTierClassificationService(db, TestPricingClock())));
        var prices = await catalog.Send(new GetCustomerSubscriptionPricingCatalogQuery());
        var persistedPricing = prices.Value.Single(x => x.ProgramId == program.Id && x.BundleId == bundle.Id && x.Tier == RestaurantTier.Basic);
        shortTier.TotalPrice.Should().Be(persistedPricing.Prices.Single(x => x.DurationDays == 6).TotalPriceKwd);
        longTier.TotalPrice.Should().Be(persistedPricing.Prices.Single(x => x.DurationDays == 12).TotalPriceKwd);
    }

    private static IDateTimeProvider TestPricingClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));
        return clock;
    }

    [Fact]
    public async Task MemberPreferences_EachSelectionPersistsImmediatelyWithoutConfirmation()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.AddRange(
            new AllergenDefinition { Code = "MILK", NameAr = "حليب", NameEn = "Milk", IsActive = true },
            new AllergenDefinition { Code = "PEANUTS", NameAr = "فول سوداني", NameEn = "Peanuts", IsActive = true });
        db.FoodPreferenceDefinitions.AddRange(
            new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true },
            new FoodPreferenceDefinition { Code = "LOW_CARB", NameAr = "قليل الكربوهيدرات", NameEn = "Low carb", IsActive = true });
        await db.SaveChangesAsync();
        var (controller, memberId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        foreach (var codes in new IReadOnlyList<string>[] { new[] { "MILK" }, new[] { "MILK", "PEANUTS" }, new[] { "PEANUTS" }, Array.Empty<string>() })
        {
            var response = await controller.SaveMemberAllergies(memberId,
                new SaveFamilyMemberAllergiesRequest(SelectedCodes: codes), CancellationToken.None);
            response.Should().BeOfType<OkObjectResult>();
            db.ChangeTracker.Clear();
            var stored = await db.CustomerAllergyPreferences.AsNoTracking()
                .Where(x => x.CustomerUserId == linkedUserId && !x.IsDislike).Select(x => x.AllergenCode).ToListAsync();
            stored.Should().BeEquivalentTo(codes);
            var reloaded = await controller.GetMemberAllergies(memberId, CancellationToken.None);
            var state = reloaded.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberAllergiesDto>().Subject;
            state.SelectedCodes.Should().BeEquivalentTo(codes);
            state.Completed.Should().BeTrue();
            state.HasNoAllergies.Should().Be(codes.Count == 0);
        }

        foreach (var codes in new IReadOnlyList<string>[] { new[] { "VEGETARIAN" }, new[] { "VEGETARIAN", "LOW_CARB" }, new[] { "LOW_CARB" }, Array.Empty<string>() })
        {
            var response = await controller.SaveMemberDietary(memberId,
                new SaveFamilyMemberDietaryRequest(SelectedCodes: codes), CancellationToken.None);
            response.Should().BeOfType<OkObjectResult>();
            db.ChangeTracker.Clear();
            var stored = await db.CustomerDietaryPreferences.AsNoTracking()
                .Where(x => x.CustomerUserId == linkedUserId).Select(x => x.PreferenceCode).ToListAsync();
            stored.Should().BeEquivalentTo(codes);
            var reloaded = await controller.GetMemberDietary(memberId, CancellationToken.None);
            var state = reloaded.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDietaryDto>().Subject;
            state.SelectedCodes.Should().BeEquivalentTo(codes);
            state.Completed.Should().BeTrue();
        }
    }

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
    public async Task ConvertFromIndividual_ThenAbandon_PreservesRosterAndSubscriptionHistory()
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
        (await db.Subscriptions.SingleAsync(x => x.Id == sub.Id)).FamilyGroupId.Should().BeNull();

        var abandoned = await controller.AbandonDraft(CancellationToken.None);
        abandoned.Should().BeOfType<NoContentResult>();
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
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
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(26)),
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
    public async Task Lookup_WhenPhoneHasSpacesOrLeadingZeros_NormalizesAndFindsUser()
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

        var result = await controller.LookupMember(new FamilyMemberLookupRequest(" 00965-5000-0002 "), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberLookupDto>().Subject;
        dto.Found.Should().BeTrue();
        dto.DisplayName.Should().Be("سارة");
    }

    [Fact]
    public async Task Lookup_WhenUserHasProfileDateOfBirth_AutoCalculatesAgeYears()
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

        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = otherId,
            DateOfBirth = new DateOnly(2001, 1, 1)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, managerId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);

        var result = await controller.LookupMember(new FamilyMemberLookupRequest("+96550000002"), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberLookupDto>().Subject;
        dto.Found.Should().BeTrue();
        dto.DisplayName.Should().Be("سارة");
        dto.AgeYears.Should().Be(25);
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
    public async Task AddMember_ExistingCustomer_PrefersLinkedUserAddressOverManagerAddress()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();

        var managerAddress = new CustomerAddress
        {
            UserId = managerId,
            Label = "Manager home",
            AddressLine = "Manager Street, Kuwait",
            IsDefault = true,
        };
        var memberAddress = new CustomerAddress
        {
            UserId = existingUserId,
            Label = "Member home",
            AddressLine = "Member Street, Salmiya",
            IsDefault = true,
        };
        db.CustomerAddresses.AddRange(managerAddress, memberAddress);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync("+96554444444", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                existingUserId, "member@example.com", "+96554444444", "سارة", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, managerId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);

        // Client often sends the manager address when adding a member; linked customer's own address must win.
        var add = await controller.AddMember(
            NewMemberWrite("سارة", managerAddress.Id) with { Phone = "+96554444444" },
            CancellationToken.None);

        var dto = add.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.DisplayName == "سارة").Subject;
        member.DeliveryAddressId.Should().Be(memberAddress.Id);
        member.DeliveryAddress!.AddressLine.Should().Be("Member Street, Salmiya");
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
        var catalogResult = await controller.Bundles(program.Id, CancellationToken.None);
        var catalogBundle = catalogResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<CustomerBundleCardDto>>().Subject
            .Should().ContainSingle(x => x.BundleId == bundle.Id).Subject;
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
            catalogBundle.BundleId,
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetSummary_TwoReadyMembers_MatchesQuoteAndGatesRestaurantAvailability(bool restaurantsAvailable)
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
            RegionId = Guid.NewGuid(),
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
        var restaurantEligibility = Substitute.For<IRestaurantEligibilityService>();
        restaurantEligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(restaurantsAvailable
                ? new[] { new RestaurantEligibilityDto(Guid.NewGuid(), "مطعم", "Restaurant", true, [], []) }
                : Array.Empty<RestaurantEligibilityDto>());
        var controller = CreateController(db, userId, users, new StubFamilyPricingSettingsReader(settings), restaurantEligibility: restaurantEligibility);

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

        await SeedSelectionAsync(db, managerId, spouseId);
        var tracked = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var quote = (await CreateQuoteService(db, settings).QuoteAsync(tracked, CancellationToken.None)).Value;

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;

        summary.FinalPrice.Should().Be(quote.FinalPrice);
        summary.Subtotal.Should().Be(quote.Subtotal);
        summary.FamilyDiscountAmount.Should().Be(quote.FamilyDiscountAmount);
        summary.SelectedFamilyPackageId.Should().Be(tracked.SelectedFamilyPackageId);
        summary.EffectiveFamilyPackageId.Should().Be(quote.EffectiveFamilyPackageId);
        summary.EffectiveFamilyPackageMinMembers.Should().Be(quote.EffectiveFamilyPackageMinMembers);
        summary.EffectiveFamilyPackageMaxMembers.Should().Be(quote.EffectiveFamilyPackageMaxMembers);
        summary.ParticipantCount.Should().Be(quote.ParticipantCount);
        summary.PackageSelectionMode.Should().Be(quote.PackageSelectionMode);
        summary.CheckoutBlockReason.Should().Be(restaurantsAvailable ? null : SubscriptionCheckoutGateCodes.NoRestaurantsForAddress);
        summary.CanCheckout.Should().Be(restaurantsAvailable);
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
        switchedSummary.EffectiveFamilyPackageId.Should().Be(summary.EffectiveFamilyPackageId);
        switchedSummary.ParticipantCount.Should().Be(summary.ParticipantCount);
        switchedSummary.MealOverview.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSummary_WhenMemberPlanHasNoLockedPrices_ReturnsPricingUnavailable()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));
        var controller = CreateController(db, userId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);

        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var manager = group.Members.Single(m => m.Role == FamilyMemberRole.Manager);
        manager.ProgramId = Guid.NewGuid();
        manager.BundleId = Guid.NewGuid();
        manager.SelectedTier = RestaurantTier.Basic;
        manager.DurationDays = 26;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SeedSelectionAsync(db, manager.Id);
        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);

        var badRequest = summaryResult.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("pricing.unavailable");
    }

    [Fact]
    public async Task GetSummary_UnpaidMemberWithoutPlan_DoesNotCountTowardPackageMinimum()
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

        await SeedSelectionAsync(db, managerId);
        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeFalse();
        summary.CheckoutBlockReason.Should().Be("family.min_members");
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

        var added = await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);
        var spouseId = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject
            .Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;
        await controller.SetMemberPlan(spouseId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        var spouseUserId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = spouseUserId,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
        });
        var spouseAccess = await db.FamilyMemberAccesses
            .SingleAsync(access => access.FamilyMemberId == spouseId);
        spouseAccess.HasSeparateAccount = true;
        spouseAccess.LinkedCustomerUserId = spouseUserId;
        await db.SaveChangesAsync();

        await SeedSelectionAsync(db, managerMemberId, spouseId);
        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var summary = summaryResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeFalse();
        summary.CheckoutBlockReason.Should().Be("family.allergies_required");
    }

    [Fact]
    public async Task GetSummary_ReturnsSavedMemberPreferencesAndCompletionFlags()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.AddRange(
            new AllergenDefinition { Code = "MILK", NameAr = "حليب", NameEn = "Milk", IsActive = true },
            new AllergenDefinition { Code = "PEANUTS", NameAr = "فول سوداني", NameEn = "Peanuts", IsActive = true });
        db.FoodPreferenceDefinitions.AddRange(
            new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true });
        var area = await SeedKuwaitAreaAsync(db);
        await db.SaveChangesAsync();

        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);

        var created = await controller.CreateMemberAddress(spouseId, new CustomerAddressRequest(
            "Home", "Sharq", area.Id, null, null, null, false, false), CancellationToken.None);
        var address = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<CustomerAddressDto>().Subject;

        (await controller.SaveMemberAllergies(spouseId,
                new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["MILK"]), CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        (await controller.SaveMemberDislikedFoods(spouseId,
                new SaveFamilyMemberDislikedFoodsRequest(IngredientCodes: ["INGREDIENT:11111111-1111-1111-1111-111111111111"]),
                CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        (await controller.SaveMemberDietary(spouseId,
                new SaveFamilyMemberDietaryRequest(DietaryPreferenceCodes: ["VEGETARIAN"]), CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();

        db.ChangeTracker.Clear();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        await db.SaveChangesAsync();
        var planResult = await controller.SetMemberPlan(spouseId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        planResult.Should().BeOfType<OkObjectResult>();

        db.ChangeTracker.Clear();
        var startResult = await controller.SetMemberStartDate(spouseId,
            new SetFamilyMemberStartDateRequest(new DateOnly(2026, 8, 20)), CancellationToken.None);
        startResult.Should().BeOfType<OkObjectResult>();

        db.ChangeTracker.Clear();
        var summaryResult = await controller.Summary(spouseId, CancellationToken.None);
        var summary = summaryResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.SelectedMemberId.Should().Be(spouseId);

        var spouse = summary.Members.Should().ContainSingle(m => m.MemberId == spouseId).Subject;
        spouse.AllergiesCompleted.Should().BeTrue();
        spouse.Selected.Select(x => x.Code).Should().BeEquivalentTo(["MILK"]);
        (spouse.DislikedFoods ?? []).Select(x => x.Code)
            .Should().BeEquivalentTo(["INGREDIENT:11111111-1111-1111-1111-111111111111"]);
        spouse.DietaryCompleted.Should().BeTrue();
        (spouse.DietaryPreferences ?? []).Select(x => x.Code).Should().BeEquivalentTo(["VEGETARIAN"]);
        spouse.HasNoAllergies.Should().BeFalse();
        spouse.HasNoDietaryRestriction.Should().BeFalse();
        spouse.DeliveryAddressId.Should().Be(address.Id);
        spouse.DeliveryAddress.Should().NotBeNull();
        spouse.DeliveryAddress!.AddressLine.Should().Be("Sharq");

        summary.SelectedMember.MemberId.Should().Be(spouseId);

        var schedule = summary.MemberSchedules.Should().NotBeNull().And.ContainSingle(s => s.MemberId == spouseId).Subject;
        var scheduleService = new SubscriptionScheduleService(Substitute.For<IDateTimeProvider>());
        var expectedDates = scheduleService.GetServiceDates(
            scheduleService.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 20), false), 6, false);
        schedule.PaymentDate.Should().Be(new DateOnly(2026, 8, 19));
        schedule.FirstDeliveryDate.Should().Be(expectedDates[0]);
        schedule.EndDate.Should().Be(expectedDates[^1]);
        schedule.DeliveryDaysCount.Should().Be(expectedDates.Count);
        schedule.MealsPerDay.Should().Be(bundle.MealsPerDay);
        schedule.TotalMeals.Should().Be(expectedDates.Count * bundle.MealsPerDay);
        schedule.Restaurants.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task SaveMemberAllergies_EmptyShotgunFields_PreserveSavedDislikesAndDietary()
    {
        await using var db = CreateDb();
        db.FoodPreferenceDefinitions.AddRange(
            new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true });
        await db.SaveChangesAsync();

        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);

        (await controller.SaveMemberDislikedFoods(spouseId,
                new SaveFamilyMemberDislikedFoodsRequest(IngredientCodes: ["INGREDIENT:11111111-1111-1111-1111-111111111111"]),
                CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        (await controller.SaveMemberDietary(spouseId,
                new SaveFamilyMemberDietaryRequest(DietaryPreferenceCodes: ["VEGETARIAN"]), CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();

        // Mobile wizard re-sends empty cross-field arrays while saving another step.
        (await controller.SaveMemberAllergies(spouseId,
                new SaveFamilyMemberAllergiesRequest(SelectedCodes: [], IngredientCodes: [], DietaryPreferenceCodes: []),
                CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();

        db.ChangeTracker.Clear();
        var dislikes = await controller.GetMemberDislikedFoods(spouseId, CancellationToken.None);
        dislikes.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDislikedFoodsDto>().Subject
            .Selected.Should().ContainSingle(x => x.Code == "INGREDIENT:11111111-1111-1111-1111-111111111111");

        var dietary = await controller.GetMemberDietary(spouseId, CancellationToken.None);
        var dietaryState = dietary.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDietaryDto>().Subject;
        dietaryState.Completed.Should().BeTrue();
        dietaryState.SelectedCodes.Should().BeEquivalentTo(["VEGETARIAN"]);
    }

    [Fact]
    public async Task SaveMemberSteps_EmptyAliasField_DoesNotShadowRealSelection()
    {
        await using var db = CreateDb();
        db.FoodPreferenceDefinitions.AddRange(
            new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true });
        await db.SaveChangesAsync();

        var (controller, spouseId, _) = await CreateLinkedSpouseAsync(db);

        (await controller.SaveMemberDietary(spouseId,
                new SaveFamilyMemberDietaryRequest(DietaryPreferenceCodes: [], SelectedCodes: ["VEGETARIAN"]), CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        db.ChangeTracker.Clear();
        var dietary = await controller.GetMemberDietary(spouseId, CancellationToken.None);
        dietary.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDietaryDto>().Subject
            .SelectedCodes.Should().BeEquivalentTo(["VEGETARIAN"]);

        (await controller.SaveMemberDislikedFoods(spouseId,
                new SaveFamilyMemberDislikedFoodsRequest(DislikedIngredients: [], IngredientCodes: ["INGREDIENT:22222222-2222-2222-2222-222222222222"]),
                CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        db.ChangeTracker.Clear();
        var dislikes = await controller.GetMemberDislikedFoods(spouseId, CancellationToken.None);
        dislikes.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyMemberDislikedFoodsDto>().Subject
            .Selected.Should().ContainSingle(x => x.Code == "INGREDIENT:22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public async Task SaveMemberAllergies_None_AllowsCheckoutReadinessWhenPackageMinimumIsMet()
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

        var added = await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);
        var spouseId = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject
            .Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;
        await controller.SetMemberPlan(spouseId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);
        var spouseUserId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = spouseUserId,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
        });
        var spouseAccess = await db.FamilyMemberAccesses
            .SingleAsync(access => access.FamilyMemberId == spouseId);
        spouseAccess.HasSeparateAccount = true;
        spouseAccess.LinkedCustomerUserId = spouseUserId;
        await db.SaveChangesAsync();

        await SeedSelectionAsync(db, managerMemberId, spouseId);
        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var summary = summaryResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.Members.Select(member => member.MemberId).Should().BeEquivalentTo([managerMemberId, spouseId]);
        summary.CheckoutBlockReason.Should().BeNull();
        summary.CanCheckout.Should().BeTrue();
    }

    [Fact]
    public async Task SaveMemberAllergies_PersistsCodesForLinkedMember()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition
        {
            Code = "PEANUTS",
            NameAr = "فول سوداني",
            NameEn = "Peanuts",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var save = await controller.SaveMemberAllergies(
            spouseId,
            new SaveFamilyMemberAllergiesRequest(
                [new FoodPreferenceItemDto("PEANUTS", null, null, null)]),
            CancellationToken.None);
        save.Should().BeOfType<OkObjectResult>();

        var stored = await db.CustomerAllergyPreferences
            .Where(x => x.CustomerUserId == linkedUserId && !x.IsDislike)
            .ToListAsync();
        stored.Should().ContainSingle(x => x.AllergenCode == "PEANUTS");

        var get = await controller.GetMemberAllergies(spouseId, CancellationToken.None);
        var dto = get.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyMemberAllergiesDto>().Subject;
        dto.Completed.Should().BeTrue();
        dto.HasNoAllergies.Should().BeFalse();
        dto.Selected.Should().ContainSingle(x => x.Code == "PEANUTS");
    }

    [Fact]
    public async Task SaveMemberAllergies_AcceptsSelectedAliasAndDislikedFoods()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition
        {
            Code = "MILK",
            NameAr = "لاكتوز",
            NameEn = "Lactose",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);
        var save = await controller.SaveMemberAllergies(
            spouseId,
            new SaveFamilyMemberAllergiesRequest(
                Allergies: null,
                Selected: [new FoodPreferenceItemDto("MILK", null, null, null)],
                DislikedFoods: [new FoodPreferenceItemDto("INGREDIENT:onion", "بصل", "Onion", null)]),
            CancellationToken.None);
        save.Should().BeOfType<OkObjectResult>();

        var current = await controller.Current(CancellationToken.None);
        var member = current.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject
            .Members.Should().ContainSingle(m => m.MemberId == spouseId).Subject;
        member.Selected.Should().ContainSingle(x => x.Code == "MILK" && x.NameEn == "Lactose");
        member.Allergies.Should().ContainSingle(x => x.Code == "MILK");
        member.DislikedFoods.Should().ContainSingle(x => x.Code == "INGREDIENT:onion" && x.NameEn == "Onion");

        var storedDislikes = await db.CustomerAllergyPreferences
            .Where(x => x.CustomerUserId == linkedUserId && x.IsDislike)
            .ToListAsync();
        storedDislikes.Should().ContainSingle(x => x.AllergenCode == "INGREDIENT:onion");
    }

    [Fact]
    public async Task SaveMemberAllergies_AcceptsSelectedCodesAndReturnsThemOnCurrent()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition
        {
            Code = "MILK",
            NameAr = "لاكتوز",
            NameEn = "Lactose",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);
        var request = JsonSerializer.Deserialize<SaveFamilyMemberAllergiesRequest>(
            """{"selectedCodes":["MILK"]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var save = await controller.SaveMemberAllergies(spouseId, request!, CancellationToken.None);
        save.Should().BeOfType<OkObjectResult>();

        var stored = await db.CustomerAllergyPreferences
            .Where(x => x.CustomerUserId == linkedUserId && !x.IsDislike)
            .ToListAsync();
        stored.Should().ContainSingle(x => x.AllergenCode == "MILK");

        var current = await controller.Current(CancellationToken.None);
        var member = current.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject
            .Members.Should().ContainSingle(m => m.MemberId == spouseId).Subject;
        member.SelectedCodes.Should().Equal("MILK");
        member.Selected.Should().ContainSingle(x => x.Code == "MILK");
    }

    [Fact]
    public async Task SaveMemberAllergies_ReusesSoftDeletedRowInsteadOfViolatingUniqueIndex()
    {
        await using var db = CreateDbWithSoftDelete();
        db.AllergenDefinitions.Add(new AllergenDefinition
        {
            Code = "MILK",
            NameAr = "لاكتوز",
            NameEn = "Lactose",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);
        db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = linkedUserId,
            AllergenCode = "MILK",
            AllergenNameAr = "لاكتوز",
            AllergenNameEn = "Lactose",
            IsDislike = false,
            IsDeleted = true,
            DeletedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var save = await controller.SaveMemberAllergies(
            spouseId,
            new SaveFamilyMemberAllergiesRequest(SelectedCodes: ["MILK"]),
            CancellationToken.None);

        save.Should().BeOfType<OkObjectResult>();
        var rows = await db.CustomerAllergyPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == linkedUserId && !x.IsDislike)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].AllergenCode.Should().Be("MILK");
        rows[0].IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SaveMemberDietary_ReusesSoftDeletedRowInsteadOfViolatingUniqueIndex()
    {
        await using var db = CreateDbWithSoftDelete();
        db.FoodPreferenceDefinitions.Add(new FoodPreferenceDefinition
        {
            Code = "VEGETARIAN",
            NameAr = "نباتي",
            NameEn = "Vegetarian",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);
        db.CustomerDietaryPreferences.Add(new CustomerDietaryPreference
        {
            CustomerUserId = linkedUserId,
            PreferenceCode = "VEGETARIAN",
            IsDeleted = true,
            DeletedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var save = await controller.SaveMemberDietary(
            spouseId,
            new SaveFamilyMemberDietaryRequest(["VEGETARIAN"]),
            CancellationToken.None);

        save.Should().BeOfType<OkObjectResult>();
        var rows = await db.CustomerDietaryPreferences.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == linkedUserId)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].PreferenceCode.Should().Be("VEGETARIAN");
        rows[0].IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SaveMemberDislikedFoods_PersistsForLinkedMember()
    {
        await using var db = CreateDb();
        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var save = await controller.SaveMemberDislikedFoods(
            spouseId,
            new SaveFamilyMemberDislikedFoodsRequest(IngredientCodes: ["INGREDIENT:garlic"]),
            CancellationToken.None);
        save.Should().BeOfType<OkObjectResult>();

        var get = await controller.GetMemberDislikedFoods(spouseId, CancellationToken.None);
        var dto = get.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyMemberDislikedFoodsDto>().Subject;
        dto.Selected.Should().ContainSingle(x => x.Code == "INGREDIENT:garlic");
        dto.EatsEverything.Should().BeFalse();

        (await db.CustomerAllergyPreferences.CountAsync(x => x.CustomerUserId == linkedUserId && x.IsDislike))
            .Should().Be(1);
    }

    [Fact]
    public async Task SaveMemberDislikedFoods_AutoPrefixesBareGuids()
    {
        // Mobile may send raw GUIDs without the INGREDIENT: prefix; backend must auto-prefix them.
        await using var db = CreateDb();
        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);

        var rawGuid = Guid.NewGuid().ToString();
        var save = await controller.SaveMemberDislikedFoods(
            spouseId,
            new SaveFamilyMemberDislikedFoodsRequest(IngredientCodes: [rawGuid]),
            CancellationToken.None);
        save.Should().BeOfType<OkObjectResult>("bare GUIDs should be accepted and auto-prefixed");

        var get = await controller.GetMemberDislikedFoods(spouseId, CancellationToken.None);
        var dto = get.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyMemberDislikedFoodsDto>().Subject;
        dto.Selected.Should().ContainSingle(x => x.Code == $"INGREDIENT:{rawGuid.ToLowerInvariant()}");
        dto.EatsEverything.Should().BeFalse();
    }

    [Fact]
    public async Task Current_ReturnsFullAddressAllergiesAndDietaryForLinkedMember()
    {
        await using var db = CreateDb();
        db.AllergenDefinitions.Add(new AllergenDefinition
        {
            Code = "PEANUTS",
            NameAr = "فول سوداني",
            NameEn = "Peanuts",
            IsActive = true,
            SortOrder = 1,
        });
        db.FoodPreferenceDefinitions.Add(new FoodPreferenceDefinition
        {
            Code = "VEGETARIAN",
            NameAr = "نباتي",
            NameEn = "Vegetarian",
            IsActive = true,
            SortOrder = 1,
        });
        await db.SaveChangesAsync();

        var (controller, spouseId, linkedUserId) = await CreateLinkedSpouseAsync(db);
        var created = await controller.CreateMemberAddress(
            spouseId,
            new CustomerAddressRequest(
                "المنزل",
                "حولي, السالمية, قطعة 4",
                null,
                29.3759m,
                47.9774m,
                null,
                true,
                Governorate: "حولي",
                Area: "السالمية",
                Block: "قطعة 4",
                Street: "شارع البحرين"),
            CancellationToken.None);
        var address = created.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<CustomerAddressDto>().Subject;

        var saveAllergies = await controller.SaveMemberAllergies(
            spouseId,
            new SaveFamilyMemberAllergiesRequest(
                [new FoodPreferenceItemDto("PEANUTS", null, null, null)]),
            CancellationToken.None);
        saveAllergies.Should().BeOfType<OkObjectResult>();

        db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
        {
            CustomerUserId = linkedUserId,
            AllergenCode = "INGREDIENT:onion",
            AllergenNameAr = "بصل",
            AllergenNameEn = "Onion",
            IsDislike = true,
        });
        db.CustomerDietaryPreferences.Add(new CustomerDietaryPreference
        {
            CustomerUserId = linkedUserId,
            PreferenceCode = "VEGETARIAN",
        });
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == linkedUserId);
        profile.DietaryPreferencesCompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var current = await controller.Current(CancellationToken.None);
        var screen = current.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = screen.Members.Should().ContainSingle(m => m.MemberId == spouseId).Subject;

        member.DeliveryAddressId.Should().Be(address.Id);
        member.DeliveryAddress.Should().NotBeNull();
        member.DeliveryAddress!.Id.Should().Be(address.Id);
        member.DeliveryAddress.Label.Should().Be("المنزل");
        member.DeliveryAddress.AddressLine.Should().Be("حولي, السالمية, قطعة 4");
        member.DeliveryAddress.Governorate.Should().Be("حولي");
        member.DeliveryAddress.Area.Should().Be("السالمية");
        member.DeliveryAddress.Block.Should().Be("قطعة 4");
        member.DeliveryAddress.Street.Should().Be("شارع البحرين");
        member.DeliveryAddress.Latitude.Should().Be(29.3759m);
        member.DeliveryAddress.Longitude.Should().Be(47.9774m);

        member.AllergiesCompleted.Should().BeTrue();
        member.HasNoAllergies.Should().BeFalse();
        member.Allergies.Should().ContainSingle(x => x.Code == "PEANUTS" && x.NameEn == "Peanuts");
        member.DislikedFoods.Should().ContainSingle(x => x.Code == "INGREDIENT:onion" && x.NameEn == "Onion");
        member.DietaryCompleted.Should().BeTrue();
        member.DietaryPreferences.Should().ContainSingle(x => x.Code == "VEGETARIAN" && x.IsSelected && x.NameEn == "Vegetarian");
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
        dto.SessionExpired.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrent_WhenQuoteTtlElapsed_ReturnsSessionExpiredAndSameRoster()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));
        var controller = CreateController(db, userId, users);

        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var original = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        original.SessionExpired.Should().BeFalse();

        var stored = await db.FamilyGroups.SingleAsync();
        await SeedSelectionAsync(db, stored.Members.Select(x => x.Id).ToArray());
        var expiredBatch = await db.FamilyCheckoutBatches.SingleAsync();
        expiredBatch.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20);
        stored.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20);
        stored.ModifiedAtUtc = DateTime.UtcNow.AddMinutes(-16);
        await db.SaveChangesAsync();

        var result = await controller.Current(CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.SessionExpired.Should().BeTrue();
        dto.SessionMessage.Should().Be("family.session_expired");
        dto.FamilyGroupId.Should().Be(original.FamilyGroupId);
        dto.Members.Select(x => x.MemberId).Should().BeEquivalentTo(original.Members.Select(x => x.MemberId));
        (await db.FamilyCheckoutBatches.SingleAsync()).Status.Should().Be(FamilyCheckoutBatchStatus.Expired);
        dto.Status.Should().Be(FamilyGroupStatus.Draft);
    }

    [Fact]
    public async Task SaveMemberAllergies_WhenQuoteTtlElapsed_PreservesRosterAndExpiresQuote()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));
        var controller = CreateController(db, userId, users);

        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var screen = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = screen.Members.Single(m => m.Role == FamilyMemberRole.Manager).MemberId;

        var stored = await db.FamilyGroups.SingleAsync();
        await SeedSelectionAsync(db, stored.Members.Select(x => x.Id).ToArray());
        var expiredBatch = await db.FamilyCheckoutBatches.SingleAsync();
        expiredBatch.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20);
        stored.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20);
        stored.ModifiedAtUtc = DateTime.UtcNow.AddMinutes(-16);
        await db.SaveChangesAsync();

        var result = await controller.SaveMemberAllergies(
            memberId,
            new SaveFamilyMemberAllergiesRequest(
                [new FoodPreferenceItemDto(FoodPreferenceNoneCodes.NoAllergies, null, null, null)]),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        (await db.FamilyGroups.SingleAsync()).Id.Should().Be(screen.FamilyGroupId);
        (await db.FamilyCheckoutBatches.SingleAsync()).Status.Should().Be(FamilyCheckoutBatchStatus.Expired);
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
        var sharedQueries = CreateSharedSubscriptionFlowQueryService(db);
        var controller = CreateController(db, userId, users, queryService: sharedQueries);

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
    public async Task FamilyCatalog_MatchesSharedIndividualCatalog_AndIncludesFamilyUnflaggedBundles()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        program.AvailableForFamily = false;
        bundle.AvailableForFamily = false;
        var additionalBundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE_SECOND",
            NameAr = "باقة ثانية",
            NameEn = "Second bundle",
            MealsPerDay = 1,
            BreakfastCount = 1,
            SortOrder = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = false,
        };
        db.Bundles.Add(additionalBundle);
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = userId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Paid,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 26),
            PaidAmount = new Money(120m, "KWD"),
        });
        await db.SaveChangesAsync();

        var sharedQueries = CreateSharedSubscriptionFlowQueryService(db);
        var familyFlow = new FamilySubscriptionFlowService(
            Substitute.For<ISender>(),
            db,
            Substitute.For<IPaymentGatewayService>(),
            Substitute.For<IFamilyCheckoutService>(),
            sharedQueries);

        var individualPrograms = await sharedQueries.GetProgramsAsync(userId, CancellationToken.None);
        var familyPrograms = await familyFlow.GetProgramsAsync(userId, CancellationToken.None);
        familyPrograms.Value.Should().BeEquivalentTo(individualPrograms.Value, options => options.WithStrictOrdering());

        var individualBundles = await sharedQueries.GetProgramBundlesAsync(program.Id, userId, CancellationToken.None);
        var familyController = CreateController(db, userId, queryService: sharedQueries);
        var familyBundlesResponse = await familyController.Bundles(program.Id, CancellationToken.None);
        var familyBundles = familyBundlesResponse.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<CustomerBundleCardDto>>().Subject;
        familyBundles.Should().BeEquivalentTo(
            individualBundles.Value.Bundles,
            options => options.WithStrictOrdering());
        familyBundles.Select(x => x.BundleId).Should().Contain([bundle.Id, additionalBundle.Id]);
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
        var catalogResult = await controller.Bundles(program.Id, CancellationToken.None);
        var catalogBundle = catalogResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<CustomerBundleCardDto>>().Subject
            .Should().ContainSingle(x => x.BundleId == bundle.Id).Subject;
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

    [Fact]
    public async Task DeleteMember_WhenActiveFamilyGroupAndMemberUnpaid_DeletesMemberSuccessfully()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Active,
        };
        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
            MemberSubscriptionId = Guid.NewGuid(),
        };
        group.Members.Add(managerMember);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, managerId, users);
        var added = await controller.AddMember(
            NewMemberWrite("سارة", Guid.Empty) with { DeliveryAddressId = null, Phone = "+96553333333" },
            CancellationToken.None);
        var addedDto = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = addedDto.Members.Single(m => m.Role == FamilyMemberRole.Member).MemberId;

        var deleteResult = await controller.DeleteMember(memberId, CancellationToken.None);
        var screen = deleteResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.Members.Should().HaveCount(1);
        screen.Members.Single().Role.Should().Be(FamilyMemberRole.Manager);
        (await db.FamilyMembers.AnyAsync(m => m.Id == memberId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteMember_WhenCheckoutPending_ReturnsConflict()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.CheckoutPending,
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "عضو",
            RelationshipCode = "child",
            Gender = FamilyMemberGender.Male,
            AgeYears = 10,
            NutritionalGoalCode = "maintain",
        };
        group.Members.Add(member);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, managerId, users);
        var deleteResult = await controller.DeleteMember(member.Id, CancellationToken.None);
        var conflict = deleteResult.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"]?.ToString().Should().Be("family.not_draft");
    }

    [Fact]
    public async Task Packages_ReturnedIdCanBeSelected_AndUnknownOrInactiveIdsAreRejected()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var settings = new FamilyPricingSettings
        {
            MaxMembers = 6,
            Packages =
            [
                new FamilySizePackageSettings { Id = activeId, NameAr = "باقة متاحة", NameEn = "Available", MinMembers = 2, MaxMembers = 6, IsActive = true },
                new FamilySizePackageSettings { Id = inactiveId, NameAr = "باقة موقوفة", NameEn = "Inactive", MinMembers = 2, MaxMembers = 6, IsActive = false },
            ],
        };
        var controller = CreateController(
            db,
            managerId,
            settings: new StubFamilyPricingSettingsReader(settings));

        var packagesResult = await controller.Packages(CancellationToken.None);
        var packages = packagesResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IEnumerable<FamilySizePackageDto>>().Subject;
        var selectedId = packages.Should().ContainSingle().Which.Id;
        selectedId.Should().Be(activeId);

        var selectedResult = await controller.UpsertGroup(
            new UpsertFamilyGroupRequest("عائلة أحمد", selectedId),
            CancellationToken.None);
        var selected = selectedResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject;
        selected.SelectedFamilyPackageId.Should().Be(selectedId);

        foreach (var unavailableId in new[] { Guid.NewGuid(), inactiveId })
        {
            var unavailable = await controller.UpsertGroup(
                new UpsertFamilyGroupRequest("اسم لا يجب حفظه", unavailableId),
                CancellationToken.None);
            var problem = unavailable.Should().BeOfType<ObjectResult>().Subject;
            problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            problem.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"]?.ToString()
                .Should().Be("family.package_not_found");
        }
    }

    [Fact]
    public async Task UpsertGroup_WhenPackageBecomesUnavailable_ReturnsNotFoundWithoutChangingDraft()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var settings = new FamilyPricingSettings
        {
            MaxMembers = 8,
            Packages =
            [new FamilySizePackageSettings { Id = Guid.NewGuid(), NameEn = "Available", MinMembers = 2, MaxMembers = 8, IsActive = true }],
        };
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Original name",
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = staleId,
        };
        group.Members.Add(new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "Manager",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            managerId,
            settings: new StubFamilyPricingSettingsReader(settings));
        var result = await controller.UpsertGroup(
            new UpsertFamilyGroupRequest("Must not be stored", staleId),
            CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"]?.ToString()
            .Should().Be("family.package_not_found");
        var stored = await db.FamilyGroups.AsNoTracking().SingleAsync(x => x.Id == group.Id);
        stored.Name.Should().Be("Original name");
        stored.SelectedFamilyPackageId.Should().Be(staleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PricingSettingsUpdate_CannotDeactivateOrRemovePackageUsedByOpenFamilyDrafts(bool removePackage)
    {
        await using var db = CreateDb();
        var current = SubscriptionPricingSettingsReader.Deserialize(null);
        var referencedPackage = current.Family.Packages[0];
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(current, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        db.FamilyGroups.AddRange(
            new FamilyGroup
            {
                ManagerCustomerUserId = Guid.NewGuid(),
                Name = "Draft family",
                Status = FamilyGroupStatus.Draft,
                SelectedFamilyPackageId = referencedPackage.Id,
            },
            new FamilyGroup
            {
                ManagerCustomerUserId = Guid.NewGuid(),
                Name = "Pending family",
                Status = FamilyGroupStatus.CheckoutPending,
                SelectedFamilyPackageId = referencedPackage.Id,
            });
        await db.SaveChangesAsync();

        var requested = SubscriptionPricingSettingsReader.Deserialize(
            JsonSerializer.Serialize(current, SubscriptionPricingSettingsDefaults.JsonOptions));
        if (removePackage)
            requested.Family.Packages.RemoveAll(x => x.Id == referencedPackage.Id);
        else
            requested.Family.Packages.Single(x => x.Id == referencedPackage.Id).IsActive = false;
        requested.Family.Packages.Any(x => x.Id == referencedPackage.Id && x.IsActive).Should().BeFalse();
        SubscriptionPricingSettingsReader.Normalize(requested).Family.Packages
            .Any(x => x.Id == referencedPackage.Id && x.IsActive).Should().BeFalse();
        SubscriptionPricingSettingsReader.Deserialize((await db.SystemSettings.AsNoTracking().SingleAsync()).ValueJson)
            .Family.Packages.Single(x => x.Id == referencedPackage.Id).IsActive.Should().BeTrue();
        SubscriptionPricingSettingsReader.Deserialize((await db.SystemSettings.AsNoTracking().SingleAsync()).ValueJson)
            .Family.Packages.Where(x => x.IsActive).Select(x => x.Id)
            .Except(SubscriptionPricingSettingsReader.Normalize(requested).Family.Packages.Where(x => x.IsActive).Select(x => x.Id))
            .Should().Contain(referencedPackage.Id);
        (await db.FamilyGroups.AsNoTracking().CountAsync(x => x.SelectedFamilyPackageId == referencedPackage.Id))
            .Should().Be(2);
        (await db.FamilyGroups.AsNoTracking().CountAsync(x =>
            x.SelectedFamilyPackageId == referencedPackage.Id
            && (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending)))
            .Should().Be(2);
        var service = new SubscriptionPricingSettingsService(db, Substitute.For<IAuditLogger>());

        var result = await service.UpdateSettingsAsync(requested);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.package_in_use");
        result.Error.Arguments.Should().ContainSingle().Which.Should().Be(2);
        var stored = await db.SystemSettings.AsNoTracking().SingleAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key);
        SubscriptionPricingSettingsReader.Deserialize(stored.ValueJson).Family.Packages
            .Should().Contain(x => x.Id == referencedPackage.Id && x.IsActive);
    }

    [Fact]
    public async Task DeleteMember_WhenActiveGroupAndMemberHasSubscription_ReturnsConflict()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Active,
        };
        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
        };
        var paidMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "سارة",
            RelationshipCode = "spouse",
            Gender = FamilyMemberGender.Female,
            AgeYears = 30,
            NutritionalGoalCode = "maintain",
            MemberSubscriptionId = Guid.NewGuid(),
        };
        group.Members.Add(managerMember);
        group.Members.Add(paidMember);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, managerId, users);
        var deleteResult = await controller.DeleteMember(paidMember.Id, CancellationToken.None);
        var conflict = deleteResult.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"]?.ToString().Should().Be("family.cannot_delete_active_member");
    }

    [Fact]
    public async Task UpdateMember_WhenActiveFamilyGroup_UpdatesMemberSuccessfully()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Active,
        };
        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
        };
        group.Members.Add(managerMember);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, managerId, users);
        var added = await controller.AddMember(
            NewMemberWrite("سارة", Guid.Empty) with { DeliveryAddressId = null, Phone = "+96553333333" },
            CancellationToken.None);
        var addedDto = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = addedDto.Members.Single(m => m.Role == FamilyMemberRole.Member).MemberId;

        var updateResult = await controller.UpdateMember(
            memberId,
            new FamilyMemberWriteDto("سارة المعدلة", "spouse", FamilyMemberGender.Female, 31, null, "+96553333333"),
            CancellationToken.None);

        var screen = updateResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.Members.Single(m => m.MemberId == memberId).DisplayName.Should().Be("سارة المعدلة");
        screen.Members.Single(m => m.MemberId == memberId).AgeYears.Should().Be(31);
    }

    [Fact]
    public async Task DurationsAndTiers_CanBeLoadedDirectlyByProgramIdWithoutBundleId()
    {
        var db = CreateDb();
        var managerId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);

        var queryService = Substitute.For<ISubscriptionFlowQueryService>();
        queryService.GetDurationsAsync(program.Id, null, managerId, false, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<CustomerDurationOptionDto>>([
                new CustomerDurationOptionDto(26, "26 يوم", "26 days", 10, true, null, true, false, 0, 26,
                    new CustomerFridayOptionDto("INCLUDE_FRIDAY", "الجمعة", "Friday", false, true),
                    new CustomerFridayAvailabilityDto(true, 1, "AVAILABLE", "متاح", "Available", "INFO"),
                    false, null, null)
            ]));

        var memberId = Guid.NewGuid();
        queryService.GetTiersAsync(program.Id, null, managerId, null, false, memberId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<CustomerTierOptionDto>>([
                new CustomerTierOptionDto(RestaurantTier.Basic, "بيسك", "Basic", null, "وصف", "Desc", 1, [], null, null, null, null, null, 5, true, false, null, null,
                    new CustomerFridayAvailabilityDto(true, 1, "AVAILABLE", "متاح", "Available", "INFO"))
            ]));

        var familyFlow = new FamilySubscriptionFlowService(
            Substitute.For<ISender>(), db, Substitute.For<IPaymentGatewayService>(),
            Substitute.For<IFamilyCheckoutService>(), queryService);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var controller = new CustomerFamilySubscriptionFlowController(currentUser, familyFlow);

        // 1. Fetch durations without bundleId
        var durationsResult = await controller.Durations(program.Id, null, false, CancellationToken.None);
        var durations = durationsResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<CustomerDurationOptionDto>>().Subject;
        durations.Should().ContainSingle(d => d.DurationDays == 26);

        // 2. Fetch tiers without bundleId and without durationDays
        var tiersResult = await controller.Tiers(program.Id, null, null, false, memberId, CancellationToken.None);
        var tiers = tiersResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<CustomerTierOptionDto>>().Subject;
        tiers.Should().ContainSingle(t => t.Tier == RestaurantTier.Basic);
    }

    [Fact]
    public async Task Tiers_RequiresExplicitMemberIdOnFamilyRoute()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var (program, _) = await SeedProgramBundleOfferAsync(db);
        var controller = CreateController(db, managerId);

        var result = await controller.Tiers(program.Id, null, null, false, null, CancellationToken.None);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"]?.ToString().Should().Be("family.member_required");
    }

    [Fact]
    public async Task DurationsAndTiers_SupportSpecificBundleIdAndDurationDays()
    {
        var db = CreateDb();
        var managerId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);

        var queryService = Substitute.For<ISubscriptionFlowQueryService>();
        queryService.GetDurationsAsync(program.Id, bundle.Id, managerId, true, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<CustomerDurationOptionDto>>([
                new CustomerDurationOptionDto(26, "26 يوم", "26 days", 10, true, null, true, true, 4, 30,
                    new CustomerFridayOptionDto("INCLUDE_FRIDAY", "الجمعة", "Friday", false, true),
                    new CustomerFridayAvailabilityDto(true, 1, "AVAILABLE", "متاح", "Available", "INFO"),
                    false, null, null)
            ]));

        var memberId = Guid.NewGuid();
        queryService.GetTiersAsync(program.Id, bundle.Id, managerId, 26, true, memberId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<CustomerTierOptionDto>>([
                new CustomerTierOptionDto(RestaurantTier.Platinum, "بلاتينيوم", "Platinum", null, "وصف", "Desc", 1, [], 26, 120m, 4m, null, "KWD", 8, true, true, 4, 30,
                    new CustomerFridayAvailabilityDto(true, 1, "AVAILABLE", "متاح", "Available", "INFO"))
            ]));

        var familyFlow = new FamilySubscriptionFlowService(
            Substitute.For<ISender>(), db, Substitute.For<IPaymentGatewayService>(),
            Substitute.For<IFamilyCheckoutService>(), queryService);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var controller = new CustomerFamilySubscriptionFlowController(currentUser, familyFlow);

        // 1. Fetch durations with bundleId
        var durationsResult = await controller.Durations(program.Id, bundle.Id, true, CancellationToken.None);
        var durations = durationsResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<CustomerDurationOptionDto>>().Subject;
        durations.Should().ContainSingle(d => d.DurationDays == 26 && d.IncludeFriday);

        // 2. Fetch tiers with bundleId and durationDays
        var tiersResult = await controller.Tiers(program.Id, bundle.Id, 26, true, memberId, CancellationToken.None);
        var tiers = tiersResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<CustomerTierOptionDto>>().Subject;
        tiers.Should().ContainSingle(t => t.Tier == RestaurantTier.Platinum && t.TotalPrice == 120m);
    }

    [Fact]
    public async Task UpsertGroup_WhenStatusIsCheckoutPending_ReturnsConflict_AndDoesNotMutateGroup()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var controller = CreateController(db, userId, users);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Original Name"), CancellationToken.None);

        var group = await db.FamilyGroups.SingleAsync(g => g.ManagerCustomerUserId == userId);
        group.QuotedFinalPrice = 123.45m;
        group.CurrencyCode = "KWD";
        group.PaymentInvoiceId = "invoice-locked";
        group.Status = FamilyGroupStatus.CheckoutPending;
        await db.SaveChangesAsync();

        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("Mutated Name"), CancellationToken.None);
        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"]?.ToString().Should().Be("family.not_draft");

        var reloaded = await db.FamilyGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        reloaded.Name.Should().Be("Original Name");
        reloaded.Status.Should().Be(FamilyGroupStatus.CheckoutPending);
        reloaded.QuotedFinalPrice.Should().Be(123.45m);
        reloaded.CurrencyCode.Should().Be("KWD");
        reloaded.PaymentInvoiceId.Should().Be("invoice-locked");
    }

    [Fact]
    public async Task GetCurrent_WhenPackageIsDeactivated_RetainsStoredPackageId_AndExposesPackageAvailableFalse()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var stalePackageId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = userId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = stalePackageId,
        };
        group.Members.Add(new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId, users);
        var currentResult = await controller.Current(CancellationToken.None);
        var current = currentResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        current.SelectedFamilyPackageId.Should().Be(stalePackageId);
        current.PackageAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task UpsertGroup_WithValidReplacement_RecoversStaleDraft()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var stalePackageId = Guid.NewGuid();
        var activePackageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = userId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = stalePackageId,
        };
        group.Members.Add(new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "maintain",
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId, users);
        var updatedResult = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد المحدثة", activePackageId), CancellationToken.None);
        var screen = updatedResult.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.SelectedFamilyPackageId.Should().Be(activePackageId);
        screen.PackageAvailable.Should().BeTrue();

        var reloaded = await db.FamilyGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id);
        reloaded.SelectedFamilyPackageId.Should().Be(activePackageId);
    }

    [Fact]
    public async Task AdminSettingsUpdate_WhenPackageReferencedByDraft_RejectsDeactivation()
    {
        await using var db = CreateDb();
        var activePackageId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "Draft Family",
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = activePackageId,
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var settingsService = new SubscriptionPricingSettingsService(db, audit);

        var doc = SubscriptionPricingSettingsDefaults.Create();
        var pkg = doc.Family.Packages.First(p => p.Id == activePackageId);
        pkg.IsActive = false;

        var result = await settingsService.UpdateSettingsAsync(doc, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.package_in_use");
    }

    [Fact]
    public async Task PackagesContract_GetPackages_SelectReturnedId_UpsertsGroupWithMatchingSelectedId()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(userId));

        var deactivatedId = Guid.NewGuid();
        var pricingSettings = new StubFamilyPricingSettingsReader(new FamilyPricingSettings
        {
            MaxMembers = 8,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    NameAr = "باقة صغيرة",
                    NameEn = "Small package",
                    MinMembers = 2,
                    MaxMembers = 3,
                    DiscountPercent = 0.08m,
                    IsActive = true,
                    SortOrder = 0,
                },
                new FamilySizePackageSettings
                {
                    Id = deactivatedId,
                    NameAr = "باقة معطلة",
                    NameEn = "Deactivated package",
                    MinMembers = 4,
                    MaxMembers = 5,
                    DiscountPercent = 0.10m,
                    IsActive = false,
                    SortOrder = 1,
                },
            ],
        });

        var controller = CreateController(db, userId, users, settings: pricingSettings);

        // 1. Get packages from endpoint
        var packagesResult = await controller.Packages(CancellationToken.None);
        var packages = packagesResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<FamilySizePackageDto>>().Subject;
        packages.Should().NotBeEmpty();
        packages.Should().NotContain(p => p.Id == deactivatedId);

        var chosenPackage = packages.First();

        // 2. Select returned ID on PUT /group -> succeeds and returns matching SelectedFamilyPackageId
        var upsertResult = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة الاختبار", chosenPackage.Id), CancellationToken.None);
        var screen = upsertResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<FamilyGroupScreenDto>().Subject;
        screen.SelectedFamilyPackageId.Should().Be(chosenPackage.Id);
        screen.PackageAvailable.Should().BeTrue();

        // 3. Unknown ID -> 400 BadRequest with family.package_not_found
        var unknownIdResult = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة الاختبار", Guid.NewGuid()), CancellationToken.None);
        var badRequestUnknown = unknownIdResult.Should().BeOfType<ObjectResult>().Subject;
        badRequestUnknown.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problemUnknown = badRequestUnknown.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemUnknown.Extensions["code"].Should().Be("family.package_not_found");

        // 4. Deactivated ID -> 400 BadRequest with family.package_not_found
        var deactivatedIdResult = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة الاختبار", deactivatedId), CancellationToken.None);
        var badRequestDeactivated = deactivatedIdResult.Should().BeOfType<ObjectResult>().Subject;
        badRequestDeactivated.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problemDeactivated = badRequestDeactivated.Value.Should().BeOfType<ProblemDetails>().Subject;
        problemDeactivated.Extensions["code"].Should().Be("family.package_not_found");
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
            new SubscriptionScheduleService(clock), db);
    }

    private static async Task<(CustomerFamilySubscriptionFlowController Controller, Guid SpouseId, Guid LinkedUserId)> CreateLinkedSpouseAsync(
        AppDbContext db, bool transactionalLocks = false)
    {
        var managerId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(ManagerAccount(managerId));
        users.FindByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var controller = CreateController(db, managerId, users, transactionalLocks: transactionalLocks);
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

    private static async Task SeedSelectionAsync(AppDbContext db, params Guid[] memberIds)
    {
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id, PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = DateTime.UtcNow,
            Status = FamilyCheckoutBatchStatus.Draft,
        };
        foreach (var id in memberIds)
            batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = id, CheckoutSelected = true });
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
    }

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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 6)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    public async Task Summary_RestaurantsUseDeliveryDatesAndIndividualRepetition(int count, int expectedLimit)
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress { UserId = userId, Label = "Home", AddressLine = "Home", RegionId = Guid.NewGuid() };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        var rows = Enumerable.Range(0, count).Select(i => new RestaurantEligibilityDto(
            Guid.NewGuid(), $"مطعم {i}", $"Restaurant {i}", true, [], [])).ToList();
        if (count > 0)
        {
            db.Documents.Add(new Document { OwnerType = DocumentOwnerType.Restaurant, OwnerId = rows[0].RestaurantId,
                DocumentType = DocumentType.ProfileImage, FileName = "logo.png", ContentType = "image/png",
                StoragePath = "uploads/restaurants/logo.png" });
            await db.SaveChangesAsync();
        }
        rows.Add(new RestaurantEligibilityDto(Guid.NewGuid(), "غير متاح", "Unavailable", false, ["capacity_unavailable"], []));
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(rows);
        var controller = CreateController(db, userId, restaurantEligibility: eligibility);
        await controller.UpsertGroup(new UpsertFamilyGroupRequest("Family"), CancellationToken.None);
        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var member = group.Members.Single();
        member.ProgramId = program.Id;
        member.BundleId = bundle.Id;
        member.SelectedTier = RestaurantTier.Basic;
        member.DurationDays = 6;
        member.StartDate = new DateOnly(2026, 10, 13);
        member.DeliveryAddressId = address.Id;
        await db.SaveChangesAsync();

        var result = await controller.Summary(null, CancellationToken.None);
        var summary = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        var schedule = summary.MemberSchedules!.Single();
        schedule.Restaurants.Should().HaveCount(count);
        if (count > 0)
        {
            schedule.Restaurants.Should().OnlyContain(x => x.OccurrenceCount == expectedLimit);
            var restaurant = schedule.Restaurants.Single(x => x.RestaurantId == rows[0].RestaurantId);
            restaurant.NameAr.Should().Be(rows[0].NameAr);
            restaurant.NameEn.Should().Be(rows[0].NameEn);
            restaurant.LogoUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Frestaurants%2Flogo.png");
            restaurant.Tier.Should().Be(RestaurantTier.Basic);
        }
        schedule.RestaurantRepetition.Should().Be(new RestaurantRepetitionSummaryDto(expectedLimit, count, 6));
        summary.Restaurants.Should().BeEquivalentTo(schedule.Restaurants);
        summary.RestaurantRepetition.Should().Be(schedule.RestaurantRepetition);
        summary.Availability.Should().Be(schedule.Availability);
        summary.Availability!.EligibleRestaurantCount.Should().Be(count);
        summary.Availability.Status.Should().Be(count > 0 ? "available" : "no_restaurants_for_selection");
        summary.Availability.FailureReasons.Should().ContainKey("capacity_unavailable").WhoseValue.Should().Be(1);
        await eligibility.Received(1).EvaluateAsync(Arg.Is<RestaurantEligibilityRequest>(r =>
            r.StartDate == schedule.FirstDeliveryDate && r.RegionId == address.RegionId
            && r.ServiceDates != null && r.ServiceDates.Count == schedule.DeliveryDaysCount
            && r.ServiceDates[0] == schedule.FirstDeliveryDate && r.ServiceDates[r.ServiceDates.Count - 1] == schedule.EndDate), Arg.Any<CancellationToken>());
    }

    private static CustomerFamilySubscriptionFlowController CreateController(
        AppDbContext db,
        Guid userId,
        IUserAccountService? users = null,
        IFamilyPricingSettingsReader? settings = null,
        ISubscriptionFlowQueryService? queryService = null,
        IRestaurantEligibilityService? restaurantEligibility = null,
        ICapacityHoldExpiryService? holdExpiry = null, bool transactionalLocks = false)
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
            new SubscriptionScheduleService(clock), db);
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 19));
        var addressBook = new CustomerAddressHandlers(
            db,
            currentUser,
            Substitute.For<IAuditLogger>(),
            new CustomerDeliveryAddressService(db, clock),
            clock);
        var handlers = new FamilyDraftHandlers(db, currentUser, resolvedUsers, pricingSettings, eligibility, quote, clock, addressBook,
            restaurantEligibility: restaurantEligibility, capacityHoldExpiry: holdExpiry,
            transactions: transactionalLocks ? new DirectTransactions() : null,
            locks: transactionalLocks ? Substitute.For<IApplicationLockManager>() : null);
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
            Substitute.For<IPaymentGatewayService>(),
            Substitute.For<IFamilyCheckoutService>(),
            configuration,
            CreateTestWebHostEnvironment(),
            queryService: queryService ?? CreateSharedSubscriptionFlowQueryService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private static SubscriptionFlowQueryService CreateSharedSubscriptionFlowQueryService(AppDbContext db, bool realPricing = false)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 30));
        var tiers = new RestaurantTierClassificationService(db, clock);
        return new SubscriptionFlowQueryService(
            db,
            Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<IMenuPlanResolutionService>(),
            Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IBundleSlotRequirementService>(),
            Substitute.For<IPaymentGatewayService>(),
            new AllowedSubscriptionDurationService(db),
            tiers,
            Substitute.For<ISubscriptionCheckoutReadinessService>(),
            Substitute.For<ICustomerIndividualSubscriptionGate>(),
            Substitute.For<IRestaurantDiversificationCalculator>(),
            realPricing
                ? new CatalogQuerySender(new PricingAndClassificationHandlers(db, Substitute.For<IAuditLogger>(), clock, tiers))
                : Substitute.For<ISender>());
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static AppDbContext CreateDbWithSoftDelete()
    {
        var interceptor = new AuditableEntityInterceptor(
            Substitute.For<ICurrentUser>(),
            Substitute.For<IDateTimeProvider>());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

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

    private sealed class DirectTransactions : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
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
                GetFamilyMemberAllergiesQuery q => await handlers.Handle(q, cancellationToken),
                SaveFamilyMemberDislikedFoodsCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyMemberDislikedFoodsQuery q => await handlers.Handle(q, cancellationToken),
                SaveFamilyMemberDietaryCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyMemberDietaryQuery q => await handlers.Handle(q, cancellationToken),
                CreateFamilyMemberAddressCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyMemberAddressesQuery q => await handlers.Handle(q, cancellationToken),
                UpdateFamilyMemberAddressCommand c => await handlers.Handle(c, cancellationToken),
                PreviewFamilyMemberAddressCoverageCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberDefaultAddressCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberPlanCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberStartDateCommand c => await handlers.Handle(c, cancellationToken),
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
