using System.Text.RegularExpressions;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminDashboard.Tenants;

public sealed record TenantDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    Guid CountryId,
    string CountryCode,
    string CountryNameAr,
    string CountryNameEn,
    string Domain,
    string CurrencyCode,
    string TimeZoneId,
    string DefaultLanguage,
    TenantStatus Status,
    bool IsActive,
    Guid? AccessScopeId,
    DateTime CreatedAtUtc);

public sealed record ListTenantsQuery : IQuery<IReadOnlyList<TenantDto>>;

public sealed class ListTenantsQueryHandler : IQueryHandler<ListTenantsQuery, IReadOnlyList<TenantDto>>
{
    private readonly IAppDbContext _db;

    public ListTenantsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<TenantDto>>> Handle(
        ListTenantsQuery request,
        CancellationToken cancellationToken)
    {
        var tenants = await (
            from tenant in _db.Tenants.AsNoTracking()
            join country in _db.Countries.AsNoTracking() on tenant.CountryId equals country.Id
            where !tenant.IsDeleted
            orderby tenant.Code
            select new TenantDto(
                tenant.Id,
                tenant.Code,
                tenant.NameAr,
                tenant.NameEn,
                country.Id,
                country.Code,
                country.NameAr,
                country.NameEn,
                tenant.Domain,
                tenant.CurrencyCode,
                tenant.TimeZoneId,
                tenant.DefaultLanguage,
                tenant.Status,
                tenant.IsActive,
                _db.AccessScopes
                    .Where(scope => scope.TenantId == tenant.Id
                        && scope.ScopeType == AccessScopeType.Tenant
                        && !scope.IsDeleted)
                    .Select(scope => (Guid?)scope.Id)
                    .FirstOrDefault(),
                tenant.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return tenants;
    }
}

public sealed record CreateTenantCommand(
    string Code,
    string NameAr,
    string NameEn,
    string CountryCode,
    string CountryNameAr,
    string CountryNameEn,
    string CurrencyCode,
    string TimeZoneId,
    string DefaultLanguage,
    string Domain,
    string Reason) : ICommand<TenantDto>;

public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    private static readonly Regex TenantCodePattern =
        new("^[A-Za-z0-9][A-Za-z0-9-]{1,48}[A-Za-z0-9]$", RegexOptions.Compiled);

    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Must(code => TenantCodePattern.IsMatch(code.Trim()))
            .WithMessage("Tenant code may contain letters, numbers, and hyphens only.");
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryCode).NotEmpty().Matches("^[A-Za-z]{2,3}$");
        RuleFor(x => x.CountryNameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryNameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrencyCode).NotEmpty().Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DefaultLanguage)
            .Must(language => string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Default language must be ar or en.");
        RuleFor(x => x.Domain)
            .NotEmpty()
            .MaximumLength(253)
            .Must(IsValidHostName)
            .WithMessage("Domain must be a valid host name without a scheme or path.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }

    private static bool IsValidHostName(string domain)
    {
        var value = domain.Trim().TrimEnd('.');
        return !value.Contains("://", StringComparison.Ordinal)
            && !value.Contains("/", StringComparison.Ordinal)
            && Uri.CheckHostName(value) != UriHostNameType.Unknown;
    }
}

public sealed class CreateTenantCommandHandler : ICommandHandler<CreateTenantCommand, TenantDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;

    public CreateTenantCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<TenantDto>> Handle(
        CreateTenantCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can create tenants.");
        }

        var tenantCode = request.Code.Trim().ToUpperInvariant();
        var countryCode = request.CountryCode.Trim().ToUpperInvariant();
        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        var domain = request.Domain.Trim().TrimEnd('.').ToLowerInvariant();
        var language = request.DefaultLanguage.Trim().ToLowerInvariant();

        if (await _db.Tenants.AnyAsync(
                tenant => tenant.Code == tenantCode || tenant.Domain == domain,
                cancellationToken))
        {
            return Error.Conflict(MessageCodes.Validation.Unknown, "Tenant code or domain already exists.");
        }

        var country = await _db.Countries.FirstOrDefaultAsync(
            candidate => candidate.Code == countryCode,
            cancellationToken);

        if (country is null)
        {
            country = new Country
            {
                Code = countryCode,
                NameAr = request.CountryNameAr.Trim(),
                NameEn = request.CountryNameEn.Trim(),
                CurrencyCode = currencyCode,
                IsActive = true,
                CreatedAtUtc = _clock.UtcNow,
                CreatedByUserId = _currentUser.UserId,
            };
            _db.Countries.Add(country);
        }
        else if (!country.IsActive)
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Country is inactive.");
        }

        var tenant = new Tenant
        {
            Code = tenantCode,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            CountryId = country.Id,
            Domain = domain,
            CurrencyCode = currencyCode,
            TimeZoneId = request.TimeZoneId.Trim(),
            DefaultLanguage = language,
            Status = TenantStatus.Setup,
            IsActive = false,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = _currentUser.UserId,
        };

        var accessScope = new AccessScope
        {
            Name = $"{tenant.Code} — {tenant.NameEn}",
            ScopeType = AccessScopeType.Tenant,
            CountryId = country.Id,
            TenantId = tenant.Id,
            EntityId = tenant.Id,
            EntityType = nameof(Tenant),
            IsActive = false,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = _currentUser.UserId,
        };

        _db.Tenants.Add(tenant);
        _db.AccessScopes.Add(accessScope);

        await _audit.LogAsync(new AuditEntry(
            "TenantCreated",
            nameof(Tenant),
            tenant.Id,
            After: new
            {
                tenant.Code,
                tenant.NameAr,
                tenant.NameEn,
                tenant.CountryId,
                tenant.Domain,
                tenant.CurrencyCode,
                tenant.TimeZoneId,
                tenant.DefaultLanguage,
                tenant.Status,
                AccessScopeId = accessScope.Id,
            },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return ToDto(tenant, country, accessScope.Id);
    }

    internal static TenantDto ToDto(Tenant tenant, Country country, Guid? accessScopeId) =>
        new(
            tenant.Id,
            tenant.Code,
            tenant.NameAr,
            tenant.NameEn,
            country.Id,
            country.Code,
            country.NameAr,
            country.NameEn,
            tenant.Domain,
            tenant.CurrencyCode,
            tenant.TimeZoneId,
            tenant.DefaultLanguage,
            tenant.Status,
            tenant.IsActive,
            accessScopeId,
            tenant.CreatedAtUtc);
}

