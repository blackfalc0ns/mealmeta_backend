using FluentValidation;
using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed record InfluencerDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? InfluencerCode,
    Guid? LinkedCustomerUserId,
    VerificationStatus VerificationStatus,
    bool IsActive);

public sealed record AdminInfluencerDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? Email,
    string? Phone,
    string? InfluencerCode,
    Guid? LinkedCustomerUserId,
    VerificationStatus VerificationStatus,
    bool IsActive,
    decimal? DefaultCommissionRate,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record ListInfluencersQuery : IQuery<IReadOnlyList<AdminInfluencerDto>>;

public sealed record RegisterInfluencerCommand(
    string DisplayName,
    string Email,
    string? Phone,
    string Password,
    string InfluencerCode) : ICommand<InfluencerDto>;

public sealed class RegisterInfluencerCommandValidator : AbstractValidator<RegisterInfluencerCommand>
{
    public RegisterInfluencerCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.InfluencerCode).NotEmpty().MaximumLength(50);
    }
}

public sealed record PromoteCustomerToInfluencerCommand(
    Guid CustomerUserId,
    string DisplayName,
    string InfluencerCode,
    decimal DefaultCommissionRate,
    bool ActivateNow = true) : ICommand<InfluencerDto>;

public sealed class PromoteCustomerToInfluencerCommandValidator : AbstractValidator<PromoteCustomerToInfluencerCommand>
{
    public PromoteCustomerToInfluencerCommandValidator()
    {
        RuleFor(x => x.CustomerUserId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty();
        RuleFor(x => x.InfluencerCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DefaultCommissionRate).InclusiveBetween(0m, 100m);
    }
}

public sealed record LinkInfluencerCustomerCommand(
    Guid InfluencerProfileId,
    Guid CustomerUserId,
    string Reason) : ICommand<InfluencerDto>;

public sealed record UnlinkInfluencerCustomerCommand(
    Guid InfluencerProfileId,
    string Reason) : ICommand<InfluencerDto>;
