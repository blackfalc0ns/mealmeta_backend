using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyParticipationContextTests
{
    [Fact]
    public async Task Deferred_roster_member_occupies_a_seat_without_a_price_or_qualification()
    {
        await using var db = CreateDb();
        var group = CreateGroup(managerExempt: true);
        group.SelectedFamilyPackageId = Guid.NewGuid();
        var managerSubscription = CreateSubscription(group.ManagerCustomerUserId, SubscriptionStatus.Expired, new DateOnly(2026, 10, 12));
        var manager = AddMember(group, FamilyMemberRole.Manager, managerSubscription);
        var selected = AddMember(group, FamilyMemberRole.Member);
        AddMember(group, FamilyMemberRole.Member);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(managerSubscription);
        await db.SaveChangesAsync();

        var context = await CreateReader(db).GetAsync(group, new HashSet<Guid> { selected.Id }, default);

        context.OccupiedSeats.Should().Be(2);
        context.DeferredMemberCount.Should().Be(1);
        context.CurrentPurchaseCount.Should().Be(1);
        context.QualifyingParticipantCount.Should().Be(1);
        context.ManagerIndividualIsLive.Should().BeFalse();
        manager.MemberSubscriptionId.Should().Be(managerSubscription.Id);
    }

    [Fact]
    public async Task Live_retained_manager_individual_qualifies_without_occupying_a_family_seat()
    {
        await using var db = CreateDb();
        var group = CreateGroup(managerExempt: true);
        group.SelectedFamilyPackageId = Guid.NewGuid();
        var managerSubscription = CreateSubscription(group.ManagerCustomerUserId, SubscriptionStatus.Paid, new DateOnly(2026, 10, 30));
        AddMember(group, FamilyMemberRole.Manager, managerSubscription);
        var selected = AddMember(group, FamilyMemberRole.Member);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(managerSubscription);
        await db.SaveChangesAsync();

        var context = await CreateReader(db).GetAsync(group, new HashSet<Guid> { selected.Id }, default);

        context.OccupiedSeats.Should().Be(1);
        context.QualifyingParticipantCount.Should().Be(2);
        context.ManagerSeatExempt.Should().BeTrue();
        context.ManagerIndividualIsLive.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_converted_manager_pointer_DoesNotQualifyAfterItsServiceExpires()
    {
        await using var db = CreateDb();
        var group = CreateGroup(managerExempt: false);
        group.ManagerIndividualSeatExempt = null;
        group.SelectedFamilyPackageId = Guid.NewGuid();
        var managerSubscription = CreateSubscription(group.ManagerCustomerUserId, SubscriptionStatus.Expired, new DateOnly(2026, 10, 12));
        managerSubscription.FamilyGroupId = group.Id;
        var manager = AddMember(group, FamilyMemberRole.Manager, managerSubscription);
        managerSubscription.FamilyMemberId = manager.Id;
        group.ConvertedFromSubscriptionId = managerSubscription.Id;
        var selected = AddMember(group, FamilyMemberRole.Member);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(managerSubscription);
        await db.SaveChangesAsync();

        var context = await CreateReader(db).GetAsync(group, new HashSet<Guid> { selected.Id }, default);

        context.ManagerSeatExempt.Should().BeTrue();
        context.ManagerIndividualIsLive.Should().BeFalse();
        context.OccupiedSeats.Should().Be(1);
        context.QualifyingParticipantCount.Should().Be(1);
    }

    private static FamilyGroup CreateGroup(bool managerExempt) => new()
    {
        ManagerCustomerUserId = Guid.NewGuid(),
        Name = "Family",
        Status = FamilyGroupStatus.Draft,
        ManagerIndividualSeatExempt = managerExempt,
    };

    private static FamilyMember AddMember(FamilyGroup group, FamilyMemberRole role, Subscription? subscription = null)
    {
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = role,
            DisplayName = role.ToString(),
            RelationshipCode = role == FamilyMemberRole.Manager ? "self" : "family",
            Gender = FamilyMemberGender.Male,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            ProgramId = role == FamilyMemberRole.Manager ? null : Guid.NewGuid(),
            BundleId = role == FamilyMemberRole.Manager ? null : Guid.NewGuid(),
            SelectedTier = role == FamilyMemberRole.Manager ? null : RestaurantTier.Basic,
            DurationDays = role == FamilyMemberRole.Manager ? null : 18,
            MemberSubscriptionId = subscription?.Id,
            MemberSubscription = subscription,
        };
        group.Members.Add(member);
        return member;
    }

    private static Subscription CreateSubscription(Guid customerId, SubscriptionStatus status, DateOnly endDate) => new()
    {
        CustomerUserId = customerId,
        ProgramId = Guid.NewGuid(),
        BundleId = Guid.NewGuid(),
        SelectedTier = RestaurantTier.Basic,
        DurationDays = 18,
        StartDate = new DateOnly(2026, 10, 1),
        EndDate = endDate,
        Status = status,
        PaidAmount = new Money(100m, "KWD"),
    };

    private static FamilyParticipationContextReader CreateReader(AppDbContext db)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 13));
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings
        {
            Packages = [new FamilySizePackageSettings { Id = db.FamilyGroups.Local.LastOrDefault()?.SelectedFamilyPackageId ?? Guid.Empty, MaxMembers = 5, IsActive = true }],
        });
        return new FamilyParticipationContextReader(db, clock, settings);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
