using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class SubscriptionPricingSettingsFamilyTests
{
    [Fact]
    public void Deserialize_MissingFamilyBlock_UsesDefaults()
    {
        var doc = SubscriptionPricingSettingsReader.Deserialize("""{"mode":"global"}""");
        doc.Family.DiscountPercent.Should().Be(0.10m);
        doc.Family.MaxMembers.Should().Be(4);
        doc.Family.MinMembersForDiscount.Should().Be(2);
    }

    [Fact]
    public void Normalize_ClampsDiscountAndMemberBounds()
    {
        var doc = new SubscriptionPricingSettingsDocument
        {
            Family = new FamilyPricingSettings
            {
                DiscountPercent = 1.5m,
                MaxMembers = 99,
                MinMembersForDiscount = 0,
            },
        };
        var normalized = SubscriptionPricingSettingsReader.Normalize(doc);
        normalized.Family.DiscountPercent.Should().Be(1.00m);
        normalized.Family.MaxMembers.Should().Be(4);
        normalized.Family.MinMembersForDiscount.Should().Be(2);
    }
}
