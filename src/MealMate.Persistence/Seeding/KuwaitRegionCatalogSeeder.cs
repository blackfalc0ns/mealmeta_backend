using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Seeding;

/// <summary>Idempotent Kuwait governorate/area catalog seed.</summary>
public static class KuwaitRegionCatalogSeeder
{
    private sealed record CatalogItem(string Code, string Ar, string En, RegionType Type, string? ParentCode = null);

    private sealed record Centroid(decimal Lat, decimal Lng);

    /// <summary>Approximate centers for map markers (not official boundaries).</summary>
    private static readonly Dictionary<string, Centroid> GovernorateCentroids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KW-CAP"] = new(29.3759m, 47.9774m),
        ["KW-HAW"] = new(29.3375m, 48.0289m),
        ["KW-MUB"] = new(29.2000m, 48.0889m),
        ["KW-AHM"] = new(29.0769m, 48.0837m),
        ["KW-FAR"] = new(29.2775m, 47.9586m),
        ["KW-JAH"] = new(29.3375m, 47.6581m),
    };

    /// <summary>
    /// Approximate real-world Area centroids for map pin → catalog matching.
    /// Values are operational (not cadastral boundaries); keep within each Area.
    /// </summary>
    private static readonly Dictionary<string, Centroid> AreaCentroids = new(StringComparer.OrdinalIgnoreCase)
    {
        // Capital
        ["KW-KC"] = new(29.3759m, 47.9774m),
        ["KW-CAP-ABDULLA-AL-SALEM"] = new(29.3510m, 47.9930m),
        ["KW-CAP-ADAILIYA"] = new(29.3580m, 47.9800m),
        ["KW-CAP-SOUR-GARDENS"] = new(29.3720m, 47.9680m),
        ["KW-CAP-BNAID"] = new(29.3780m, 48.0000m),
        ["KW-CAP-DAIYA"] = new(29.3700m, 48.0050m),
        ["KW-DS"] = new(29.3880m, 48.0000m),
        ["KW-CAP-DOHA"] = new(29.3400m, 47.8200m),
        ["KW-CAP-DOHA-PORT"] = new(29.3550m, 47.8050m),
        ["KW-CAP-FAIHA"] = new(29.3450m, 47.9850m),
        ["KW-CAP-FAILAKA"] = new(29.4390m, 48.3300m),
        ["KW-CAP-GRANADA"] = new(29.3050m, 47.8900m),
        ["KW-CAP-JIBLA"] = new(29.3720m, 47.9700m),
        ["KW-KF"] = new(29.3400m, 47.9650m),
        ["KW-CAP-KHALDIYA"] = new(29.3500m, 47.9700m),
        ["KW-CAP-MANSOURIYA"] = new(29.3620m, 47.9950m),
        ["KW-CAP-MIRQAB"] = new(29.3700m, 47.9750m),
        ["KW-CAP-NAHDHA"] = new(29.3200m, 47.8700m),
        ["KW-CAP-NW-SULAIBIKHAT"] = new(29.3200m, 47.8300m),
        ["KW-CAP-NUZHA"] = new(29.3480m, 47.9900m),
        ["KW-CAP-QADSIYA"] = new(29.3550m, 48.0000m),
        ["KW-CAP-QORTUBA"] = new(29.3300m, 47.9600m),
        ["KW-CAP-RAWDA"] = new(29.3450m, 47.9950m),
        ["KW-CAP-SHAMIYA"] = new(29.3600m, 47.9600m),
        ["KW-CAP-SHARQ"] = new(29.3780m, 47.9900m),
        ["KW-SW"] = new(29.3450m, 47.9300m),
        ["KW-CAP-SHUWAIKH-INDUSTRIAL"] = new(29.3350m, 47.9200m),
        ["KW-CAP-SHUWAIKH-PORT"] = new(29.3550m, 47.9100m),
        ["KW-CAP-SULAIBIKHAT"] = new(29.3100m, 47.8400m),
        ["KW-CAP-QAIRAWAN"] = new(29.3000m, 47.8600m),
        ["KW-CAP-SURRA"] = new(29.3300m, 47.9800m),
        ["KW-CAP-OUHA"] = new(29.3800m, 48.2200m),
        ["KW-CAP-MISKAN"] = new(29.4900m, 48.2500m),
        ["KW-CAP-UMM-NAMIL"] = new(29.3800m, 47.8700m),
        ["KW-CAP-YARMOUK"] = new(29.3250m, 47.9700m),

        // Hawalli
        ["KW-HAW-BAYAN"] = new(29.3030m, 48.0400m),
        ["KW-HAW-JABRIYA"] = new(29.3170m, 48.0680m),
        ["KW-RM"] = new(29.3100m, 48.0700m),
        ["KW-HAW-SALAM"] = new(29.2950m, 48.0550m),
        ["KW-HAW-SALWA"] = new(29.2850m, 48.0800m),
        ["KW-HAW-BIDAA"] = new(29.3500m, 48.0500m),
        ["KW-HAW-ANJAFHA"] = new(29.3250m, 48.0500m),
        ["KW-HW"] = new(29.3330m, 48.0290m),
        ["KW-HAW-HITTEEN"] = new(29.2950m, 48.0300m),
        ["KW-HAW-MISHREF"] = new(29.2750m, 48.0700m),
        ["KW-HAW-MUBARAK-ABDULLAH"] = new(29.2700m, 48.0550m),
        ["KW-SL"] = new(29.3390m, 48.0760m),
        ["KW-HAW-SHAAB"] = new(29.3550m, 48.0400m),
        ["KW-HAW-SHUHADA"] = new(29.2900m, 48.0450m),
        ["KW-HAW-SIDDIQ"] = new(29.3000m, 48.0200m),
        ["KW-HAW-MINISTRIES"] = new(29.3100m, 48.0150m),
        ["KW-HAW-ZAHRA"] = new(29.2800m, 48.0350m),

        // Mubarak Al-Kabeer
        ["KW-MUB-ABU-HASANIYA"] = new(29.2200m, 48.1100m),
        ["KW-MUB-ABU-FUTAIRA"] = new(29.2000m, 48.1000m),
        ["KW-MUB-ADAN"] = new(29.2200m, 48.0700m),
        ["KW-MUB-QURAIN"] = new(29.2050m, 48.0800m),
        ["KW-MUB-QUSOUR"] = new(29.2150m, 48.0600m),
        ["KW-MUB-FNAITEES"] = new(29.1900m, 48.1200m),
        ["KW-MUB-MESSILA"] = new(29.2400m, 48.0900m),
        ["KW-MUB-MASAYEL"] = new(29.2300m, 48.1000m),
        ["KW-MK"] = new(29.2000m, 48.0889m),
        ["KW-MUB-SABAH-AL-SALEM"] = new(29.2550m, 48.0700m),
        ["KW-MUB-SUBHAN"] = new(29.1800m, 48.0500m),
        ["KW-MUB-WISTA"] = new(29.2100m, 48.0900m),
        ["KW-MUB-WEST-ABU-FUTAIRA"] = new(29.1950m, 48.0850m),

        // Ahmadi
        ["KW-AHM-ABU-HALIFA"] = new(29.1400m, 48.1200m),
        ["KW-AHM-MINA-ABDULLA"] = new(28.9700m, 48.1600m),
        ["KW-AH"] = new(29.0769m, 48.0837m),
        ["KW-AHM-ALI-SABAH"] = new(28.9000m, 48.1500m),
        ["KW-AHM-EGAILA"] = new(29.1600m, 48.1000m),
        ["KW-AHM-BAR"] = new(29.0500m, 47.9500m),
        ["KW-AHM-BNAIDER"] = new(28.8500m, 48.2800m),
        ["KW-AHM-DHAHER"] = new(29.1000m, 48.0700m),
        ["KW-FH"] = new(29.0820m, 48.1300m),
        ["KW-AHM-FAHAD-AHMAD"] = new(29.0900m, 48.0900m),
        ["KW-AHM-HADIYA"] = new(29.1000m, 48.1000m),
        ["KW-AHM-JABER-ALI"] = new(29.1200m, 48.0800m),
        ["KW-AHM-JULAIA"] = new(28.8800m, 48.2700m),
        ["KW-AHM-KHAIRAN"] = new(28.6500m, 48.3800m),
        ["KW-AHM-MAHBOULA"] = new(29.1450m, 48.1300m),
        ["KW-MN"] = new(29.1000m, 48.1200m),
        ["KW-AHM-MAGWA"] = new(29.0000m, 48.0500m),
        ["KW-AHM-WAFRA-RES"] = new(28.6000m, 47.9500m),
        ["KW-AHM-NUWAISEEB"] = new(28.5500m, 48.4000m),
        ["KW-AHM-RIQQA"] = new(29.1100m, 48.0900m),
        ["KW-AHM-SABAH-AHMAD"] = new(28.7500m, 48.2000m),
        ["KW-AHM-SEA-CITY"] = new(28.7000m, 48.3500m),
        ["KW-AHM-SABAHIYA"] = new(29.1000m, 48.0800m),
        ["KW-AHM-SHUAIBA"] = new(29.0400m, 48.1600m),
        ["KW-AHM-SOUTH-SABAHIYA"] = new(29.0850m, 48.0750m),
        ["KW-AHM-WAFRA"] = new(28.5800m, 47.9300m),
        ["KW-AHM-ZOOR"] = new(28.7500m, 48.4000m),
        ["KW-AHM-FINTAS"] = new(29.1700m, 48.1200m),
        ["KW-AHM-SHADADIYA"] = new(29.1500m, 47.9000m),

        // Farwaniya
        ["KW-FAR-ABDULLAH-MUBARAK"] = new(29.2500m, 47.9200m),
        ["KW-FAR-AIRPORT"] = new(29.2260m, 47.9680m),
        ["KW-FAR-ANDALUS"] = new(29.2700m, 47.9500m),
        ["KW-FAR-ARDIYA"] = new(29.2700m, 47.9100m),
        ["KW-FAR-ARDIYA-HERAFIYA"] = new(29.2650m, 47.9000m),
        ["KW-FAR-ISHBILIYA"] = new(29.2800m, 47.9400m),
        ["KW-FAR-DAJEEJ"] = new(29.2400m, 47.9400m),
        ["KW-FR"] = new(29.2775m, 47.9586m),
        ["KW-FAR-FERDOUS"] = new(29.2700m, 47.9700m),
        ["KW-FAR-JLEEB"] = new(29.2600m, 47.9300m),
        ["KW-FAR-KHAITAN"] = new(29.2900m, 47.9700m),
        ["KW-FAR-OMARIYA"] = new(29.2850m, 47.9800m),
        ["KW-FAR-RABIYA"] = new(29.2800m, 47.9900m),
        ["KW-FAR-RAI"] = new(29.3000m, 47.9400m),
        ["KW-FAR-RIGGAI"] = new(29.2950m, 47.9600m),
        ["KW-FAR-REHAB"] = new(29.2750m, 47.9800m),
        ["KW-FAR-SABAH-NASSER"] = new(29.2500m, 47.9000m),
        ["KW-FAR-SABAH-SALEM-UNIVERSITY"] = new(29.2100m, 48.0200m),
        ["KW-FAR-WEST-ABDULLAH"] = new(29.2450m, 47.9050m),
        ["KW-FAR-SOUTH-ABDULLAH"] = new(29.2400m, 47.9150m),
        ["KW-FAR-SULAIBIYA-INDUSTRIAL"] = new(29.2700m, 47.8200m),

        // Jahra
        ["KW-JAH-ABDALI"] = new(30.0200m, 47.7200m),
        ["KW-JAH-MUTLAA"] = new(29.4500m, 47.6800m),
        ["KW-JAH-KAZMA"] = new(29.4000m, 47.7000m),
        ["KW-JAH-BAHRA"] = new(29.3800m, 47.6500m),
        ["KW-JAH-KABD"] = new(29.2000m, 47.7000m),
        ["KW-JAH-SHEQAYA"] = new(29.1000m, 47.4000m),
        ["KW-JAH-NAHDA"] = new(29.3400m, 47.7000m),
        ["KW-JAH-AMGHARA"] = new(29.3000m, 47.7500m),
        ["KW-JAH-BAR"] = new(29.3500m, 47.5500m),
        ["KW-JH"] = new(29.3375m, 47.6581m),
        ["KW-JAH-INDUSTRIAL-HERAFIYA"] = new(29.3300m, 47.6400m),
        ["KW-JAH-NAEEM"] = new(29.3500m, 47.6800m),
        ["KW-JAH-NASSEEM"] = new(29.3450m, 47.6700m),
        ["KW-JAH-OYOUN"] = new(29.3600m, 47.6900m),
        ["KW-JAH-QASR"] = new(29.3200m, 47.6600m),
        ["KW-JAH-JABER-AHMAD"] = new(29.3000m, 47.8000m),
        ["KW-JAH-SAAD-ABDULLAH"] = new(29.3600m, 47.7200m),
        ["KW-JAH-SALMI"] = new(29.1000m, 46.7000m),
        ["KW-JAH-SUBIYA"] = new(29.5500m, 48.0000m),
        ["KW-JAH-SULAIBIYA"] = new(29.2800m, 47.8200m),
        ["KW-JAH-SULAIBIYA-AGRICULTURAL"] = new(29.2700m, 47.8000m),
        ["KW-JAH-SULAIBIYA-RESIDENTIAL"] = new(29.2750m, 47.8100m),
        ["KW-JAH-TAIMA"] = new(29.3400m, 47.6900m),
        ["KW-JAH-WAHA"] = new(29.3550m, 47.7000m),
        ["KW-JAH-BUBIYAN"] = new(29.7500m, 48.3000m),
        ["KW-JAH-WARBAH"] = new(29.9900m, 48.0700m),
    };

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
            if (GovernorateCentroids.TryGetValue(item.Code, out var centroid))
            {
                entity.CentroidLatitude = centroid.Lat;
                entity.CentroidLongitude = centroid.Lng;
            }
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
            if (AreaCentroids.TryGetValue(item.Code, out var areaCentroid))
            {
                entity.CentroidLatitude = areaCentroid.Lat;
                entity.CentroidLongitude = areaCentroid.Lng;
            }
            else if (item.ParentCode is not null
                && GovernorateCentroids.TryGetValue(item.ParentCode, out var parentCentroid))
            {
                // Fallback only when an Area code is missing from AreaCentroids.
                var hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(item.Code));
                var latOffset = ((hash % 17) - 8) * 0.004m;
                var lngOffset = (((hash / 17) % 17) - 8) * 0.004m;
                entity.CentroidLatitude = parentCentroid.Lat + latOffset;
                entity.CentroidLongitude = parentCentroid.Lng + lngOffset;
            }
        }

        // Address capture requires a selectable block after the customer chooses
        // an area. Keep a stable, editable operational starter catalog per area;
        // administrators can later rename or deactivate a block for local rules.
        var areas = existing.Values.Where(x => x.Type == RegionType.Area).ToList();
        foreach (var area in areas)
        {
            for (var number = 1; number <= 12; number++)
            {
                var code = $"{area.Code}-BLK-{number:D2}";
                if (!existing.TryGetValue(code, out var block))
                {
                    block = new Region { CountryId = country.Id, Code = code, CreatedAtUtc = nowUtc };
                    db.Regions.Add(block);
                    existing[code] = block;
                }

                block.NameAr = $"قطعة {number}";
                block.NameEn = $"Block {number}";
                block.Type = RegionType.Block;
                block.ParentRegionId = area.Id;
                block.IsActive = true;
            }
        }

        var supportedCodes = Items.Select(x => x.Code)
            .Concat(areas.SelectMany(area => Enumerable.Range(1, 12).Select(number => $"{area.Code}-BLK-{number:D2}")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in existing.Values.Where(x => !supportedCodes.Contains(x.Code)))
        {
            stale.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
