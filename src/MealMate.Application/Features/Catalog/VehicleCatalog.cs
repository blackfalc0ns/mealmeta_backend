using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Catalog;

public sealed record VehicleTypeDto(
    string Code,
    string NameAr,
    string NameEn,
    string IconKey);

public sealed record VehicleColorDto(
    string Hex,
    string NameAr,
    string NameEn,
    bool IsDefault,
    int DisplayOrder);

public sealed record VehicleModelSearchResultDto(
    string Value,
    string MakeCode,
    string MakeNameAr,
    string MakeNameEn,
    string ModelCode,
    string ModelNameAr,
    string ModelNameEn,
    string FullNameAr,
    string FullNameEn,
    string VehicleType);

public sealed record VehicleModelItemDto(
    string Code,
    string NameAr,
    string NameEn,
    string FullNameAr,
    string FullNameEn,
    string VehicleType);

public sealed record VehicleMakeDto(
    string Code,
    string NameAr,
    string NameEn,
    IReadOnlyList<string> VehicleTypes,
    IReadOnlyList<VehicleModelItemDto> Models);

public sealed record GetVehicleTypesQuery : IQuery<IReadOnlyList<VehicleTypeDto>>;

public sealed record GetVehicleColorsQuery : IQuery<IReadOnlyList<VehicleColorDto>>;

public sealed record SearchVehicleModelsQuery(
    string? Search = null,
    string? VehicleType = null,
    int Limit = 40) : IQuery<IReadOnlyList<VehicleModelSearchResultDto>>;

public sealed record GetVehicleMakesQuery(
    string? VehicleType = null) : IQuery<IReadOnlyList<VehicleMakeDto>>;

public static class VehicleCatalogData
{
    public static readonly IReadOnlyList<VehicleTypeDto> Types = new List<VehicleTypeDto>
    {
        new("Car", "سيارة", "Car", "car"),
        new("Motorcycle", "دراجة نارية", "Motorcycle", "motorcycle"),
        new("Van", "فان / شاحنة صغيرة", "Van", "van"),
        new("Bicycle", "دراجة هوائية", "Bicycle", "bicycle")
    };

    public static readonly IReadOnlyList<VehicleColorDto> Colors = new List<VehicleColorDto>
    {
        // UI Mockup Palette (Step 2 Stepper)
        new("#D32F2F", "أحمر", "Red", true, 1),
        new("#1976D2", "أزرق", "Blue", true, 2),
        new("#9E9E9E", "فضي / رمادي", "Silver / Grey", true, 3),
        new("#000000", "أسود", "Black", true, 4),
        new("#5E35B1", "بنفسجي", "Purple", true, 5),
        new("#FFFFFF", "أبيض", "White", true, 6),

        // Additional Standard Colors
        new("#FBC02D", "أصفر", "Yellow", false, 7),
        new("#FB8C00", "برتقالي", "Orange", false, 8),
        new("#43A047", "أخضر", "Green", false, 9),
        new("#6D4C41", "بني", "Brown", false, 10),
        new("#1A237E", "كحلي", "Navy Blue", false, 11),
        new("#D4AF37", "ذهبي", "Gold", false, 12)
    };