public sealed record SetTenantStatusCommand(
    Guid TenantId,
    TenantStatus Status,
    string Reason) : ICommand<TenantDto>;

public sealed class SetTenantStatusCommandValidator : AbstractValidator<SetTenantStatusCommand>
{
    public SetTenantStatusCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class SetTenantStatusCommandHandler : ICommandHandler<SetTenantStatusCommand, TenantDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;

    public SetTenantStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<TenantDto>> Handle(
        SetTenantStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can change tenant status.");
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(
            candidate => candidate.Id == request.TenantId && !candidate.IsDeleted,
            cancellationToken);
        if (tenant is null)
        {
            return Error.NotFound(MessageCodes.Validation.Unknown, "Tenant not found.");
        }

        var country = await _db.Countries.FirstAsync(
            candidate => candidate.Id == tenant.CountryId,
            cancellationToken);
        var scope = await _db.AccessScopes.FirstOrDefaultAsync(
            candidate => candidate.TenantId == tenant.Id
                && candidate.ScopeType == AccessScopeType.Tenant
                && !candidate.IsDeleted,
            cancellationToken);

        var previousStatus = tenant.Status;
        tenant.Status = request.Status;
        tenant.IsActive = request.Status == TenantStatus.Active;
        tenant.ModifiedAtUtc = _clock.UtcNow;
        tenant.ModifiedByUserId = _currentUser.UserId;

        if (scope is not null)
        {
            scope.IsActive = tenant.IsActive;
            scope.ModifiedAtUtc = _clock.UtcNow;
            scope.ModifiedByUserId = _currentUser.UserId;
        }

        await _audit.LogAsync(new AuditEntry(
            "TenantStatusChanged",
            nameof(Tenant),
            tenant.Id,
            Before: new { Status = previousStatus },
            After: new { tenant.Status, tenant.IsActive },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return CreateTenantCommandHandler.ToDto(tenant, country, scope?.Id);
    }
}
