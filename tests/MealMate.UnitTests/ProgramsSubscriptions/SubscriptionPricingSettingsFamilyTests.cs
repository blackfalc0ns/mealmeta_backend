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
    public void Deserialize_DefaultPackageIds_AreStableAcrossFallbacks()
    {
        var expectedIds = new[]
        {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
        };

        var first = SubscriptionPricingSettingsReader.Deserialize(null).Family.Packages.Select(x => x.Id).ToArray();
        var second = SubscriptionPricingSettingsReader.Deserialize(null).Family.Packages.Select(x => x.Id).ToArray();
        var emptyLegacy = SubscriptionPricingSettingsReader.Deserialize("{\"family\":{\"packages\":[]}}")
            .Family.Packages.Select(x => x.Id).ToArray();
        var invalid = SubscriptionPricingSettingsReader.Deserialize("{").Family.Packages.Select(x => x.Id).ToArray();

        first.Should().Equal(expectedIds);
        second.Should().Equal(expectedIds);
        emptyLegacy.Should().Equal(expectedIds);
        invalid.Should().Equal(expectedIds);
        first.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Normalize_PreservesPersistedCustomPackageIds()
    {
        var customId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var normalized = SubscriptionPricingSettingsReader.Normalize(new SubscriptionPricingSettingsDocument
        {
            Family = new FamilyPricingSettings
            {
                Packages =
                [
                    new FamilySizePackageSettings
                    {
                        Id = customId,
                        NameAr = "مخصصة",
                        NameEn = "Custom",
                        MinMembers = 2,
                        MaxMembers = 4,
                        IsActive = true,
                    },
                ],
            },
        });

        normalized.Family.Packages[0].Id.Should().Be(customId);
    }

    [Fact]
    public void Validate_RejectsCustomFamilyPackageWithEmptyId()
    {
        var document = SubscriptionPricingSettingsReader.Normalize(new SubscriptionPricingSettingsDocument
        {
            Family = new FamilyPricingSettings
            {
                Packages =
                [
                    new FamilySizePackageSettings
                    {
                        Id = Guid.Empty,
                        NameAr = "مخصصة",
                        NameEn = "Custom",
                        MinMembers = 2,
                        MaxMembers = 4,
                        IsActive = true,
                    },
                ],
            },
        });

        var error = SubscriptionPricingSettingsValidator.Validate(document);
        error.Should().NotBeNull();
        error!.Code.Should().Be("family.package_id_required");
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
