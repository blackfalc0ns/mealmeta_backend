using FluentValidation;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Queries;

namespace MealMate.Application.Features.VoiceCalls.Validators;

public sealed class InitiateCallCommandValidator : AbstractValidator<InitiateCallCommand>
{
    public InitiateCallCommandValidator()
    {
        RuleFor(x => x.TripStopId)
            .NotEmpty()
            .WithMessage("TripStopId must not be empty.");

        RuleFor(x => x.ClientRequestId)
            .NotEmpty()
            .WithMessage("ClientRequestId must not be empty.");
    }
}

public sealed class AnswerCallCommandValidator : AbstractValidator<AnswerCallCommand>
{
    public AnswerCallCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class RejectCallCommandValidator : AbstractValidator<RejectCallCommand>
{
    public RejectCallCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class CancelCallCommandValidator : AbstractValidator<CancelCallCommand>
{
    public CancelCallCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class EndCallCommandValidator : AbstractValidator<EndCallCommand>
{
    public EndCallCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class MarkCallConnectingCommandValidator : AbstractValidator<MarkCallConnectingCommand>
{
    public MarkCallConnectingCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class MarkCallConnectedCommandValidator : AbstractValidator<MarkCallConnectedCommand>
{
    public MarkCallConnectedCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class MarkCallFailedCommandValidator : AbstractValidator<MarkCallFailedCommand>
{
    public MarkCallFailedCommandValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}

public sealed class GetCallByIdQueryValidator : AbstractValidator<GetCallByIdQuery>
{
    public GetCallByIdQueryValidator()
    {
        RuleFor(x => x.CallId)
            .NotEmpty()
            .WithMessage("CallId must not be empty.");
    }
}
