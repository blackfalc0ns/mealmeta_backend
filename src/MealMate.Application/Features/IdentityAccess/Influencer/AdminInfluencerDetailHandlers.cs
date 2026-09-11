using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class GetAdminInfluencerDetailQueryHandler : IQueryHandler<GetAdminInfluencerDetailQuery, AdminInfluencerDetailDto>
{
    private readonly IInfluencerAdminService _service;

    public GetAdminInfluencerDetailQueryHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<AdminInfluencerDetailDto>> Handle(GetAdminInfluencerDetailQuery request, CancellationToken cancellationToken)
        => _service.GetInfluencerDetailAsync(request.Id, cancellationToken);
}

public sealed class UpdateInfluencerStatusCommandHandler : ICommandHandler<UpdateInfluencerStatusCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public UpdateInfluencerStatusCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(UpdateInfluencerStatusCommand request, CancellationToken cancellationToken)
        => _service.UpdateInfluencerStatusAsync(request.Id, request.Status, request.Reason, cancellationToken);
}

public sealed class UpdateInfluencerCommissionRateCommandHandler : ICommandHandler<UpdateInfluencerCommissionRateCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public UpdateInfluencerCommissionRateCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(UpdateInfluencerCommissionRateCommand request, CancellationToken cancellationToken)
        => _service.UpdateInfluencerCommissionRateAsync(request.Id, request.NewRatePercent, request.Reason, cancellationToken);
}

public sealed class UpdateInfluencerBankInfoCommandHandler : ICommandHandler<UpdateInfluencerBankInfoCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public UpdateInfluencerBankInfoCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(UpdateInfluencerBankInfoCommand request, CancellationToken cancellationToken)
        => _service.UpdateInfluencerBankInfoAsync(request.Id, request.BankName, request.AccountHolderName, request.Iban, cancellationToken);
}
