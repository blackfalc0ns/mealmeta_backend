using FluentAssertions;
using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilySubscriptionVisibilityTests
{
    [Fact]
    public async Task Customer_current_view_uses_paid_service_when_financial_anchor_is_terminal()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 9);
        var program = new Program { Code = "P-current", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B-current", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Family", Status = FamilyGroupStatus.Active };
        var first = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "First", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var second = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Second", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var oldAnchor = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = first.Id, Status = SubscriptionStatus.Completed,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(90, "KWD"), StartDate = today.AddDays(-20),
            EndDate = today.AddDays(-1), DurationDays = 20 };
        var current = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = first.Id, Status = SubscriptionStatus.Scheduled,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(90, "KWD"), StartDate = today,
            EndDate = today.AddDays(20), DurationDays = 20 };
        var otherMemberCurrent = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = second.Id, Status = SubscriptionStatus.Active,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(90, "KWD"), StartDate = today,
            EndDate = today.AddDays(20), DurationDays = 20 };
        first.MemberSubscriptionId = current.Id;
        second.MemberSubscriptionId = otherMemberCurrent.Id;
        group.Members.Add(first); group.Members.Add(second);
        group.PaymentAnchorSubscriptionId = oldAnchor.Id;
        db.AddRange(program, bundle, group, oldAnchor, current, otherMemberCurrent,
            new SubscriptionDay { SubscriptionId = current.Id, ServiceDate = today, Status = SubscriptionDayStatus.Open },
            new SubscriptionDay { SubscriptionId = otherMemberCurrent.Id, ServiceDate = today, Status = SubscriptionDayStatus.Open });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(today);
        var service = new CustomerSubscriptionQueryService(db, clock);

        var screen = await service.GetCurrentSubscriptionAsync(manager);

        Assert.Contains(screen.Value!.Subscription!.SubscriptionId, new[] { current.Id, otherMemberCurrent.Id });
        screen.Value.Subscription.Status.Should().BeOneOf(nameof(SubscriptionStatus.Scheduled), nameof(SubscriptionStatus.Active));
    }

    [Fact]
    public async Task Shared_service_resolver_ignores_paid_pointer_from_another_family()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var owner = new FamilyGroup { ManagerCustomerUserId = Guid.NewGuid(), Name = "Owner", Status = FamilyGroupStatus.Active };
        var foreign = new FamilyGroup { ManagerCustomerUserId = Guid.NewGuid(), Name = "Foreign", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = owner.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var subscription = new Subscription { CustomerUserId = foreign.ManagerCustomerUserId, FamilyGroupId = foreign.Id,
            FamilyMemberId = null, Status = SubscriptionStatus.Active, PaymentVerifiedAtUtc = DateTime.UtcNow,
            ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), PaidAmount = new Money(90, "KWD"),
            StartDate = new DateOnly(2026, 10, 9), EndDate = new DateOnly(2026, 10, 30), DurationDays = 20 };
        member.MemberSubscriptionId = subscription.Id;
        db.AddRange(owner, foreign, member, subscription);
        await db.SaveChangesAsync();

        var projection = await new FamilyServiceSubscriptionResolver(db).ResolveAsync(
            [member.Id], new Dictionary<Guid, Guid?> { [member.Id] = subscription.Id }, new DateOnly(2026, 10, 9));

        projection[member.Id].CurrentId.Should().BeNull();
        projection[member.Id].UpcomingId.Should().BeNull();
    }

    [Fact]
    public async Task Member_invoice_uses_own_frozen_allocation_on_all_invoice_routes()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var actor = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Family", Status = FamilyGroupStatus.Active, QuotedFinalPrice = 999 };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var parent = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Manager", Role = FamilyMemberRole.Manager,
            RelationshipCode = "self", NutritionalGoalCode = "maintain" };
        var sub = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Active,
            PaidAmount = new Money(7, "KWD"), StartDate = new(2026, 10, 20), EndDate = new(2026, 10, 21), DurationDays = 2 };
        var anchor = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = parent.Id, Status = SubscriptionStatus.Active,
            PaidAmount = new Money(13, "KWD"), StartDate = sub.StartDate, EndDate = sub.EndDate, DurationDays = 2 };
        member.MemberSubscriptionId = sub.Id;
        parent.MemberSubscriptionId = anchor.Id;
        group.Members.Add(member);
        group.Members.Add(parent);
        group.PaymentAnchorSubscriptionId = anchor.Id;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "paid", FinalPrice = 20,
            PaymentAnchorSubscriptionId = anchor.Id, GatewayPaymentId = "payment", GatewayInvoiceId = "invoice",
            PaidAtUtc = DateTime.UtcNow };
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = member.Id, SubscriptionId = sub.Id,
            PriceBeforeFamilyDiscount = 10, FamilyDiscountAllocated = 3, NetPaidAmount = 7 });
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = parent.Id, SubscriptionId = anchor.Id,
            PriceBeforeFamilyDiscount = 20, FamilyDiscountAllocated = 7, NetPaidAmount = 13 });
        var receipt = new PaymentWebhookReceipt { SubscriptionId = anchor.Id,
            EventKey = "event", PaymentId = "payment", InvoiceId = "invoice", Amount = 20, CurrencyCode = "KWD",
            Status = "FulfillmentCompleted", Gateway = "Payzah", VerifiedAtUtc = DateTime.UtcNow, ProcessedAtUtc = DateTime.UtcNow };
        db.AddRange(program, bundle, group, sub, anchor, batch, receipt,
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = actor, HasSeparateAccount = true },
            new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewInvoices = true, ViewProgram = true });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        var service = new CustomerSubscriptionQueryService(db, clock);
        var card = await service.GetManagementDetailsAsync(sub.Id, actor);
        card.Value.PaidAmount.Should().Be(7);
        var familyPermissions = await db.FamilyMemberPermissions.SingleAsync();
        familyPermissions.ViewFamilyPlans = true;
        await db.SaveChangesAsync();
        var memberCards = await service.ListSubscriptionsAsync(actor, true);
        memberCards.Value.Should().HaveCount(2);
        sub.Status = SubscriptionStatus.Completed;
        anchor.Status = SubscriptionStatus.Completed;
        await db.SaveChangesAsync();
        var memberArchive = await service.GetArchivedSubscriptionsAsync(actor);
        memberArchive.Value.Should().HaveCount(2);
        var managerArchive = await service.GetArchivedSubscriptionsAsync(manager);
        managerArchive.Value.Should().ContainSingle().Which.PaidAmount.Should().Be(20);
        familyPermissions.ViewFamilyPlans = false;
        familyPermissions.ViewProgram = false;
        await db.SaveChangesAsync();
        (await service.GetArchivedSubscriptionsAsync(actor)).Value.Should().BeEmpty();
        var memberInvoices = await service.GetSubscriptionInvoicesAsync(sub.Id, actor);
        memberInvoices.Value.Should().ContainSingle().Which.Amount.Should().Be(7);
        var allInvoices = await service.GetInvoicesAsync(actor);
        allInvoices.Value.Should().ContainSingle().Which.Amount.Should().Be(7);
        var invoice = await service.GetInvoiceDetailAsync(receipt.Id, actor);
        invoice.IsSuccess.Should().BeTrue();
        invoice.Value.Amount.Should().Be(7);
        group.ManagerCustomerUserId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var historicalPayer = await service.GetInvoiceDetailAsync(receipt.Id, manager);
        historicalPayer.IsSuccess.Should().BeTrue();
        historicalPayer.Value.Amount.Should().Be(20);
        (await service.GetManagementDetailsAsync(sub.Id, manager)).IsFailure.Should().BeTrue();
        receipt.Amount = 21m;
        await db.SaveChangesAsync();
        (await service.GetSubscriptionInvoicesAsync(sub.Id, manager)).Value.Should().BeEmpty();

    }

    [Fact]
    public async Task Terminal_upgrade_payer_sees_only_their_verified_paid_review_receipt()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var upgradePayer = Guid.NewGuid();
        var batchPayer = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var program = new Program { Code = "P-review", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B-review", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Completed,
            PaidAmount = new Money(100m, "KWD"), StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), DurationDays = 26 };
        member.MemberSubscriptionId = subscription.Id;
        group.Members.Add(member);
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = batchPayer,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "base", FinalPrice = 100m, CurrencyCode = "KWD",
            GatewayPaymentId = "base-payment", GatewayInvoiceId = "base-invoice", PaidAtUtc = DateTime.UtcNow };
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = member.Id,
            SubscriptionId = subscription.Id, PriceBeforeFamilyDiscount = 100m, NetPaidAmount = 100m, CurrencyCode = "KWD" });
        var upgrade = new SubscriptionTierUpgrade { SubscriptionId = subscription.Id, FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum, PriceDifference = new Money(12m, "KWD"), QuotedTotalDue = new Money(12m, "KWD"),
            PaymentId = "upgrade-payment", PayerCustomerUserId = upgradePayer, VerifiedPaidAmount = 12m,
            VerifiedCurrencyCode = "KWD", VerifiedInvoiceId = "upgrade-invoice", VerifiedPaidAtUtc = DateTime.UtcNow,
            Status = VerificationStatus.Submitted };
        var reviewReceipt = new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = "upgrade-payment",
            SubscriptionId = subscription.Id, PaymentId = "upgrade-payment", InvoiceId = "upgrade-invoice",
            Amount = 12m, CurrencyCode = "KWD", Status = "FinancialReviewRequired", VerifiedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = DateTime.UtcNow };
        var baseReceipt = new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = "base-payment",
            SubscriptionId = subscription.Id, PaymentId = "base-payment", InvoiceId = "base-invoice",
            Amount = 100m, CurrencyCode = "KWD", Status = "FulfillmentCompleted", VerifiedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = DateTime.UtcNow };
        db.AddRange(program, bundle, group, member, subscription, batch, upgrade, reviewReceipt, baseReceipt);
        await db.SaveChangesAsync();
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(MessageCodes.Subscriptions.InvoicePaidReviewRequired, Arg.Any<CultureInfo>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(call => call.Arg<CultureInfo>().TwoLetterISOLanguageName == "ar" ? "مراجعة مالية" : "Financial review");
        var service = new CustomerSubscriptionQueryService(db, Substitute.For<IDateTimeProvider>(), messages: messages);

        var invoices = await service.GetInvoicesAsync(upgradePayer);

        invoices.Value.Should().ContainSingle();
        invoices.Value[0].Amount.Should().Be(12m);
        invoices.Value[0].CurrencyCode.Should().Be("KWD");
        invoices.Value[0].PayerCustomerUserId.Should().Be(upgradePayer);
        invoices.Value[0].UpgradeId.Should().Be(upgrade.Id);
        invoices.Value[0].BatchId.Should().BeNull();
        invoices.Value[0].IsPaidReviewRequired.Should().BeTrue();
        invoices.Value[0].Status.Should().Be("PAID_REVIEW_REQUIRED");
        invoices.Value[0].StatusNameAr.Should().NotBe(invoices.Value[0].StatusNameEn);
        upgrade.Status.Should().Be(VerificationStatus.Submitted);
        var directInvoices = await service.GetSubscriptionInvoicesAsync(subscription.Id, upgradePayer);
        directInvoices.IsSuccess.Should().BeTrue();
        directInvoices.Value.Should().ContainSingle().Which.UpgradeId.Should().Be(upgrade.Id);
        (await service.GetManagementDetailsAsync(subscription.Id, upgradePayer)).IsFailure.Should().BeTrue();

        var historicalBasePayerInvoices = await service.GetInvoicesAsync(batchPayer);
        historicalBasePayerInvoices.Value.Should().ContainSingle();
        historicalBasePayerInvoices.Value[0].ReceiptId.Should().Be(baseReceipt.Id);
        historicalBasePayerInvoices.Value[0].Amount.Should().Be(100m);
        historicalBasePayerInvoices.Value.Should().NotContain(x => x.ReceiptId == reviewReceipt.Id);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Own_plan_and_invoice_grants_are_independent(bool plan, bool invoices)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var actor = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var sub = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Active,
            PaidAmount = new Money(10, "KWD"), StartDate = new(2026, 10, 20), EndDate = new(2026, 10, 21), DurationDays = 2 };
        member.MemberSubscriptionId = sub.Id;
        group.Members.Add(member);
        db.AddRange(program, bundle, group, sub,
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = actor, HasSeparateAccount = true },
            new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewProgram = plan, ViewInvoices = invoices });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        var service = new CustomerSubscriptionQueryService(db, clock);
        var detail = await service.GetManagementDetailsAsync(sub.Id, actor);
        detail.IsSuccess.Should().Be(plan);
        if (plan && !invoices) ((object?)detail.Value.PaidAmount).Should().BeNull();
        var list = await service.ListSubscriptionsAsync(actor, true);
        list.Value.Count.Should().Be(plan ? 1 : 0);
        sub.CustomerUserId = actor;
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { SubscriptionId = sub.Id, EventKey = "private-method",
            Gateway = "Payzah", Status = "Paid", VerifiedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var method = await service.GetDefaultPaymentMethodAsync(actor);
        if (!invoices) method.Value.Gateway.Should().BeNull();
        var invoiceResult = await service.GetSubscriptionInvoicesAsync(sub.Id, actor);
        invoiceResult.IsSuccess.Should().Be(invoices);
    }
}
