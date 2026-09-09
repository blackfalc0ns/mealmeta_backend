namespace MealMate.Persistence.Seeding;

public static class DemoRestaurantBrandCatalog
{
    private const string Media = "https://ik.imagekit.io/f7h9cj23x/catalog/restaurants/";

    public static IReadOnlyList<DemoRestaurantBrand> All { get; } =
    [
        Brand("diet-care", "DC01", "مطبخ دايت كير", "Diet Care", "شركة دايت كير الغذائية ذ.م.م", "Diet Care Food Company W.L.L", "dietcare.hq@mealmate.kw", "+96550001011", "باسل الشمري", 1, "العاصمة، برج الحمراء، الدور 15", "Kuwait City, Al Hamra Tower, Floor 15", 29.3789m, 47.9904m, "العاصمة", "Capital", 88m),
        Brand("green-bowl", "GB02", "جرين بول", "Green Bowl", "جرين بول للوجبات الصحية ذ.م.م", "Green Bowl Healthy Meals W.L.L", "greenbowl@mealmate.kw", "+96550001002", "نورة العتيبي", 2, "السالمية، شارع سالم المبارك", "Salmiya, Salem Al Mubarak St", 29.3374m, 48.0761m, "حولي", "Hawalli", 92m),
        Brand("protein-lab", "PL03", "بروتين لاب", "Protein Lab", "بروتين لاب للمطاعم ذ.م.م", "Protein Lab Restaurants W.L.L", "proteinlab@mealmate.kw", "+96550001003", "خالد العنزي", 3, "حولي، شارع تونس", "Hawally, Tunisia St", 29.3328m, 48.0284m, "حولي", "Hawalli", 96m),
        Brand("fit-plate", "FP04", "فيت بليت", "Fit Plate", "فيت بليت للتغذية ذ.م.م", "Fit Plate Nutrition W.L.L", "fitplate@mealmate.kw", "+96550001004", "سارة العلي", 4, "الجابرية، قطعة 1أ", "Jabriya, Block 1A", 29.3162m, 48.0281m, "حولي", "Hawalli", 132m),
        Brand("olive-herb", "OH05", "زيتون وأعشاب", "Olive Herb", "زيتون وأعشاب للمواد الغذائية", "Olive Herb Food Establishment", "oliveherb@mealmate.kw", "+96550001005", "ليان المطيري", 5, "الشويخ، القطعة 4", "Shuwaikh, Block 4", 29.3398m, 47.9386m, "العاصمة", "Capital", 138m),
        Brand("balance-box", "BB06", "بالانس بوكس", "Balance Box", "بالانس بوكس للوجبات المعلبة ذ.م.م", "Balance Box Meal Prep W.L.L", "balancebox@mealmate.kw", "+96550001006", "عبدالرحمن المطيري", 6, "الفروانية، شارع حبيب مناور", "Farwaniya, Habib Munawer St", 29.2774m, 47.9589m, "الفروانية", "Farwaniya", 144m),
        Brand("harvest-kitchen", "HK07", "هارفست كيتشن", "Harvest Kitchen", "هارفست كيتشن للمطابخ المركزية", "Harvest Kitchen Central Kitchens", "harvest@mealmate.kw", "+96550001007", "مريم الشمري", 7, "الري، المنطقة الصناعية", "Rai, Industrial Area", 29.3051m, 47.9248m, "الفروانية", "Farwaniya", 148m),
        Brand("lean-flame", "LF08", "لين فليم", "Lean Flame", "لين فليم للمشاوي الصحية ذ.م.م", "Lean Flame Healthy Grill W.L.L", "leanflame@mealmate.kw", "+96550001008", "فهد الدوسري", 8, "الأحمدي، الفحيحيل", "Ahmadi, Fahaheel", 29.0824m, 48.1302m, "الأحمدي", "Ahmadi", 182m),
        Brand("noon-nutrition", "NN09", "نون نيوتريشن", "Noon Nutrition", "نون نيوتريشن للحمية ذ.م.م", "Noon Nutrition Diet W.L.L", "noon@mealmate.kw", "+96550001009", "هند الرشيد", 9, "الجهراء، سوق الجهراء", "Jahra, Jahra Souq", 29.3376m, 47.6581m, "الجهراء", "Jahra", 190m),
        Brand("aura-meals", "AM10", "أورا ميلز", "Aura Meals", "أورا ميلز للوجبات الفاخرة ذ.م.م", "Aura Meals Premium W.L.L", "aura@mealmate.kw", "+96550001010", "دانة الخالد", 10, "المسيلة، شارع الخليج", "Messila, Gulf Road", 29.1998m, 48.0884m, "مبارك الكبير", "Mubarak Al-Kabeer", 198m),
        Brand("qibla-kitchen", "QK11", "مطبخ القبلة", "Qibla Kitchen", "مطبخ القبلة للوجبات ذ.م.م", "Qibla Kitchen Meals W.L.L", "qibla@mealmate.kw", "+96550001035", "يوسف العلي", 11, "القبلة، شارع فهد السالم", "Qibla, Fahad Al Salem St", 29.3721m, 47.9732m, "العاصمة", "Capital", 85m),
        Brand("dasman-bite", "DB12", "دسمان بايت", "Dasman Bite", "دسمان بايت للأغذية ذ.م.م", "Dasman Bite Food W.L.L", "dasman@mealmate.kw", "+96550001036", "منى الدوسري", 12, "دسمان، شارع الخليج", "Dasman, Gulf Road", 29.3884m, 48.0052m, "العاصمة", "Capital", 90m),
        Brand("khaitan-bowl", "KB13", "خيتان بول", "Khaitan Bowl", "خيتان بول للوجبات الصحية", "Khaitan Bowl Healthy Meals", "khaitan@mealmate.kw", "+96550001037", "علي المطيري", 13, "خيطان، القطعة 8", "Khaitan, Block 8", 29.2924m, 47.9781m, "الفروانية", "Farwaniya", 94m),
        Brand("sulaibiya-fresh", "SF14", "صليبية فريش", "Sulaibiya Fresh", "صليبية فريش للتغذية ذ.م.م", "Sulaibiya Fresh Nutrition W.L.L", "sulaibiya@mealmate.kw", "+96550001038", "فاطمة العنزي", 14, "الصليبية، المنطقة السكنية", "Sulaibiya, Residential Area", 29.2872m, 47.8184m, "الجهراء", "Jahra", 98m),
        Brand("mangaf-lite", "ML15", "المنقف لايت", "Mangaf Lite", "المنقف لايت للوجبات ذ.م.م", "Mangaf Lite Meals W.L.L", "mangaf@mealmate.kw", "+96550001039", "حمد العتيبي", 15, "المنقف، شارع الساحل", "Mangaf, Coastal Road", 29.0963m, 48.1324m, "الأحمدي", "Ahmadi", 100m),
        Brand("adan-daily", "AD16", "العدان ديلي", "Adan Daily", "العدان ديلي للمطابخ ذ.م.م", "Adan Daily Kitchens W.L.L", "adan@mealmate.kw", "+96550001040", "نوف الشمري", 16, "العدان، القطعة 4", "Adan, Block 4", 29.2154m, 48.0651m, "مبارك الكبير", "Mubarak Al-Kabeer", 87m),
        Brand("riggae-plate", "RP17", "الرقعي بليت", "Riggae Plate", "الرقعي بليت للأغذية", "Riggae Plate Food Co.", "riggae@mealmate.kw", "+96550001041", "بدر الخالد", 17, "الرقعي، شارع محمد بن القاسم", "Riggae, Mohammed Bin Qasim St", 29.3004m, 47.9202m, "الفروانية", "Farwaniya", 91m),
        Brand("naeem-box", "NB18", "النعيم بوكس", "Naeem Box", "النعيم بوكس للوجبات المعلبة", "Naeem Box Meal Prep", "naeem@mealmate.kw", "+96550001042", "سارة الرشيد", 18, "النعيم، سوق الجهراء الغربي", "Naeem, West Jahra Souq", 29.3201m, 47.6408m, "الجهراء", "Jahra", 95m),
        Brand("shaab-kitchen", "SK19", "مطبخ الشعب", "Shaab Kitchen", "مطبخ الشعب الصحي ذ.م.م", "Shaab Healthy Kitchen W.L.L", "shaab@mealmate.kw", "+96550001043", "لطيفة العلي", 19, "الشعب، شارع الخليج", "Shaab, Gulf Road", 29.3482m, 48.0453m, "حولي", "Hawalli", 128m),
        Brand("salwa-greens", "SG20", "سلوى جرينز", "Salwa Greens", "سلوى جرينز للتغذية ذ.م.م", "Salwa Greens Nutrition W.L.L", "salwa@mealmate.kw", "+96550001044", "جاسم الكندري", 20, "سلوى، القطعة 7", "Salwa, Block 7", 29.2813m, 48.0842m, "حولي", "Hawalli", 134m),
        Brand("jleeb-fuel", "JF21", "الجليب فيول", "Jleeb Fuel", "الجليب فيول للرياضيين ذ.م.م", "Jleeb Fuel Athletes W.L.L", "jleeb@mealmate.kw", "+96550001045", "عمر الدوسري", 21, "الجليب، شارع المطار", "Jleeb Al-Shuyoukh, Airport Road", 29.2704m, 47.8206m, "الفروانية", "Farwaniya", 140m),
        Brand("fintas-plate", "FT22", "الفنطاس بليت", "Fintas Plate", "الفنطاس بليت للوجبات ذ.م.م", "Fintas Plate Meals W.L.L", "fintas@mealmate.kw", "+96550001046", "إيمان المطيري", 22, "الفنطاس، شارع الساحل", "Fintas, Coastal Road", 29.1732m, 48.1214m, "الأحمدي", "Ahmadi", 146m),
        Brand("qurain-herb", "QH23", "القرين هيرب", "Qurain Herb", "القرين هيرب للمواد الغذائية", "Qurain Herb Food Establishment", "qurain@mealmate.kw", "+96550001047", "راشد العنزي", 23, "القرين، القطعة 3", "Qurain, Block 3", 29.2034m, 48.0742m, "مبارك الكبير", "Mubarak Al-Kabeer", 150m),
        Brand("sabah-salem", "SS24", "صباح السالم كيتشن", "Sabah Salem Kitchen", "صباح السالم للمطابخ الصحية ذ.م.م", "Sabah Al Salem Healthy Kitchens W.L.L", "sabahsalem@mealmate.kw", "+96550001048", "غادة الشمري", 24, "صباح السالم، الدائري السادس", "Sabah Al Salem, 6th Ring Road", 29.2571m, 48.0574m, "مبارك الكبير", "Mubarak Al-Kabeer", 136m),
        Brand("abu-halifa", "AH25", "أبو حليفة جريل", "Abu Halifa Grill", "أبو حليفة للمشاوي الصحية ذ.م.م", "Abu Halifa Healthy Grill W.L.L", "abuhalifa@mealmate.kw", "+96550001049", "مشعل العتيبي", 25, "أبو حليفة، شارع الساحل", "Abu Halifa, Coastal Road", 29.1264m, 48.1263m, "الأحمدي", "Ahmadi", 142m),
        Brand("qasr-kitchen", "QS26", "مطبخ القصر", "Qasr Kitchen", "مطبخ القصر للوجبات ذ.م.م", "Qasr Kitchen Meals W.L.L", "qasr@mealmate.kw", "+96550001050", "هيا الجهري", 26, "القصر، الجهراء", "Al Qasr, Jahra", 29.3502m, 47.6804m, "الجهراء", "Jahra", 152m),
        Brand("sharq-atelier", "SA27", "شرق أتيلبيه", "Sharq Atelier", "شرق أتيلبيه للوجبات الفاخرة ذ.م.م", "Sharq Atelier Premium W.L.L", "sharq@mealmate.kw", "+96550001051", "طلال الخالد", 27, "الشرق، شارع أحمد الجابر", "Sharq, Ahmad Al Jaber St", 29.3858m, 47.9921m, "العاصمة", "Capital", 176m),
        Brand("bneid-prestige", "BP28", "بنيد برستيج", "Bneid Prestige", "بنيد برستيج للضيافة ذ.م.م", "Bneid Prestige Hospitality W.L.L", "bneid@mealmate.kw", "+96550001052", "دلال العلي", 28, "بنيد القار، شارع الخليج", "Bneid Al Gar, Gulf Road", 29.3702m, 48.0054m, "العاصمة", "Capital", 184m),
        Brand("mahboula-gold", "MB29", "المهبولة جولد", "Mahboula Gold", "المهبولة جولد للوجبات الفاخرة", "Mahboula Gold Premium Meals", "mahboula@mealmate.kw", "+96550001053", "فيصل الدوسري", 29, "المهبولة، الكورنيش", "Mahboula, Corniche", 29.1452m, 48.1304m, "الأحمدي", "Ahmadi", 192m),
        Brand("ahmadi-prime", "AP30", "الأحمدي برايم", "Ahmadi Prime", "الأحمدي برايم للمطابخ ذ.م.م", "Ahmadi Prime Kitchens W.L.L", "ahmadi@mealmate.kw", "+96550001054", "لولوة الرشيد", 30, "الأحمدي، المركز", "Ahmadi, City Center", 29.0768m, 48.0839m, "الأحمدي", "Ahmadi", 200m),
        Brand("marina-elite", "ME31", "مارينا إيليت", "Marina Elite", "مارينا إيليت للوجبات الفاخرة ذ.م.م", "Marina Elite Premium W.L.L", "marina@mealmate.kw", "+96550001055", "عبدالعزيز الصباح", 31, "السالمية، مارينا كريسنت", "Salmiya, Marina Crescent", 29.3391m, 48.0712m, "حولي", "Hawalli", 206m),
        Brand("shuwaikh-gourmet", "SW32", "الشويخ جورميه", "Shuwaikh Gourmet", "الشويخ جورميه للأغذية ذ.م.م", "Shuwaikh Gourmet Foods W.L.L", "shuwaikh@mealmate.kw", "+96550001056", "ريم الكندري", 32, "الشويخ الصناعية، القطعة 1", "Shuwaikh Industrial, Block 1", 29.3354m, 47.9321m, "العاصمة", "Capital", 178m),
        Brand("fahaheel-royale", "FR33", "الفحيحيل رويال", "Fahaheel Royale", "الفحيحيل رويال للضيافة ذ.م.م", "Fahaheel Royale Hospitality W.L.L", "fahaheel@mealmate.kw", "+96550001057", "ناصر المطيري", 33, "الفحيحيل، الكورنيش", "Fahaheel, Corniche", 29.0782m, 48.1413m, "الأحمدي", "Ahmadi", 208m),
        Brand("jahra-palace", "JP34", "قصر الجهراء", "Jahra Palace", "قصر الجهراء للوجبات الفاخرة ذ.م.م", "Jahra Palace Premium W.L.L", "jahrapalace@mealmate.kw", "+96550001058", "موضي العتيبي", 34, "الجهراء، الدائري الرابع", "Jahra, 4th Ring Road", 29.3412m, 47.6744m, "الجهراء", "Jahra", 210m),
    ];

