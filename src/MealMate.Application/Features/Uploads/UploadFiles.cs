using System.Text.RegularExpressions;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Validation;

namespace MealMate.Application.Features.Uploads;

public sealed record UploadedFileDto(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ReadUrl,
    string? Folder,
    string? Scope);

public sealed record UploadFileCommand(
    string FileName,
    string ContentType,
    Stream Content,
    string? Folder = null,
    string? Scope = null) : ICommand<UploadedFileDto>;

public sealed class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    private static readonly Regex SafeSegmentRegex = new(
        @"^[a-zA-Z0-9_\-/]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public UploadFileCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);

        RuleFor(x => x.Folder)
            .MaximumLength(100)
            .WithMessageCode(MessageCodes.Validation.MaxLength)
            .Must(f => f is null || SafeSegmentRegex.IsMatch(f))
            .WithMessage("Folder contains invalid characters. Only letters, digits, dashes, underscores, and slashes are allowed.")
            .When(x => x.Folder is not null);

        RuleFor(x => x.Scope)
            .MaximumLength(100)
            .WithMessageCode(MessageCodes.Validation.MaxLength)
            .Must(s => s is null || SafeSegmentRegex.IsMatch(s))
            .WithMessage("Scope contains invalid characters. Only letters, digits, dashes, underscores, and slashes are allowed.")
            .When(x => x.Scope is not null);
    }
}

public sealed class UploadFileCommandHandler : ICommandHandler<UploadFileCommand, UploadedFileDto>
{
    private const long MaxBytes = 25 * 1024 * 1024;
    private static readonly TimeSpan ReadUrlTtl = TimeSpan.FromHours(1);

    private readonly IFileStorage _storage;

    public UploadFileCommandHandler(IFileStorage storage)
    {
        _storage = storage;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        // Reject empty file stream if seekable and length is zero
        if (request.Content.CanSeek && request.Content.Length == 0)
        {
            return Error.Validation(
                MessageCodes.Validation.RequiredField,
                "Uploaded file is empty.");
        }

        // Sanitize folder and scope: only safe path segments
        var sanitizedFolder = SanitizeSegment(request.Folder) ?? "misc";
        var sanitizedScope = SanitizeSegment(request.Scope) ?? "general";

        // Build folder path for storage
        var storageFolder = $"uploads/{sanitizedScope}/{sanitizedFolder}";

        // Save through IFileStorage
        StoredFile stored;
        try
        {
            stored = await _storage.SaveAsync(
                request.Content,
                request.FileName,
                request.ContentType,
                storageFolder,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Error.Failure(
                MessageCodes.Common.ServerError,
                $"Failed to save file: {ex.Message}");
        }

        // Check final size after save
        if (stored.SizeBytes > MaxBytes)
        {
            try
            {
                await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            }
            catch
            {
                // Cleanup best-effort; ignore failures.
            }

            return Error.Validation(
                MessageCodes.Validation.MaxLength,
                "File exceeds 25MB limit.");
        }

        // Get read URL with 1-hour TTL
        string readUrl;
        try
        {
            readUrl = await _storage.GetReadUrlAsync(stored.StorageKey, ReadUrlTtl, cancellationToken);
        }
        catch (Exception ex)
        {
            return Error.Failure(
                MessageCodes.Common.ServerError,
                $"Failed to generate read URL: {ex.Message}");
        }

        return new UploadedFileDto(
            stored.StorageKey,
            stored.FileName,
            stored.ContentType,
            stored.SizeBytes,
            stored.Checksum,
            readUrl,
            request.Folder,
            request.Scope);
    }

    private static string? SanitizeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        // Remove any characters that are not letters, digits, dash, underscore, or slash
        var sanitized = Regex.Replace(
            segment,
            @"[^a-zA-Z0-9_\-/]",
            "",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Collapse consecutive slashes
        sanitized = Regex.Replace(sanitized, @"/{2,}", "/", RegexOptions.Compiled);

        // Remove leading/trailing slashes and dots (prevent traversal)
        sanitized = sanitized.Trim('/', '.');

        // Reject if traversal patterns remain
        if (sanitized.Contains("..") || sanitized.StartsWith('/') || sanitized.EndsWith('/'))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }
}
