using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests;

public class UploadsControllerTests
{
    [Fact]
    public async Task Upload_valid_image_file_returns_uploaded_file_dto()
    {
        var storage = Substitute.For<IFileStorage>();
        var expectedStored = new StoredFile(
            "uploads/general/images/test-image.png",
            "test-image.png",
            "image/png",
            1024,
            "hash123");

        storage.SaveAsync(
            Arg.Any<Stream>(),
            "test-image.png",
            "image/png",
            "uploads/general/images",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedStored));

        storage.GetReadUrlAsync(
            expectedStored.StorageKey,
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("https://storage.local/uploads/general/images/test-image.png"));

        var handler = new UploadFileCommandHandler(storage, CreateUploadDb(), Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-binary-data"));

        var command = new UploadFileCommand(
            "test-image.png",
            "image/png",
            stream,
            "images",
            "general");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StorageKey.Should().Be("uploads/general/images/test-image.png");
        result.Value.FileName.Should().Be("test-image.png");
        result.Value.ContentType.Should().Be("image/png");
        result.Value.ReadUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fgeneral%2Fimages%2Ftest-image.png");
    }

    [Fact]
    public async Task Upload_valid_document_pdf_returns_uploaded_file_dto()
    {
        var storage = Substitute.For<IFileStorage>();
        var expectedStored = new StoredFile(
            "uploads/documents/contracts/contract.pdf",
            "contract.pdf",
            "application/pdf",
            2048,
            "pdfhash456");

        storage.SaveAsync(
            Arg.Any<Stream>(),
            "contract.pdf",
            "application/pdf",
            "uploads/documents/contracts",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedStored));

        storage.GetReadUrlAsync(
            expectedStored.StorageKey,
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("https://storage.local/uploads/documents/contracts/contract.pdf"));

        var handler = new UploadFileCommandHandler(storage, CreateUploadDb(), Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 fake pdf content"));

        var command = new UploadFileCommand(
            "contract.pdf",
            "application/pdf",
            stream,
            "contracts",
            "documents");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StorageKey.Should().Be("uploads/documents/contracts/contract.pdf");
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.Folder.Should().Be("contracts");
        result.Value.Scope.Should().Be("documents");
    }

    private static MealMate.Persistence.AppDbContext CreateUploadDb()
    {
        var options = new DbContextOptionsBuilder<MealMate.Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MealMate.Persistence.AppDbContext(options, Substitute.For<MealMate.Application.Common.Abstractions.IDomainEventDispatcher>());
    }

}