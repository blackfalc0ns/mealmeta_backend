using FluentAssertions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Validation;
using MealMate.Application.Common.Behaviors;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Messaging;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using AppValidationException = MealMate.Application.Common.Exceptions.ValidationException;

namespace MealMate.UnitTests.Localization;

public sealed class SampleValidationRequest : ICommand
{
    public string Email { get; init; } = string.Empty;
}

public sealed class SampleValidationRequestValidator : AbstractValidator<SampleValidationRequest>
{
    public SampleValidationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .EmailAddress()
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
    }
}

public sealed class SampleValidationRequestHandler : ICommandHandler<SampleValidationRequest>
{
    public Task<Result> Handle(SampleValidationRequest request, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success());
}

public class ValidationBehaviorLocalizationTests
{
    [Fact]
    public async Task ValidationBehavior_UsesStableCodes()
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssemblyContaining<SampleValidationRequestValidator>();
        services.AddTransient<IRequestHandler<SampleValidationRequest, Result>, SampleValidationRequestHandler>();
        services.AddTransient<IValidator<SampleValidationRequest>, SampleValidationRequestValidator>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SampleValidationRequestHandler>());

        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var act = () => sender.Send(new SampleValidationRequest());

        var exception = await act.Should().ThrowAsync<AppValidationException>();
        exception.Which.FieldErrors.Should().ContainKey(nameof(SampleValidationRequest.Email));
        exception.Which.FieldErrors[nameof(SampleValidationRequest.Email)]
            .Should().Contain(f => f.Code == MessageCodes.Validation.Required);
    }
}
