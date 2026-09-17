using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record TransitionOfferStatusCommand(
    Guid OfferId,
    OfferStatus TargetStatus,
    string ActorId,
    string Reason
) : ICommand<OfferDto>;

public sealed class TransitionOfferStatusCommandHandler : ICommandHandler<TransitionOfferStatusCommand, OfferDto>
{
    private readonly IAppDbContext _context;

    public TransitionOfferStatusCommandHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(TransitionOfferStatusCommand request, CancellationToken cancellationToken)
    {
        var offer = await _context.Offers
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", $"Offer with ID '{request.OfferId}' was not found.");
        }

        var canTransition = offer.CanTransitionTo(request.TargetStatus);
        if (canTransition.IsFailure)
        {
            return Error.Validation("Offer.InvalidStateTransition", canTransition.Error);
        }

        var oldStatus = offer.Status;
        offer.TransitionTo(request.TargetStatus, request.ActorId, request.Reason);

        _context.OfferAuditLogs.Add(new OfferAuditLog
        {
            OfferId = offer.Id,
            ActorId = request.ActorId,
            Action = "OfferStatusChanged",
            BeforeStateJson = $"{{\"Status\":\"{oldStatus}\"}}",
            AfterStateJson = $"{{\"Status\":\"{request.TargetStatus}\"}}",
            JustificationReason = request.Reason,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return CreateOfferDraftCommandHandler.MapOfferDto(offer);
    }
}
