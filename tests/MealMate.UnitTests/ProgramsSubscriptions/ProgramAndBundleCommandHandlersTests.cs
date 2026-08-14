using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class ProgramAndBundleCommandHandlersTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task CreateProgram_Succeeds_WhenCodeIsUnique()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new ProgramCommandHandlers(db, audit, clock);
        var command = new CreateProgramCommand("PROG_KETO", "برنامج كيتو", "Keto Program", "وصف عربي", "English desc", SortOrder: 1);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("PROG_KETO");
        result.Value.Status.Should().Be(ProgramStatus.Draft);

        var saved = await db.Programs.FirstOrDefaultAsync(x => x.Code == "PROG_KETO");
        saved.Should().NotBeNull();
        saved!.NameAr.Should().Be("برنامج كيتو");
    }

    [Fact]
    public async Task CreateProgram_Fails_WhenCodeAlreadyExists()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        db.Programs.Add(new Program { Code = "PROG_DUP", NameAr = "قديم", NameEn = "Old" });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();

        var handler = new ProgramCommandHandlers(db, audit, clock);
        var command = new CreateProgramCommand("PROG_DUP", "مكرر", "Duplicate");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task ChangeProgramStatus_Approve_CreatesPackageContentVersion()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var prog = new Program
        {
            Code = "PROG_VER",
            NameAr = "برنامج متنسخ",
            NameEn = "Versioned Program",
            Status = ProgramStatus.PendingApproval,
        };
        db.Programs.Add(prog);
        db.Bundles.Add(new Bundle
        {
            ProgramId = prog.Id,
            Code = "BND_V1",
            NameAr = "باقة 1",
            NameEn = "Bundle 1",
            MealsPerDay = 2,
        });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new ProgramCommandHandlers(db, audit, clock);
        var command = new ChangeProgramStatusCommand(prog.Id, ProgramStatus.Approved, "Approved by admin review");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ProgramStatus.Approved);

        var versions = await db.PackageContentVersions.Where(x => x.ProgramId == prog.Id).ToListAsync();
        versions.Should().HaveCount(1);
        versions[0].ContentSnapshotJson.Should().Contain("BND_V1");
    }

    [Fact]
    public async Task CreateBundle_Succeeds_WhenProgramExistsAndCodeIsUnique()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var prog = new Program { Code = "PROG_BND", NameAr = "برنامج", NameEn = "Program" };
        db.Programs.Add(prog);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var handler = new BundleCommandHandlers(db, audit);

        var command = new CreateBundleCommand(prog.Id, "BND_FULL", "باقة كاملة", "Full Bundle", Description: "3 meals daily", SortOrder: 1, MealsPerDay: 3);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("BND_FULL");
        result.Value.MealsPerDay.Should().Be(3);

        var saved = await db.Bundles.FirstOrDefaultAsync(x => x.Code == "BND_FULL");
        saved.Should().NotBeNull();
        saved!.ProgramId.Should().Be(prog.Id);
    }

    [Fact]
    public async Task CreateProgram_PersistsImageStorageKey()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var handler = new ProgramCommandHandlers(db, Substitute.For<IAuditLogger>(), Substitute.For<IDateTimeProvider>());

        var result = await handler.Handle(
            new CreateProgramCommand("PROG_IMG", "برنامج", "Program", ImageStorageKey: "uploads/admin/programs/wl.webp"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImageStorageKey.Should().Be("uploads/admin/programs/wl.webp");
        result.Value.ImageUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fadmin%2Fprograms%2Fwl.webp");
        (await db.Programs.SingleAsync(x => x.Code == "PROG_IMG")).ImageStorageKey.Should().Be("uploads/admin/programs/wl.webp");
    }

    [Fact]
    public async Task UpdateProgram_SetsAndClearsImageStorageKey()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var program = new Program { Code = "PROG_UPD_IMG", NameAr = "برنامج", NameEn = "Program" };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        var handler = new ProgramCommandHandlers(db, Substitute.For<IAuditLogger>(), Substitute.For<IDateTimeProvider>());

        var set = await handler.Handle(
            new UpdateProgramCommand(program.Id, "برنامج", "Program", null, null, 0, ImageStorageKey: "uploads/admin/programs/set.webp"),
            CancellationToken.None);
        set.IsSuccess.Should().BeTrue();
        set.Value.ImageStorageKey.Should().Be("uploads/admin/programs/set.webp");

        var cleared = await handler.Handle(
            new UpdateProgramCommand(program.Id, "برنامج", "Program", null, null, 0, ImageStorageKey: null),
            CancellationToken.None);
        cleared.IsSuccess.Should().BeTrue();
        cleared.Value.ImageStorageKey.Should().BeNull();
        (await db.Programs.SingleAsync(x => x.Id == program.Id)).ImageStorageKey.Should().BeNull();
    }

    [Fact]
    public async Task CreateBundle_PersistsImageStorageKey()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var prog = new Program { Code = "PROG_BND_IMG", NameAr = "برنامج", NameEn = "Program" };
        db.Programs.Add(prog);
        await db.SaveChangesAsync();
        var handler = new BundleCommandHandlers(db, Substitute.For<IAuditLogger>());

        var result = await handler.Handle(
            new CreateBundleCommand(prog.Id, "BND_IMG", "باقة", "Bundle", ImageStorageKey: "uploads/admin/bundles/b.webp"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ImageStorageKey.Should().Be("uploads/admin/bundles/b.webp");
        result.Value.ImageUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fadmin%2Fbundles%2Fb.webp");
        (await db.Bundles.SingleAsync(x => x.Code == "BND_IMG")).ImageStorageKey.Should().Be("uploads/admin/bundles/b.webp");
    }

    [Fact]
    public async Task UpdateBundle_SetsAndClearsImageStorageKey()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);
        var prog = new Program { Code = "PROG_UPD_BND_IMG", NameAr = "برنامج", NameEn = "Program" };
        db.Programs.Add(prog);
        var bundle = new Bundle { ProgramId = prog.Id, Code = "BND_UPD_IMG", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1 };
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        var handler = new BundleCommandHandlers(db, Substitute.For<IAuditLogger>());

        var set = await handler.Handle(
            new UpdateBundleCommand(bundle.Id, "باقة", "Bundle", 0, 1, 0, 0, null, 0, MealsPerDay: 1, ImageStorageKey: "uploads/admin/bundles/set.webp"),
            CancellationToken.None);
        set.IsSuccess.Should().BeTrue();
        set.Value.ImageStorageKey.Should().Be("uploads/admin/bundles/set.webp");

        var cleared = await handler.Handle(
            new UpdateBundleCommand(bundle.Id, "باقة", "Bundle", 0, 1, 0, 0, null, 0, MealsPerDay: 1, ImageStorageKey: null),
            CancellationToken.None);
        cleared.IsSuccess.Should().BeTrue();
        cleared.Value.ImageStorageKey.Should().BeNull();
        (await db.Bundles.SingleAsync(x => x.Id == bundle.Id)).ImageStorageKey.Should().BeNull();
    }
}
