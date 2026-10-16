using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public sealed class AdminOperationsContractTests
{
    [Fact]
    public void AdminOrders_exposes_prep_documents_for_invoice_and_labels()
    {
        var routes = typeof(AdminOrdersController).GetCustomAttributes<RouteAttribute>().ToArray();
        routes.Select(x => x.Template).Should().Contain("api/v1/admin/orders");
        routes
            .Select(x => x.Template)
            .Should().Contain("api/v1/admin/operations/orders");

        var method = typeof(AdminOrdersController).GetMethod("GetPrepDocuments");
        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{orderId:guid}/prep-documents");

        typeof(AdminOrdersController).GetMethod("GetInvoice")!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{orderId:guid}/invoice");
        typeof(AdminOrdersController).GetMethod("GetLabels")!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{orderId:guid}/labels");
    }
}
