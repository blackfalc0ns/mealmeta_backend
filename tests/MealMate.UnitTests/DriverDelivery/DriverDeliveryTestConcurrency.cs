using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>In-memory test adapters. Production handlers remain fail-closed when these are absent.</summary>
internal static class DriverDeliveryTestConcurrency
{
    public static IApplicationLockManager Locks() => Substitute.For<IApplicationLockManager>();
    public static IApplicationTransactionExecutor Transactions() => new InlineTransactionExecutor();
    public static SaveChangesInterceptor ActiveDriverAccounts() => new ActiveDriverAccountInterceptor();

    private sealed class ActiveDriverAccountInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            EnsureAccounts(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            EnsureAccounts(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void EnsureAccounts(DbContext? context)
        {
            if (context is null) return;
            var users = context.Set<ApplicationUser>();
            var knownIds = users.Local.Select(x => x.Id).ToHashSet();
            foreach (var entry in context.ChangeTracker.Entries<DriverProfile>().Where(x => x.State == EntityState.Added).ToList())
                if (knownIds.Add(entry.Entity.UserId)) users.Add(new ApplicationUser { Id = entry.Entity.UserId, UserType = UserType.Driver, AccountStatus = AccountStatus.Active });
        }
    }

    private sealed class InlineTransactionExecutor : IApplicationTransactionExecutor
    {
        public bool HasActiveTransaction => true;
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);

        public Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