    public static readonly IReadOnlyList<VehicleMakeDto> Makes = new List<VehicleMakeDto>
    {
        new("Toyota", "تويوتا", "Toyota", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Camry", "كامري", "Camry", "تويوتا كامري", "Toyota Camry", "Car"),
            new("Corolla", "كورولا", "Corolla", "تويوتا كورولا", "Toyota Corolla", "Car"),
            new("Yaris", "يارس", "Yaris", "تويوتا يارس", "Toyota Yaris", "Car"),
            new("Avalon", "أفالون", "Avalon", "تويوتا أفالون", "Toyota Avalon", "Car"),
            new("RAV4", "راف 4", "RAV4", "تويوتا راف 4", "Toyota RAV4", "Car"),
            new("Prado", "برادو", "Prado", "تويوتا برادو", "Toyota Prado", "Car"),
            new("LandCruiser", "لاند كروزر", "Land Cruiser", "تويوتا لاند كروزر", "Toyota Land Cruiser", "Car"),
            new("Fortuner", "فورتشنر", "Fortuner", "تويوتا فورتشنر", "Toyota Fortuner", "Car"),
            new("Rush", "راش", "Rush", "تويوتا راش", "Toyota Rush", "Car"),
            new("Hilux", "هايلكس", "Hilux", "تويوتا هايلكس", "Toyota Hilux", "Car"),
            new("HiAce", "هاي اس", "HiAce", "تويوتا هاي اس", "Toyota HiAce", "Van"),
            new("LiteAce", "لايت اس", "LiteAce", "تويوتا لايت اس", "Toyota LiteAce", "Van")
        }),
        new("Nissan", "نيسان", "Nissan", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Sunny", "صني", "Sunny", "نيسان صني", "Nissan Sunny", "Car"),
            new("Sentra", "سنترا", "Sentra", "نيسان سنترا", "Nissan Sentra", "Car"),
            new("Altima", "ألتيما", "Altima", "نيسان ألتيما", "Nissan Altima", "Car"),
            new("Maxima", "مكسيما", "Maxima", "نيسان مكسيما", "Nissan Maxima", "Car"),
            new("Kicks", "كيكس", "Kicks", "نيسان كيكس", "Nissan Kicks", "Car"),
            new("XTrail", "إكس تريل", "X-Trail", "نيسان إكس تريل", "Nissan X-Trail", "Car"),
            new("Patrol", "باترول", "Patrol", "نيسان باترول", "Nissan Patrol", "Car"),
            new("Urvan", "أورفان", "Urvan", "نيسان أورفان", "Nissan Urvan", "Van")
        }),
        new("Hyundai", "هيونداي", "Hyundai", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Elantra", "إلنترا", "Elantra", "هيونداي إلنترا", "Hyundai Elantra", "Car"),
            new("Accent", "أكسنت", "Accent", "هيونداي أكسنت", "Hyundai Accent", "Car"),
            new("Sonata", "سوناتا", "Sonata", "هيونداي سوناتا", "Hyundai Sonata", "Car"),
            new("Azera", "أزيرا", "Azera", "هيونداي أزيرا", "Hyundai Azera", "Car"),
            new("Creta", "كريتا", "Creta", "هيونداي كريتا", "Hyundai Creta", "Car"),
            new("Tucson", "توسان", "Tucson", "هيونداي توسان", "Hyundai Tucson", "Car"),
            new("SantaFe", "سانتافي", "Santa Fe", "هيونداي سانتافي", "Hyundai Santa Fe", "Car"),
            new("Staria", "ستاريا", "Staria", "هيونداي ستاريا", "Hyundai Staria", "Van"),
            new("H1", "اتش 1", "H-1", "هيونداي اتش 1", "Hyundai H-1", "Van")
        }),
        new("Kia", "كيا", "Kia", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Cerato", "سيراتو", "Cerato", "كيا سيراتو", "Kia Cerato", "Car"),
            new("Pegas", "بيجاس", "Pegas", "كيا بيجاس", "Kia Pegas", "Car"),
            new("Rio", "ريو", "Rio", "كيا ريو", "Kia Rio", "Car"),
            new("K5", "كي 5 / أوبتيما", "K5 / Optima", "كيا كي 5", "Kia K5", "Car"),
            new("Sportage", "سبورتاج", "Sportage", "كيا سبورتاج", "Kia Sportage", "Car"),
            new("Seltos", "سيلتوس", "Seltos", "كيا سيلتوس", "Kia Seltos", "Car"),
            new("Carnival", "كارنيفال", "Carnival", "كيا كارنيفال", "Kia Carnival", "Van")
        }),
        new("Honda", "هوندا", "Honda", new[] { "Car", "Motorcycle" }, new List<VehicleModelItemDto>
        {
            new("Civic", "سيفيك", "Civic", "هوندا سيفيك", "Honda Civic", "Car"),
            new("Accord", "أكورد", "Accord", "هوندا أكورد", "Honda Accord", "Car"),
            new("City", "سيتي", "City", "هوندا سيتي", "Honda City", "Car"),
            new("CRV", "سي آر في", "CR-V", "هوندا سي آر في", "Honda CR-V", "Car"),
            new("HRV", "إتش آر في", "HR-V", "هوندا إتش آر في", "Honda HR-V", "Car"),
            new("PCX150", "بي سي إكس 150", "PCX 150", "هوندا بي سي إكس 150", "Honda PCX 150", "Motorcycle"),
            new("Dio", "ديو", "Dio", "هوندا ديو", "Honda Dio", "Motorcycle"),
            new("CG125", "سي جي 125", "CG 125", "هوندا سي جي 125", "Honda CG 125", "Motorcycle"),
            new("CB150", "سي بي 150", "CB 150", "هوندا سي بي 150", "Honda CB 150", "Motorcycle")
        }),
        new("Ford", "فورد", "Ford", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Taurus", "تورس", "Taurus", "فورد تورس", "Ford Taurus", "Car"),
            new("Explorer", "إكسبلورر", "Explorer", "فورد إكسبلورر", "Ford Explorer", "Car"),
            new("Escape", "إسكيب", "Escape", "فورد إسكيب", "Ford Escape", "Car"),
            new("F150", "إف 150", "F-150", "فورد إف 150", "Ford F-150", "Car"),
            new("Transit", "ترانزيت", "Transit", "فورد ترانزيت", "Ford Transit", "Van")
        }),
        new("Chevrolet", "شفروليه", "Chevrolet", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Malibu", "ماليبو", "Malibu", "شفروليه ماليبو", "Chevrolet Malibu", "Car"),
            new("Captiva", "كابتيفا", "Captiva", "شفروليه كابتيفا", "Chevrolet Captiva", "Car"),
            new("Groove", "جروف", "Groove", "شفروليه جروف", "Chevrolet Groove", "Car"),
            new("Tahoe", "تاهو", "Tahoe", "شفروليه تاهو", "Chevrolet Tahoe", "Car"),
            new("Express", "إكسبريس", "Express", "شفروليه إكسبريس", "Chevrolet Express", "Van")
        }),
        new("Mitsubishi", "ميتسوبيشي", "Mitsubishi", new[] { "Car", "Van" }, new List<VehicleModelItemDto>
        {
            new("Attrage", "أتراج", "Attrage", "ميتسوبيشي أتراج", "Mitsubishi Attrage", "Car"),
            new("Mirage", "ميراج", "Mirage", "ميتسوبيشي ميراج", "Mitsubishi Mirage", "Car"),
            new("Lancer", "لانسر", "Lancer", "ميتسوبيشي لانسر", "Mitsubishi Lancer", "Car"),
            new("Outlander", "أوتلاندر", "Outlander", "ميتسوبيشي أوتلاندر", "Mitsubishi Outlander", "Car"),
            new("Pajero", "باجيرو", "Pajero", "ميتسوبيشي باجيرو", "Mitsubishi Pajero", "Car"),
            new("L200", "إل 200", "L200", "ميتسوبيشي إل 200", "Mitsubishi L200", "Car")
        }),
        new("Suzuki", "سوزوكي", "Suzuki", new[] { "Car", "Motorcycle" }, new List<VehicleModelItemDto>
        {
            new("Dzire", "ديزاير", "Dzire", "سوزوكي ديزاير", "Suzuki Dzire", "Car"),
            new("Swift", "سويفت", "Swift", "سوزوكي سويفت", "Suzuki Swift", "Car"),
            new("Baleno", "بالينو", "Baleno", "سوزوكي بالينو", "Suzuki Baleno", "Car"),
            new("Ciaz", "سياز", "Ciaz", "سوزوكي سياز", "Suzuki Ciaz", "Car"),
            new("Ertiga", "إرتيجا", "Ertiga", "سوزوكي إرتيجا", "Suzuki Ertiga", "Car"),
            new("Jimny", "جيمني", "Jimny", "سوزوكي جيمني", "Suzuki Jimny", "Car"),
            new("GD110", "جي دي 110", "GD 110", "سوزوكي جي دي 110", "Suzuki GD 110", "Motorcycle"),
            new("GSX150", "جي إس إكس 150", "GSX 150", "سوزوكي جي إس إكس 150", "Suzuki GSX 150", "Motorcycle")
        }),
        new("Mazda", "مازدا", "Mazda", new[] { "Car" }, new List<VehicleModelItemDto>
        {
            new("Mazda3", "مازدا 3", "Mazda 3", "مازدا 3", "Mazda 3", "Car"),
            new("Mazda6", "مازدا 6", "Mazda 6", "مازدا 6", "Mazda 6", "Car"),
            new("CX5", "سي إكس 5", "CX-5", "مازدا سي إكس 5", "Mazda CX-5", "Car"),
            new("CX9", "سي إكس 9", "CX-9", "مازدا سي إكس 9", "Mazda CX-9", "Car")
        }),
        new("MG", "إم جي", "MG", new[] { "Car" }, new List<VehicleModelItemDto>
        {
            new("MG5", "إم جي 5", "MG 5", "إم جي 5", "MG 5", "Car"),
            new("MG6", "إم جي 6", "MG 6", "إم جي 6", "MG 6", "Car"),
            new("MGZS", "إم جي زد إس", "MG ZS", "إم جي زد إس", "MG ZS", "Car"),
            new("MGRX5", "إم جي آر إكس 5", "MG RX5", "إم جي آر إكس 5", "MG RX5", "Car"),
            new("MGGT", "إم جي جي تي", "MG GT", "إم جي جي تي", "MG GT", "Car")
        }),
        new("Geely", "جيلي", "Geely", new[] { "Car" }, new List<VehicleModelItemDto>
        {
            new("Emgrand", "إمجراند", "Emgrand", "جيلي إمجراند", "Geely Emgrand", "Car"),
            new("Coolray", "كول راي", "Coolray", "جيلي كول راي", "Geely Coolray", "Car"),
            new("Tugella", "توجيلا", "Tugella", "جيلي توجيلا", "Geely Tugella", "Car"),
            new("Monjaro", "مونجارو", "Monjaro", "جيلي مونجارو", "Geely Monjaro", "Car")
        }),
        new("Changan", "شانجان", "Changan", new[] { "Car" }, new List<VehicleModelItemDto>
        {
            new("Alsvin", "ألسفن", "Alsvin", "شانجان ألسفن", "Changan Alsvin", "Car"),
            new("Eado", "إيدو", "Eado", "شانجان إيدو", "Changan Eado", "Car"),
            new("CS35Plus", "سي إس 35 بلس", "CS35 Plus", "شانجان سي إس 35 بلس", "Changan CS35 Plus", "Car"),
            new("CS85", "سي إس 85", "CS85", "شانجان سي إس 85", "Changan CS85", "Car")
        }),
        new("Yamaha", "ياماها", "Yamaha", new[] { "Motorcycle" }, new List<VehicleModelItemDto>
        {
            new("YBR125", "واي بي آر 125", "YBR 125", "ياماها واي بي آر 125", "Yamaha YBR 125", "Motorcycle"),
            new("NMAX155", "إن ماكس 155", "NMAX 155", "ياماها إن ماكس 155", "Yamaha NMAX 155", "Motorcycle"),
            new("TMAX", "تي ماكس", "TMAX", "ياماها تي ماكس", "Yamaha TMAX", "Motorcycle"),
            new("MT03", "إم تي 03", "MT-03", "ياماها إم تي 03", "Yamaha MT-03", "Motorcycle")
        }),
        new("Bajaj", "باجاج", "Bajaj", new[] { "Motorcycle" }, new List<VehicleModelItemDto>
        {
            new("Boxer150", "بوكسر 150", "Boxer 150", "باجاج بوكسر 150", "Bajaj Boxer 150", "Motorcycle"),
            new("Pulsar150", "بولسار 150", "Pulsar 150", "باجاج بولسار 150", "Bajaj Pulsar 150", "Motorcycle"),
            new("Platina100", "بلاتينا 100", "Platina 100", "باجاج بلاتينا 100", "Bajaj Platina 100", "Motorcycle")
        }),
        new("TVS", "تي في إس", "TVS", new[] { "Motorcycle" }, new List<VehicleModelItemDto>
        {
            new("HLX150", "إتش إل إكس 150", "HLX 150", "تي في إس إتش إل إكس 150", "TVS HLX 150", "Motorcycle"),
            new("ApacheRTR", "أباتشي آر تي آر", "Apache RTR", "تي في إس أباتشي آر تي آر", "TVS Apache RTR", "Motorcycle"),
            new("Jupiter", "جوبيتر", "Jupiter", "تي في إس جوبيتر", "TVS Jupiter", "Motorcycle")
        }),
        new("BicycleGeneric", "دراجة هوائية", "Bicycle", new[] { "Bicycle" }, new List<VehicleModelItemDto>
        {
            new("StandardBicycle", "دراجة هوائية عادية", "Standard Bicycle", "دراجة هوائية عادية", "Standard Bicycle", "Bicycle"),
            new("ElectricBicycle", "دراجة كهربائية (E-Bike)", "Electric Bike (E-Bike)", "دراجة كهربائية (E-Bike)", "Electric Bike (E-Bike)", "Bicycle")
        })
    };
}

