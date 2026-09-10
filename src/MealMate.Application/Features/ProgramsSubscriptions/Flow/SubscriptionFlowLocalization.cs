using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public static class SubscriptionFlowLocalization
{
    public static (string NameAr, string NameEn, string DescriptionAr, string DescriptionEn) IndividualType() =>
        ("اشتراك فردي", "Individual subscription", "خطة غذائية لشخص واحد", "A meal plan for one person");

    public static (string NameAr, string NameEn) FamilyType() =>
        ("اشتراك عائلي", "Family subscription");

    public static string FamilyDescriptionAr(bool hasIndividualSubscription, bool hasFamilyDraftOrSubscription)
    {
        if (hasIndividualSubscription)
            return "حوّل اشتراكك الفردي لعائلة وابقَ مديرًا";
        if (hasFamilyDraftOrSubscription)
            return "أكمل مسودة العائلة أو الاشتراك العائلي الحالي";
        return "خصص برنامجاً وباقة لكل فرد وادفع مرة واحدة";
    }

    public static string FamilyDescriptionEn(bool hasIndividualSubscription, bool hasFamilyDraftOrSubscription)
    {
        if (hasIndividualSubscription)
            return "Convert your individual subscription to family and remain manager";
        if (hasFamilyDraftOrSubscription)
            return "Complete your family draft or current family subscription";
        return "Customize a program and bundle for each member and pay once";
    }

    public static (string NameAr, string NameEn) MostSelectedBadge() =>
        ("الأكثر اختيارًا", "Most selected");

    public static (string NameAr, string NameEn) MostPopularBadge() =>
        ("الأكثر اختيارًا", "Most popular");

    public static (string NameAr, string NameEn) IncludeFridayOption() =>
        ("إضافة يوم الجمعة", "Include Friday");

    public static (string MessageAr, string MessageEn) FridayAvailability(bool available) =>
        available
            ? ("يوم الجمعة متاح ضمن هذه الباقة", "Friday delivery is available for this bundle")
            : ("يوم الجمعة غير متاح لعدم وجود مطاعم تعمل الجمعة ضمن هذه الباقة", "Friday delivery is unavailable because no restaurants operate on Fridays for this bundle");

    public static (string TitleAr, string TitleEn) IncludesTitle() =>
        ("يشمل اشتراكك", "Your subscription includes");

    public static (string TextAr, string TextEn) MealsPerDayInclude(int count)
    {
        var ar = count switch
        {
            1 => "وجبة واحدة يوميًا",
            2 => "وجبتان يوميًا",
            >= 3 and <= 10 => $"{count} وجبات يوميًا",
            _ => $"{count} وجبة يوميًا"
        };
        var en = count == 1 ? "1 meal per day" : $"{count} meals per day";
        return (ar, en);
    }

    public static (string TextAr, string TextEn) RestaurantsInTierInclude(int count)
    {
        var ar = count switch
        {
            1 => "مطعم واحد في هذا التصنيف",
            2 => "مطعمَان في هذا التصنيف",
            >= 3 and <= 10 => $"{count} مطاعم في هذا التصنيف",
            _ => $"{count} مطعمًا في هذا التصنيف"
        };
        var en = count == 1 ? "1 restaurant in this tier" : $"{count} restaurants in this tier";
        return (ar, en);
    }

    public static (string TextAr, string TextEn) ServiceDaysInclude(int count)
    {
        var ar = count switch
        {
            1 => "يوم خدمة واحد",
            2 => "يومان خدمة",
            >= 3 and <= 10 => $"{count} أيام خدمة",
            _ => $"{count} يوم خدمة"
        };
        var en = count == 1 ? "1 service day" : $"{count} service days";
        return (ar, en);
    }

    public static (string TextAr, string TextEn) ApprovedMealsInclude(int count)
    {
        var ar = count switch
        {
            1 => "وجبة معتمدة واحدة للاختيار",
            2 => "وجبتان معتمدتان للاختيار",
            >= 3 and <= 10 => $"{count} وجبات معتمدة للاختيار",
            _ => $"{count} وجبة معتمدة للاختيار"
        };
        var en = count == 1 ? "1 approved meal to choose from" : $"{count} approved meals to choose from";
        return (ar, en);
    }

    public static string DurationNameAr(int days) => days switch
    {
        6 => "6 أيام",
        12 => "12 يومًا",
        26 => "26 يومًا",
        _ => $"{days} يومًا"
    };

    public static string DurationNameEn(int days) => days == 1 ? "1 day" : $"{days} days";

    public static (string NameAr, string NameEn) SlotName(string slot) => slot.ToLowerInvariant() switch
    {
        "breakfast" => ("الإفطار", "Breakfast"),
        "lunch" => ("الغداء", "Lunch"),
        "dinner" => ("العشاء", "Dinner"),
        "snack" => ("سناك", "Snack"),
        _ => (slot, slot)
    };

    public static (string NameAr, string NameEn) IngredientCategory(string category) => category.ToLowerInvariant() switch
    {
        "oils" => ("الزيوت", "Oils"),
        "seafood" => ("الأسماك", "Fish"),
        "poultry" => ("الدواجن", "Poultry"),
        "meat" => ("اللحوم", "Meat"),
        "dairy" => ("ألبان وبيض", "Dairy"),
        "produce" => ("خضار وفواكه", "Produce"),
        "grains" => ("حبوب", "Grains"),
        _ => ("أخرى", "Other")
    };

    public static (string SourceAr, string SourceEn) IngredientSource(bool isLocal) =>
        isLocal ? ("طازجة محلية", "Local fresh") : ("مستوردة", "Imported");

    public static (string DescriptionAr, string DescriptionEn) TierMarketing(RestaurantTier tier) => tier switch
    {
        RestaurantTier.Basic => ("مطاعم مختارة بجودة مميزة", "Selected restaurants with quality meals"),
        RestaurantTier.Platinum => ("خيارات أوسع من المطاعم المميزة", "A wider choice of premium restaurants"),
        _ => ("أعلى تصنيف للمطاعم وتجربة مميزة", "Top-tier restaurants and an elevated experience")
    };
}
