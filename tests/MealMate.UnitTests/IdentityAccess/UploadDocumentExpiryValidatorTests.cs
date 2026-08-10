using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using NSubstitute;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class UploadDocumentExpiryValidatorTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Missing_expiry_is_rejected()
    {
        var result = CreateValidator().Validate(CreateCommand(null));

        Assert.Contains(result.Errors, error => error.PropertyName == "ExpiresAtUtc");
    }

    [Fact]
    public void Past_expiry_is_rejected()
    {
        var result = CreateValidator().Validate(CreateCommand(Now.AddMinutes(-1)));

        Assert.Contains(result.Errors, error => error.PropertyName == "ExpiresAtUtc");
    }

    [Fact]
    public void Future_utc_expiry_is_accepted()
    {
        var result = CreateValidator().Validate(CreateCommand(Now.AddDays(1)));

        Assert.DoesNotContain(result.Errors, error => error.PropertyName == "ExpiresAtUtc");
    }

    [Fact]
    public void Restaurant_logo_does_not_require_expiry()
    {
        var result = CreateValidator().Validate(CreateLogoCommand("image/png"));

        Assert.DoesNotContain(result.Errors, error => error.PropertyName == "ExpiresAtUtc");
    }

    [Fact]
    public void Restaurant_logo_rejects_non_image_content()
    {
        var result = CreateValidator().Validate(CreateLogoCommand("application/pdf"));

        Assert.Contains(result.Errors, error => error.PropertyName == "ContentType");
    }

    private static UploadDocumentCommandValidator CreateValidator()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new UploadDocumentCommandValidator(clock);
    }

    private static UploadDocumentCommand CreateCommand(DateTime? expiresAtUtc) =>
        new(
            DocumentOwnerType.Registration,
            Guid.NewGuid(),
            DocumentType.CommercialLicense,
            "license.pdf",
            "application/pdf",
            Stream.Null,
            expiresAtUtc);

    private static UploadDocumentCommand CreateLogoCommand(string contentType) =>
        new(
            DocumentOwnerType.Registration,
            Guid.NewGuid(),
            DocumentType.ProfileImage,
            "restaurant-logo.png",
            contentType,
            Stream.Null);
}
