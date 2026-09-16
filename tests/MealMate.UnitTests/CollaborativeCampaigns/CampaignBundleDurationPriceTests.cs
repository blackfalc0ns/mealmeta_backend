using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignBundleDurationPriceTests
{
    [Fact]
    public void Create_ValidParameters_Succeeds()
    {
        var bundleId = Guid.NewGuid();
        var price = new Money(55.000m, "KWD");
        var result = CampaignBundleDurationPrice.Create(bundleId, 26, price, "StandardMenuCatalog", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.CampaignBundleId.Should().Be(bundleId);
        result.Value.DurationDays.Should().Be(26);
        result.Value.ReferenceCustomerPrice.Amount.Should().Be(55.000m);
        result.Value.ReferenceCustomerPrice.CurrencyCode.Should().Be("KWD");
        result.Value.Source.Should().Be("StandardMenuCatalog");
        result.Value.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_InvalidDurationDays_Fails(int durationDays)
    {
        var result = CampaignBundleDurationPrice.Create(
            Guid.NewGuid(),
            durationDays,
            new Money(50m, "KWD"),
            "Catalog",
            1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Duration days");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_ZeroOrNegativePrice_Fails(decimal amount)
    {
        var result = CampaignBundleDurationPrice.Create(
            Guid.NewGuid(),
            20,
            new Money(amount, "KWD"),
            "Catalog",
            1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Reference customer price");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptySource_Fails(string source)
    {
        var result = CampaignBundleDurationPrice.Create(
            Guid.NewGuid(),
            20,
            new Money(40m, "KWD"),
            source,
            1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Price source");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidVersion_Fails(int version)
    {
        var result = CampaignBundleDurationPrice.Create(
            Guid.NewGuid(),
            20,
            new Money(40m, "KWD"),
            "Catalog",
            version);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Price version");
    }

    [Fact]
    public void CampaignBundle_AddDurationPrice_Success_And_Retrieval()
    {
        var bundle = new CampaignBundle { CampaignId = Guid.NewGuid(), BundleId = Guid.NewGuid() };

        var add14 = bundle.AddDurationPrice(14, new Money(30.000m, "KWD"), "Catalog", 1);
        var add26 = bundle.AddDurationPrice(26, new Money(55.000m, "KWD"), "Catalog", 1);

        add14.IsSuccess.Should().BeTrue();
        add26.IsSuccess.Should().BeTrue();
        bundle.DurationPrices.Should().HaveCount(2);

        var retrieved26 = bundle.GetDurationPrice(26);
        retrieved26.IsSuccess.Should().BeTrue();
        retrieved26.Value.ReferenceCustomerPrice.Amount.Should().Be(55.000m);

        var retrieved30 = bundle.GetDurationPrice(30);
        retrieved30.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CampaignBundle_AddDuplicateDurationPrice_Fails()
    {
        var bundle = new CampaignBundle { CampaignId = Guid.NewGuid(), BundleId = Guid.NewGuid() };

        bundle.AddDurationPrice(26, new Money(55.000m, "KWD"), "Catalog", 1);
        var duplicate = bundle.AddDurationPrice(26, new Money(56.000m, "KWD"), "Catalog", 2);

        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Should().Contain("already exists");
    }
}
