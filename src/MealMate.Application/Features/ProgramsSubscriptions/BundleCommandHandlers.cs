using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class CreateBundleCommandValidator : AbstractValidator<CreateBundleCommand>
{
    public CreateBundleCommandValidator()
    {
        RuleFor(x => x.ProgramId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BreakfastCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LunchCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DinnerCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SnackCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount > 0)
            .WithMessage("At least one meal slot count must be greater than zero.");
        RuleFor(x => x).Must(x => !x.MealsPerDay.HasValue || x.MealsPerDay.Value == x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount)
            .WithMessage("MealsPerDay must equal the sum of the slot counts.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class UpdateBundleCommandValidator : AbstractValidator<UpdateBundleCommand>
{
    public UpdateBundleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BreakfastCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LunchCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DinnerCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SnackCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(x => x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount > 0)
            .WithMessage("At least one meal slot count must be greater than zero.");
        RuleFor(x => x).Must(x => !x.MealsPerDay.HasValue || x.MealsPerDay.Value == x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount)
            .WithMessage("MealsPerDay must equal the sum of the slot counts.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class ChangeBundleStatusCommandValidator : AbstractValidator<ChangeBundleStatusCommand>
{
    public ChangeBundleStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

// Handlers
public sealed class BundleCommandHandlers :
    ICommandHandler<CreateBundleCommand, BundleDto>,
    ICommandHandler<UpdateBundleCommand, BundleDto>,
    ICommandHandler<ChangeBundleStatusCommand, BundleDto>,
    IQueryHandler<GetBundleByIdQuery, BundleDto>,
    IQueryHandler<GetBundlesListQuery, PagedResult<BundleDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public BundleCommandHandlers(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<BundleDto>> Handle(CreateBundleCommand request, CancellationToken cancellationToken)
    {
        var programExists = await _db.Programs.AnyAsync(x => x.Id == request.ProgramId, cancellationToken);
        if (!programExists)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.ProgramId}' was not found.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var duplicateCode = await _db.Bundles.AnyAsync(x => x.ProgramId == request.ProgramId && x.Code == code, cancellationToken);
        if (duplicateCode)
        {
            return Error.Conflict(MessageCodes.Validation.DuplicateCode, $"Bundle with code '{code}' already exists in this program.");
        }

        var breakfast = Math.Max(0, request.BreakfastCount);
        var lunch = Math.Max(0, request.LunchCount);
        var dinner = Math.Max(0, request.DinnerCount);
        var snack = Math.Max(0, request.SnackCount);
        var mealsPerDay = request.MealsPerDay is > 0
            ? request.MealsPerDay.Value
            : breakfast + lunch + dinner + snack;

        var bundle = new Bundle
        {
            ProgramId = request.ProgramId,
            Code = code,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            BreakfastCount = breakfast,
            LunchCount = lunch,
            DinnerCount = dinner,
            SnackCount = snack,
            MealsPerDay = Math.Max(1, mealsPerDay),
            Description = request.Description?.Trim(),
            Status = ProgramStatus.Draft,
            IsActive = false,
            SortOrder = request.SortOrder,
        };

        _db.Bundles.Add(bundle);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "BundleCreated",
            nameof(Bundle),
            bundle.Id,
            After: new
            {
                bundle.ProgramId,
                bundle.Code,
                bundle.MealsPerDay,
                bundle.BreakfastCount,
                bundle.LunchCount,
                bundle.DinnerCount,
                bundle.SnackCount,
            },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapBundleDto(bundle);
    }

    public async Task<Result<BundleDto>> Handle(UpdateBundleCommand request, CancellationToken cancellationToken)
    {
        var bundle = await _db.Bundles.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (bundle is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle with ID '{request.Id}' was not found.");
        }

        var breakfast = Math.Max(0, request.BreakfastCount);
        var lunch = Math.Max(0, request.LunchCount);
        var dinner = Math.Max(0, request.DinnerCount);
        var snack = Math.Max(0, request.SnackCount);
        var mealsPerDay = request.MealsPerDay is > 0
            ? request.MealsPerDay.Value
            : breakfast + lunch + dinner + snack;

        bundle.NameAr = request.NameAr.Trim();
        bundle.NameEn = request.NameEn.Trim();
        bundle.BreakfastCount = breakfast;
        bundle.LunchCount = lunch;
        bundle.DinnerCount = dinner;
        bundle.SnackCount = snack;
        bundle.MealsPerDay = Math.Max(1, mealsPerDay);
        bundle.Description = request.Description?.Trim();
        bundle.SortOrder = request.SortOrder;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "BundleUpdated",
            nameof(Bundle),
            bundle.Id,
            After: new
            {
                bundle.NameEn,
                bundle.MealsPerDay,
                bundle.BreakfastCount,
                bundle.LunchCount,
                bundle.DinnerCount,
                bundle.SnackCount,
            },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapBundleDto(bundle);
    }

    public async Task<Result<BundleDto>> Handle(ChangeBundleStatusCommand request, CancellationToken cancellationToken)
    {
        var bundle = await _db.Bundles.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (bundle is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle with ID '{request.Id}' was not found.");
        }

        if (request.Status == ProgramStatus.Active)
        {
            if (!await _db.Programs.AsNoTracking().AnyAsync(x => x.Id == bundle.ProgramId && (x.Status == ProgramStatus.Approved || x.Status == ProgramStatus.Active), cancellationToken))
                return Error.Validation("bundle.program_inactive", "The parent program must be approved before activating this bundle.");
            var requiredSlots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => x.ProgramId == bundle.ProgramId && x.IsRequired).Select(x => x.Slot).ToListAsync(cancellationToken);
            var configured = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["breakfast"] = bundle.BreakfastCount, ["lunch"] = bundle.LunchCount, ["dinner"] = bundle.DinnerCount, ["snack"] = bundle.SnackCount };
            if (bundle.MealsPerDay <= 0 || configured.Values.Sum() != bundle.MealsPerDay || configured.Where(x => x.Value > 0).Any(x => !requiredSlots.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
                return Error.Validation("bundle.slot_requirements_invalid", "Bundle slot quantities must match MealsPerDay and use program-supported slots.");
        }
        bundle.Status = request.Status;
        bundle.StatusReason = request.Reason;
        bundle.IsActive = request.Status == ProgramStatus.Active;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "BundleStatusChanged",
            nameof(Bundle),
            bundle.Id,
            After: new { NewStatus = request.Status, Reason = request.Reason },
            SourceChannel: "admin-dashboard"), cancellationToken);

        return MapBundleDto(bundle);
    }

    public async Task<Result<BundleDto>> Handle(GetBundleByIdQuery request, CancellationToken cancellationToken)
    {
        var bundle = await _db.Bundles.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (bundle is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle with ID '{request.Id}' was not found.");
        }

        return MapBundleDto(bundle);
    }

    public async Task<Result<PagedResult<BundleDto>>> Handle(GetBundlesListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Bundles.AsNoTracking().AsQueryable();

        if (request.ProgramId.HasValue)
        {
            query = query.Where(x => x.ProgramId == request.ProgramId.Value);
        }

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

        var dtos = items.Select(MapBundleDto).ToList();

        return new PagedResult<BundleDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    private static BundleDto MapBundleDto(Bundle b) => new(
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
        b.Status,
        b.StatusReason,
        b.IsActive,
        b.SortOrder,
        b.CreatedAtUtc);
}
