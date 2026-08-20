using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyMemberWriteDtoJsonTests
{
    [Fact]
    public void Deserializes_EmptyDeliveryAddressId_AsNull()
    {
        const string json = """
            {
              "displayName": "Yahya",
              "relationshipCode": "self",
              "gender": 1,
              "ageYears": 43,
              "nutritionalGoalCode": "balanced",
              "activityLevel": 2,
              "deliveryAddressId": ""
            }
            """;

        var dto = JsonSerializer.Deserialize<FamilyMemberWriteDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        dto.Should().NotBeNull();
        dto!.DisplayName.Should().Be("Yahya");
        dto.DeliveryAddressId.Should().BeNull();
        dto.AgeYears.Should().Be(43);
        dto.Gender.Should().Be(FamilyMemberGender.Male);
    }

    [Fact]
    public void Deserializes_ValidDeliveryAddressId()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var json = $$"""
            {
              "displayName": "Sara",
              "relationshipCode": "spouse",
              "gender": 2,
              "ageYears": 28,
              "deliveryAddressId": "{{id}}"
            }
            """;

        var dto = JsonSerializer.Deserialize<FamilyMemberWriteDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        dto!.DeliveryAddressId.Should().Be(id);
    }
}
