using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/customer/family-subscription-flow")]
public sealed class CustomerFamilySubscriptionFlowController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IAppDbContext _db;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IFamilyCheckoutService _checkout;
    private readonly IConfiguration _configuration;

    public CustomerFamilySubscriptionFlowController(
        ICurrentUser currentUser,
        IAppDbContext db,
        IMyFatoorahPaymentService payments,
        IFamilyCheckoutService checkout,
        IConfiguration configuration)
    {
        _currentUser = currentUser;
        _db = db;
        _payments = payments;
        _checkout = checkout;
        _configuration = configuration;
    }

    [HttpGet("metadata")]
    [ProducesResponseType(typeof(FamilyDraftMetadataDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyDraftMetadataQuery(), cancellationToken));
    }

    [HttpGet("relationships")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilyLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Relationships(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyRelationshipsQuery(), cancellationToken));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCurrentFamilyGroupQuery(), cancellationToken));
    }

    [HttpPut("group")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertGroup([FromBody] UpsertFamilyGroupRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpsertFamilyGroupCommand(request.Name), cancellationToken));
    }

    [HttpPost("members")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember([FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new AddFamilyDraftMemberCommand(request), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMember(Guid memberId, [FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpdateFamilyMemberCommand(memberId, request), cancellationToken));
    }

    [HttpDelete("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMember(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new DeleteFamilyMemberCommand(memberId), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/plan")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberPlan(
        Guid memberId,
        [FromBody] SetFamilyMemberPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new SetFamilyMemberPlanCommand(memberId, request), cancellationToken));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(FamilyCheckoutSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] Guid? memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyCheckoutSummaryQuery(memberId), cancellationToken));
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods([FromQuery] decimal amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (amount <= 0) return BadRequest(new { error = "subscription_flow.invalid_amount" });
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);
        if (config is null) return Conflict(new { error = "payment.gateway_not_configured" });
        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try { methods = await _payments.GetPaymentMethodsAsync(amount, ct); }
        catch { return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment methods unavailable", detail: "MyFatoorah did not return payment methods."); }
        var result = methods.Where(x => IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "MYFATOORAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null)).ToList();
        result.Add(new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, config.DefaultCurrency, false, "wallet_not_implemented"));
        return Ok(result);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(FamilyCheckoutStartedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout([FromBody] FamilyStartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return BadRequest(new { error = "checkout.redirect_url_not_allowed" });

        var result = await _checkout.StartAsync(request, cancellationToken);
        if (result.IsFailure && result.Error.Code == "payment.initialization_failed")
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: result.Error.Description);
        return HandleResult(result);
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration.GetSection("Checkout:AllowedRedirectSchemes").Get<string[]>() ?? ["https", "mealmate"];
        if (!schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration.GetSection("Checkout:AllowedRedirectHosts").Get<string[]>() ?? ["maelmate.runasp.net", "localhost"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER")) return card;
        return false;
    }
}
