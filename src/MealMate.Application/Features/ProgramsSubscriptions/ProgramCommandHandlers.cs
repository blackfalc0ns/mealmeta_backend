using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
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
    }
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
    }
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

        var program = new Program
        {
            Code = code,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            Status = ProgramStatus.Draft,
            IsActive = true,
            SortOrder = request.SortOrder,
        };

        _db.Programs.Add(program);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "ProgramCreated",
            nameof(Program),
            program.Id,
            After: new { program.Code, program.NameEn, program.Status },
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
        program.SortOrder = request.SortOrder;

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

        program.Status = newStatus;
        program.StatusReason = request.Reason;
        program.IsActive = newStatus is ProgramStatus.Active or ProgramStatus.Approved or ProgramStatus.Draft;

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
        p.NameAr,
        p.NameEn,
        p.DescriptionAr,
        p.DescriptionEn,
        p.Status,
        p.StatusReason,
        p.IsActive,
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
            b.Description,
            b.Status,
            b.StatusReason,
            b.IsActive,
            b.SortOrder,
            b.CreatedAtUtc)).ToList() ?? (IReadOnlyList<BundleDto>)Array.Empty<BundleDto>());
}
