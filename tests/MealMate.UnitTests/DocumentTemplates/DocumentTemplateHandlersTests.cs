using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DocumentTemplates;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DocumentTemplates;

public class DocumentTemplateHandlersTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static IDateTimeProvider CreateClock(DateTime utcNow)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

  [Fact]
    public async Task Publish_archives_previous_published_of_same_kind()
    {
        using var db = CreateDb();
        var now = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var clock = CreateClock(now);

        var templateA = new DocumentTemplate
        {
            Kind = DocumentTemplateKind.Invoice,
            Status = DocumentTemplateStatus.Published,
            NameAr = "A",
            NameEn = "A",
            PageWidthPx = 794,
            PageHeightPx = 1123,
            Version = 3,
            PublishedAtUtc = now.AddDays(-1),
        };
        var templateB = new DocumentTemplate
        {
            Kind = DocumentTemplateKind.Invoice,
            Status = DocumentTemplateStatus.Draft,
            NameAr = "B",
            NameEn = "B",
            PageWidthPx = 794,
            PageHeightPx = 1123,
            Version = 1,
        };
        db.DocumentTemplates.AddRange(templateA, templateB);
        await db.SaveChangesAsync();

        var handler = new PublishDocumentTemplateCommandHandler(db, clock);
        var result = await handler.Handle(new PublishDocumentTemplateCommand(templateB.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var refreshedA = await db.DocumentTemplates.AsNoTracking().SingleAsync(x => x.Id == templateA.Id);
        var refreshedB = await db.DocumentTemplates.AsNoTracking().SingleAsync(x => x.Id == templateB.Id);

        refreshedA.Status.Should().Be(DocumentTemplateStatus.Archived);
        refreshedB.Status.Should().Be(DocumentTemplateStatus.Published);
        refreshedB.Version.Should().Be(2);
        refreshedB.PublishedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task Update_allows_published_template_and_bumps_version()
    {
        using var db = CreateDb();
        var now = new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
        var clock = CreateClock(now);

        var template = new DocumentTemplate
        {
            Kind = DocumentTemplateKind.Invoice,
            Status = DocumentTemplateStatus.Published,
            NameAr = "Published",
            NameEn = "Published",
            PageWidthPx = 794,
            PageHeightPx = 1123,
            PageSizeLabel = "A4",
            BackgroundColor = "#FFFFFF",
            AccentColor = "#0B6E4F",
            LayoutJson = "[]",
            Version = 2,
            PublishedAtUtc = now.AddDays(-1),
        };
        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync();

        var handler = new UpdateDocumentTemplateCommandHandler(db, clock);
        var command = new UpdateDocumentTemplateCommand(
            template.Id,
            "Updated Ar",
            "Updated En",
            null,
            null,
            "both",
            794,
            1123,
            38,
            "A4",
            "#FFFFFF",
            "#0B6E4F",
            true,
            true,
            """[{"id":"el-1","kind":"text","source":"static","labelAr":"نص","labelEn":"Text","textAr":"أ","textEn":"A","x":10,"y":10,"width":100,"height":40,"visible":true,"locked":false,"zIndex":1}]""");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var refreshed = await db.DocumentTemplates.AsNoTracking().SingleAsync(x => x.Id == template.Id);
        refreshed.NameEn.Should().Be("Updated En");
        refreshed.Status.Should().Be(DocumentTemplateStatus.Published);
        refreshed.Version.Should().Be(3);
        refreshed.LayoutJson.Should().Contain("el-1");
        refreshed.ModifiedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task Seed_creates_two_templates_per_kind_and_publishes_classic()
    {
        using var db = CreateDb();
        var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var clock = CreateClock(now);

        var handler = new SeedDocumentTemplatesCommandHandler(db, clock);
        var result = await handler.Handle(new SeedDocumentTemplatesCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(6);

        var active = await db.DocumentTemplates.AsNoTracking()
            .Where(x => x.Status != DocumentTemplateStatus.Archived)
            .ToListAsync();
        active.Should().HaveCount(6);
        active.Count(x => x.Status == DocumentTemplateStatus.Published).Should().Be(3);
        active.Count(x => x.Status == DocumentTemplateStatus.Draft).Should().Be(3);

        foreach (var kind in Enum.GetValues<DocumentTemplateKind>())
        {
            active.Count(x => x.Kind == kind && x.Status == DocumentTemplateStatus.Published).Should().Be(1);
            active.Count(x => x.Kind == kind && x.Status == DocumentTemplateStatus.Draft).Should().Be(1);
        }

        // Idempotent re-seed keeps six active rows and published set.
        var again = await handler.Handle(new SeedDocumentTemplatesCommand(), CancellationToken.None);
        again.IsSuccess.Should().BeTrue();
        (await db.DocumentTemplates.CountAsync(x => x.Status != DocumentTemplateStatus.Archived)).Should().Be(6);
        (await db.DocumentTemplates.CountAsync(x => x.Status == DocumentTemplateStatus.Published)).Should().Be(3);
    }

    [Fact]
    public async Task Seed_archives_obsolete_templates_outside_production_pack()
    {
        using var db = CreateDb();
        var now = new DateTime(2026, 8, 27, 13, 0, 0, DateTimeKind.Utc);
        var clock = CreateClock(now);

        db.DocumentTemplates.Add(new DocumentTemplate
        {
            Kind = DocumentTemplateKind.Invoice,
            Status = DocumentTemplateStatus.Published,
            NameAr = "قديم",
            NameEn = "Classic Prep Invoice",
            PageWidthPx = 794,
            PageHeightPx = 1123,
            PageSizeLabel = "A4",
            BackgroundColor = "#FFFFFF",
            AccentColor = "#0B6E4F",
            LayoutJson = "[]",
            Version = 1,
            PublishedAtUtc = now.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var handler = new SeedDocumentTemplatesCommandHandler(db, clock);
        var result = await handler.Handle(new SeedDocumentTemplatesCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var obsolete = await db.DocumentTemplates.AsNoTracking()
            .SingleAsync(x => x.NameEn == "Classic Prep Invoice");
        obsolete.Status.Should().Be(DocumentTemplateStatus.Archived);

        var publishedInvoice = await db.DocumentTemplates.AsNoTracking()
            .SingleAsync(x => x.Kind == DocumentTemplateKind.Invoice && x.Status == DocumentTemplateStatus.Published);
        publishedInvoice.NameEn.Should().Be("Ops Invoice Pro");
        publishedInvoice.LayoutJson.Should().Contain("meals");
        publishedInvoice.LayoutJson.Should().Contain("allergies");
    }
}
