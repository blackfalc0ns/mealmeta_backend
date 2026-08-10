using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Seeding;

/// <summary>Idempotent Kuwait governorate/area catalog seed.</summary>
public static class KuwaitRegionCatalogSeeder
{
    private sealed record CatalogItem(string Code, string Ar, string En, RegionType Type, string? ParentCode = null);

    private static readonly CatalogItem[] Items =
    [
        new("KW-CAP", "العاصمة", "Capital", RegionType.Governorate),
        new("KW-HAW", "حولي", "Hawalli", RegionType.Governorate),
        new("KW-MUB", "مبارك الكبير", "Mubarak Al-Kabeer", RegionType.Governorate),
        new("KW-AHM", "الأحمدي", "Ahmadi", RegionType.Governorate),
        new("KW-FAR", "الفروانية", "Farwaniya", RegionType.Governorate),
        new("KW-JAH", "الجهراء", "Jahra", RegionType.Governorate),

        // Capital
        new("KW-KC", "مدينة الكويت", "Kuwait City", RegionType.Area, "KW-CAP"),
        new("KW-CAP-ABDULLA-AL-SALEM", "ضاحية عبد الله السالم", "Abdulla Al-Salem", RegionType.Area, "KW-CAP"),
        new("KW-CAP-ADAILIYA", "العديلية", "Adailiya", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SOUR-GARDENS", "حدائق السور", "Al-Sour Gardens", RegionType.Area, "KW-CAP"),
        new("KW-CAP-BNAID", "بنيد القار", "Bnaid Al-Qar", RegionType.Area, "KW-CAP"),
        new("KW-CAP-DAIYA", "الدعية", "Daiya", RegionType.Area, "KW-CAP"),
        new("KW-DS", "الدسمة", "Dasma", RegionType.Area, "KW-CAP"),
        new("KW-CAP-DOHA", "الدوحة", "Doha", RegionType.Area, "KW-CAP"),
        new("KW-CAP-DOHA-PORT", "ميناء الدوحة", "Doha Port", RegionType.Area, "KW-CAP"),
        new("KW-CAP-FAIHA", "الفيحاء", "Faiha", RegionType.Area, "KW-CAP"),
        new("KW-CAP-FAILAKA", "فيلكا", "Failaka Island", RegionType.Area, "KW-CAP"),
        new("KW-CAP-GRANADA", "غرناطة", "Granada", RegionType.Area, "KW-CAP"),
        new("KW-CAP-JIBLA", "جِبْلَة", "Jibla", RegionType.Area, "KW-CAP"),
        new("KW-KF", "كيفان", "Kaifan", RegionType.Area, "KW-CAP"),
        new("KW-CAP-KHALDIYA", "الخالدية", "Khaldiya", RegionType.Area, "KW-CAP"),
        new("KW-CAP-MANSOURIYA", "المنصورية", "Mansouriya", RegionType.Area, "KW-CAP"),
        new("KW-CAP-MIRQAB", "المرقاب", "Mirqab", RegionType.Area, "KW-CAP"),
        new("KW-CAP-NAHDHA", "النهضة", "Nahdha", RegionType.Area, "KW-CAP"),
        new("KW-CAP-NW-SULAIBIKHAT", "شمال غرب الصليبيخات", "North West Sulaibikhat", RegionType.Area, "KW-CAP"),
        new("KW-CAP-NUZHA", "النزهة", "Nuzha", RegionType.Area, "KW-CAP"),
        new("KW-CAP-QADSIYA", "القادسية", "Qadsiya", RegionType.Area, "KW-CAP"),
        new("KW-CAP-QORTUBA", "قرطبة", "Qortuba", RegionType.Area, "KW-CAP"),
        new("KW-CAP-RAWDA", "الروضة", "Rawda", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SHAMIYA", "الشامية", "Shamiya", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SHARQ", "شرق", "Sharq", RegionType.Area, "KW-CAP"),
        new("KW-SW", "الشويخ", "Shuwaikh", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SHUWAIKH-INDUSTRIAL", "الشويخ الصناعية", "Shuwaikh Industrial Area", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SHUWAIKH-PORT", "ميناء الشويخ", "Shuwaikh Port", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SULAIBIKHAT", "الصليبخات", "Sulaibikhat", RegionType.Area, "KW-CAP"),
        new("KW-CAP-QAIRAWAN", "القيروان", "Qairawan", RegionType.Area, "KW-CAP"),
        new("KW-CAP-SURRA", "السرة", "Surra", RegionType.Area, "KW-CAP"),
        new("KW-CAP-OUHA", "جزيرة عوهة", "Ouha Island", RegionType.Area, "KW-CAP"),
        new("KW-CAP-MISKAN", "جزيرة مسكان", "Miskan Island", RegionType.Area, "KW-CAP"),
        new("KW-CAP-UMM-NAMIL", "جزيرة أم النمل", "Umm an Namil Island", RegionType.Area, "KW-CAP"),
        new("KW-CAP-YARMOUK", "اليرموك", "Yarmouk", RegionType.Area, "KW-CAP"),

        // Hawalli
        new("KW-HAW-BAYAN", "بيان", "Bayan", RegionType.Area, "KW-HAW"),
        new("KW-HAW-JABRIYA", "الجابرية", "Jabriya", RegionType.Area, "KW-HAW"),
        new("KW-RM", "الرميثية", "Rumaithiya", RegionType.Area, "KW-HAW"),
        new("KW-HAW-SALAM", "سلام", "Salam", RegionType.Area, "KW-HAW"),
        new("KW-HAW-SALWA", "سلوى", "Salwa", RegionType.Area, "KW-HAW"),
        new("KW-HAW-BIDAA", "البدع", "Al-Bida'a", RegionType.Area, "KW-HAW"),
        new("KW-HAW-ANJAFHA", "أنجفة", "Anjafa", RegionType.Area, "KW-HAW"),
        new("KW-HW", "حولي", "Hawalli", RegionType.Area, "KW-HAW"),
        new("KW-HAW-HITTEEN", "حطين", "Hitteen", RegionType.Area, "KW-HAW"),
        new("KW-HAW-MISHREF", "مشرف", "Mishrif", RegionType.Area, "KW-HAW"),
        new("KW-HAW-MUBARAK-ABDULLAH", "مبارك العبدالله", "Mubarak Al-Abdullah", RegionType.Area, "KW-HAW"),
        new("KW-SL", "السالمية", "Salmiya", RegionType.Area, "KW-HAW"),
        new("KW-HAW-SHAAB", "الشعب", "Shaab", RegionType.Area, "KW-HAW"),
        new("KW-HAW-SHUHADA", "الشهداء", "Shuhada", RegionType.Area, "KW-HAW"),
        new("KW-HAW-SIDDIQ", "الصديق", "Al-Siddiq", RegionType.Area, "KW-HAW"),
        new("KW-HAW-MINISTRIES", "منطقة الوزارات", "Ministries Area", RegionType.Area, "KW-HAW"),
        new("KW-HAW-ZAHRA", "الزهراء", "Zahra", RegionType.Area, "KW-HAW"),

        // Mubarak Al-Kabeer
        new("KW-MUB-ABU-HASANIYA", "أبو الحصانية", "Abu Al Hasaniya", RegionType.Area, "KW-MUB"),
        new("KW-MUB-ABU-FUTAIRA", "أبو فطيرة", "Abu Ftaira", RegionType.Area, "KW-MUB"),
        new("KW-MUB-ADAN", "العدان", "Al-Adan", RegionType.Area, "KW-MUB"),
        new("KW-MUB-QURAIN", "القرين", "Al Qurain", RegionType.Area, "KW-MUB"),
        new("KW-MUB-QUSOUR", "القصور", "Al-Qusour", RegionType.Area, "KW-MUB"),
        new("KW-MUB-FNAITEES", "الفنيطيس", "Al-Fnaitees", RegionType.Area, "KW-MUB"),
        new("KW-MUB-MESSILA", "المسيلة", "Messila", RegionType.Area, "KW-MUB"),
        new("KW-MUB-MASAYEL", "المسايل", "Al-Masayel", RegionType.Area, "KW-MUB"),
        new("KW-MK", "مبارك الكبير", "Mubarak Al-Kabeer", RegionType.Area, "KW-MUB"),
        new("KW-MUB-SABAH-AL-SALEM", "صباح السالم", "Sabah Al-Salem", RegionType.Area, "KW-MUB"),
        new("KW-MUB-SUBHAN", "صبحان", "Subhan Industrial", RegionType.Area, "KW-MUB"),
        new("KW-MUB-WISTA", "الوسطى", "Wista", RegionType.Area, "KW-MUB"),
        new("KW-MUB-WEST-ABU-FUTAIRA", "غرب أبو فطيرة الحرفية", "West Abu Ftaira Herafiya", RegionType.Area, "KW-MUB"),

        // Ahmadi
        new("KW-AHM-ABU-HALIFA", "أبو حليفة", "Abu Halifa", RegionType.Area, "KW-AHM"),
        new("KW-AHM-MINA-ABDULLA", "ميناء عبدالله", "Mina Abdulla", RegionType.Area, "KW-AHM"),
        new("KW-AH", "الأحمدي", "Ahmadi", RegionType.Area, "KW-AHM"),
        new("KW-AHM-ALI-SABAH", "علي صباح السالم", "Ali Sabah Al-Salem", RegionType.Area, "KW-AHM"),
        new("KW-AHM-EGAILA", "العقيلة", "Egaila", RegionType.Area, "KW-AHM"),
        new("KW-AHM-BAR", "بر الأحمدي", "Bar Al-Ahmadi", RegionType.Area, "KW-AHM"),
        new("KW-AHM-BNAIDER", "بنيدر", "Bnaider", RegionType.Area, "KW-AHM"),
        new("KW-AHM-DHAHER", "الظهر", "Dhaher", RegionType.Area, "KW-AHM"),
        new("KW-FH", "الفحيحيل", "Fahaheel", RegionType.Area, "KW-AHM"),
        new("KW-AHM-FAHAD-AHMAD", "فهد الأحمد", "Fahad Al-Ahmad", RegionType.Area, "KW-AHM"),
        new("KW-AHM-HADIYA", "هدية", "Hadiya", RegionType.Area, "KW-AHM"),
        new("KW-AHM-JABER-ALI", "جابر العلي", "Jaber Al-Ali", RegionType.Area, "KW-AHM"),
        new("KW-AHM-JULAIA", "الجليعة", "Al-Julaia'a", RegionType.Area, "KW-AHM"),
        new("KW-AHM-KHAIRAN", "الخيران", "Khairan", RegionType.Area, "KW-AHM"),
        new("KW-AHM-MAHBOULA", "المهبولة", "Mahboula", RegionType.Area, "KW-AHM"),
        new("KW-MN", "المنقف", "Mangaf", RegionType.Area, "KW-AHM"),
        new("KW-AHM-MAGWA", "المقوع", "Magwa", RegionType.Area, "KW-AHM"),
        new("KW-AHM-WAFRA-RES", "وفرة السكنية", "Wafra Residential", RegionType.Area, "KW-AHM"),
        new("KW-AHM-NUWAISEEB", "النويصيب", "Al-Nuwaiseeb", RegionType.Area, "KW-AHM"),
        new("KW-AHM-RIQQA", "الرقة", "Riqqa", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SABAH-AHMAD", "صباح الأحمد", "Sabah Al Ahmad", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SEA-CITY", "مدينة صباح الأحمد البحرية", "Sabah Al Ahmad Sea City", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SABAHIYA", "الصباحية", "Sabahiya", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SHUAIBA", "الشعيبة الصناعية", "Shuaiba Industrial", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SOUTH-SABAHIYA", "جنوب الصباحية", "South Sabahiya", RegionType.Area, "KW-AHM"),
        new("KW-AHM-WAFRA", "الوفرة", "Wafra", RegionType.Area, "KW-AHM"),
        new("KW-AHM-ZOOR", "الزور", "Zoor", RegionType.Area, "KW-AHM"),
        new("KW-AHM-FINTAS", "الفنطاس", "Fintas", RegionType.Area, "KW-AHM"),
        new("KW-AHM-SHADADIYA", "الشدادية الصناعية", "Al Shadadiya Industrial", RegionType.Area, "KW-AHM"),

        // Farwaniya
        new("KW-FAR-ABDULLAH-MUBARAK", "عبدالله المبارك", "Abdullah Al-Mubarak", RegionType.Area, "KW-FAR"),
        new("KW-FAR-AIRPORT", "منطقة المطار", "Airport District", RegionType.Area, "KW-FAR"),
        new("KW-FAR-ANDALUS", "الأندلس", "Andalus", RegionType.Area, "KW-FAR"),
        new("KW-FAR-ARDIYA", "العارضية", "Ardiya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-ARDIYA-HERAFIYA", "العارضية حرفية", "Ardiya Herafiya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-ISHBILIYA", "إشبيلية", "Ishbiliya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-DAJEEJ", "الضجيج", "Al-Dajeej", RegionType.Area, "KW-FAR"),
        new("KW-FR", "الفروانية", "Farwaniya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-FERDOUS", "الفردوس", "Ferdous", RegionType.Area, "KW-FAR"),
        new("KW-FAR-JLEEB", "جليب الشيوخ", "Jleeb Al-Shuyoukh", RegionType.Area, "KW-FAR"),
        new("KW-FAR-KHAITAN", "خيطان", "Khaitan", RegionType.Area, "KW-FAR"),
        new("KW-FAR-OMARIYA", "العمرية", "Omariya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-RABIYA", "الرابية", "Rabiya", RegionType.Area, "KW-FAR"),
        new("KW-FAR-RAI", "الري", "Al-Rai", RegionType.Area, "KW-FAR"),
        new("KW-FAR-RIGGAI", "الرقعي", "Al-Riggai", RegionType.Area, "KW-FAR"),
        new("KW-FAR-REHAB", "الرحاب", "Rehab", RegionType.Area, "KW-FAR"),
        new("KW-FAR-SABAH-NASSER", "صباح الناصر", "Sabah Al-Nasser", RegionType.Area, "KW-FAR"),
        new("KW-FAR-SABAH-SALEM-UNIVERSITY", "جامعة صباح السالم", "Sabah Al-Salem University", RegionType.Area, "KW-FAR"),
        new("KW-FAR-WEST-ABDULLAH", "غرب عبدالله المبارك", "West Abdullah Al-Mubarak", RegionType.Area, "KW-FAR"),
        new("KW-FAR-SOUTH-ABDULLAH", "جنوب عبدالله المبارك", "South Abdullah Al-Mubarak", RegionType.Area, "KW-FAR"),
        new("KW-FAR-SULAIBIYA-INDUSTRIAL", "الصليبية الصناعية", "Sulaibiya Industrial", RegionType.Area, "KW-FAR"),

        // Jahra
        new("KW-JAH-ABDALI", "العبدلي", "Abdali", RegionType.Area, "KW-JAH"),
        new("KW-JAH-MUTLAA", "المطلاع", "Al-Mutlaa", RegionType.Area, "KW-JAH"),
        new("KW-JAH-KAZMA", "كاظمة", "Kazma", RegionType.Area, "KW-JAH"),
        new("KW-JAH-BAHRA", "بحرة", "Bahra", RegionType.Area, "KW-JAH"),
        new("KW-JAH-KABD", "كبد", "Kabd", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SHEQAYA", "الشقايه", "Al-Sheqaya", RegionType.Area, "KW-JAH"),
        new("KW-JAH-NAHDA", "النهضة", "Al-Nahda", RegionType.Area, "KW-JAH"),
        new("KW-JAH-AMGHARA", "أمغرة", "Amghara Industrial", RegionType.Area, "KW-JAH"),
        new("KW-JAH-BAR", "بر الجهراء", "Bar Al-Jahra", RegionType.Area, "KW-JAH"),
        new("KW-JH", "الجهراء", "Jahra", RegionType.Area, "KW-JAH"),
        new("KW-JAH-INDUSTRIAL-HERAFIYA", "الجهراء الصناعية الحرفية", "Jahra Industrial Herafiya", RegionType.Area, "KW-JAH"),
        new("KW-JAH-NAEEM", "النعيم", "Naeem", RegionType.Area, "KW-JAH"),
        new("KW-JAH-NASSEEM", "النسيم", "Nasseem", RegionType.Area, "KW-JAH"),
        new("KW-JAH-OYOUN", "العيون", "Oyoun", RegionType.Area, "KW-JAH"),
        new("KW-JAH-QASR", "القصر", "Qasr", RegionType.Area, "KW-JAH"),
        new("KW-JAH-JABER-AHMAD", "جابر الأحمد", "Jaber Al-Ahmad", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SAAD-ABDULLAH", "سعد العبدالله", "Saad Al Abdullah", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SALMI", "السالمي", "Salmi", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SUBIYA", "الصبية", "Subiya", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SULAIBIYA", "الصليبية", "Sulaibiya", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SULAIBIYA-AGRICULTURAL", "الصليبية الزراعية", "Sulaibiya Agricultural Area", RegionType.Area, "KW-JAH"),
        new("KW-JAH-SULAIBIYA-RESIDENTIAL", "الصليبية السكنية", "Sulaibiya Residential", RegionType.Area, "KW-JAH"),
        new("KW-JAH-TAIMA", "تيماء", "Taima", RegionType.Area, "KW-JAH"),
        new("KW-JAH-WAHA", "الواحة", "Waha", RegionType.Area, "KW-JAH"),
        new("KW-JAH-BUBIYAN", "جزيرة بوبيان", "Bubiyan Island", RegionType.Area, "KW-JAH"),
        new("KW-JAH-WARBAH", "جزيرة وربة", "Warbah Island", RegionType.Area, "KW-JAH"),
    ];

    public static async Task SeedAsync(AppDbContext db, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstAsync(x => x.Code == "KW", cancellationToken);
        var existing = await db.Regions.Where(x => x.CountryId == country.Id).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items.Where(x => x.Type == RegionType.Governorate))
        {
            if (!existing.TryGetValue(item.Code, out var entity))
            {
                entity = new Region { CountryId = country.Id, Code = item.Code, CreatedAtUtc = nowUtc };
                db.Regions.Add(entity);
                existing[item.Code] = entity;
            }
            entity.NameAr = item.Ar;
            entity.NameEn = item.En;
            entity.Type = item.Type;
            entity.ParentRegionId = null;
            entity.IsActive = true;
            ids[item.Code] = entity.Id;
        }

        foreach (var item in Items.Where(x => x.Type == RegionType.Area))
        {
            if (!existing.TryGetValue(item.Code, out var entity))
            {
                entity = new Region { CountryId = country.Id, Code = item.Code, CreatedAtUtc = nowUtc };
                db.Regions.Add(entity);
                existing[item.Code] = entity;
            }
            entity.NameAr = item.Ar;
            entity.NameEn = item.En;
            entity.Type = item.Type;
            entity.ParentRegionId = item.ParentCode is null ? null : ids[item.ParentCode];
            entity.IsActive = true;
        }

        var supportedCodes = Items.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in existing.Values.Where(x => !supportedCodes.Contains(x.Code)))
        {
            stale.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
