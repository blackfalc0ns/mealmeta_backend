using System.Text.Json;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.DocumentTemplates;

/// <summary>
/// Production-ready layout pack: two complete designs per kind.
/// Privacy: short address only on invoice; no phone; labels have no customer identity.
/// Meal labels include full macros + allergens.
/// </summary>
internal static class DocumentTemplateProfessionalLayouts
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record PackSpec(
        string Code,
        DocumentTemplateKind Kind,
        bool Publish,
        string NameAr,
        string NameEn,
        string DescriptionAr,
        string DescriptionEn,
        int WidthPx,
        int HeightPx,
        int MarginPx,
        string PageSizeLabel,
        string AccentColor,
        string LayoutJson);

    /// <summary>Stable English names used for upsert / replace.</summary>
    public static IReadOnlyList<string> PackNameEns { get; } =
    [
        "Ops Invoice Pro",
        "Kitchen Invoice Fast",
        "Box Label Pro",
        "Box Label Compact",
        "Meal Label Pro",
        "Meal Label Compact",
    ];

    public static IReadOnlyList<PackSpec> AllPacks() =>
    [
        InvoicePro(),
        InvoiceKitchenFast(),
        BoxLabelPro(),
        BoxLabelCompact(),
        MealLabelPro(),
        MealLabelCompact(),
    ];

    public static DocumentTemplate ToEntity(PackSpec pack, DateTime createdAtUtc) => new()
    {
        Kind = pack.Kind,
        NameAr = pack.NameAr,
        NameEn = pack.NameEn,
        DescriptionAr = pack.DescriptionAr,
        DescriptionEn = pack.DescriptionEn,
        Language = "both",
        Status = DocumentTemplateStatus.Draft,
        PageWidthPx = pack.WidthPx,
        PageHeightPx = pack.HeightPx,
        MarginPx = pack.MarginPx,
        PageSizeLabel = pack.PageSizeLabel,
        BackgroundColor = "#FFFFFF",
        AccentColor = pack.AccentColor,
        ShowAppLogo = true,
        ShowRestaurantLogo = true,
        LayoutJson = pack.LayoutJson,
        Version = 1,
        CreatedAtUtc = createdAtUtc,
    };

    // ── Invoice A4 Pro (published) ──────────────────────────────────────────
    private static PackSpec InvoicePro() => new(
        Code: "ops-invoice-pro",
        Kind: DocumentTemplateKind.Invoice,
        Publish: true,
        NameAr: "فاتورة تشغيل احترافية",
        NameEn: "Ops Invoice Pro",
        DescriptionAr: "A4 كامل: شعارين، باركود البوكس، جدول وجبات مع ماكروز، حساسية مجمّعة، منطقة توصيل مختصرة بدون هاتف.",
        DescriptionEn: "Full A4: both logos, box barcode, meals table with macros, allergy summary, short delivery area, no phone.",
        WidthPx: 794,
        HeightPx: 1123,
        MarginPx: 32,
        PageSizeLabel: "A4",
        AccentColor: "#0B6E4F",
        LayoutJson: Serialize(
            El("header-bar", "bilingual-text", "static", "شريط", "Bar", "", "", 32, 24, 730, 8, 0, "#0B6E4F", "#0B6E4F", 8, 700, "center", "#0B6E4F"),
            El("mm-logo", "logo", "static", "شعار MealMate", "MealMate logo", "mealmate_logo", "mealmate_logo", 32, 44, 100, 58, 1, "#ecfdf5", "#0B6E4F", 11, 700, "center"),
            El("rest-logo", "logo", "restaurant", "شعار المطعم", "Restaurant logo", "restaurant_logo", "restaurant_logo", 144, 44, 100, 58, 2, "#f0fdf4", "#166534", 11, 700, "center"),
            El("title", "bilingual-text", "static", "العنوان", "Title",
                "فاتورة تحضير البوكس", "Box Preparation Invoice",
                260, 48, 300, 52, 3, "transparent", "#0f172a", 22, 800, "center"),
            El("barcode", "barcode", "barcode", "باركود البوكس", "Box barcode", "", "", 580, 40, 182, 70, 4),
            El("order", "text", "order-id", "رقم الطلب", "Order ID", "رقم الطلب", "Order ID", 32, 120, 236, 50, 5, "#f8fafc", "#0f172a", 13),
            El("restaurant", "text", "restaurant", "المطعم", "Restaurant", "المطعم", "Restaurant", 280, 120, 236, 50, 6, "#f8fafc", "#0f172a", 13),
            El("date", "text", "delivery-date", "تاريخ التوصيل", "Delivery date", "التوصيل", "Delivery", 528, 120, 234, 50, 7, "#f8fafc", "#0f172a", 13),
            El("meals", "table", "meal-details", "جدول الوجبات والماكروز", "Meals & macros", "", "", 32, 188, 730, 460, 8),
            El("allergies", "allergy-notes", "allergies", "الحساسية", "Allergies",
                "ملاحظات الحساسية لكل الوجبات", "Allergy notes for all meals",
                32, 668, 360, 120, 9, "#fff7ed", "#9a3412", 13, 700, "start", "#fed7aa"),
            El("address", "customer-address", "customer-address", "منطقة التوصيل", "Delivery area", "", "",
                408, 668, 354, 120, 10, "#f8fafc", "#334155", 13),
            El("footer", "bilingual-text", "static", "ملاحظة تشغيلية", "Ops note",
                "استخدام تشغيلي فقط · بدون مبالغ · عنوان مختصر · بدون هاتف",
                "Operational use only · no amounts · short address · no phone",
                32, 812, 730, 52, 11, "#f1f5f9", "#475569", 12, 600, "center")));

    // ── Invoice Kitchen Fast (draft) ────────────────────────────────────────
    private static PackSpec InvoiceKitchenFast() => new(
        Code: "kitchen-invoice-fast",
        Kind: DocumentTemplateKind.Invoice,
        Publish: false,
        NameAr: "فاتورة مطبخ سريعة",
        NameEn: "Kitchen Invoice Fast",
        DescriptionAr: "نسخة مطبخ مضغوطة: باركود، جدول وجبات مع ماكروز، حساسية، منطقة توصيل.",
        DescriptionEn: "Dense kitchen print: barcode, meals+macros table, allergies, delivery area.",
        WidthPx: 794,
        HeightPx: 980,
        MarginPx: 24,
        PageSizeLabel: "A4",
        AccentColor: "#0369A1",
        LayoutJson: Serialize(
            El("mm-logo", "logo", "static", "MealMate", "MealMate", "mealmate_logo", "mealmate_logo", 24, 20, 88, 48, 1, "#e0f2fe", "#0369A1", 10, 700, "center"),
            El("rest-logo", "logo", "restaurant", "المطعم", "Restaurant", "restaurant_logo", "restaurant_logo", 124, 20, 88, 48, 2, "#e0f2fe", "#0369A1", 10, 700, "center"),
            El("title", "bilingual-text", "static", "العنوان", "Title", "فاتورة مطبخ", "Kitchen Invoice", 228, 24, 300, 44, 3, "transparent", "#0c4a6e", 20, 800, "center"),
            El("barcode", "barcode", "barcode", "باركود", "Barcode", "", "", 548, 16, 222, 64, 4),
            El("order", "text", "order-id", "الطلب", "Order", "", "", 24, 88, 240, 46, 5, "#f8fafc"),
            El("restaurant", "text", "restaurant", "المطعم", "Restaurant", "", "", 276, 88, 240, 46, 6, "#f8fafc"),
            El("date", "text", "delivery-date", "التاريخ", "Date", "", "", 528, 88, 242, 46, 7, "#f8fafc"),
            El("meals", "table", "meal-details", "الوجبات", "Meals", "", "", 24, 150, 746, 420, 8),
            El("allergies", "allergy-notes", "allergies", "حساسية", "Allergies", "", "", 24, 590, 360, 90, 9, "#fff7ed", "#9a3412", 12, 700, "start", "#fed7aa"),
            El("address", "customer-address", "customer-address", "المنطقة", "Area", "", "", 400, 590, 370, 90, 10, "#f8fafc", "#334155", 12),
            El("footer", "bilingual-text", "static", "ملاحظة", "Note",
                "بدون مبالغ · بدون هاتف", "No amounts · no phone",
                24, 700, 746, 40, 11, "#f1f5f9", "#64748b", 11, 600, "center")));

    // ── Box Label Pro (published) ───────────────────────────────────────────
    private static PackSpec BoxLabelPro() => new(
        Code: "box-label-pro",
        Kind: DocumentTemplateKind.BoxLabel,
        Publish: true,
        NameAr: "ملصق بوكس احترافي",
        NameEn: "Box Label Pro",
        DescriptionAr: "ملصق بوكس أفقي: شعارين، باركود المسح، ملخص الوجبات، حساسية مجمّعة. بدون هوية عميل.",
        DescriptionEn: "Landscape box sticker: logos, scan barcode, meal summary, allergy rollup. No customer identity.",
        WidthPx: 500,
        HeightPx: 340,
        MarginPx: 14,
        PageSizeLabel: "100x70mm",
        AccentColor: "#0B6E4F",
        LayoutJson: Serialize(
            El("mm-logo", "logo", "static", "MealMate", "MealMate", "mealmate_logo", "mealmate_logo", 14, 12, 72, 42, 1, "#ecfdf5", "#0B6E4F", 10, 700, "center"),
            El("rest-logo", "logo", "restaurant", "المطعم", "Restaurant", "restaurant_logo", "restaurant_logo", 96, 12, 72, 42, 2, "#f0fdf4", "#166534", 10, 700, "center"),
            El("title", "bilingual-text", "static", "عنوان", "Title", "ملصق البوكس", "BOX LABEL", 184, 14, 170, 40, 3, "transparent", "#0f172a", 15, 800, "center"),
            El("barcode", "barcode", "barcode", "باركود", "Barcode", "", "", 368, 10, 118, 52, 4),
            El("restaurant", "text", "restaurant", "المطعم", "Restaurant", "", "", 14, 66, 230, 38, 5, "#f8fafc", "#0f172a", 12),
            El("date", "text", "delivery-date", "التاريخ", "Date", "", "", 256, 66, 230, 38, 6, "#f8fafc", "#0f172a", 12),
            El("meals", "meal-details", "meal-details", "الوجبات", "Meals", "", "", 14, 116, 472, 130, 7, "#ffffff", "#334155", 12),
            El("allergies", "allergy-notes", "allergies", "الحساسية", "Allergies", "", "", 14, 258, 472, 66, 8, "#fff7ed", "#9a3412", 11, 700, "start", "#fed7aa")));

    // ── Box Label Compact (draft) ───────────────────────────────────────────
    private static PackSpec BoxLabelCompact() => new(
        Code: "box-label-compact",
        Kind: DocumentTemplateKind.BoxLabel,
        Publish: false,
        NameAr: "ملصق بوكس مضغوط",
        NameEn: "Box Label Compact",
        DescriptionAr: "ملصق أصغر للطباعة الكثيفة مع باركود وملخص وجبات وحساسية.",
        DescriptionEn: "Smaller dense print sticker with barcode, meal summary, and allergies.",
        WidthPx: 420,
        HeightPx: 300,
        MarginPx: 12,
        PageSizeLabel: "90x60mm",
        AccentColor: "#115E59",
        LayoutJson: Serialize(
            El("mm-logo", "logo", "static", "MealMate", "MealMate", "mealmate_logo", "mealmate_logo", 12, 10, 64, 36, 1, "#f0fdfa", "#115E59", 10, 700, "center"),
            El("rest-logo", "logo", "restaurant", "المطعم", "Restaurant", "restaurant_logo", "restaurant_logo", 86, 10, 64, 36, 2, "#f0fdfa", "#0f766e", 10, 700, "center"),
            El("barcode", "barcode", "barcode", "باركود", "Barcode", "", "", 270, 8, 138, 48, 3),
            El("title", "bilingual-text", "static", "عنوان", "Title", "بوكس", "BOX", 12, 54, 396, 28, 4, "transparent", "#134e4a", 14, 800, "center"),
            El("meals", "meal-details", "meal-details", "الوجبات", "Meals", "", "", 12, 90, 396, 120, 5),
            El("allergies", "allergy-notes", "allergies", "حساسية", "Allergies", "", "", 12, 222, 396, 64, 6, "#fef2f2", "#991b1b", 11, 700, "start", "#fecaca")));

    // ── Meal Label Pro (published) — full macros + allergies ────────────────
    private static PackSpec MealLabelPro() => new(
        Code: "meal-label-pro",
        Kind: DocumentTemplateKind.MealLabel,
        Publish: true,
        NameAr: "ملصق وجبة احترافي",
        NameEn: "Meal Label Pro",
        DescriptionAr: "ملصق وجبة كامل: شعارين، اسم الوجبة، ماكروز (سعرات/بروتين/كارب/دهون)، مكونات، حساسية. بدون هوية عميل.",
        DescriptionEn: "Full meal sticker: logos, meal name, macros (kcal/protein/carbs/fat), ingredients, allergens. No customer identity.",
        WidthPx: 380,
        HeightPx: 560,
        MarginPx: 14,
        PageSizeLabel: "80x120mm",
        AccentColor: "#0B6E4F",
        LayoutJson: Serialize(
            El("mm-logo", "logo", "static", "MealMate", "MealMate", "mealmate_logo", "mealmate_logo", 14, 12, 72, 42, 1, "#ecfdf5", "#0B6E4F", 10, 700, "center"),
            El("rest-logo", "logo", "restaurant", "المطعم", "Restaurant", "restaurant_logo", "restaurant_logo", 96, 12, 72, 42, 2, "#f0fdf4", "#166534", 10, 700, "center"),
            El("slot", "text", "static", "الفئة", "Slot", "وجبة", "MEAL", 184, 16, 182, 36, 3, "#f8fafc", "#64748b", 11, 700, "center"),
            El("name", "bilingual-text", "meal-name", "اسم الوجبة", "Meal name", "", "", 14, 66, 352, 60, 4, "transparent", "#0f172a", 18, 800, "center"),
            El("nutrition", "nutrition-block", "nutrition", "الماكروز", "Macros", "", "", 14, 140, 352, 96, 5, "#f0fdf4", "#0f172a", 12, 700, "center", "#bbf7d0"),
            El("details", "meal-details", "meal-details", "المكونات", "Ingredients", "", "", 14, 252, 352, 130, 6, "#ffffff", "#334155", 12),
            El("allergies", "allergy-notes", "allergies", "الحساسية", "Allergies", "", "", 14, 396, 352, 100, 7, "#fff7ed", "#9a3412", 12, 700, "start", "#fed7aa"),
            El("footer", "text", "restaurant", "المطعم", "Restaurant", "", "", 14, 508, 352, 36, 8, "#f1f5f9", "#64748b", 11, 600, "center")));

    // ── Meal Label Compact (draft) ──────────────────────────────────────────
    private static PackSpec MealLabelCompact() => new(
        Code: "meal-label-compact",
        Kind: DocumentTemplateKind.MealLabel,
        Publish: false,
        NameAr: "ملصق وجبة مضغوط",
        NameEn: "Meal Label Compact",
        DescriptionAr: "ملصق صغير سريع: اسم، ماكروز كاملة، حساسية.",
        DescriptionEn: "Small fast sticker: name, full macros, allergens.",
        WidthPx: 320,
        HeightPx: 420,
        MarginPx: 12,
        PageSizeLabel: "70x90mm",
        AccentColor: "#B45309",
        LayoutJson: Serialize(
            El("mm-logo", "logo", "static", "MealMate", "MealMate", "mealmate_logo", "mealmate_logo", 12, 10, 58, 34, 1, "#fffbeb", "#B45309", 10, 700, "center"),
            El("rest-logo", "logo", "restaurant", "المطعم", "Restaurant", "restaurant_logo", "restaurant_logo", 80, 10, 58, 34, 2, "#fffbeb", "#92400e", 10, 700, "center"),
            El("name", "bilingual-text", "meal-name", "الوجبة", "Meal", "", "", 12, 54, 296, 52, 3, "transparent", "#78350f", 16, 800, "center"),
            El("nutrition", "nutrition-block", "nutrition", "الماكروز", "Macros", "", "", 12, 118, 296, 84, 4, "#fffbeb", "#0f172a", 11, 700, "center", "#fde68a"),
            El("details", "meal-details", "meal-details", "المكونات", "Ingredients", "", "", 12, 214, 296, 90, 5),
            El("allergies", "allergy-notes", "allergies", "حساسية", "Allergies", "", "", 12, 316, 296, 88, 6, "#fef2f2", "#991b1b", 11, 700, "start", "#fecaca")));

    private static object El(
        string id,
        string kind,
        string source,
        string labelAr,
        string labelEn,
        string textAr,
        string textEn,
        int x,
        int y,
        int width,
        int height,
        int zIndex,
        string backgroundColor = "transparent",
        string color = "#0f172a",
        int fontSize = 13,
        int fontWeight = 700,
        string align = "start",
        string borderColor = "#e2e8f0")
        => new
        {
            id,
            kind,
            source,
            labelAr,
            labelEn,
            textAr,
            textEn,
            x,
            y,
            width,
            height,
            visible = true,
            locked = false,
            zIndex,
            style = new
            {
                fontSize,
                fontWeight,
                color,
                backgroundColor,
                borderColor,
                radius = 8,
                padding = 8,
                align,
            },
        };

    private static string Serialize(params object[] elements)
        => JsonSerializer.Serialize(elements, JsonOptions);
}
