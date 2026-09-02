using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MealMate.Api.Common;

internal static class DevPaymentMethods
{
    public static bool IsMockEnabled(IConfiguration configuration, IWebHostEnvironment environment)
        => environment.IsDevelopment()
            && configuration.GetValue("Payments:AllowMockPaymentMethods", false);

    public static List<CustomerPaymentMethodDto> BuildMock(decimal amount)
    {
        return
        [
            new CustomerPaymentMethodDto(1, "KNET", "كي نت", "KNET", "MYFATOORAH_HOSTED", 0m, amount, "KWD", true, null),
            new CustomerPaymentMethodDto(2, "VM", "فيزا / ماستر", "VISA / MASTER", "MYFATOORAH_HOSTED", 0m, amount, "KWD", true, null),
            new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, "KWD", false, "wallet_not_implemented"),
        ];
    }
}
