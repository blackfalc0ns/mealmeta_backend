using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class UserAccountServiceTests : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly UserAccountService _users;

    public UserAccountServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddDataProtection();
        services.AddLogging();
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddRoles<ApplicationRole>()
            .AddPasswordValidator<LetterAndDigitPasswordValidator>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        services.AddSingleton<IDomainEventDispatcher>(_ => Substitute.For<IDomainEventDispatcher>());
        services.AddSingleton(clock);
        services.AddScoped<UserAccountService>();

        var provider = services.BuildServiceProvider();
        _scope = provider.CreateScope();
        _users = _scope.ServiceProvider.GetRequiredService<UserAccountService>();
    }

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task CreateAsync_allows_phone_only_customer_with_letter_digit_password()
    {
        var result = await _users.CreateAsync(new CreateUserRequest(
            Email: null,
            PhoneNumber: "+96590001111",
            Password: "ahmed2026",
            FullName: "Ahmed",
            UserType: UserType.Customer,
            AccountStatus: AccountStatus.PhonePending), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().BeNull();
        result.Value.PhoneNumber.Should().Be("+96590001111");
    }
}
