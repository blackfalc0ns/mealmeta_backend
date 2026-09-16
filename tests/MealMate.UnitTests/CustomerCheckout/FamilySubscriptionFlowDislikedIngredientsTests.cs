using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MediatR;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilySubscriptionFlowDislikedIngredientsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserializes_DislikedIngredients_Alias_With_Strings()
    {
        const string json = """
            {
              "dislikedIngredients": ["d3b07384-113e-dec4-9eaa-6238ad5ff000", "INGREDIENT:22222222-2222-2222-2222-222222222222"]
            }
            """;

        var request = JsonSerializer.Deserialize<SaveFamilyMemberDislikedFoodsRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.DislikedIngredients.Should().NotBeNull();
        request.DislikedIngredients.Should().HaveCount(2);
        request.DislikedIngredients![0].Code.Should().Be("d3b07384-113e-dec4-9eaa-6238ad5ff000");
        request.DislikedIngredients![1].Code.Should().Be("INGREDIENT:22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void Deserializes_Ingredients_Alias()
    {
        const string json = """
            {
              "ingredients": ["d3b07384-113e-dec4-9eaa-6238ad5ff000"]
            }
            """;

        var request = JsonSerializer.Deserialize<SaveFamilyMemberDislikedFoodsRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Ingredients.Should().NotBeNull();
        request.Ingredients.Should().ContainSingle().Which.Should().Be("d3b07384-113e-dec4-9eaa-6238ad5ff000");
    }

    [Fact]
    public void Deserializes_Items_Alias()
    {
        const string json = """
            {
              "items": [
                { "code": "d3b07384-113e-dec4-9eaa-6238ad5ff000", "nameAr": "دجاج" }
              ]
            }
            """;

        var request = JsonSerializer.Deserialize<SaveFamilyMemberDislikedFoodsRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Items.Should().NotBeNull();
        request.Items.Should().ContainSingle().Which.Code.Should().Be("d3b07384-113e-dec4-9eaa-6238ad5ff000");
    }

    [Fact]
    public async Task Service_Resolves_DislikedIngredients_And_Normalizes_Bare_Guids()
    {
        var sender = Substitute.For<ISender>();
        SaveFamilyMemberDislikedFoodsCommand? capturedCommand = null;
        sender.Send(Arg.Do<SaveFamilyMemberDislikedFoodsCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var service = new FamilySubscriptionFlowService(
            sender,
            Substitute.For<IAppDbContext>(),
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<IFamilyCheckoutService>());

        var memberId = Guid.NewGuid();
        var rawGuid = "d3b07384-113e-dec4-9eaa-6238ad5ff000";

        var request = new SaveFamilyMemberDislikedFoodsRequest(
            Ingredients: [rawGuid]);

        var result = await service.SaveMemberDislikedFoodsAsync(memberId, request);

        result.IsSuccess.Should().BeTrue();
        capturedCommand.Should().NotBeNull();
        capturedCommand!.MemberId.Should().Be(memberId);
        capturedCommand.DislikedFoods.Should().ContainSingle();
        capturedCommand.DislikedFoods[0].Code.Should().Be($"INGREDIENT:{rawGuid.ToLowerInvariant()}");
    }

    [Fact]
    public async Task Service_Resolves_DislikedFoods_Objects_And_Normalizes_Bare_Guids()
    {
        var sender = Substitute.For<ISender>();
        SaveFamilyMemberDislikedFoodsCommand? capturedCommand = null;
        sender.Send(Arg.Do<SaveFamilyMemberDislikedFoodsCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var service = new FamilySubscriptionFlowService(
            sender,
            Substitute.For<IAppDbContext>(),
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<IFamilyCheckoutService>());

        var memberId = Guid.NewGuid();
        var rawGuid = "d3b07384-113e-dec4-9eaa-6238ad5ff000";

        var request = new SaveFamilyMemberDislikedFoodsRequest(
            DislikedFoods: [new FoodPreferenceItemDto(rawGuid, "دجاج", "Chicken", null)]);

        var result = await service.SaveMemberDislikedFoodsAsync(memberId, request);

        result.IsSuccess.Should().BeTrue();
        capturedCommand.Should().NotBeNull();
        capturedCommand!.DislikedFoods[0].Code.Should().Be($"INGREDIENT:{rawGuid.ToLowerInvariant()}");
        capturedCommand.DislikedFoods[0].NameAr.Should().Be("دجاج");
    }

    [Fact]
    public void PreviewAddressCoverage_Request_Deserializes_Page_And_PageSize()
    {
        const string json = """
            {
              "governorate": "العاصمة",
              "area": "شرق",
              "block": "1",
              "page": 2,
              "pageSize": 10
            }
            """;

        var request = JsonSerializer.Deserialize<PreviewAddressCoverageRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.Page.Should().Be(2);
        request.PageSize.Should().Be(10);
    }
}
