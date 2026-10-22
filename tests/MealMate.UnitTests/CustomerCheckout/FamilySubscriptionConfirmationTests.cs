using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilySubscriptionConfirmationTests
{
    [Fact]
    public async Task Paid_batch_returns_invoice_total_and_each_purchased_subscription_without_writing()
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        // Mutable roster pointers and plans must not replace the purchased subscriptions.
        var manager = await db.FamilyMembers.SingleAsync(x => x.Role == FamilyMemberRole.Manager);
        manager.MemberSubscriptionId = Guid.NewGuid();
        manager.DurationDays = 99;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(batch.FamilyGroup.ManagerCustomerUserId), default);

        result.IsSuccess.Should().BeTrue();
        var confirmation = result.Value;
        confirmation.FamilyGroupId.Should().Be(batch.FamilyGroupId);
        confirmation.FamilyName.Should().Be(batch.FamilyGroup.Name);
        confirmation.TotalAmountPaid.Should().Be(77.97m);
        confirmation.CurrencyCode.Should().Be("KWD");
        confirmation.PaymentVerifiedAtUtc.Should().Be(batch.PaidAtUtc);
        confirmation.Members.Should().HaveCount(3);
        confirmation.Members[0].IsManager.Should().BeTrue();
        confirmation.Members[0].Role.Should().Be("Manager");
        confirmation.Members[0].DurationDays.Should().Be(6);
        confirmation.Members[0].StartDate.Should().Be(new DateOnly(2026, 10, 26));
        confirmation.Members[0].EndDate.Should().Be(new DateOnly(2026, 11, 1));
        confirmation.Members[0].TierNameAr.Should().Be("أساسي");
        confirmation.Members.Should().Contain(x => x.DurationDays == 12 && x.TierNameAr == "بلاتينيوم"
            && x.StartDate == new DateOnly(2026, 10, 27) && x.EndDate == new DateOnly(2026, 11, 9));
        confirmation.Members.Select(x => x.SubscriptionId).Should().BeEquivalentTo(
            batch.Lines.Where(x => x.CheckoutSelected).Select(x => x.SubscriptionId!.Value));
        confirmation.Members.Should().OnlyContain(x => x.ProgramNameAr == "خسارة الوزن" && x.BundleNameAr == "ريوق وغداء");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(FamilyCheckoutBatchStatus.Draft)]
    [InlineData(FamilyCheckoutBatchStatus.PaymentPending)]
    [InlineData(FamilyCheckoutBatchStatus.Failed)]
    [InlineData(FamilyCheckoutBatchStatus.Expired)]
    [InlineData(FamilyCheckoutBatchStatus.Cancelled)]
    [InlineData(FamilyCheckoutBatchStatus.ReviewRequired)]
    public async Task Unpaid_batch_never_returns_confirmation(FamilyCheckoutBatchStatus status)
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        batch.Status = status;
        await db.SaveChangesAsync();
        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(batch.FamilyGroup.ManagerCustomerUserId), default);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("family.confirmation_not_ready");
    }

    [Fact]
    public async Task Later_upgrade_does_not_change_the_tier_on_the_original_family_purchase()
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        var managerLine = batch.Lines.Single(x => x.FamilyMember.Role == FamilyMemberRole.Manager);
        managerLine.Subscription!.SelectedTier = RestaurantTier.Elite;
        await db.SaveChangesAsync();

        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(batch.PayerCustomerUserId, batch.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Members[0].TierNameAr.Should().Be("أساسي");
    }

    [Fact]
    public async Task Paid_status_without_verified_timestamp_is_not_confirmation()
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        batch.PaidAtUtc = null;
        await db.SaveChangesAsync();
        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(batch.FamilyGroup.ManagerCustomerUserId), default);
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_customer_cannot_read_family_invoice(bool explicitBatch)
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        var memberCustomer = batch.Lines.First(x => x.FamilyMember.Role != FamilyMemberRole.Manager).Subscription!.CustomerUserId;
        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(memberCustomer, explicitBatch ? batch.Id : null), default);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task No_batch_returns_not_found()
    {
        using var db = CreateDb();
        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(Guid.NewGuid()), default);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Latest_initial_batch_does_not_fall_back_to_an_older_payment_and_explicit_id_is_stable()
    {
        using var db = CreateDb();
        var paid = await SeedAsync(db);
        var pending = new FamilyCheckoutBatch
        {
            FamilyGroupId = paid.FamilyGroupId, PayerCustomerUserId = paid.PayerCustomerUserId,
            IdempotencyKey = "next-purchase", Status = FamilyCheckoutBatchStatus.PaymentPending,
            CreatedAtUtc = paid.CreatedAtUtc.AddDays(1)
        };
        db.FamilyCheckoutBatches.Add(pending);
        await db.SaveChangesAsync();
        var handler = new FamilySubscriptionConfirmationHandler(db);
        var latest = await handler.Handle(new GetFamilySubscriptionConfirmationQuery(paid.PayerCustomerUserId), default);
        latest.Error.Type.Should().Be(ErrorType.Conflict);
        var original = await handler.Handle(new GetFamilySubscriptionConfirmationQuery(paid.PayerCustomerUserId, paid.Id), default);
        original.IsSuccess.Should().BeTrue();
        original.Value.Members.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("unverified")]
    [InlineData("empty")]
    public async Task Incomplete_or_inconsistent_purchased_members_do_not_return_partial_success(string defect)
    {
        using var db = CreateDb();
        var batch = await SeedAsync(db);
        var line = batch.Lines.First(x => x.CheckoutSelected);
        if (defect == "missing") { line.SubscriptionId = null; line.Subscription = null; }
        if (defect == "foreign") line.Subscription!.FamilyGroupId = Guid.NewGuid();
        if (defect == "unverified") line.Subscription!.PaymentVerifiedAtUtc = null;
        if (defect == "empty") foreach (var item in batch.Lines) item.CheckoutSelected = false;
        await db.SaveChangesAsync();
        var result = await new FamilySubscriptionConfirmationHandler(db).Handle(
            new GetFamilySubscriptionConfirmationQuery(batch.PayerCustomerUserId), default);
        result.Error.Code.Should().Be("family.batch_targets_invalid");
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static async Task<FamilyCheckoutBatch> SeedAsync(AppDbContext db)
    {
        var owner = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = owner, Name = "عائلة التأكيد", Status = FamilyGroupStatus.Active };
        var program = new Program { Code = "confirm", NameAr = "خسارة الوزن", NameEn = "Weight loss" };
        var bundle = new Bundle { ProgramId = program.Id, Code = "confirm", NameAr = "ريوق وغداء", NameEn = "Breakfast and lunch" };
        var verified = new DateTime(2026, 10, 22, 4, 50, 24, DateTimeKind.Utc);
        var batch = new FamilyCheckoutBatch
        {
            FamilyGroup = group, FamilyGroupId = group.Id, PayerCustomerUserId = owner,
            IdempotencyKey = "paid-purchase", Status = FamilyCheckoutBatchStatus.Paid,
            FinalPrice = 77.97m, CurrencyCode = "KWD", PaidAtUtc = verified, CreatedAtUtc = verified.AddMinutes(-5)
        };
        for (var i = 0; i < 4; i++)
        {
            var member = new FamilyMember
            {
                FamilyGroupId = group.Id, FamilyGroup = group,
                Role = i == 0 ? FamilyMemberRole.Manager : FamilyMemberRole.Member,
                DisplayName = $"فرد {i}", RelationshipCode = "family", NutritionalGoalCode = "health"
            };
            var subscription = new Subscription
            {
                CustomerUserId = i == 0 ? owner : Guid.NewGuid(), FamilyGroupId = group.Id, FamilyMemberId = member.Id,
                Program = program, ProgramId = program.Id, Bundle = bundle, BundleId = bundle.Id,
                SelectedTier = i == 1 ? RestaurantTier.Platinum : RestaurantTier.Basic,
                DurationDays = i == 1 ? 12 : 6, StartDate = new DateOnly(2026, 10, 26).AddDays(i),
                EndDate = i == 1 ? new DateOnly(2026, 11, 9) : new DateOnly(2026, 11, 1).AddDays(i),
                Status = SubscriptionStatus.Scheduled, PaidAmount = new Money(25.99m, "KWD"), PaymentVerifiedAtUtc = verified
            };
            batch.Lines.Add(new FamilyCheckoutBatchLine
            {
                BatchId = batch.Id, FamilyMemberId = member.Id, FamilyMember = member,
                SubscriptionId = subscription.Id, Subscription = subscription, CheckoutSelected = i < 3,
                Tier = subscription.SelectedTier,
                NetPaidAmount = 25.99m, PriceBeforeFamilyDiscount = 25.99m
            });
            db.AddRange(member, subscription);
        }
        db.AddRange(group, program, bundle, batch);
        await db.SaveChangesAsync();
        return batch;
    }
}
