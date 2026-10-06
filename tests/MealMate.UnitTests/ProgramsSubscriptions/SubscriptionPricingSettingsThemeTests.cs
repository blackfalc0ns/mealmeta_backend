using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

/// <summary>
/// The mobile design choice is a closed catalog (TierThemeDesign): the admin sends the design
/// number, the name is always derived from the catalog, and unknown numbers are rejected on save
/// while reads fall back to no theme.
/// </summary>
public sealed class SubscriptionPricingSettingsThemeTests
{
    [Fact]
    public void Normalize_DerivesDesignNameFromCatalog()
    {
        var doc = new SubscriptionPricingSettingsDocument
        {
            Theme = new TierThemeSettings { DesignId = (int)TierThemeDesign.Aurora },
        };

        var normalized = SubscriptionPricingSettingsReader.Normalize(doc);

        normalized.Theme.Should().NotBeNull();
        normalized.Theme!.DesignId.Should().Be((int)TierThemeDesign.Aurora);
        normalized.Theme.DesignName.Should().Be("Aurora");
    }

    [Fact]
    public void Normalize_EmptyDesignClearsTheme()
    {
        var doc = new SubscriptionPricingSettingsDocument
        {
            Theme = new TierThemeSettings { DesignId = 0, DesignName = "" },
        };

        SubscriptionPricingSettingsReader.Normalize(doc).Theme.Should().BeNull();
    }

    [Fact]
    public void Validate_UnknownDesignNumber_IsRejected()
    {
        var doc = SubscriptionPricingSettingsReader.Normalize(new SubscriptionPricingSettingsDocument
        {
            Theme = new TierThemeSettings { DesignId = 99, DesignName = "Ghost" },
        });

        var error = SubscriptionPricingSettingsValidator.Validate(doc);

        error.Should().NotBeNull();
        error!.Code.Should().Be("pricing_settings.theme_invalid");
        error.Description.Should().Contain("1 (Classic)");
        error.Description.Should().Contain("2 (Aurora)");
    }

    [Fact]
    public void Validate_NameNotMatchingCatalog_IsRejected()
    {
        var doc = SubscriptionPricingSettingsReader.Normalize(new SubscriptionPricingSettingsDocument
        {
            Theme = new TierThemeSettings { DesignId = (int)TierThemeDesign.Classic, DesignName = "Whatever" },
        });

        // Normalize derives the catalog name, so a mismatched typed name is corrected rather than stored.
        doc.Theme!.DesignName.Should().Be("Classic");
        SubscriptionPricingSettingsValidator.Validate(doc).Should().BeNull();
    }

    [Fact]
    public void Validate_KnownDesign_Passes()
    {
        var doc = SubscriptionPricingSettingsReader.Normalize(new SubscriptionPricingSettingsDocument
        {
            Theme = new TierThemeSettings { DesignId = (int)TierThemeDesign.Classic },
        });

        SubscriptionPricingSettingsValidator.Validate(doc).Should().BeNull();
    }
}
