using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class SubscriptionPricingSettingsFamilyTests
{
    [Fact]
    public void Deserialize_MissingFamilyBlock_SeedsDefaultSizePackages()
    {
        var doc = SubscriptionPricingSettingsReader.Deserialize("""{"mode":"global"}""");
        doc.Family.Packages.Should().HaveCount(3);
        doc.Family.DiscountPercent.Should().Be(0.08m);
        doc.Family.MinMembersForDiscount.Should().Be(2);
        doc.Family.MaxMembers.Should().Be(8);
        doc.Family.Packages[0].NameEn.Should().Be("Small family");
        doc.Family.Packages[0].MinMembers.Should().Be(2);
        doc.Family.Packages[0].MaxMembers.Should().Be(3);
    }

    [Fact]
    public void Normalize_EmptyPackages_SeedsDefaults_AndClampsPackageBounds()
    {
        var doc = new SubscriptionPricingSettingsDocument
        {
            Family = new FamilyPricingSettings
            {
                DiscountPercent = 1.5m,
                MaxMembers = 99,
                MinMembersForDiscount = 0,
                Packages =
                [
                    new FamilySizePackageSettings
                    {
                        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        NameAr = "اختبار",
                        NameEn = "Test",
                        MinMembers = 0,
                        MaxMembers = 99,
                        DiscountPercent = 1.5m,
                        IsActive = true,
                        SortOrder = 0,
                    },
                ],
            },
        };
        var normalized = SubscriptionPricingSettingsReader.Normalize(doc);
        normalized.Family.Packages.Should().HaveCount(1);
        normalized.Family.Packages[0].MinMembers.Should().Be(2);
        normalized.Family.Packages[0].MaxMembers.Should().Be(12);
        normalized.Family.Packages[0].DiscountPercent.Should().Be(1.00m);
        normalized.Family.DiscountPercent.Should().Be(1.00m);
        normalized.Family.MaxMembers.Should().Be(12);
        normalized.Family.MinMembersForDiscount.Should().Be(2);
    }
}