public sealed class GetVehicleTypesQueryHandler : IQueryHandler<GetVehicleTypesQuery, IReadOnlyList<VehicleTypeDto>>
{
    public Task<Result<IReadOnlyList<VehicleTypeDto>>> Handle(GetVehicleTypesQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(VehicleCatalogData.Types));
    }
}

public sealed class GetVehicleColorsQueryHandler : IQueryHandler<GetVehicleColorsQuery, IReadOnlyList<VehicleColorDto>>
{
    public Task<Result<IReadOnlyList<VehicleColorDto>>> Handle(GetVehicleColorsQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(VehicleCatalogData.Colors));
    }
}

public sealed class SearchVehicleModelsQueryHandler : IQueryHandler<SearchVehicleModelsQuery, IReadOnlyList<VehicleModelSearchResultDto>>
{
    public Task<Result<IReadOnlyList<VehicleModelSearchResultDto>>> Handle(SearchVehicleModelsQuery request, CancellationToken cancellationToken)
    {
        var query = VehicleCatalogData.Makes
            .SelectMany(make => make.Models.Select(model => new VehicleModelSearchResultDto(
                Value: model.FullNameEn,
                MakeCode: make.Code,
                MakeNameAr: make.NameAr,
                MakeNameEn: make.NameEn,
                ModelCode: model.Code,
                ModelNameAr: model.NameAr,
                ModelNameEn: model.NameEn,
                FullNameAr: model.FullNameAr,
                FullNameEn: model.FullNameEn,
                VehicleType: model.VehicleType)));

        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            var typeFilter = request.VehicleType.Trim();
            query = query.Where(x => string.Equals(x.VehicleType, typeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x =>
                x.FullNameAr.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.FullNameEn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.MakeNameAr.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.MakeNameEn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ModelNameAr.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ModelNameEn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.MakeCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ModelCode.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var limit = request.Limit > 0 ? request.Limit : 40;
        IReadOnlyList<VehicleModelSearchResultDto> results = query.Take(limit).ToList();
        return Task.FromResult(Result.Success(results));
    }
}

public sealed class GetVehicleMakesQueryHandler : IQueryHandler<GetVehicleMakesQuery, IReadOnlyList<VehicleMakeDto>>
{
    public Task<Result<IReadOnlyList<VehicleMakeDto>>> Handle(GetVehicleMakesQuery request, CancellationToken cancellationToken)
    {
        var makes = VehicleCatalogData.Makes.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            var type = request.VehicleType.Trim();
            makes = makes
                .Where(m => m.VehicleTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)))
                .Select(m => m with
                {
                    Models = m.Models.Where(model => string.Equals(model.VehicleType, type, StringComparison.OrdinalIgnoreCase)).ToList()
                })
                .Where(m => m.Models.Count > 0);
        }

        IReadOnlyList<VehicleMakeDto> result = makes.ToList();
        return Task.FromResult(Result.Success(result));
    }
}
