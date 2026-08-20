using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class CreateProgramCommandValidator : AbstractValidator<CreateProgramCommand>
{
    public CreateProgramCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionAr).MaximumLength(2000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(2000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.KeywordsAr).Must(HaveValidKeywords).When(x => x.KeywordsAr is not null)
            .WithMessage("Arabic keywords must contain at most 4 non-empty values of 60 characters or less.");
        RuleFor(x => x.KeywordsEn).Must(HaveValidKeywords).When(x => x.KeywordsEn is not null)
            .WithMessage("English keywords must contain at most 4 non-empty values of 60 characters or less.");
        RuleFor(x => x.ShortCode).MaximumLength(10).When(x => x.ShortCode is not null);
        RuleFor(x => x.ImageStorageKey).MaximumLength(CatalogImageStorage.MaxKeyLength).When(x => x.ImageStorageKey is not null);
        RuleFor(x => x.ImageStorageKey)
            .Must(key => CatalogImageStorage.HasAllowedPrefix(CatalogImageStorage.Normalize(key), CatalogImageStorage.ProgramPrefix))
            .When(x => !string.IsNullOrWhiteSpace(x.ImageStorageKey))
            .WithMessage("Program image must be stored under uploads/admin/programs/.");
    }

    private static bool HaveValidKeywords(IReadOnlyList<string>? values) =>
        values is null || values.Count <= 4 && values.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 60);
}

public sealed class UpdateProgramCommandValidator : AbstractValidator<UpdateProgramCommand>
{
    public UpdateProgramCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DescriptionAr).MaximumLength(2000).When(x => x.DescriptionAr is not null);
        RuleFor(x => x.DescriptionEn).MaximumLength(2000).When(x => x.DescriptionEn is not null);
        RuleFor(x => x.KeywordsAr).Must(HaveValidKeywords).When(x => x.KeywordsAr is not null)
            .WithMessage("Arabic keywords must contain at most 4 non-empty values of 60 characters or less.");
        RuleFor(x => x.KeywordsEn).Must(HaveValidKeywords).When(x => x.KeywordsEn is not null)
            .WithMessage("English keywords must contain at most 4 non-empty values of 60 characters or less.");
        RuleFor(x => x.ImageStorageKey).MaximumLength(CatalogImageStorage.MaxKeyLength).When(x => x.ImageStorageKey is not null);
        RuleFor(x => x.ImageStorageKey)
            .Must(key => CatalogImageStorage.HasAllowedPrefix(CatalogImageStorage.Normalize(key), CatalogImageStorage.ProgramPrefix))
            .When(x => !string.IsNullOrWhiteSpace(x.ImageStorageKey))
            .WithMessage("Program image must be stored under uploads/admin/programs/.");
    }

    private static bool HaveValidKeywords(IReadOnlyList<string>? values) =>
        values is null || values.Count <= 4 && values.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 60);
}

