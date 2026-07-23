using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Common;

public static class MoneyConfigurationExtensions
{
    /// <summary>
    /// Maps optional <see cref="Money"/> as an owned type. Navigation may be null;
    /// value-type columns stay required when the owned instance is present.
    /// </summary>
    public static void OwnsMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money?>> navigation,
        string columnPrefix,
        bool required = true)
        where TEntity : class
    {
        builder.OwnsOne(navigation, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName($"{columnPrefix}_Amount")
                .HasPrecision(18, 3);

            money.Property(m => m.CurrencyCode)
                .HasColumnName($"{columnPrefix}_CurrencyCode")
                .HasMaxLength(3);
        });

        builder.Navigation(navigation).IsRequired(required);
    }

    public static void OwnsRequiredMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money>> navigation,
        string columnPrefix)
        where TEntity : class
    {
        builder.OwnsOne(
            navigation!,
            money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName($"{columnPrefix}_Amount")
                    .HasPrecision(18, 3);

                money.Property(m => m.CurrencyCode)
                    .HasColumnName($"{columnPrefix}_CurrencyCode")
                    .HasMaxLength(3);
            });

        builder.Navigation(navigation!).IsRequired();
    }
}
