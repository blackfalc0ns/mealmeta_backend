using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Catalog;

public sealed record RegionCatalogItemDto(
    Guid Id,
    Guid CountryId,
    string Code,
    string NameAr,
    string NameEn,
    RegionType Type,
    Guid? ParentId,
    bool IsActive);

public sealed record CountryCatalogDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    IReadOnlyList<RegionCatalogItemDto> Regions);

public sealed record CallingCodeDto(
    string CountryCode,
    string NameAr,
    string NameEn,
    string DialCode,
    string FlagEmoji);

public sealed record GetArabicCallingCodesQuery : IQuery<IReadOnlyList<CallingCodeDto>>;

public sealed class GetArabicCallingCodesQueryHandler
    : IQueryHandler<GetArabicCallingCodesQuery, IReadOnlyList<CallingCodeDto>>
{
    private static readonly string[] ArabicCountryCodes =
    [
        "KW", "SA", "AE", "QA", "BH", "OM",
        "EG", "JO", "LB", "SY", "IQ", "PS", "YE",
        "MA", "DZ", "TN", "LY", "SD"
    ];

    private readonly IAppDbContext _db;

    public GetArabicCallingCodesQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<CallingCodeDto>>> Handle(GetArabicCallingCodesQuery request, CancellationToken cancellationToken)
    {
        var countries = await _db.Countries
            .AsNoTracking()
            .Where(country => country.IsActive && ArabicCountryCodes.Contains(country.Code))
            .Select(country => new CallingCodeDto(
                country.Code,
                country.NameAr,
                country.NameEn,
                country.DialCode,
                country.FlagEmoji))
            .ToListAsync(cancellationToken);

        var ordered = countries
            .OrderBy(country => Array.IndexOf(ArabicCountryCodes, country.CountryCode))
            .ToList();

        return Result.Success<IReadOnlyList<CallingCodeDto>>(ordered);
    }
}

public sealed record GetCountryRegionCatalogQuery(string CountryCode = "KW", bool LeafOnly = false, bool IncludeInactive = false)
    : IQuery<CountryCatalogDto>;

public sealed record LocationBlockDto(Guid Id, string Code, string NameAr, string NameEn);
public sealed record LocationAreaDto(Guid Id, string Code, string NameAr, string NameEn, IReadOnlyList<LocationBlockDto> Blocks);
public sealed record LocationGovernorateDto(Guid Id, string Code, string NameAr, string NameEn, IReadOnlyList<LocationAreaDto> Areas);
public sealed record LocationCatalogDto(Guid CountryId, string CountryCode, IReadOnlyList<LocationGovernorateDto> Governorates);
/// <summary>
/// Returns the address hierarchy. Blocks are deliberately loaded only for the
/// selected area to keep the mobile payload small.
/// </summary>
public sealed record GetLocationCatalogQuery(string CountryCode = "KW", Guid? AreaId = null) : IQuery<LocationCatalogDto>;

/// <summary>Single shared location source for customer delivery addresses and restaurant service-area selection.</summary>
public sealed class LocationCatalogService
{
    private readonly IAppDbContext _db;
    private readonly KuwaitTenantResolver _tenantResolver;
    public LocationCatalogService(IAppDbContext db, KuwaitTenantResolver tenantResolver) => (_db, _tenantResolver) = (db, tenantResolver);

    public async Task<Result<LocationCatalogDto>> GetAsync(string countryCode, Guid? areaId, CancellationToken ct)
    {
        var tenant = await _tenantResolver.ResolveCatalogReadAsync(ct);
        if (tenant.IsFailure) return Result.Failure<LocationCatalogDto>(tenant.Error);
        var code = countryCode.Trim().ToUpperInvariant();
        var country = await _db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenant.Value.CountryId && x.Code == code && x.IsActive, ct);
        if (country is null) return Result.Failure<LocationCatalogDto>(Error.NotFound("catalog.country_not_found"));
        var regions = await _db.Regions.AsNoTracking().Where(x => x.CountryId == country.Id && x.IsActive).OrderBy(x => x.NameAr).ToListAsync(ct);
        var governorates = regions.Where(x => x.Type == RegionType.Governorate).Select(g => new LocationGovernorateDto(g.Id, g.Code, g.NameAr, g.NameEn,
            regions.Where(a => a.Type == RegionType.Area && a.ParentRegionId == g.Id).Select(a => new LocationAreaDto(a.Id, a.Code, a.NameAr, a.NameEn,
                areaId == a.Id
                    ? regions.Where(b => b.Type == RegionType.Block && b.ParentRegionId == a.Id).OrderBy(b => b.Code).Select(b => new LocationBlockDto(b.Id, b.Code, b.NameAr, b.NameEn)).ToList()
                    : [])).ToList())).ToList();
        return Result.Success(new LocationCatalogDto(country.Id, country.Code, governorates));
    }
}