public sealed class ChangeProgramStatusCommandValidator : AbstractValidator<ChangeProgramStatusCommand>
{
    public ChangeProgramStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

// Handlers
public sealed class ProgramCommandHandlers :
    ICommandHandler<CreateProgramCommand, ProgramDto>,
    ICommandHandler<UpdateProgramCommand, ProgramDto>,
    ICommandHandler<ChangeProgramStatusCommand, ProgramDto>,
    IQueryHandler<GetProgramByIdQuery, ProgramDto>,
    IQueryHandler<GetProgramsListQuery, PagedResult<ProgramDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public ProgramCommandHandlers(IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<ProgramDto>> Handle(CreateProgramCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var exists = await _db.Programs.AnyAsync(x => x.Code == code, cancellationToken);
        if (exists)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Program with code '{code}' already exists.");
        }

        var shortCode = string.IsNullOrWhiteSpace(request.ShortCode)
            ? null
            : request.ShortCode.Trim().ToUpperInvariant();

        var program = new Program
        {
            Code = code,
            ShortCode = shortCode,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            KeywordsAr = NormalizeKeywords(request.KeywordsAr),
            KeywordsEn = NormalizeKeywords(request.KeywordsEn),
            ImageStorageKey = CatalogImageStorage.Normalize(request.ImageStorageKey),
            Status = ProgramStatus.Draft,
            IsActive = false,
            AvailableForFamily = request.AvailableForFamily,
            SortOrder = request.SortOrder,
        };

        _db.Programs.Add(program);
        await _db.SaveChangesAsync(cancellationToken);

        var standards = request.NutritionStandards;
        if (standards is null || standards.Count == 0)
        {
            ProgramNutritionDefaults.TryGet(shortCode, out standards);
        }

        if (standards is { Count: > 0 })
        {
            foreach (var input in standards)
            {
                var category = input.MealCategory.Trim().ToUpperInvariant();
                _db.ProgramNutritionStandards.Add(new ProgramNutritionStandard
                {
                    ProgramId = program.Id,
                    MealCategory = category,
                    KcalMin = input.KcalMin,
                    KcalMax = input.KcalMax,
                    ProteinGMin = input.ProteinGMin,
                    ProteinGMax = input.ProteinGMax,
                    CarbMode = input.CarbMode.Trim().ToUpperInvariant(),
                    CarbsGMin = input.CarbsGMin,
                    CarbsGMax = input.CarbsGMax,
                    FatGMin = input.FatGMin,
                    FatGMax = input.FatGMax,
                    FiberGMin = input.FiberGMin,
                    CookedProteinGMin = input.CookedProteinGMin,
                    CookedProteinGMax = input.CookedProteinGMax,
                    CookedCarbsGMin = input.CookedCarbsGMin,
                    CookedCarbsGMax = input.CookedCarbsGMax,
                    KetoDayNetCarbsLimit = input.KetoDayNetCarbsLimit,
                    StandardVersion = string.IsNullOrWhiteSpace(input.StandardVersion)
                        ? ProgramNutritionDefaults.StandardVersion
                        : input.StandardVersion.Trim(),
                    IsActive = input.IsActive,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry(
            "ProgramCreated",
            nameof(Program),
            program.Id,
            After: new { program.Code, program.ShortCode, program.NameEn, program.Status },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapProgramDto(program);
    }

    public async Task<Result<ProgramDto>> Handle(UpdateProgramCommand request, CancellationToken cancellationToken)
    {
        var program = await _db.Programs
            .Include(x => x.Bundles)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (program is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.Id}' was not found.");
        }

        program.NameAr = request.NameAr.Trim();
        program.NameEn = request.NameEn.Trim();
        program.DescriptionAr = request.DescriptionAr?.Trim();
        program.DescriptionEn = request.DescriptionEn?.Trim();
        if (request.KeywordsAr is not null) program.KeywordsAr = NormalizeKeywords(request.KeywordsAr);
        if (request.KeywordsEn is not null) program.KeywordsEn = NormalizeKeywords(request.KeywordsEn);
        program.ImageStorageKey = CatalogImageStorage.Normalize(request.ImageStorageKey);
        if (request.ShortCode is not null)
        {
            program.ShortCode = string.IsNullOrWhiteSpace(request.ShortCode)
                ? null
                : request.ShortCode.Trim().ToUpperInvariant();
        }
        program.SortOrder = request.SortOrder;
        program.AvailableForFamily = request.AvailableForFamily;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "ProgramUpdated",
            nameof(Program),
            program.Id,
            After: new { program.NameEn, program.SortOrder },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapProgramDto(program);
    }

    public async Task<Result<ProgramDto>> Handle(ChangeProgramStatusCommand request, CancellationToken cancellationToken)
    {
        var program = await _db.Programs
            .Include(x => x.Bundles)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (program is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.Id}' was not found.");
        }

        var oldStatus = program.Status;
        var newStatus = request.Status;

        // Lifecycle transition validation
        var isValidTransition = (oldStatus, newStatus) switch
        {
            (ProgramStatus.Draft, ProgramStatus.PendingApproval) => true,
            (ProgramStatus.PendingApproval, ProgramStatus.Approved) => true,
            (ProgramStatus.PendingApproval, ProgramStatus.Rejected) => true,
            (ProgramStatus.Approved, ProgramStatus.Active) => true,
            (ProgramStatus.Rejected, ProgramStatus.Draft) => true,
            (ProgramStatus.Active, ProgramStatus.Suspended) => true,
            (ProgramStatus.Active, ProgramStatus.Archived) => true,
            (ProgramStatus.Suspended, ProgramStatus.Active) => true,
            (ProgramStatus.Suspended, ProgramStatus.Archived) => true,
            _ when oldStatus == newStatus => true,
            _ => false,
        };

        if (!isValidTransition)
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidStateTransition,
                $"Cannot transition program status from '{oldStatus}' to '{newStatus}'.");
        }

        if (newStatus == ProgramStatus.Active)
        {
            var hasBundle = await _db.Bundles.AsNoTracking().AnyAsync(x => x.ProgramId == program.Id && x.Status == ProgramStatus.Active && x.IsActive, cancellationToken);
            var requiredSlots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => x.ProgramId == program.Id && x.IsRequired).Select(x => x.Slot).ToListAsync(cancellationToken);
            var requiredNutritionCategories = requiredSlots.Select(NutritionMealCategories.FromSlot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var nutritionCategories = await _db.ProgramNutritionStandards.AsNoTracking().Where(x => x.ProgramId == program.Id && x.IsActive).Select(x => x.MealCategory).Distinct().ToListAsync(cancellationToken);
            var hasSlots = requiredSlots.Count > 0;
            var hasNutrition = requiredNutritionCategories.Count > 0 && requiredNutritionCategories.All(category => nutritionCategories.Contains(category, StringComparer.OrdinalIgnoreCase));
            if (!hasBundle || !hasSlots || !hasNutrition)
                return Error.Validation("program.not_ready_for_activation", "Program activation requires at least one active bundle, required meal slots, and active nutrition standards.");
        }

        program.Status = newStatus;
        program.StatusReason = request.Reason;
        program.IsActive = newStatus == ProgramStatus.Active;

        // If published / approved, create PackageContentVersion snapshot
        if (newStatus is ProgramStatus.Approved or ProgramStatus.Active && oldStatus != newStatus)
        {
            var versionNumber = await _db.PackageContentVersions
                .Where(x => x.ProgramId == program.Id)
                .CountAsync(cancellationToken) + 1;

            var snapshotJson = JsonSerializer.Serialize(new
            {
                ProgramId = program.Id,
                program.Code,
                program.NameAr,
                program.NameEn,
                program.DescriptionAr,
                program.DescriptionEn,
                program.KeywordsAr,
                program.KeywordsEn,
                Bundles = program.Bundles.Select(b => new
                {
                    b.Id,
                    b.Code,
                    b.NameAr,
                    b.NameEn,
                    b.MealsPerDay,
                })
            });

            var contentVersion = new PackageContentVersion
            {
                ProgramId = program.Id,
                VersionNumber = versionNumber,
                ContentSnapshotJson = snapshotJson,
                EffectiveFromUtc = _clock.UtcNow,
                IsActive = true,
                ChangeSummary = request.Reason ?? $"Status changed to {newStatus}",
            };

            _db.PackageContentVersions.Add(contentVersion);
            program.CurrentVersionId = contentVersion.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "ProgramStatusChanged",
            nameof(Program),
            program.Id,
            After: new { PreviousStatus = oldStatus, NewStatus = newStatus, Reason = request.Reason },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapProgramDto(program);
    }

    public async Task<Result<ProgramDto>> Handle(GetProgramByIdQuery request, CancellationToken cancellationToken)
    {
        var program = await _db.Programs
            .Include(x => x.Bundles)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (program is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.Id}' was not found.");
        }

        return MapProgramDto(program);
    }

    public async Task<Result<PagedResult<ProgramDto>>> Handle(GetProgramsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Programs.Include(x => x.Bundles).AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(search) ||
                x.NameAr.ToLower().Contains(search) ||
                x.NameEn.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderBy(x => x.SortOrder)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapProgramDto).ToList();

        return new PagedResult<ProgramDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    private static ProgramDto MapProgramDto(Program p) => new(
        p.Id,
        p.Code,
        p.ShortCode,
        p.NameAr,
        p.NameEn,
        p.DescriptionAr,
        p.DescriptionEn,
        p.KeywordsAr,
        p.KeywordsEn,
        p.Status,
        p.StatusReason,
        p.IsActive,
        p.AvailableForFamily,
        p.SortOrder,
        p.CurrentVersionId,
        p.CreatedAtUtc,
        p.Bundles?.Select(b => new BundleDto(
            b.Id,
            b.ProgramId,
            b.Code,
            b.NameAr,
            b.NameEn,
            b.MealsPerDay,
            b.BreakfastCount,
            b.LunchCount,
            b.DinnerCount,
            b.SnackCount,
            b.Description,
            b.DescriptionAr,
            b.DescriptionEn,
            b.Status,
            b.StatusReason,
            b.IsActive,
            b.AvailableForFamily,
            b.SortOrder,
            b.CreatedAtUtc,
            b.ImageStorageKey,
            CatalogImageStorage.ToContentUrl(b.ImageStorageKey))).ToList() ?? (IReadOnlyList<BundleDto>)Array.Empty<BundleDto>(),
        p.ImageStorageKey,
        CatalogImageStorage.ToContentUrl(p.ImageStorageKey));

    private static string[] NormalizeKeywords(IReadOnlyList<string>? values) => values?
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(4)
        .ToArray() ?? [];
}
