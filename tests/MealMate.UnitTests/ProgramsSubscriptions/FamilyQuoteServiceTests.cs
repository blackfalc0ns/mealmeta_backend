using FluentAssertions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyQuoteServiceTests
{
    [Fact]
    public async Task Quote_Empty_selection_uses_persisted_family_currency()
    {
        var sender = Substitute.For<ISender>();
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings());
        var sut = new FamilyQuoteService(sender, settings, Substitute.For<ISubscriptionScheduleService>());
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft, CurrencyCode = "USD" };

        var result = await sut.QuoteAsync(group, CancellationToken.None,
            new FamilyQuoteScope(new HashSet<Guid>(), new HashSet<Guid>()));

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrencyCode.Should().Be("USD");
        await sender.DidNotReceive().Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quote_Rejects_mixed_currency_preview_results_before_summing()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<PreviewCustomerPricingQuery>();
                var currency = query.DurationDays == 26 ? "KWD" : "USD";
                return Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                    1, query.DurationDays, 0, 100m, 0, 0, 0, 100m, 0, 100m, 0, 0, 0, false, currency)));
            });
        var package = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 1, MaxMembers = 4, IsActive = true };
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
        {
            Packages = [package]
        });
        var sut = new FamilyQuoteService(sender, settings, Substitute.For<ISubscriptionScheduleService>());
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft, SelectedFamilyPackageId = package.Id };
        group.Members.Add(ReadyMember("KWD", 26));
        group.Members.Add(ReadyMember("USD", 12));
        var ids = group.Members.Select(x => x.Id).ToHashSet();

        var result = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(ids, ids));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.currency_mismatch");
    }

    [Fact]
    public void PackagePolicy_DraftUsesSelectedPackageEvenWhenCountMatchesAnotherPackage()
    {
        var small = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 2, MaxMembers = 3, IsActive = true };
        var medium = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 4, MaxMembers = 5, IsActive = true };
        var settings = new FamilyPricingSettings { Packages = [small, medium] };
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft, SelectedFamilyPackageId = medium.Id };

        var result = FamilyPackagePolicy.Resolve(settings, group, eligiblePaidCount: 0, payableCount: 2);

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectivePackage.Id.Should().Be(medium.Id);
        result.Value.ParticipantCount.Should().Be(2);
        result.Value.PackageSelectionMode.Should().Be("selected");
    }

    [Fact]
    public void PackagePolicy_DraftRejectsInactiveSelectedPackage()
    {
        var package = new FamilySizePackageSettings { Id = Guid.NewGuid(), IsActive = false };
        var active = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 2, MaxMembers = 4, IsActive = true };
        var settings = new FamilyPricingSettings { Packages = [package, active] };
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft, SelectedFamilyPackageId = package.Id };

        var result = FamilyPackagePolicy.Resolve(settings, group, 0, 2);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.package_not_found");
    }

    [Fact]
    public void PackagePolicy_ActiveGroupKeepsSelectedPackageForCurrentPurchase()
    {
        var small = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 2, MaxMembers = 3, IsActive = true };
        var medium = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 4, MaxMembers = 5, IsActive = true };
        var settings = new FamilyPricingSettings { Packages = [small, medium] };
        var group = new FamilyGroup { Status = FamilyGroupStatus.Active, SelectedFamilyPackageId = small.Id };

        var result = FamilyPackagePolicy.Resolve(settings, group, eligiblePaidCount: 3, payableCount: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectivePackage.Id.Should().Be(small.Id);
        result.Value.ParticipantCount.Should().Be(4);
        result.Value.PackageSelectionMode.Should().Be("snapshot");
    }

    [Fact]
    public void PackagePolicy_ActiveGroupWithoutSelectedPackageRequiresSnapshot()
    {
        var small = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 2, MaxMembers = 3, IsActive = true };
        var large = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 5, MaxMembers = 6, IsActive = true };
        var settings = new FamilyPricingSettings { Packages = [small, large] };
        var group = new FamilyGroup { Status = FamilyGroupStatus.Active };

        var result = FamilyPackagePolicy.Resolve(settings, group, eligiblePaidCount: 3, payableCount: 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.package_required");
    }

    [Fact]
    public async Task Quote_DraftUsesSelectedPackageAndExposesEffectivePackageDetails()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 100m, 0, 0, 0, 100m, 0, 100m, 0, 0, 0, false))));
        var small = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 2, MaxMembers = 3, DiscountPercent = 0.08m, IsActive = true };
        var medium = new FamilySizePackageSettings { Id = Guid.NewGuid(), MinMembers = 4, MaxMembers = 5, DiscountPercent = 0.10m, IsActive = true };
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings { Packages = [small, medium] });
        var sut = new FamilyQuoteService(sender, settings, Substitute.For<ISubscriptionScheduleService>());
        var group = new FamilyGroup
        {
            Name = "Selected medium",
            Status = FamilyGroupStatus.Draft,
            SelectedFamilyPackageId = medium.Id,
        };
        group.Members.Add(ReadyMember("A", 26));
        group.Members.Add(ReadyMember("B", 26));

        var result = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));

        result.IsSuccess.Should().BeTrue();
        result.Value.EffectiveFamilyPackageId.Should().Be(medium.Id);
        result.Value.EffectiveFamilyPackageMinMembers.Should().Be(4);
        result.Value.EffectiveFamilyPackageMaxMembers.Should().Be(5);
        result.Value.ParticipantCount.Should().Be(2);
        result.Value.PackageSelectionMode.Should().Be("selected");
        result.Value.FamilyDiscountAmount.Should().Be(20m);
        result.Value.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public void CheckoutGate_DraftBelowPackageMinimumBlocksCheckout()
    {
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft };
        var member = ReadyMember("A", 26);
        var quote = new FamilyQuoteResult(
            [new FamilyQuoteLine(member.Id, member.DisplayName, 100m, 0m, 0m, 0m)],
            100m, 0m, 0m, 100m, "KWD", false,
            Guid.NewGuid(), 2, 3, 1, "selected");

        var result = FamilyCheckoutGate.Evaluate(group, [member], quote);

        result.CanCheckout.Should().BeFalse();
        result.BlockReason.Should().Be("family.min_members");
    }

    [Fact]
    public void CheckoutGate_DraftAbovePackageMaximumBlocksCheckout()
    {
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft };
        var member = ReadyMember("A", 26);
        var quote = new FamilyQuoteResult(
            [new FamilyQuoteLine(member.Id, member.DisplayName, 100m, 0m, 0m, 0m)],
            100m, 0m, 0m, 100m, "KWD", false,
            Guid.NewGuid(), 2, 3, 4, "selected");

        var result = FamilyCheckoutGate.Evaluate(group, [member], quote);

        result.CanCheckout.Should().BeFalse();
        result.BlockReason.Should().Be("family.max_members");
    }

    [Fact]
    public void CheckoutGate_ConvertedManagerWithoutNewPayableMemberIsBlocked()
    {
        var group = new FamilyGroup { Status = FamilyGroupStatus.Draft, ConvertedFromSubscriptionId = Guid.NewGuid() };
        var manager = ReadyMember("Manager", 26);
        manager.Role = FamilyMemberRole.Manager;
        manager.MemberSubscriptionId = group.ConvertedFromSubscriptionId;
        var quote = new FamilyQuoteResult([], 0m, 0m, 0m, 0m, "KWD", false,
            Guid.NewGuid(), 2, 3, 1, "selected");

        var result = FamilyCheckoutGate.Evaluate(group, [manager], quote);

        result.CanCheckout.Should().BeFalse();
        result.BlockReason.Should().Be("family.checkout_no_selection");
    }

    [Fact]
    public async Task Quote_SumsMemberPrices_ThenAppliesFamilyPercent()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var q = ci.Arg<PreviewCustomerPricingQuery>();
                var price = q.DurationDays == 26 ? 200m : 100m;
                return Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                    1, q.DurationDays, 0.15m, price, 0, 0, 0, price, 0, price, 0, 0, 0, false)));
            });

        var pricingSettings = new FamilyPricingSettings
        {
            DiscountPercent = 0.10m,
            MaxMembers = 4,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.NewGuid(),
                    NameAr = "باقة",
                    NameEn = "Package",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        };
        var settings = new TestFamilySettingsReader(pricingSettings);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.GetFridayAddonDays(Arg.Any<int>()).Returns(ci => Math.Max(1, ci.Arg<int>() / 6));
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Test", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Draft, SelectedFamilyPackageId = pricingSettings.Packages[0].Id };
        group.Members.Add(ReadyMember("A", 26));
        group.Members.Add(ReadyMember("B", 12));

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.Subtotal.Should().Be(300m);
        quote.FamilyDiscountAmount.Should().Be(30m);
        quote.FinalPrice.Should().Be(270m);
        quote.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_SingleMember_DoesNotApplyDiscount()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 200m, 0, 0, 0, 200m, 0, 200m, 0, 0, 0, false))));
        var pricingSettings = new FamilyPricingSettings { DiscountPercent = 0.10m, MinMembersForDiscount = 2, MaxMembers = 4 };
        var settings = new TestFamilySettingsReader(pricingSettings);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Solo", ManagerCustomerUserId = Guid.NewGuid(), SelectedFamilyPackageId = pricingSettings.Packages[0].Id };
        group.Members.Add(ReadyMember("A", 26));
        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.FamilyDiscountAmount.Should().Be(0m);
        quote.FinalPrice.Should().Be(200m);
        quote.DiscountApplied.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_UnpaidExtraRosterMember_DoesNotInflateDiscountCount()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 200m, 0, 0, 0, 200m, 0, 200m, 0, 0, 0, false))));
        var pricingSettings = new FamilyPricingSettings
        {
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            MaxMembers = 4,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.NewGuid(),
                    NameAr = "ع",
                    NameEn = "P",
                    MinMembers = 3,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        };
        var settings = new TestFamilySettingsReader(pricingSettings);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Roster", ManagerCustomerUserId = Guid.NewGuid(), SelectedFamilyPackageId = pricingSettings.Packages[0].Id };
        group.Members.Add(ReadyMember("A", 26));
        group.Members.Add(ReadyMember("B", 26));
        group.Members.Add(new FamilyMember { DisplayName = "C", SetupStatus = FamilyMemberSetupStatus.NotStarted });

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.Subtotal.Should().Be(400m);
        quote.FamilyDiscountAmount.Should().Be(40m);
        quote.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_LaterAddon_UsesActivePackageDiscountSnapshot()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 200m, 0, 0, 0, 200m, 0, 200m, 0, 0, 0, false))));
        var packageId = Guid.NewGuid();
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
        {
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = packageId,
                    NameAr = "ع",
                    NameEn = "P",
                    MinMembers = 4,
                    MaxMembers = 6,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Active", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Active,
            SelectedFamilyPackageId = packageId, DiscountPercentSnapshot = 0.10m };
        var paidA = ReadyMember("A", 26); paidA.MemberSubscriptionId = Guid.NewGuid(); paidA.MemberSubscription = CreateActiveSubscription(paidA.MemberSubscriptionId.Value);
        var paidB = ReadyMember("B", 26); paidB.MemberSubscriptionId = Guid.NewGuid(); paidB.MemberSubscription = CreateActiveSubscription(paidB.MemberSubscriptionId.Value);
        var paidC = ReadyMember("C", 26); paidC.MemberSubscriptionId = Guid.NewGuid(); paidC.MemberSubscription = CreateActiveSubscription(paidC.MemberSubscriptionId.Value);
        group.Members.Add(paidA);
        group.Members.Add(paidB);
        group.Members.Add(paidC);
        group.Members.Add(ReadyMember("D", 26));

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.Subtotal.Should().Be(200m);
        quote.FamilyDiscountAmount.Should().Be(20m);
        quote.FinalPrice.Should().Be(180m);
        quote.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_InProgressMemberWithFullPlan_IsPriced()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 150m, 0, 0, 0, 150m, 0, 150m, 0, 0, 0, false))));
        var pricingSettings = new FamilyPricingSettings { DiscountPercent = 0.10m, MinMembersForDiscount = 2, MaxMembers = 4 };
        var settings = new TestFamilySettingsReader(pricingSettings);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Draft", ManagerCustomerUserId = Guid.NewGuid(), SelectedFamilyPackageId = pricingSettings.Packages[0].Id };
        group.Members.Add(new FamilyMember
        {
            DisplayName = "No address yet",
            SetupStatus = FamilyMemberSetupStatus.InProgress,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            IncludeFriday = false,
            DeliveryAddressId = null,
            AgeYears = 0,
        });

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.Subtotal.Should().Be(150m);
        quote.FinalPrice.Should().Be(150m);
        quote.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task Quote_ConvertedManager_IsZero_AndDiscountOnlyOnPayableMembers()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var q = ci.Arg<PreviewCustomerPricingQuery>();
                var price = q.DurationDays == 26 ? 200m : 100m;
                return Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                    1, q.DurationDays, 0.15m, price, 0, 0, 0, price, 0, price, 0, 0, 0, false)));
            });

        var packageId = Guid.NewGuid();
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
        {
            DiscountPercent = 0.10m,
            MaxMembers = 4,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = packageId,
                    NameAr = "ع",
                    NameEn = "P",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.GetFridayAddonDays(Arg.Any<int>()).Returns(0);
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var convertedSubId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            Name = "Converted",
            ManagerCustomerUserId = Guid.NewGuid(),
            Status = FamilyGroupStatus.Draft,
            ConvertedFromSubscriptionId = convertedSubId,
            SelectedFamilyPackageId = packageId,
        };
        var manager = ReadyMember("Manager", 26);
        manager.Role = FamilyMemberRole.Manager;
        manager.MemberSubscriptionId = convertedSubId;
        manager.MemberSubscription = CreateActiveSubscription(convertedSubId);
        group.Members.Add(manager);
        group.Members.Add(ReadyMember("Spouse", 12));

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        var quote = quoted.Value;
        quote.Lines.Should().ContainSingle();
        quote.Lines.Should().NotContain(x => x.MemberId == manager.Id);
        quote.Subtotal.Should().Be(100m);
        quote.FamilyDiscountAmount.Should().Be(10m);
        quote.FinalPrice.Should().Be(90m);
        quote.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_WhenPricingPreviewFails_ReturnsPricingError()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<PricingEngineResult>(
                Error.Validation("pricing.unavailable", "No active restaurant pricing data is available for this program and bundle."))));
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings { DiscountPercent = 0.10m, MinMembersForDiscount = 2, MaxMembers = 4 });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Draft", ManagerCustomerUserId = Guid.NewGuid() };
        group.Members.Add(ReadyMember("A", 26));

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));

        quoted.IsFailure.Should().BeTrue();
        quoted.Error.Code.Should().Be("pricing.unavailable");
    }

    [Fact]
    public void ResolveSetupStatus_ReadyOnlyWhenPlanComplete()
    {
        var incomplete = new FamilyMember { ProgramId = Guid.NewGuid() };
        FamilyMemberPlanRules.ResolveSetupStatus(incomplete).Should().Be(FamilyMemberSetupStatus.InProgress);
        var ready = new FamilyMember
        {
            DisplayName = "Ready Member",
            AgeYears = 30,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            DeliveryAddressId = Guid.NewGuid(),
        };
        FamilyMemberPlanRules.ResolveSetupStatus(ready).Should().Be(FamilyMemberSetupStatus.Ready);
    }

    [Fact]
    public async Task Quote_ConvertedManager_NotCountedTwiceInDiscountCount()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var q = ci.Arg<PreviewCustomerPricingQuery>();
                return Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                    1, q.DurationDays, 0.15m, 100m, 0, 0, 0, 100m, 0, 100m, 0, 0, 0, false)));
            });

        var firstPackageId = Guid.NewGuid();
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
        {
            Packages = new List<FamilySizePackageSettings>
            {
                new() { Id = firstPackageId, MinMembers = 2, MaxMembers = 3, DiscountPercent = 0.08m, IsActive = true },
                new() { Id = Guid.NewGuid(), MinMembers = 4, MaxMembers = 5, DiscountPercent = 0.10m, IsActive = true }
            }
        });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);

        var convertedSubId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            Name = "Converted",
            ManagerCustomerUserId = Guid.NewGuid(),
            Status = FamilyGroupStatus.Draft,
            ConvertedFromSubscriptionId = convertedSubId,
            SelectedFamilyPackageId = firstPackageId
        };
        var manager = ReadyMember("Manager", 26);
        manager.Role = FamilyMemberRole.Manager;
        manager.MemberSubscriptionId = convertedSubId;
        manager.MemberSubscription = CreateActiveSubscription(convertedSubId);
        group.Members.Add(manager);

        group.Members.Add(ReadyMember("Member 1", 26));
        group.Members.Add(ReadyMember("Member 2", 26));

        var quoted = await sut.QuoteAsync(group, CancellationToken.None, new FamilyQuoteScope(group.Members.Select(x => x.Id).ToHashSet(), group.Members.Select(x => x.Id).ToHashSet()));
        quoted.IsSuccess.Should().BeTrue();
        quoted.Value.FamilyDiscountPercent.Should().Be(0.08m);
        quoted.Value.Subtotal.Should().Be(200m);
        quoted.Value.FamilyDiscountAmount.Should().Be(16m);
        quoted.Value.FinalPrice.Should().Be(184m);
    }

    private static Subscription CreateActiveSubscription(Guid id)
    {
        var sub = new Subscription { Status = SubscriptionStatus.Active };
        typeof(MealMate.Domain.Common.BaseEntity).GetProperty("Id")!.SetValue(sub, id);
        return sub;
    }

    private static FamilyMember ReadyMember(string name, int days) => new()
    {
        DisplayName = name,
        RelationshipCode = "self",
        NutritionalGoalCode = "balanced",
        Gender = FamilyMemberGender.Male,
        AgeYears = 20,
        ActivityLevel = FamilyActivityLevel.Moderate,
        SetupStatus = FamilyMemberSetupStatus.Ready,
        ProgramId = Guid.NewGuid(),
        BundleId = Guid.NewGuid(),
        SelectedTier = RestaurantTier.Basic,
        DurationDays = days,
        IncludeFriday = false,
    };

    private sealed class TestFamilySettingsReader : IFamilyPricingSettingsReader
    {
        private readonly FamilyPricingSettings _settings;

        public TestFamilySettingsReader(FamilyPricingSettings settings) => _settings = settings;

        public Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);
    }
}
