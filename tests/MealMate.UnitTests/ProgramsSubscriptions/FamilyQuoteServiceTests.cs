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

        var settings = new TestFamilySettingsReader(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.GetFridayAddonDays(Arg.Any<int>()).Returns(ci => Math.Max(1, ci.Arg<int>() / 6));
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Test", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Draft };
        group.Members.Add(ReadyMember("A", 26));
        group.Members.Add(ReadyMember("B", 12));

        var quote = await sut.QuoteAsync(group, CancellationToken.None);
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
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings { DiscountPercent = 0.10m, MinMembersForDiscount = 2, MaxMembers = 4 });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Solo", ManagerCustomerUserId = Guid.NewGuid() };
        group.Members.Add(ReadyMember("A", 26));
        var quote = await sut.QuoteAsync(group, CancellationToken.None);
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
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
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
        });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Roster", ManagerCustomerUserId = Guid.NewGuid() };
        group.Members.Add(ReadyMember("A", 26));
        group.Members.Add(ReadyMember("B", 26));
        group.Members.Add(new FamilyMember { DisplayName = "C", SetupStatus = FamilyMemberSetupStatus.NotStarted });

        var quote = await sut.QuoteAsync(group, CancellationToken.None);
        quote.Subtotal.Should().Be(400m);
        quote.FamilyDiscountAmount.Should().Be(0m);
        quote.DiscountApplied.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_LaterAddon_UsesActivePlusNewCountForPackage()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                1, 26, 0.15m, 200m, 0, 0, 0, 200m, 0, 200m, 0, 0, 0, false))));
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings
        {
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.NewGuid(),
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
        var group = new FamilyGroup { Name = "Active", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Active };
        var paidA = ReadyMember("A", 26); paidA.MemberSubscriptionId = Guid.NewGuid();
        var paidB = ReadyMember("B", 26); paidB.MemberSubscriptionId = Guid.NewGuid();
        var paidC = ReadyMember("C", 26); paidC.MemberSubscriptionId = Guid.NewGuid();
        group.Members.Add(paidA);
        group.Members.Add(paidB);
        group.Members.Add(paidC);
        group.Members.Add(ReadyMember("D", 26));

        var quote = await sut.QuoteAsync(group, CancellationToken.None);
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
        var settings = new TestFamilySettingsReader(new FamilyPricingSettings { DiscountPercent = 0.10m, MinMembersForDiscount = 2, MaxMembers = 4 });
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var sut = new FamilyQuoteService(sender, settings, schedule);
        var group = new FamilyGroup { Name = "Draft", ManagerCustomerUserId = Guid.NewGuid() };
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

        var quote = await sut.QuoteAsync(group, CancellationToken.None);
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
        group.Members.Add(manager);
        group.Members.Add(ReadyMember("Spouse", 12));

        var quote = await sut.QuoteAsync(group, CancellationToken.None);
        quote.Lines.Should().HaveCount(2);
        quote.Lines.Single(x => x.MemberId == manager.Id).MemberPrice.Should().Be(0m);
        quote.Subtotal.Should().Be(100m);
        quote.FamilyDiscountAmount.Should().Be(10m);
        quote.FinalPrice.Should().Be(90m);
        quote.DiscountApplied.Should().BeTrue();
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