    private static DemoRestaurantBrand Brand(
        string slug,
        string code,
        string nameAr,
        string nameEn,
        string legalAr,
        string legalEn,
        string email,
        string phone,
        string ownerName,
        int sequence,
        string addressAr,
        string addressEn,
        decimal latitude,
        decimal longitude,
        string governorateAr,
        string governorateEn,
        decimal price26)
        => new(
            slug,
            code,
            nameAr,
            nameEn,
            legalAr,
            legalEn,
            email,
            phone,
            ownerName,
            $"KW-CR-{100000 + sequence}",
            $"LIC-{100000 + sequence}",
            $"28510123{4566 + sequence:D4}",
            $"KW89NBKK0000000123456789{sequence:D4}",
            $"https://{slug}.kw",
            $"@{slug}.kw",
            addressAr,
            addressEn,
            latitude,
            longitude,
            governorateAr,
            governorateEn,
            price26);

    public static string LogoUrl(string slug) => $"{Media}logo-{slug}.png";

    public static string PhotoUrl(string slug) => $"{Media}photo-{slug}.jpg";
}

public sealed record DemoRestaurantBrand(
    string Slug,
    string Code,
    string NameAr,
    string NameEn,
    string LegalAr,
    string LegalEn,
    string Email,
    string Phone,
    string OwnerName,
    string Cr,
    string License,
    string CivilId,
    string Iban,
    string Website,
    string Instagram,
    string AddressAr,
    string AddressEn,
    decimal Latitude,
    decimal Longitude,
    string GovernorateAr,
    string GovernorateEn,
    decimal Price26)
{
    public string LogoUrl => DemoRestaurantBrandCatalog.LogoUrl(Slug);

    public string PhotoUrl => DemoRestaurantBrandCatalog.PhotoUrl(Slug);
}
