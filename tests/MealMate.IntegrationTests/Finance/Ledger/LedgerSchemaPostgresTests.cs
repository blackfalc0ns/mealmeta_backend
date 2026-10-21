using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.Finance.Ledger;

public sealed class LedgerSchemaPostgresTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _connectionString = _database.GetConnectionString();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task FullMigrationChainAppliesAndEmptyLedgerMigrationCanRollBackAndReapply()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        (await db.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'TR_JournalEntries_LedgerBalance') AS \"Value\"").SingleAsync()).Should().BeTrue();

        await db.Database.MigrateAsync("20261021120000_Phase01GovernanceHardening");
        (await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"AccountingPeriods\"') IS NOT NULL AS \"Value\"").SingleAsync()).Should().BeFalse();

        await db.Database.MigrateAsync();
        (await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"AccountingPeriods\"') IS NOT NULL AS \"Value\"").SingleAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task RawSqlPostingIsDeferredBalancedAndRejectsUnbalancedFinalState()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var entityId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var otherEntityId = Guid.NewGuid();
        var debitId = Guid.NewGuid();
        var creditId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        db.LegalEntities.Add(new LegalEntity
        {
            Id = entityId, LegalNameAr = "اختبار", LegalNameEn = "Test", RegistrationNo = $"LEDGER-{entityId:N}",
            Currency = "KWD", Status = LegalEntityStatus.Active,
        });
        db.LegalEntities.Add(new LegalEntity
        {
            Id = otherEntityId, LegalNameAr = "كيان آخر", LegalNameEn = "Other entity", RegistrationNo = $"LEDGER-{otherEntityId:N}",
            Currency = "KWD", Status = LegalEntityStatus.Active,
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = periodId, LegalEntityId = entityId, StartDate = new DateOnly(2026, 1, 1), EndDateExclusive = new DateOnly(2027, 1, 1),
        });
        db.ChartOfAccounts.AddRange(
            new ChartOfAccount { Id = debitId, LegalEntityId = entityId, Code = "1000", NameAr = "مدين", NameEn = "Debit", AccountType = AccountType.Asset, NormalBalance = AccountNormalBalance.Debit, IsActive = true, IsPostable = true },
            new ChartOfAccount { Id = creditId, LegalEntityId = entityId, Code = "2000", NameAr = "دائن", NameEn = "Credit", AccountType = AccountType.Liability, NormalBalance = AccountNormalBalance.Credit, IsActive = true, IsPostable = true },
            new ChartOfAccount { Id = otherAccountId, LegalEntityId = otherEntityId, Code = "1000", NameAr = "أصل", NameEn = "Asset", AccountType = AccountType.Asset, NormalBalance = AccountNormalBalance.Debit, IsActive = true, IsPostable = true });
        var legacyAccountId = Guid.NewGuid();
        var legacyJournalId = Guid.NewGuid();
        db.ChartOfAccounts.Add(new ChartOfAccount
        {
            Id = legacyAccountId, LegalEntityId = null, Code = $"LEGACY-{legacyAccountId:N}", NameAr = "قديم", NameEn = "Legacy",
            AccountType = AccountType.Asset, NormalBalance = null, IsActive = true,
        });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = legacyJournalId, EntryNumber = $"LEGACY-{legacyJournalId:N}", Description = "Legacy unclassified journal",
            LegalEntityId = null, AccountingPeriodId = null, BusinessDate = null, CurrencyCode = null, PayloadHash = null,
        });
        await db.SaveChangesAsync();
        (await db.ChartOfAccounts.AsNoTracking().SingleAsync(x => x.Id == legacyAccountId)).LegalEntityId.Should().BeNull();
        (await db.JournalEntries.AsNoTracking().SingleAsync(x => x.Id == legacyJournalId)).LegalEntityId.Should().BeNull();

        var journal = NewDraft(entityId, periodId, debitId, creditId, balanced: true);
        db.JournalEntries.Add(journal);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"JournalEntries\" SET \"Status\" = 2, \"PostedAtUtc\" = now() WHERE \"Id\" = {journal.Id}");

        var directJournalMutation = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"JournalEntries\" SET \"Description\" = 'rewritten' WHERE \"Id\" = {journal.Id}");
        await directJournalMutation.Should().ThrowAsync<Npgsql.PostgresException>()
            .WithMessage("*ledger.posted_journal_immutable*");
        var directLineMutation = async () => await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"JournalEntryLines\" WHERE \"JournalEntryId\" = {journal.Id}");
        await directLineMutation.Should().ThrowAsync<Npgsql.PostgresException>()
            .WithMessage("*ledger.journal_line_append_only*");

        db.ChangeTracker.Clear();
        var trackedPosted = await db.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == journal.Id);
        trackedPosted.Lines.Single(x => x.Debit is not null).Debit = new Money(11m, "KWD");
        var efOwnedMoneyMutation = async () => await db.SaveChangesAsync();
        await efOwnedMoneyMutation.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ledger.journal_line_append_only*");

        db.ChangeTracker.Clear();
        var crossEntityDraft = new JournalEntry
        {
            Id = Guid.NewGuid(), EntryNumber = $"TEST-{Guid.NewGuid():N}", Description = "cross entity test",
            LegalEntityId = entityId, AccountingPeriodId = periodId, BusinessDate = new DateOnly(2026, 10, 21),
            CurrencyCode = "KWD", PayloadHash = new string('c', 64),
        };
        db.JournalEntries.Add(crossEntityDraft);
        await db.SaveChangesAsync();
        var crossEntityInsert = async () => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "JournalEntryLines" ("Id", "LegalEntityId", "JournalEntryId", "AccountId", "Debit_Amount", "Debit_CurrencyCode")
            VALUES ({Guid.NewGuid()}, {entityId}, {crossEntityDraft.Id}, {otherAccountId}, 1.000, 'KWD')
            """);
        await crossEntityInsert.Should().ThrowAsync<Npgsql.PostgresException>().WithMessage("*FK_JournalEntryLines_Entity_Account*");

        var unbalanced = NewDraft(entityId, periodId, debitId, creditId, balanced: false);
        db.JournalEntries.Add(unbalanced);
        await db.SaveChangesAsync();
        var act = async () => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"JournalEntries\" SET \"Status\" = 2, \"PostedAtUtc\" = now() WHERE \"Id\" = {unbalanced.Id}");
        await act.Should().ThrowAsync<Npgsql.PostgresException>().WithMessage("*ledger.posted_journal_not_balanced*");
    }

    private static JournalEntry NewDraft(Guid entityId, Guid periodId, Guid debitId, Guid creditId, bool balanced)
    {
        var id = Guid.NewGuid();
        return new JournalEntry
        {
            Id = id, EntryNumber = $"TEST-{id:N}", Description = "test", LegalEntityId = entityId, AccountingPeriodId = periodId,
            BusinessDate = new DateOnly(2026, 10, 21), CurrencyCode = "KWD", PayloadHash = new string('a', 64),
            Lines = new List<JournalEntryLine>
            {
                new() { Id = Guid.NewGuid(), LegalEntityId = entityId, AccountId = debitId, Debit = new Money(10m, "KWD") },
                new() { Id = Guid.NewGuid(), LegalEntityId = entityId, AccountId = creditId, Credit = new Money(balanced ? 10m : 9m, "KWD") },
            },
        };
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options,
        Substitute.For<IDomainEventDispatcher>());
}
