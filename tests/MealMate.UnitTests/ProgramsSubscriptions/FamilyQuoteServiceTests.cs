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