public sealed class GetLocationCatalogQueryHandler : IQueryHandler<GetLocationCatalogQuery, LocationCatalogDto>
{
    private readonly LocationCatalogService _service;
    public GetLocationCatalogQueryHandler(LocationCatalogService service) => _service = service;
    public Task<Result<LocationCatalogDto>> Handle(GetLocationCatalogQuery request, CancellationToken ct) => _service.GetAsync(request.CountryCode, request.AreaId, ct);
}

public sealed class GetCountryRegionCatalogQueryHandler
    : IQueryHandler<GetCountryRegionCatalogQuery, CountryCatalogDto>
{
    private readonly IAppDbContext _db;
    private readonly KuwaitTenantResolver _tenantResolver;

    public GetCountryRegionCatalogQueryHandler(IAppDbContext db, KuwaitTenantResolver tenantResolver)
    {
        _db = db;
        _tenantResolver = tenantResolver;
    }

    public async Task<Result<CountryCatalogDto>> Handle(GetCountryRegionCatalogQuery request, CancellationToken cancellationToken)
    {
        var code = request.CountryCode.Trim().ToUpperInvariant();
        var tenantResult = await _tenantResolver.ResolveCatalogReadAsync(cancellationToken);
        if (tenantResult.IsFailure) return Result.Failure<CountryCatalogDto>(tenantResult.Error);
        var tenant = tenantResult.Value;
        if (code != "KW") return Result.Failure<CountryCatalogDto>(Error.Validation("catalog.country_not_allowed"));

        var country = await _db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenant.CountryId && x.Code == code, cancellationToken);
        if (country is null)
            return Result.Failure<CountryCatalogDto>(Error.NotFound("catalog.country_not_found"));

        var query = _db.Regions.AsNoTracking().Where(x => x.CountryId == country.Id);
        if (!request.IncludeInactive)
            query = query.Where(x => x.IsActive);
        if (request.LeafOnly)
            query = query.Where(x => x.Type == RegionType.Area);

        var regions = await query
            .OrderBy(x => x.Type)
            .ThenBy(x => x.NameAr)
            .Select(x => new RegionCatalogItemDto(x.Id, x.CountryId, x.Code, x.NameAr, x.NameEn, x.Type, x.ParentRegionId, x.IsActive))
            .ToListAsync(cancellationToken);

        return Result.Success(new CountryCatalogDto(country.Id, country.Code, country.NameAr, country.NameEn, regions));
    }
}

public sealed record AdminCreateRegionCommand(
    Guid CountryId,
    string Code,
    string NameAr,
    string NameEn,
    RegionType Type,
    Guid? ParentId) : ICommand<RegionCatalogItemDto>;

public sealed record AdminUpdateRegionCommand(
    Guid RegionId,
    string Code,
    string NameAr,
    string NameEn,
    RegionType Type,
    Guid? ParentId,
    bool IsActive) : ICommand<RegionCatalogItemDto>;

public sealed record AdminDeleteRegionCommand(Guid RegionId) : ICommand<bool>;

