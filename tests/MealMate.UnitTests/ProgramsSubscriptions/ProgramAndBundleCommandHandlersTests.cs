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
}
