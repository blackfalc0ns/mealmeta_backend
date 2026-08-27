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
    public async Task Update_rejects_when_status_is_published()
    {
        using var db = CreateDb();
        var clock = CreateClock(DateTime.UtcNow);

        var template = new DocumentTemplate
        {
            Kind = DocumentTemplateKind.Invoice,
            Status = DocumentTemplateStatus.Published,
            NameAr = "Published",
            NameEn = "Published",
            PageWidthPx = 794,
            PageHeightPx = 1123,
            Version = 2,
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
            "[]");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }
}