public sealed class AdminRegionCatalogHandlers
    : ICommandHandler<AdminCreateRegionCommand, RegionCatalogItemDto>,
      ICommandHandler<AdminUpdateRegionCommand, RegionCatalogItemDto>,
      ICommandHandler<AdminDeleteRegionCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;

    public AdminRegionCatalogHandlers(IAppDbContext db, IDateTimeProvider clock, KuwaitTenantResolver tenantResolver)
    {
        _db = db;
        _clock = clock;
        _tenantResolver = tenantResolver;
    }

    public async Task<Result<RegionCatalogItemDto>> Handle(AdminCreateRegionCommand request, CancellationToken cancellationToken)
    {
        var tenantResult = await _tenantResolver.ResolveAsync(allowSuperAdminFallback: true, cancellationToken);
        if (tenantResult.IsFailure) return Result.Failure<RegionCatalogItemDto>(tenantResult.Error);
        if (request.CountryId != tenantResult.Value.CountryId)
            return Result.Failure<RegionCatalogItemDto>(Error.Forbidden("catalog.country_not_allowed"));
        var country = await _db.Countries.FirstOrDefaultAsync(x => x.Id == tenantResult.Value.CountryId, cancellationToken);
        if (country is null) return Result.Failure<RegionCatalogItemDto>(Error.NotFound("catalog.country_not_found"));
        var validation = await ValidateParentAsync(request.Type, request.ParentId, request.CountryId, null, cancellationToken);
        if (validation is not null) return Result.Failure<RegionCatalogItemDto>(validation);
        if (await _db.Regions.AnyAsync(x => x.CountryId == request.CountryId && x.Code == request.Code, cancellationToken))
            return Result.Failure<RegionCatalogItemDto>(Error.Conflict("catalog.duplicate_code"));

        var entity = new Region { CountryId = request.CountryId, Code = request.Code.Trim(), NameAr = request.NameAr.Trim(), NameEn = request.NameEn.Trim(), Type = request.Type, ParentRegionId = request.ParentId, IsActive = true, CreatedAtUtc = _clock.UtcNow };
        _db.Regions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result<RegionCatalogItemDto>> Handle(AdminUpdateRegionCommand request, CancellationToken cancellationToken)
    {
        var tenantResult = await _tenantResolver.ResolveAsync(allowSuperAdminFallback: true, cancellationToken);
        if (tenantResult.IsFailure) return Result.Failure<RegionCatalogItemDto>(tenantResult.Error);
        var entity = await _db.Regions.FirstOrDefaultAsync(x => x.Id == request.RegionId, cancellationToken);
        if (entity is null) return Result.Failure<RegionCatalogItemDto>(Error.NotFound("catalog.region_not_found"));
        if (entity.CountryId != tenantResult.Value.CountryId)
            return Result.Failure<RegionCatalogItemDto>(Error.Forbidden("catalog.country_not_allowed"));
        var validation = await ValidateParentAsync(request.Type, request.ParentId, entity.CountryId, entity.Id, cancellationToken);
        if (validation is not null) return Result.Failure<RegionCatalogItemDto>(validation);
        if (await _db.Regions.AnyAsync(x => x.Id != entity.Id && x.CountryId == entity.CountryId && x.Code == request.Code, cancellationToken))
            return Result.Failure<RegionCatalogItemDto>(Error.Conflict("catalog.duplicate_code"));
        if (entity.Type == RegionType.Governorate && request.Type == RegionType.Area && await _db.Regions.AnyAsync(x => x.ParentRegionId == entity.Id && x.IsActive, cancellationToken))
            return Result.Failure<RegionCatalogItemDto>(Error.Conflict("catalog.governorate_has_active_areas"));
        if (entity.Type == RegionType.Governorate && !request.IsActive && await _db.Regions.AnyAsync(x => x.ParentRegionId == entity.Id && x.IsActive, cancellationToken))
            return Result.Failure<RegionCatalogItemDto>(Error.Conflict("catalog.governorate_has_active_areas"));

        entity.Code = request.Code.Trim(); entity.NameAr = request.NameAr.Trim(); entity.NameEn = request.NameEn.Trim(); entity.Type = request.Type; entity.ParentRegionId = request.ParentId; entity.IsActive = request.IsActive; entity.ModifiedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result<bool>> Handle(AdminDeleteRegionCommand request, CancellationToken cancellationToken)
    {
        var tenantResult = await _tenantResolver.ResolveAsync(allowSuperAdminFallback: true, cancellationToken);
        if (tenantResult.IsFailure) return Result.Failure<bool>(tenantResult.Error);
        var entity = await _db.Regions.FirstOrDefaultAsync(x => x.Id == request.RegionId, cancellationToken);
        if (entity is null) return Result.Failure<bool>(Error.NotFound("catalog.region_not_found"));
        if (entity.CountryId != tenantResult.Value.CountryId)
            return Result.Failure<bool>(Error.Forbidden("catalog.country_not_allowed"));
        var used = await _db.Regions.AnyAsync(x => x.ParentRegionId == entity.Id, cancellationToken)
            || await _db.RestaurantServiceRegions.AnyAsync(x => x.RegionId == entity.Id, cancellationToken)
            || await _db.RestaurantProfiles.AnyAsync(x => x.PrimaryRegionId == entity.Id, cancellationToken)
            || await _db.RestaurantRegistrations.AnyAsync(x => x.RegionId == entity.Id, cancellationToken);
        if (used) return Result.Failure<bool>(Error.Conflict("catalog.region_in_use"));
        _db.Regions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    private async Task<Error?> ValidateParentAsync(RegionType type, Guid? parentId, Guid countryId, Guid? selfId, CancellationToken cancellationToken)
    {
        if (type == RegionType.Governorate && parentId.HasValue) return Error.Validation("catalog.governorate_parent_invalid");
        if ((type == RegionType.Area || type == RegionType.Block) && !parentId.HasValue) return Error.Validation("catalog.region_parent_required");
        if (parentId.HasValue)
        {
            var parent = await _db.Regions.FirstOrDefaultAsync(x => x.Id == parentId.Value, cancellationToken);
            var expectedParent = type == RegionType.Area ? RegionType.Governorate : RegionType.Area;
            if (parent is null || parent.CountryId != countryId || parent.Type != expectedParent || parent.Id == selfId)
                return Error.Validation("catalog.parent_invalid");
        }
        return null;
    }

    private static RegionCatalogItemDto ToDto(Region x) => new(x.Id, x.CountryId, x.Code, x.NameAr, x.NameEn, x.Type, x.ParentRegionId, x.IsActive);
}
