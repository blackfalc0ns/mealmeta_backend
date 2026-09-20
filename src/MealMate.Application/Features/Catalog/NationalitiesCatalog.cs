using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Catalog;

public sealed record NationalityDto(
    string Code,
    string NameAr,
    string NameEn,
    string CountryNameAr,
    string CountryNameEn,
    string FlagEmoji);

public sealed record GetNationalitiesQuery : IQuery<IReadOnlyList<NationalityDto>>;

public sealed class GetNationalitiesQueryHandler : IQueryHandler<GetNationalitiesQuery, IReadOnlyList<NationalityDto>>
{
    private static readonly IReadOnlyList<NationalityDto> Catalog = new List<NationalityDto>
    {
        // GCC
        new("KW", "كويتي", "Kuwaiti", "الكويت", "Kuwait", "🇰🇼"),
        new("SA", "سعودي", "Saudi", "المملكة العربية السعودية", "Saudi Arabia", "🇸🇦"),
        new("AE", "إماراتي", "Emirati", "الإمارات العربية المتحدة", "United Arab Emirates", "🇦🇪"),
        new("QA", "قطري", "Qatari", "قطر", "Qatar", "🇶🇦"),
        new("BH", "بحريني", "Bahraini", "البحرين", "Bahrain", "🇧🇭"),
        new("OM", "عماني", "Omani", "سلطنة عمان", "Oman", "🇴🇲"),

        // Arab Countries
        new("EG", "مصري", "Egyptian", "مصر", "Egypt", "🇪🇬"),
        new("JO", "أردني", "Jordanian", "الأردن", "Jordan", "🇯🇴"),
        new("LB", "لبناني", "Lebanese", "لبنان", "Lebanon", "🇱🇧"),
        new("SY", "سوري", "Syrian", "سوريا", "Syria", "🇸🇾"),
        new("IQ", "عراقي", "Iraqi", "العراق", "Iraq", "🇮🇶"),
        new("PS", "فلسطيني", "Palestinian", "فلسطين", "Palestine", "🇵🇸"),
        new("YE", "يمني", "Yemeni", "اليمن", "Yemen", "🇾🇪"),
        new("SD", "سوداني", "Sudanese", "السودان", "Sudan", "🇸🇩"),
        new("MA", "مغربي", "Moroccan", "المغرب", "Morocco", "🇲🇦"),
        new("DZ", "جزائري", "Algerian", "الجزائر", "Algeria", "🇩🇿"),
        new("TN", "تونسي", "Tunisian", "تونس", "Tunisia", "🇹🇳"),
        new("LY", "ليبي", "Libyan", "ليبيا", "Libya", "🇱🇾"),

        // Major Asian & Expatriate workforce in GCC
        new("IN", "هندي", "Indian", "الهند", "India", "🇮🇳"),
        new("PK", "باكستاني", "Pakistani", "باكستان", "Pakistan", "🇵🇰"),
        new("BD", "بنغلاديشي", "Bangladeshi", "بنغلاديش", "Bangladesh", "🇧🇩"),
        new("PH", "فلبيني", "Filipino", "الفلبين", "Philippines", "🇵🇭"),
        new("NP", "نيبالي", "Nepalese", "نيبال", "Nepal", "🇳🇵"),
        new("LK", "سريلانكي", "Sri Lankan", "سريلانكا", "Sri Lanka", "🇱🇰"),
        new("ID", "إندونيسي", "Indonesian", "إندونيسيا", "Indonesia", "🇮🇩"),
        new("AF", "أفغاني", "Afghan", "أفغانستان", "Afghanistan", "🇦🇫"),
        new("IR", "إيراني", "Iranian", "إيران", "Iran", "🇮🇷"),
        new("TR", "تركي", "Turkish", "تركيا", "Turkey", "🇹🇷"),

        // African & International
        new("ET", "إثيوبي", "Ethiopian", "إثيوبيا", "Ethiopia", "🇪🇹"),
        new("KE", "كيني", "Kenyan", "كينيا", "Kenya", "🇰🇪"),
        new("UG", "أوغندي", "Ugandan", "أوغندا", "Uganda", "🇺🇬"),
        new("GH", "غاني", "Ghanaian", "غانا", "Ghana", "🇬🇭"),
        new("NG", "نيجيري", "Nigerian", "نيجيريا", "Nigeria", "🇳🇬"),
        new("GB", "بريطاني", "British", "المملكة المتحدة", "United Kingdom", "🇬🇧"),
        new("US", "أمريكي", "American", "الولايات المتحدة", "United States", "🇺🇸"),
        new("CA", "كندي", "Canadian", "كندا", "Canada", "🇨🇦"),
        new("FR", "فرنسي", "French", "فرنسا", "France", "🇫🇷"),
        new("DE", "ألماني", "German", "ألمانيا", "Germany", "🇩🇪"),
        new("IT", "إيطالي", "Italian", "إيطاليا", "Italy", "🇮🇹"),
        new("ES", "إسباني", "Spanish", "إسبانيا", "Spain", "🇪🇸"),
        new("RU", "روسي", "Russian", "روسيا", "Russia", "🇷🇺"),
        new("CN", "صيني", "Chinese", "الصين", "China", "🇨🇳"),
    };

    public Task<Result<IReadOnlyList<NationalityDto>>> Handle(GetNationalitiesQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(Catalog));
    }
}
