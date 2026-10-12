using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Common.Models;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyInvoiceGateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void New_invoice_rejects_omitted_or_older_contract_version(int? version)
    {
        var result = FamilyInvoiceGate.ValidateNewInvoice(version, new FamilyInvoiceOptions { MinimumContractVersion = 2 });
        Assert.Equal("family.client_update_required", result?.Code);
    }

    [Fact]
    public void New_invoice_switch_is_independent_of_contract_version()
    {
        var result = FamilyInvoiceGate.ValidateNewInvoice(2, new FamilyInvoiceOptions
        {
            MinimumContractVersion = 2,
            StopNewFamilyInvoices = true,
        });
        Assert.Equal("family.invoice_creation_stopped", result?.Code);
    }

    [Fact]
    public void Minimum_version_is_allowed_when_invoice_creation_is_enabled()
    {
        Assert.Null(FamilyInvoiceGate.ValidateNewInvoice(2, new FamilyInvoiceOptions { MinimumContractVersion = 2 }));
    }

    [Fact]
    public async Task Selected_method_requires_actual_currency_equality()
    {
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(
            [new PaymentGatewayMethod(3, "PAYZAH", "دفع", "Payzah", null, null, "KWD", false)]);

        var result = await FamilyInvoiceGate.ResolvePaymentMethodAsync(payments, 10m, 3, "USD", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("family.payment_currency_unsupported", result.Error.Code);
        await payments.DidNotReceive().ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_selected_method_and_provider_failure_fail_closed()
    {
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(
            [new PaymentGatewayMethod(8, "OTHER", "طريقة", "Other", null, null, "KWD", false)]);
        var missing = await FamilyInvoiceGate.ResolvePaymentMethodAsync(payments, 10m, 3, "KWD", CancellationToken.None);
        Assert.Equal("family.payment_method_unavailable", missing.Error.Code);

        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PaymentGatewayMethod>>>(_ => throw new InvalidOperationException("provider unavailable"));
        var failure = await FamilyInvoiceGate.ResolvePaymentMethodAsync(payments, 10m, 3, "KWD", CancellationToken.None);
        Assert.Equal("family.payment_method_unavailable", failure.Error.Code);
    }
}
