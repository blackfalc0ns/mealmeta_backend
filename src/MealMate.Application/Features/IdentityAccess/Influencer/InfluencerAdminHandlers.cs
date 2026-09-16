using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class ListInfluencersQueryHandler
    : IQueryHandler<ListInfluencersQuery, IReadOnlyList<AdminInfluencerDto>>
{
    private readonly IInfluencerAdminService _service;

    public ListInfluencersQueryHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<AdminInfluencerDto>>> Handle(
        ListInfluencersQuery request,
        CancellationToken cancellationToken)
        => _service.ListInfluencersAsync(cancellationToken);
}

public sealed class ListEligibleCustomersForPromotionQueryHandler
    : IQueryHandler<ListEligibleCustomersForPromotionQuery, IReadOnlyList<EligibleCustomerDto>>
{
    private readonly IInfluencerAdminService _service;

    public ListEligibleCustomersForPromotionQueryHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<EligibleCustomerDto>>> Handle(
        ListEligibleCustomersForPromotionQuery request,
        CancellationToken cancellationToken)
        => _service.ListEligibleCustomersAsync(request.Search, cancellationToken);
}

public sealed class RegisterInfluencerCommandHandler : ICommandHandler<RegisterInfluencerCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public RegisterInfluencerCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(RegisterInfluencerCommand request, CancellationToken cancellationToken)
        => _service.RegisterInfluencerAsync(request, cancellationToken);
}

public sealed class PromoteCustomerToInfluencerCommandHandler
    : ICommandHandler<PromoteCustomerToInfluencerCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public PromoteCustomerToInfluencerCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(
        PromoteCustomerToInfluencerCommand request,
        CancellationToken cancellationToken)
        => _service.PromoteCustomerAsync(request, cancellationToken);
}

public sealed class LinkInfluencerCustomerCommandHandler : ICommandHandler<LinkInfluencerCustomerCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public LinkInfluencerCustomerCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(LinkInfluencerCustomerCommand request, CancellationToken cancellationToken)
        => _service.LinkCustomerAsync(request.InfluencerProfileId, request.CustomerUserId, request.Reason, cancellationToken);
}

public sealed class UnlinkInfluencerCustomerCommandHandler : ICommandHandler<UnlinkInfluencerCustomerCommand, InfluencerDto>
{
    private readonly IInfluencerAdminService _service;

    public UnlinkInfluencerCustomerCommandHandler(IInfluencerAdminService service)
    {
        _service = service;
    }

    public Task<Result<InfluencerDto>> Handle(UnlinkInfluencerCustomerCommand request, CancellationToken cancellationToken)
        => _service.UnlinkCustomerAsync(request.InfluencerProfileId, request.Reason, cancellationToken);
}
