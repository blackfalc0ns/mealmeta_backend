using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;

public sealed record DeliveryManagerRegistrationDetailDto(
    DeliveryManagerRegistrationDto Registration,
    IReadOnlyList<DocumentDto> Documents);

public sealed record ListDeliveryManagerRegistrationsQuery(
    VerificationStatus? Status = null,
    Guid? RestaurantId = null) : IQuery<IReadOnlyList<DeliveryManagerRegistrationDto>>;

public sealed class ListDeliveryManagerRegistrationsQueryHandler
    : IQueryHandler<ListDeliveryManagerRegistrationsQuery, IReadOnlyList<DeliveryManagerRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListDeliveryManagerRegistrationsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<DeliveryManagerRegistrationDto>>> Handle(
        ListDeliveryManagerRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.DeliveryManagerRegistrations.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        if (request.RestaurantId.HasValue)
        {
            query = query.Where(r => r.RestaurantId == request.RestaurantId.Value);
        }

        var list = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);

        var dtos = list.Select(r => new DeliveryManagerRegistrationDto(
            r.Id,
            r.RestaurantId,
            r.FullName,
            r.Phone,
            r.Email,
            r.NationalId,
            r.NationalIdExpiryUtc,
            r.ContractExpiryUtc,
            r.CoverageType,
            r.CoverageScopeId,
            r.CoverageScopeName,
            r.Status,
            r.CreatedAtUtc,
            r.RejectionReason,
            r.ChangeRequestNotes,
            r.ResultingUserId)).ToList();

        return dtos;
    }
}

public sealed record GetDeliveryManagerRegistrationByIdQuery(Guid Id) : IQuery<DeliveryManagerRegistrationDetailDto>;

public sealed class GetDeliveryManagerRegistrationByIdQueryHandler
    : IQueryHandler<GetDeliveryManagerRegistrationByIdQuery, DeliveryManagerRegistrationDetailDto>
{
    private readonly IAppDbContext _db;

    public GetDeliveryManagerRegistrationByIdQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<DeliveryManagerRegistrationDetailDto>> Handle(
        GetDeliveryManagerRegistrationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var r = await _db.DeliveryManagerRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id || x.ResultingUserId == request.Id, cancellationToken);

        if (r is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Delivery manager registration not found.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == r.Id)
            .OrderBy(d => d.DocumentType)
            .ToListAsync(cancellationToken);

        var docDtos = docs.Select(document =>
        {
            var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new DocumentDto(
                document.Id,
                document.OwnerType,
                document.OwnerId,
                document.DocumentType,
                document.FileName,
                document.ContentType,
                document.SizeBytes,
                latest?.Status,
                document.ExpiresAtUtc,
                latest?.Reason);
        }).ToList();

        var regDto = new DeliveryManagerRegistrationDto(
            r.Id,
            r.RestaurantId,
            r.FullName,
            r.Phone,
            r.Email,
            r.NationalId,
            r.NationalIdExpiryUtc,
            r.ContractExpiryUtc,
            r.CoverageType,
            r.CoverageScopeId,
            r.CoverageScopeName,
            r.Status,
            r.CreatedAtUtc,
            r.RejectionReason,
            r.ChangeRequestNotes,
            r.ResultingUserId);

        return new DeliveryManagerRegistrationDetailDto(regDto, docDtos);
    }
}

public sealed record AdminDeliveryManagerDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<DeliveryManagerRegistrationDto>;

public sealed class AdminDeliveryManagerDecisionCommandValidator : AbstractValidator<AdminDeliveryManagerDecisionCommand>
{
    public AdminDeliveryManagerDecisionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Action).NotEmpty().Must(a =>
            a.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("reject", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("request-changes", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class AdminDeliveryManagerDecisionCommandHandler
    : ICommandHandler<AdminDeliveryManagerDecisionCommand, DeliveryManagerRegistrationDto>
{
    public Task<Result<DeliveryManagerRegistrationDto>> Handle(
        AdminDeliveryManagerDecisionCommand request,
        CancellationToken cancellationToken)
    {
        // The delivery-manager registration workflow is provisioned automatically by
        // the restaurant (no admin approval stage). The legacy decision endpoints are
        // retired: they must not mutate user status, registration state, documents or
        // audit history. Account governance happens through the admin user-status APIs.
        Result<DeliveryManagerRegistrationDto> conflict = Error.Conflict(
            "delivery_manager.admin_approval_not_required",
            "Delivery managers are onboarded by their restaurant and do not require admin approval.");
        return Task.FromResult(conflict);
    }
}
