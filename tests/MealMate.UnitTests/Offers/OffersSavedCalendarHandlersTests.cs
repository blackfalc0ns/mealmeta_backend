using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using MealMate.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OffersSavedCalendarHandlersTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static IDateTimeProvider UtcClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return clock;
    }

    private static ICurrentUser CustomerUser(Guid customerId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        return user;
    }

    private static ICurrentUser NonCustomerUser()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        user.UserType.Returns(UserType.Admin);
        return user;
    }

    private static Offer ActiveOffer(string nameEn = "Active Offer")
    {
        return new Offer
        {
            NameAr = "عرض",
            NameEn = nameEn,
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
    }

    private static CustomerSavedOffer SavedRow(Guid customerUserId, Guid offerId, DateTime savedAtUtc)
    {
        return new CustomerSavedOffer
        {
            CustomerUserId = customerUserId,
            OfferId = offerId,
            CreatedAtUtc = savedAtUtc,
        };
    }

    [Fact]
    public async Task SaveOffer_PersistsOneRow_AndRepeatSaveRemainsIdempotent()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        var handler = new SaveOfferCommandHandler(db, CustomerUser(customer), UtcClock());

        var first = await handler.Handle(new SaveOfferCommand(offer.Id), CancellationToken.None);
        var again = await handler.Handle(new SaveOfferCommand(offer.Id), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().Be(offer.Id);
        again.IsSuccess.Should().BeTrue();
        again.Value.Should().Be(offer.Id);
        (await db.CustomerSavedOffers.IgnoreQueryFilters()
            .CountAsync(x => x.CustomerUserId == customer && x.OfferId == offer.Id)).Should().Be(1);
    }

    [Fact]
    public async Task SaveOffer_NonCustomerPrincipal_IsForbidden()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new SaveOfferCommandHandler(db, NonCustomerUser(), UtcClock());

        var result = await handler.Handle(new SaveOfferCommand(offer.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
        db.CustomerSavedOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveOffer_UnknownOffer_FailsWithExistingCustomerNotFoundCode()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new SaveOfferCommandHandler(db, CustomerUser(Guid.NewGuid()), UtcClock());

        var result = await handler.Handle(new SaveOfferCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("offers.customer.unavailable");
        db.CustomerSavedOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveOffer_NonActiveOffer_ReusesListingPathNotFoundHandling()
    {
        using var db = CreateDbContext();
        var offer = new Offer
        {
            NameAr = "عرض مسودة",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new SaveOfferCommandHandler(db, CustomerUser(Guid.NewGuid()), UtcClock());

        var result = await handler.Handle(new SaveOfferCommand(offer.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("offers.customer.unavailable");
        db.CustomerSavedOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveOffer_AfterSoftDeletedRow_ResurrectsSingleActiveRow()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        var customer = Guid.NewGuid();
        db.CustomerSavedOffers.Add(new CustomerSavedOffer
        {
            CustomerUserId = customer,
            OfferId = offer.Id,
            IsDeleted = true,
        });
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var handler = new SaveOfferCommandHandler(db, CustomerUser(customer), clock);

        var result = await handler.Handle(new SaveOfferCommand(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rows = await db.CustomerSavedOffers.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == customer && x.OfferId == offer.Id)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].IsDeleted.Should().BeFalse();
        rows[0].CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task UnsaveOffer_MissingRow_FailsWithOfferNotSavedCode()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new UnsaveOfferCommandHandler(db, CustomerUser(Guid.NewGuid()));

        var result = await handler.Handle(new UnsaveOfferCommand(offer.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("offer.not_saved");
        db.CustomerSavedOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task UnsaveOffer_RemovesExistingRow()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        db.CustomerSavedOffers.Add(SavedRow(customer, offer.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var handler = new UnsaveOfferCommandHandler(db, CustomerUser(customer));

        var result = await handler.Handle(new UnsaveOfferCommand(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(offer.Id);
        db.CustomerSavedOffers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCustomerAvailableOffers_MarksOnlyCallersSavedOffers()
    {
        using var db = CreateDbContext();
        var savedOffer = ActiveOffer("Saved Offer");
        var otherOffer = ActiveOffer("Unsaved Offer");
        db.Offers.Add(savedOffer);
        db.Offers.Add(otherOffer);
        await db.SaveChangesAsync();
        var customerA = Guid.NewGuid();
        db.CustomerSavedOffers.Add(SavedRow(customerA, savedOffer.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var handler = new GetCustomerAvailableOffersQueryHandler(db, new OfferEligibilityEngine(), UtcClock());

        var forA = await handler.Handle(new GetCustomerAvailableOffersQuery(CustomerId: customerA), CancellationToken.None);

        forA.IsSuccess.Should().BeTrue();
        forA.Value.Should().HaveCount(2);
        forA.Value!.Single(x => x.Id == savedOffer.Id).IsSaved.Should().BeTrue();
        forA.Value!.Single(x => x.Id == otherOffer.Id).IsSaved.Should().BeFalse();

        var forB = await handler.Handle(new GetCustomerAvailableOffersQuery(CustomerId: Guid.NewGuid()), CancellationToken.None);

        forB.IsSuccess.Should().BeTrue();
        forB.Value!.All(x => !x.IsSaved).Should().BeTrue();
        forB.Value!.Single(x => x.Id == savedOffer.Id).IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task GetSavedOffers_ReturnsOnlyCallerRows_NewestFirst()
    {
        using var db = CreateDbContext();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var older = ActiveOffer("Older Offer");
        var newer = ActiveOffer("Newer Offer");
        db.Offers.Add(older);
        db.Offers.Add(newer);
        await db.SaveChangesAsync();
        db.CustomerSavedOffers.Add(SavedRow(customerA, older.Id, DateTime.UtcNow.AddHours(-2)));
        db.CustomerSavedOffers.Add(SavedRow(customerA, newer.Id, DateTime.UtcNow));
        db.CustomerSavedOffers.Add(SavedRow(customerB, older.Id, DateTime.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var handlerA = new GetSavedOffersQueryHandler(db, CustomerUser(customerA));
        var resultA = await handlerA.Handle(new GetSavedOffersQuery(), CancellationToken.None);

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.TotalCount.Should().Be(2);
        resultA.Value.Offers.Select(x => x.Id).Should().ContainInOrder(newer.Id, older.Id);
        resultA.Value.Offers.Should().OnlyContain(x => x.IsSaved);

        var handlerB = new GetSavedOffersQueryHandler(db, CustomerUser(customerB));
        var resultB = await handlerB.Handle(new GetSavedOffersQuery(), CancellationToken.None);

        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.TotalCount.Should().Be(1);
        resultB.Value.Offers.Single().Id.Should().Be(older.Id);
        resultB.Value.Offers.Single().IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task GetSavedOffers_NonCustomerPrincipal_IsForbidden()
    {
        using var db = CreateDbContext();
        var handler = new GetSavedOffersQueryHandler(db, NonCustomerUser());

        var result = await handler.Handle(new GetSavedOffersQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetSavedOffers_SkipsRowsWhenOfferSoftDeleted()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        var customer = Guid.NewGuid();
        db.CustomerSavedOffers.Add(SavedRow(customer, offer.Id, DateTime.UtcNow));
        await db.SaveChangesAsync();
        offer.IsDeleted = true;
        await db.SaveChangesAsync();

        var handler = new GetSavedOffersQueryHandler(db, CustomerUser(customer));
        var result = await handler.Handle(new GetSavedOffersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.Offers.Should().BeEmpty();
    }

    private static IDateTimeProvider FixedUtcClock(DateTime utcNow)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(utcNow));
        return clock;
    }

    private static CustomerOfferCalendarEntry CalendarRow(
        Guid customerUserId,
        Guid offerId,
        DateOnly scheduledDate,
        CustomerOfferCalendarEntryStatus status = CustomerOfferCalendarEntryStatus.Active,
        bool isDeleted = false)
    {
        return new CustomerOfferCalendarEntry
        {
            CustomerUserId = customerUserId,
            OfferId = offerId,
            ScheduledDate = scheduledDate,
            Status = status,
            Source = "offer-detail",
            IsDeleted = isDeleted,
        };
    }

    [Fact]
    public async Task AddOfferToCalendar_PersistsActiveEntry_WithOfferDetailSource()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        var scheduled = new DateOnly(2026, 10, 21);
        var handler = new AddOfferToCalendarCommandHandler(db, CustomerUser(customer), UtcClock());

        var result = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, scheduled), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var row = await db.CustomerOfferCalendarEntries.AsNoTracking()
            .SingleAsync(x => x.CustomerUserId == customer && x.OfferId == offer.Id);
        row.Id.Should().Be(result.Value);
        row.ScheduledDate.Should().Be(scheduled);
        row.Status.Should().Be(CustomerOfferCalendarEntryStatus.Active);
        row.Source.Should().Be("offer-detail");
    }

    [Fact]
    public async Task AddOfferToCalendar_DuplicateSameDate_IsIdempotent()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        var scheduled = new DateOnly(2026, 10, 21);
        var handler = new AddOfferToCalendarCommandHandler(db, CustomerUser(customer), UtcClock());

        var first = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, scheduled), CancellationToken.None);
        var again = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, scheduled), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().NotBe(Guid.Empty);
        again.IsSuccess.Should().BeTrue();
        again.Value.Should().Be(first.Value);
        (await db.CustomerOfferCalendarEntries.IgnoreQueryFilters()
            .CountAsync(x => x.CustomerUserId == customer && x.OfferId == offer.Id)).Should().Be(1);
    }

    [Fact]
    public async Task AddOfferToCalendar_SameOfferDifferentDates_CreatesTwoRows()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        var handler = new AddOfferToCalendarCommandHandler(db, CustomerUser(customer), UtcClock());

        var first = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, new DateOnly(2026, 10, 21)), CancellationToken.None);
        var second = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, new DateOnly(2026, 10, 22)), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().NotBe(first.Value);
        (await db.CustomerOfferCalendarEntries.IgnoreQueryFilters()
            .CountAsync(x => x.CustomerUserId == customer && x.OfferId == offer.Id)).Should().Be(2);
    }

    [Fact]
    public async Task AddOfferToCalendar_AfterSoftDeletedRow_ResurrectsSingleActiveRow()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        var customer = Guid.NewGuid();
        var scheduled = new DateOnly(2026, 10, 21);
        db.CustomerOfferCalendarEntries.Add(new CustomerOfferCalendarEntry
        {
            CustomerUserId = customer,
            OfferId = offer.Id,
            ScheduledDate = scheduled,
            IsDeleted = true,
        });
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc);
        var handler = new AddOfferToCalendarCommandHandler(db, CustomerUser(customer), FixedUtcClock(now));

        var result = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, scheduled), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rows = await db.CustomerOfferCalendarEntries.IgnoreQueryFilters()
            .Where(x => x.CustomerUserId == customer && x.OfferId == offer.Id)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].IsDeleted.Should().BeFalse();
        rows[0].CreatedAtUtc.Should().Be(now);
        rows[0].Status.Should().Be(CustomerOfferCalendarEntryStatus.Active);
        rows[0].Source.Should().Be("offer-detail");
    }

    [Fact]
    public async Task AddOfferToCalendar_UnknownOrInactiveOffer_FailsWithCustomerUnavailableCode()
    {
        using var db = CreateDbContext();
        var draft = new Offer
        {
            NameAr = "عرض مسودة",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
        db.Offers.Add(draft);
        await db.SaveChangesAsync();
        var customer = Guid.NewGuid();
        var handler = new AddOfferToCalendarCommandHandler(db, CustomerUser(customer), UtcClock());

        var unknown = await handler.Handle(new AddOfferToCalendarCommand(Guid.NewGuid(), new DateOnly(2026, 10, 21)), CancellationToken.None);
        var inactive = await handler.Handle(new AddOfferToCalendarCommand(draft.Id, new DateOnly(2026, 10, 21)), CancellationToken.None);

        unknown.IsFailure.Should().BeTrue();
        unknown.Error.Type.Should().Be(ErrorType.NotFound);
        unknown.Error.Code.Should().Be("offers.customer.unavailable");
        inactive.IsFailure.Should().BeTrue();
        inactive.Error.Type.Should().Be(ErrorType.NotFound);
        inactive.Error.Code.Should().Be("offers.customer.unavailable");
        db.CustomerOfferCalendarEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task AddOfferToCalendar_NonCustomerPrincipal_IsForbidden()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new AddOfferToCalendarCommandHandler(db, NonCustomerUser(), UtcClock());

        var result = await handler.Handle(new AddOfferToCalendarCommand(offer.Id, new DateOnly(2026, 10, 21)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
        db.CustomerOfferCalendarEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveOfferFromCalendar_ExistingRow_SoftDeletesRow()
    {
        var customer = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var scheduled = new DateOnly(2026, 10, 21);
        var dbName = Guid.NewGuid().ToString();
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var clock = FixedUtcClock(now);
        using (var seed = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            Substitute.For<IDomainEventDispatcher>()))
        {
            seed.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerId, scheduled));
            await seed.SaveChangesAsync();
        }

        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(new AuditableEntityInterceptor(CustomerUser(customer), clock))
                .Options,
            Substitute.For<IDomainEventDispatcher>());
        var handler = new RemoveOfferFromCalendarCommandHandler(db, CustomerUser(customer));

        var result = await handler.Handle(new RemoveOfferFromCalendarCommand(offerId, scheduled), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(offerId);
        db.CustomerOfferCalendarEntries.Should().BeEmpty();
        var softDeleted = await db.CustomerOfferCalendarEntries.IgnoreQueryFilters()
            .SingleAsync(x => x.CustomerUserId == customer && x.OfferId == offerId);
        softDeleted.IsDeleted.Should().BeTrue();
        softDeleted.ScheduledDate.Should().Be(scheduled);
        softDeleted.DeletedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task RemoveOfferFromCalendar_MissingRow_FailsWithCalendarNotBookedCode()
    {
        using var db = CreateDbContext();
        var offer = ActiveOffer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var handler = new RemoveOfferFromCalendarCommandHandler(db, CustomerUser(Guid.NewGuid()));

        var result = await handler.Handle(
            new RemoveOfferFromCalendarCommand(offer.Id, new DateOnly(2026, 10, 21)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("offer.calendar_not_booked");
    }

    [Fact]
    public async Task RemoveOfferFromCalendar_NonCustomerPrincipal_IsForbidden()
    {
        using var db = CreateDbContext();
        var handler = new RemoveOfferFromCalendarCommandHandler(db, NonCustomerUser());

        var result = await handler.Handle(
            new RemoveOfferFromCalendarCommand(Guid.NewGuid(), new DateOnly(2026, 10, 21)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
        db.CustomerOfferCalendarEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ListOfferCalendar_OrdersAscending_KeepsActiveRows_DropsPastDatesAndUnavailableOffers()
    {
        using var db = CreateDbContext();
        var offerA = ActiveOffer("Offer A");
        var offerB = ActiveOffer("Offer B");
        var offerGone = ActiveOffer("Deleted Offer");
        var draft = new Offer
        {
            NameAr = "عرض مسودة",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
        };
        db.Offers.Add(offerA);
        db.Offers.Add(offerB);
        db.Offers.Add(offerGone);
        db.Offers.Add(draft);
        var customer = Guid.NewGuid();
        var otherCustomer = Guid.NewGuid();
        db.CustomerSavedOffers.Add(SavedRow(customer, offerB.Id, DateTime.UtcNow));
        var today = new DateOnly(2026, 10, 20);
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerA.Id, today.AddDays(-1)));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerA.Id, today));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerB.Id, today));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerB.Id, today.AddDays(1), CustomerOfferCalendarEntryStatus.Completed));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, draft.Id, today.AddDays(2)));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerGone.Id, today.AddDays(3)));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(customer, offerA.Id, today.AddDays(5)));
        db.CustomerOfferCalendarEntries.Add(CalendarRow(otherCustomer, offerB.Id, today.AddDays(1)));
        await db.SaveChangesAsync();
        offerGone.IsDeleted = true;
        await db.SaveChangesAsync();

        var clock = FixedUtcClock(new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc));
        var result = await new ListOfferCalendarQueryHandler(db, CustomerUser(customer), clock)
            .Handle(new ListOfferCalendarQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(result.Value.Entries.Count);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Entries.Select(x => x.ScheduledDate).Should().Equal(today, today, today.AddDays(5));
        var sameDayOfferIds = result.Value.Entries
            .Where(x => x.ScheduledDate == today)
            .Select(x => x.Offer.Id)
            .ToList();
        sameDayOfferIds.Should().Contain(offerA.Id);
        sameDayOfferIds.Should().Contain(offerB.Id);
        sameDayOfferIds.Should().Equal(sameDayOfferIds.OrderBy(x => x));
        result.Value.Entries.Single(x => x.Offer.Id == offerB.Id).Offer.IsSaved.Should().BeTrue();
        result.Value.Entries.Where(x => x.Offer.Id != offerB.Id).Should().OnlyContain(x => !x.Offer.IsSaved);

        var forOther = await new ListOfferCalendarQueryHandler(db, CustomerUser(otherCustomer), clock)
            .Handle(new ListOfferCalendarQuery(), CancellationToken.None);

        forOther.IsSuccess.Should().BeTrue();
        forOther.Value!.TotalCount.Should().Be(1);
        forOther.Value.Entries.Single().ScheduledDate.Should().Be(today.AddDays(1));
        forOther.Value.Entries.Single().Offer.Id.Should().Be(offerB.Id);
        forOther.Value.Entries.Single().Offer.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task ListOfferCalendar_NonCustomerPrincipal_IsForbidden()
    {
        using var db = CreateDbContext();
        var handler = new ListOfferCalendarQueryHandler(db, NonCustomerUser(), UtcClock());

        var result = await handler.Handle(new ListOfferCalendarQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }
}
