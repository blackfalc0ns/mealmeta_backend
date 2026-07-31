using FluentAssertions;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.IntegrationTests;

public class RoleAuthControllersTests
{
    [Fact]
    public void Customer_auth_command_types_are_defined_correctly()
    {
        var command = new RegisterCustomerCommand("Test Customer", "customer@test.com", "+96550000001", "Password123!", "ar");
        command.FullName.Should().Be("Test Customer");
        command.Phone.Should().Be("+96550000001");
        command.PreferredLanguage.Should().Be("ar");
    }

    [Fact]
    public void Driver_registration_command_with_all_required_documents_and_fields()
    {
        var restId = Guid.NewGuid();
        var command = new CreateDriverRegistrationCommand(
            "أحمد علي",
            "Ahmed Ali",
            "+96555123456",
            "ahmed.ali@example.com",
            "Password123!",
            RestaurantId: restId,
            NationalId: "298010112345",
            LicenseNumber: "DL-998877",
            VehiclePlate: "KWT-1234",
            VehicleType: "Motorcycle",
            ProfileImageStorageKey: "uploads/drivers/profiles/photo.jpg",
            NationalIdFrontStorageKey: "uploads/drivers/id_front.jpg",
            NationalIdBackStorageKey: "uploads/drivers/id_back.jpg",
            DrivingLicenseStorageKey: "uploads/drivers/license.jpg",
            EmploymentContractStorageKey: "uploads/drivers/contract.pdf");

        command.FullNameAr.Should().Be("أحمد علي");
        command.Phone.Should().Be("+96555123456");
        command.RestaurantId.Should().Be(restId);
        command.NationalId.Should().Be("298010112345");
        command.LicenseNumber.Should().Be("DL-998877");
        command.VehicleType.Should().Be("Motorcycle");
        command.ProfileImageStorageKey.Should().Be("uploads/drivers/profiles/photo.jpg");
        command.NationalIdFrontStorageKey.Should().Be("uploads/drivers/id_front.jpg");
        command.NationalIdBackStorageKey.Should().Be("uploads/drivers/id_back.jpg");
        command.DrivingLicenseStorageKey.Should().Be("uploads/drivers/license.jpg");
        command.EmploymentContractStorageKey.Should().Be("uploads/drivers/contract.pdf");
    }

    [Fact]
    public void Driver_forgot_and_reset_password_commands_are_constructed()
    {
        var forgotCmd = new DriverForgotPasswordCommand("+96550000002");
        forgotCmd.Phone.Should().Be("+96550000002");

        var resetCmd = new DriverResetPasswordCommand("+96550000002", "123456", "NewPassword123!");
        resetCmd.OtpCode.Should().Be("123456");
        resetCmd.NewPassword.Should().Be("NewPassword123!");
    }

    [Fact]
    public void Delivery_manager_forgot_and_reset_password_commands_are_constructed()
    {
        var forgotCmd = new DeliveryManagerForgotPasswordCommand("dm@restaurant.com");
        forgotCmd.Email.Should().Be("dm@restaurant.com");

        var resetCmd = new DeliveryManagerResetPasswordCommand("dm@restaurant.com", "654321", "NewPassword123!");
        resetCmd.OtpCode.Should().Be("654321");
    }
}
