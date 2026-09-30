using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public static class DriverRegistrationTestStorage
{
    public static IFileStorage Create()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 })));
        return storage;
    }
}

public sealed class RegistrationStorageValidationTests
{
    [Fact]
    public async Task ValidatesEveryProvidedKeyAgainstRealStorageContent()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync("valid", Arg.Any<CancellationToken>()).Returns(new MemoryStream(new byte[] { 1 }));
        storage.OpenReadAsync("empty", Arg.Any<CancellationToken>()).Returns(new MemoryStream());
        storage.OpenReadAsync("missing", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new FileNotFoundException());

        (await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(storage, new[] { "valid" }, CancellationToken.None)).Should().BeTrue();
        (await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(storage, new[] { "empty" }, CancellationToken.None)).Should().BeFalse();
        (await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(storage, new[] { "missing" }, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingStorageIsRejectedWhenAFileKeyWasSubmitted()
    {
        (await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(null, new[] { "file-key" }, CancellationToken.None)).Should().BeFalse();
    }
}
