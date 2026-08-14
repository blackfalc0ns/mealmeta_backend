using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantMeals;

// Validators
public sealed class CreateMenuCommandValidator : AbstractValidator<CreateMenuCommand>
{
    public CreateMenuCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

public sealed class UpdateMenuCommandValidator : AbstractValidator<UpdateMenuCommand>
{
    public UpdateMenuCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
    }
}

// Handlers
public sealed class MenuHandlers :
    ICommandHandler<CreateMenuCommand, MenuDto>,
    ICommandHandler<UpdateMenuCommand, MenuDto>,
    ICommandHandler<ToggleMenuActiveCommand, MenuDto>,
    IQueryHandler<GetMenuByIdQuery, MenuDto>,
    IQueryHandler<GetMenusListQuery, PagedResult<MenuDto>>,
    IQueryHandler<GetRestaurantMealProgramsQuery, IReadOnlyList<RestaurantMealProgramDto>>
{
    private readonly IAppDbContext _db;

    public MenuHandlers(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<MenuDto>> Handle(CreateMenuCommand request, CancellationToken cancellationToken)
    {
        if (request.ProgramId.HasValue)
        {
            var restaurantHasSelections = await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId && x.IsActive,
                cancellationToken);
            var isSelectedProgram = restaurantHasSelections
                ? await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(
                x => x.RestaurantId == request.RestaurantId
                    && x.ProgramId == request.ProgramId.Value
                    && x.IsActive,
                cancellationToken)
                : await _db.Programs.AsNoTracking().AnyAsync(
                    x => x.Id == request.ProgramId.Value && x.IsActive,
                    cancellationToken);
            if (!isSelectedProgram)
            {
                return Error.Validation("meal.program_not_selected", "The selected program is not available for this restaurant.");
            }
        }

        var menu = new Menu
        {
            RestaurantId = request.RestaurantId,
            ProgramId = request.ProgramId,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Menus.Add(menu);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(menu, 0);
    }

    public async Task<Result<MenuDto>> Handle(UpdateMenuCommand request, CancellationToken cancellationToken)
    {
        var menu = await _db.Menus
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (menu is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Menu '{request.Id}' was not found.");
        }

        menu.NameAr = request.NameAr.Trim();
        menu.NameEn = request.NameEn.Trim();
        menu.ProgramId = request.ProgramId;
        menu.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var mealCount = await _db.Meals.CountAsync(x => x.MenuId == menu.Id && !x.IsDeleted, cancellationToken);
        return MapToDto(menu, mealCount);
    }

    public async Task<Result<MenuDto>> Handle(ToggleMenuActiveCommand request, CancellationToken cancellationToken)
    {
        var menu = await _db.Menus
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (menu is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Menu '{request.Id}' was not found.");
        }

        menu.IsActive = !menu.IsActive;
        menu.ModifiedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var mealCount = await _db.Meals.CountAsync(x => x.MenuId == menu.Id && !x.IsDeleted, cancellationToken);
        return MapToDto(menu, mealCount);
    }

    public async Task<Result<MenuDto>> Handle(GetMenuByIdQuery request, CancellationToken cancellationToken)
    {
        var menu = await _db.Menus
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken);
        if (menu is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Menu '{request.Id}' was not found.");
        }

        var meals = await _db.Meals
            .AsNoTracking()
            .Include(x => x.Ingredients)
                .ThenInclude(x => x.Ingredient)
            .Include(x => x.Images)
            .Where(x => x.MenuId == menu.Id && !x.IsDeleted)
            .OrderBy(x => x.NameAr)
            .ToListAsync(cancellationToken);

        var mealDtos = meals.Select(x => MealHandlers.MapToDto(x, null, null)).ToList();
        return MapToDto(menu, meals.Count, mealDtos);
    }

    public async Task<Result<PagedResult<MenuDto>>> Handle(GetMenusListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Menus
            .AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId && !x.IsDeleted);

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.NameAr.ToLower().Contains(search) || x.NameEn.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        var menus = await query
            .OrderBy(x => x.NameAr)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                Menu = x,
                MealCount = _db.Meals.Count(m => m.MenuId == x.Id && !m.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var items = menus.Select(x => MapToDto(x.Menu, x.MealCount)).ToList();

        return new PagedResult<MenuDto>(items, totalCount, page, pageSize);
    }

    public async Task<Result<IReadOnlyList<RestaurantMealProgramDto>>> Handle(
        GetRestaurantMealProgramsQuery request,
        CancellationToken cancellationToken)
    {
        var selectedRows = await (
            from offer in _db.RestaurantProgramBundleOffers.AsNoTracking()
            join program in _db.Programs.AsNoTracking() on offer.ProgramId equals program.Id
            join bundle in _db.Bundles.AsNoTracking() on offer.BundleId equals bundle.Id
            where offer.RestaurantId == request.RestaurantId
                && offer.IsActive
                && program.IsActive
                && bundle.IsActive
            orderby program.SortOrder, program.NameAr
            select new
            {
                ProgramId = program.Id,
                ProgramCode = program.Code,
                ProgramNameAr = program.NameAr,
                ProgramNameEn = program.NameEn,
                BundleId = bundle.Id,
                BundleCode = bundle.Code,
                BundleNameAr = bundle.NameAr,
                BundleNameEn = bundle.NameEn,
                BundleSortOrder = bundle.SortOrder,
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<RestaurantMealProgramDto> programs = selectedRows
            .GroupBy(row => new { row.ProgramId, row.ProgramCode, row.ProgramNameAr, row.ProgramNameEn })
            .Select(group => new RestaurantMealProgramDto(
                group.Key.ProgramId,
                group.Key.ProgramCode,
                group.Key.ProgramNameAr,
                group.Key.ProgramNameEn,
                group.OrderBy(row => row.BundleSortOrder)
                    .Select(row => new RestaurantMealBundleDto(row.BundleId, row.BundleCode, row.BundleNameAr, row.BundleNameEn))
                    .DistinctBy(bundle => bundle.Id)
                    .ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<RestaurantMealProgramDto>>(programs);
    }

    public static MenuDto MapToDto(Menu m, int mealCount, IReadOnlyList<MealDto>? meals = null) =>
        new(
            m.Id,
            m.RestaurantId,
            m.ProgramId,
            m.NameAr,
            m.NameEn,
            m.IsActive,
            m.CreatedAtUtc,
            mealCount,
            meals);
}
