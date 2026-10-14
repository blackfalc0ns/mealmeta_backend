using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Advertising.Services;

namespace MealMate.Application.Features.Advertising.Admin;

public sealed record RunAdSweepCommand : ICommand<AdAuctionSweepResult>;

public sealed class RunAdSweepCommandHandler : ICommandHandler<RunAdSweepCommand, AdAuctionSweepResult>
{
    private readonly AdAuctionSweepService _sweepService;

    public RunAdSweepCommandHandler(AdAuctionSweepService sweepService)
    {
        _sweepService = sweepService;
    }

    public async Task<Result<AdAuctionSweepResult>> Handle(RunAdSweepCommand request, CancellationToken cancellationToken)
    {
        var result = await _sweepService.SweepAsync(cancellationToken);
        return Result.Success(result);
    }
}
