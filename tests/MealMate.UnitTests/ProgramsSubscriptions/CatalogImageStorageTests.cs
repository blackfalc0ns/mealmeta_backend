using FluentAssertions;
using MealMate.Application.Common.Storage;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class CatalogImageStorageTests
{
    [Fact]
    public void Normalize_ReturnsNull_WhenEmpty()
    {
        CatalogImageStorage.Normalize(null).Should().BeNull();
        CatalogImageStorage.Normalize("   ").Should().BeNull();
    }

    [Fact]
    public void Normalize_ExtractsKey_FromContentUrl()
    {
        var url = "/api/v1/uploads/content?key=uploads%2Fadmin%2Fprograms%2Fwl.webp";
        CatalogImageStorage.Normalize(url).Should().Be("uploads/admin/programs/wl.webp");
    }

    [Fact]
    public void ToContentUrl_EncodesStorageKey()
    {
        CatalogImageStorage.ToContentUrl("uploads/admin/bundles/bl1s.webp")
            .Should().Be("/api/v1/uploads/content?key=uploads%2Fadmin%2Fbundles%2Fbl1s.webp");
    }

    [Fact]
    public void HasAllowedPrefix_AcceptsMatchingKey()
    {
        CatalogImageStorage.HasAllowedPrefix("uploads/admin/programs/wl.webp", CatalogImageStorage.ProgramPrefix)
            .Should().BeTrue();
        CatalogImageStorage.HasAllowedPrefix("uploads/restaurant/meals/x.webp", CatalogImageStorage.ProgramPrefix)
            .Should().BeFalse();
        CatalogImageStorage.HasAllowedPrefix(null, CatalogImageStorage.ProgramPrefix).Should().BeTrue();
    }
}
