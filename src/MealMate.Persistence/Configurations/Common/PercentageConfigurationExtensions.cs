using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MealMate.Persistence.Configurations.Common;

public static class PercentageConfigurationExtensions
{
    public static PropertyBuilder<Percentage> HasPercentageConversion(
        this PropertyBuilder<Percentage> builder)
    {
        builder
            .HasConversion(new PercentageValueConverter(), new PercentageValueComparer())
            .HasPrecision(9, 6);

        return builder;
    }

    public static PropertyBuilder<Percentage?> HasNullablePercentageConversion(
        this PropertyBuilder<Percentage?> builder)
    {
        var converter = new ValueConverter<Percentage?, decimal?>(
            p => p == null ? null : p.Value,
            v => v == null ? null : Percentage.FromFraction(v.Value));

        builder
            .HasConversion(converter)
            .HasPrecision(9, 6);

        return builder;
    }
}
