using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Foundation;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.Catalog;

public sealed class CallingCodesCatalogTests
{
    [Fact]
    public async Task GetArabicCallingCodes_ReturnsOnlyActiveArabicCountriesWithFlagsFromDatabase()
    {
        await using var db = CreateDb();
        db.Countries.AddRange(
            new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", CurrencyCode = "KWD", DialCode = "+965", FlagEmoji = "🇰🇼", IsActive = true },
            new Country { Code = "EG", NameAr = "مصر", NameEn = "Egypt", CurrencyCode = "EGP", DialCode = "+20", FlagEmoji = "🇪🇬", IsActive = true },
            new Country { Code = "US", NameAr = "الولايات المتحدة", NameEn = "United States", CurrencyCode = "USD", DialCode = "+1", FlagEmoji = "🇺🇸", IsActive = true },
            new Country { Code = "JO", NameAr = "الأردن", NameEn = "Jordan", CurrencyCode = "JOD", DialCode = "+962", FlagEmoji = "🇯🇴", IsActive = false });
        await db.SaveChangesAsync();

        var result = await new GetArabicCallingCodesQueryHandler(db)
            .Handle(new GetArabicCallingCodesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(country => country.CountryCode).Should().Equal("KW", "EG");
        result.Value.Should().OnlyContain(country => !string.IsNullOrWhiteSpace(country.FlagEmoji));
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
