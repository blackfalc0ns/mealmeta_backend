using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Catalog;

public sealed record NationalityDto(
    string Code,
    string Name,
    string NameAr,
    string NameEn,
    string CountryName,
    string CountryNameAr,
    string CountryNameEn,
    string FlagEmoji);

public sealed record GetNationalitiesQuery : IQuery<IReadOnlyList<NationalityDto>>;

public sealed class GetNationalitiesQueryHandler : IQueryHandler<GetNationalitiesQuery, IReadOnlyList<NationalityDto>>
{
    private readonly ILocalizedMessageProvider _messages;

    public GetNationalitiesQueryHandler(ILocalizedMessageProvider messages)
    {
        _messages = messages;
    }

    private static readonly IReadOnlyList<(string Code, string NameAr, string NameEn, string CountryNameAr, string CountryNameEn, string FlagEmoji)> RawCatalog = new List<(string, string, string, string, string, string)>
    {
        // GCC
        ("KW", "كويتي", "Kuwaiti", "الكويت", "Kuwait", "🇰🇼"),
        ("SA", "سعودي", "Saudi", "المملكة العربية السعودية", "Saudi Arabia", "🇸🇦"),
        ("AE", "إماراتي", "Emirati", "الإمارات العربية المتحدة", "United Arab Emirates", "🇦🇪"),
        ("QA", "قطري", "Qatari", "قطر", "Qatar", "🇶🇦"),
        ("BH", "بحريني", "Bahraini", "البحرين", "Bahrain", "🇧🇭"),
        ("OM", "عماني", "Omani", "سلطنة عمان", "Oman", "🇴🇲"),

        // Arab Countries
        ("EG", "مصري", "Egyptian", "مصر", "Egypt", "🇪🇬"),
        ("JO", "أردني", "Jordanian", "الأردن", "Jordan", "🇯🇴"),
        ("LB", "لبناني", "Lebanese", "لبنان", "Lebanon", "🇱🇧"),
        ("SY", "سوري", "Syrian", "سوريا", "Syria", "🇸🇾"),
        ("IQ", "عراقي", "Iraqi", "العراق", "Iraq", "🇮🇶"),
        ("PS", "فلسطيني", "Palestinian", "فلسطين", "Palestine", "🇵🇸"),
        ("YE", "يمني", "Yemeni", "اليمن", "Yemen", "🇾🇪"),
        ("SD", "سوداني", "Sudanese", "السودان", "Sudan", "🇸🇩"),
        ("MA", "مغربي", "Moroccan", "المغرب", "Morocco", "🇲🇦"),
        ("DZ", "جزائري", "Algerian", "الجزائر", "Algeria", "🇩🇿"),
        ("TN", "تونسي", "Tunisian", "تونس", "Tunisia", "🇹🇳"),
        ("LY", "ليبي", "Libyan", "ليبيا", "Libya", "🇱🇾"),

        // Major Asian & Expatriate workforce in GCC
        ("IN", "هندي", "Indian", "الهند", "India", "🇮🇳"),
        ("PK", "باكستاني", "Pakistani", "باكستان", "Pakistan", "🇵🇰"),
        ("BD", "بنغلاديشي", "Bangladeshi", "بنغلاديش", "Bangladesh", "🇧🇩"),
        ("PH", "فلبيني", "Filipino", "الفلبين", "Philippines", "🇵🇭"),
        ("NP", "نيبالي", "Nepalese", "نيبال", "Nepal", "🇳🇵"),
        ("LK", "سريلانكي", "Sri Lankan", "سريلانكا", "Sri Lanka", "🇱🇰"),
        ("ID", "إندونيسي", "Indonesian", "إندونيسيا", "Indonesia", "🇮🇩"),
        ("AF", "أفغاني", "Afghan", "أفغانستان", "Afghanistan", "🇦🇫"),
        ("IR", "إيراني", "Iranian", "إيران", "Iran", "🇮🇷"),
        ("TR", "تركي", "Turkish", "تركيا", "Turkey", "🇹🇷"),

        // African & International
        ("ET", "إثيوبي", "Ethiopian", "إثيوبيا", "Ethiopia", "🇪🇹"),
        ("KE", "كيني", "Kenyan", "كينيا", "Kenya", "🇰🇪"),
        ("UG", "أوغندي", "Ugandan", "أوغندا", "Uganda", "🇺🇬"),
        ("GH", "غاني", "Ghanaian", "غانا", "Ghana", "🇬🇭"),
        ("NG", "نيجيري", "Nigerian", "نيجيريا", "Nigeria", "🇳🇬"),
        ("GB", "بريطاني", "British", "المملكة المتحدة", "United Kingdom", "🇬🇧"),
        ("US", "أمريكي", "American", "الولايات المتحدة", "United States", "🇺🇸"),
        ("CA", "كندي", "Canadian", "كندا", "Canada", "🇨🇦"),
        ("FR", "فرنسي", "French", "فرنسا", "France", "🇫🇷"),
        ("DE", "ألماني", "German", "ألمانيا", "Germany", "🇩🇪"),
        ("IT", "إيطالي", "Italian", "إيطاليا", "Italy", "🇮🇹"),
        ("ES", "إسباني", "Spanish", "إسبانيا", "Spain", "🇪🇸"),
        ("RU", "روسي", "Russian", "روسيا", "Russia", "🇷🇺"),
        ("CN", "صيني", "Chinese", "الصين", "China", "🇨🇳"),
    };

    public Task<Result<IReadOnlyList<NationalityDto>>> Handle(GetNationalitiesQuery request, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();
        IReadOnlyList<NationalityDto> list = RawCatalog.Select(x => new NationalityDto(
            x.Code,
            isAr ? x.NameAr : x.NameEn,
            x.NameAr,
            x.NameEn,
            isAr ? x.CountryNameAr : x.CountryNameEn,
            x.CountryNameAr,
            x.CountryNameEn,
            x.FlagEmoji)).ToList();

        return Task.FromResult(Result.Success(list));
    }
}
