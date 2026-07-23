using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MealMate.Persistence.Configurations.Common;

public sealed class PercentageValueConverter : ValueConverter<Percentage, decimal>
{
    public PercentageValueConverter()
        : base(
            p => p.Value,
            v => Percentage.FromFraction(v))
    {
    }
}

public sealed class PercentageValueComparer : ValueComparer<Percentage>
{
    public PercentageValueComparer()
        : base(
            (l, r) => l != null && r != null && l.Equals(r),
            p => p == null ? 0 : p.GetHashCode(),
            p => p == null ? null! : Percentage.FromFraction(p.Value))
    {
    }
}
