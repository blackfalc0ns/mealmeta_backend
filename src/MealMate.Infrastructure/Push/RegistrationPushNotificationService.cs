using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class RegistrationPushNotificationService : IRegistrationPushNotificationService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly ILogger<RegistrationPushNotificationService> _logger;

    public RegistrationPushNotificationService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        ILogger<RegistrationPushNotificationService> logger)
    {
        _db = db;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task NotifyAdminsNewRegistrationAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var title = "طلب تسجيل مطعم جديد 🍽️";
        var body = $"قام مطعم \"{restaurantName}\" بتقديم طلب تسجيل جديد للمراجعة والتوثيق.";
        var deepLink = $"/admin/accounts/pending/{registrationId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.registration_submitted",
            ["registrationId"] = registrationId.ToString(),
            ["deepLink"] = deepLink,
        };

        // 1. Always record in-app notification in DB so admin inbox reflects it
        try
        {
            _db.AdminNotifications.Add(new MealMate.Domain.AdminDashboard.AdminNotification
            {
                TitleAr = title,
                TitleEn = "New Restaurant Registration Submitted",
                BodyAr = body,
                BodyEn = $"Restaurant \"{restaurantName}\" submitted a registration application for review.",
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(data),
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin in-app notification for registration {RegistrationId}.", registrationId);
        }

        // 2. Deliver real-time Web Push if FCM is configured
        if (!_fcm.IsConfigured)
        {
            _logger.LogWarning("Firebase FCM is not configured. Admin push notification for registration {RegistrationId} skipped.", registrationId);
            return;
        }

        var tokens = await _db.AdminDeviceTokens
            .Where(x => x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("No active admin device tokens found in DB. Web push notification for new registration {RegistrationId} ('{RestaurantName}') could not be delivered to any active browser session.", registrationId, restaurantName);
            return;
        }

        var sentCount = 0;
        foreach (var token in tokens)
        {
            try
            {
                var sendResult = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (sendResult.IsSuccess)
                {
                    sentCount++;
                }
                else
                {
                    _logger.LogWarning("Failed to deliver admin push for registration {RegistrationId}: {Error}", registrationId, sendResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send admin push for new registration {RegistrationId}.", registrationId);
            }
        }

        _logger.LogInformation("Admin push for registration {RegistrationId} sent to {SentCount}/{TotalCount} device(s).", registrationId, sentCount, tokens.Count);
    }

    public async Task NotifyAdminsNewRestaurantAccountCreatedAsync(
        Guid userId,
        string ownerName,
        string email,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var title = "حساب مطعم جديد 👤";
        var body = $"قام \"{ownerName}\" بإنشاء حساب مطعم جديد ({email}).";
        var deepLink = "/admin/accounts/pending";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.account_created",
            ["userId"] = userId.ToString(),
            ["deepLink"] = deepLink,
        };

        // 1. Always record in-app notification in DB so admin inbox reflects it
        try
        {
            _db.AdminNotifications.Add(new MealMate.Domain.AdminDashboard.AdminNotification
            {
                TitleAr = title,
                TitleEn = "New Restaurant Account Created",
                BodyAr = body,
                BodyEn = $"A new restaurant account was created by \"{ownerName}\" ({email}).",
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(data),
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin in-app notification for new restaurant account {UserId}.", userId);
        }

        // 2. Deliver real-time Web Push if FCM is configured
        if (!_fcm.IsConfigured)
        {
            _logger.LogWarning("Firebase FCM is not configured. Admin push notification for restaurant user {UserId} skipped.", userId);
            return;
        }

        var tokens = await _db.AdminDeviceTokens
            .Where(x => x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("No active admin device tokens found in DB. Push for new restaurant user {UserId} ('{OwnerName}') could not be delivered.", userId, ownerName);
            return;
        }

        var sentCount = 0;
        foreach (var token in tokens)
        {
            try
            {
                var sendResult = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (sendResult.IsSuccess)
                {
                    sentCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send admin push for new restaurant account {UserId}.", userId);
            }
        }

        _logger.LogInformation("Admin push for new restaurant account {UserId} sent to {SentCount}/{TotalCount} device(s).", userId, sentCount, tokens.Count);
    }

    public async Task NotifyAdminsRestaurantDraftCreatedAsync(
        Guid registrationId,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var title = "بدء تسجيل مطعم جديد 📝";
        var body = $"بدأ مطعم \"{restaurantName}\" إدخال بيانات التسجيل الأولية.";
        var deepLink = "/admin/accounts/pending";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.draft_created",
            ["registrationId"] = registrationId.ToString(),
            ["deepLink"] = deepLink,
        };

        // 1. Always record in-app notification in DB so admin inbox reflects it
        try
        {
            _db.AdminNotifications.Add(new MealMate.Domain.AdminDashboard.AdminNotification
            {
                TitleAr = title,
                TitleEn = "Restaurant Registration Started",
                BodyAr = body,
                BodyEn = $"Restaurant \"{restaurantName}\" started entering registration details.",
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(data),
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin in-app notification for registration draft {RegistrationId}.", registrationId);
        }

        // 2. Deliver real-time Web Push if FCM is configured
        if (!_fcm.IsConfigured)
        {
            _logger.LogWarning("Firebase FCM is not configured. Admin push notification for registration draft {RegistrationId} skipped.", registrationId);
            return;
        }

        var tokens = await _db.AdminDeviceTokens
            .Where(x => x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("No active admin device tokens found in DB. Push for registration draft {RegistrationId} ('{RestaurantName}') could not be delivered.", registrationId, restaurantName);
            return;
        }

        var sentCount = 0;
        foreach (var token in tokens)
        {
            try
            {
                var sendResult = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (sendResult.IsSuccess)
                {
                    sentCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send admin push for registration draft {RegistrationId}.", registrationId);
            }
        }

        _logger.LogInformation("Admin push for registration draft {RegistrationId} sent to {SentCount}/{TotalCount} device(s).", registrationId, sentCount, tokens.Count);
    }

    public async Task NotifyRestaurantFieldRejectedAsync(
        Guid ownerUserId,
        string fieldKey,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var fieldNameAr = TranslateFieldKey(fieldKey);
        var step = ResolveFieldStep(fieldKey);
        var deepLink = $"/restaurant/onboarding?step={step}&field={fieldKey}&correction=true";

        var title = "ملاحظات على توثيق الحساب ⚠️";
        var body = $"تم رفض حقل \"{fieldNameAr}\": {reason}. يرجى التعديل والمتابعة.";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "field.rejected",
            title,
            $"Field Rejected: {fieldKey}",
            body,
            $"The field \"{fieldKey}\" was rejected: {reason}. Please update and resubmit.",
            step,
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.field_rejected",
            ["fieldKey"] = fieldKey,
            ["reason"] = reason,
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant field rejection push to user {UserId}.", ownerUserId);
            }
        }
    }

    public async Task NotifyRestaurantDocumentRejectedAsync(
        Guid ownerUserId,
        DocumentType documentType,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var docNameAr = TranslateDocumentType(documentType);
        var deepLink = "/restaurant/onboarding?step=documents&correction=true";

        var title = "ملاحظات على المستند المرفوع ⚠️";
        var body = $"تم رفض مستند \"{docNameAr}\": {reason}. يرجى إعادة رفع المستند المطلوب.";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "document.rejected",
            title,
            $"Document Rejected: {documentType}",
            body,
            $"The document \"{documentType}\" was rejected: {reason}. Please upload the required document.",
            "documents",
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.document_rejected",
            ["documentType"] = documentType.ToString(),
            ["reason"] = reason,
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant document rejection push to user {UserId}.", ownerUserId);
            }
        }
    }

    public async Task NotifyRestaurantSectionApprovedAsync(
        Guid ownerUserId,
        string sectionTitleAr,
        string sectionKey,
        CancellationToken cancellationToken = default)
    {
        var deepLink = "/restaurant/onboarding?step=documents";
        var title = $"تم اعتماد {sectionTitleAr} بنجاح ✅";
        var body = $"تهانينا! تمت مراجعة واعتماد جميع مستندات {sectionTitleAr} من قبل الإدارة.";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "section.approved",
            title,
            $"{sectionKey} Approved ✅",
            body,
            $"All documents and information for {sectionKey} have been approved by the administration.",
            sectionKey,
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.section_approved",
            ["section"] = sectionKey,
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant section approval push to user {UserId}.", ownerUserId);
            }
        }
    }

    public async Task NotifyRestaurantRegistrationApprovedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var title = "تهانينا! تم توثيق حساب مطعمك بنجاح 🎉";
        var body = "تمت الموافقة على جميع بيانات ومستندات المطعم. يمكنك الآن الدخول والبدء في إعداد وجباتك.";
        var deepLink = "/restaurant/orders/pending-confirmation";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "registration.approved",
            title,
            "Restaurant Registration Approved 🎉",
            body,
            "Your restaurant registration has been approved. You can now access your dashboard and manage your menu.",
            "registration",
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.registration_approved",
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant registration approved push to user {UserId}.", ownerUserId);
            }
        }
    }

    public async Task NotifyRestaurantRegistrationRejectedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var title = "تم رفض طلب تسجيل المطعم ❌";
        var body = $"نأسف، تم رفض طلب التسجيل للسبب التالي: {reason}";
        var deepLink = "/restaurant/under-review";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "registration.rejected",
            title,
            "Restaurant Registration Rejected ❌",
            body,
            $"Your restaurant registration was rejected: {reason}",
            "registration",
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.registration_rejected",
            ["reason"] = reason,
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant registration rejected push to user {UserId}.", ownerUserId);
            }
        }
    }

    public async Task NotifyRestaurantRegistrationChangesRequestedAsync(
        Guid ownerUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var title = "مطلوب تعديلات على طلب التسجيل ⚠️";
        var body = $"يرجى مراجعة الملاحظات وتحديث البيانات المطلوبة: {reason}";
        var deepLink = "/restaurant/onboarding?step=review&correction=true";

        await SaveRestaurantNotificationAsync(
            ownerUserId,
            "registration.changes_requested",
            title,
            "Modifications Requested on Registration ⚠️",
            body,
            $"Modifications are required for your registration: {reason}",
            "registration",
            deepLink: deepLink,
            cancellationToken: cancellationToken);

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => x.UserId == ownerUserId && x.IsActive)
            .Select(x => x.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "restaurant.changes_requested",
            ["reason"] = reason,
            ["deepLink"] = deepLink,
        };

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, title, body, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send restaurant changes requested push to user {UserId}.", ownerUserId);
            }
        }
    }

    private async Task SaveRestaurantNotificationAsync(
        Guid userId,
        string type,
        string titleAr,
        string titleEn,
        string bodyAr,
        string bodyEn,
        string? section = null,
        Guid? subjectId = null,
        string? deepLink = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _db.RestaurantNotifications.Add(new MealMate.Domain.IdentityAccess.RestaurantNotification
            {
                UserId = userId,
                Type = type,
                TitleAr = titleAr,
                TitleEn = titleEn,
                BodyAr = bodyAr,
                BodyEn = bodyEn,
                Section = section,
                SubjectId = subjectId,
                DeepLink = deepLink,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist restaurant notification of type {Type} for user {UserId}.", type, userId);
        }
    }

    private static string TranslateFieldKey(string key) => key.ToLowerInvariant() switch
    {
        "legalnamear" => "الاسم القانوني (عربي)",
        "legalnameen" => "الاسم القانوني (إنجليزي)",
        "tradenamear" => "الاسم التجاري (عربي)",
        "tradenameen" => "الاسم التجاري (إنجليزي)",
        "restaurantcode" => "كود المطعم",
        "legalentitytype" => "نوع الكيان القانوني",
        "companycivilid" => "الرقم المدني للشركة",
        "commercialregistrationnumber" => "رقم السجل التجاري",
        "commercialregistrationexpiry" => "تاريخ انتهاء السجل التجاري",
        "commerciallicensenumber" => "رقم الترخيص التجاري",
        "commerciallicenseexpiry" => "تاريخ انتهاء الترخيص التجاري",
        "foodlicensenumber" => "رقم ترخيص الغذاء",
        "foodlicenseexpiry" => "تاريخ انتهاء ترخيص الغذاء",
        "headquartersaddressar" or "address" => "عنوان المقر / الفرع",
        "contactphone" => "رقم هاتف التواصل",
        "contactemail" => "البريد الإلكتروني للتواصل",
        "website" => "الموقع الإلكتروني",
        "instagramhandle" => "حساب إنستغرام",
        "primarycontactname" => "اسم جهة الاتصال الأساسية",
        "primarycontacttitle" => "صفة جهة الاتصال الأساسية",
        "primarycontactphone" => "هاتف جهة الاتصال الأساسية",
        "primarycontactemail" => "بريد جهة الاتصال الأساسية",
        "ownername" => "اسم المالك",
        "authorizedsignatorycivilid" => "الرقم المدني للمفوض بالتوقيع",
        "authorizedsignatorytitle" => "صفة المفوض بالتوقيع",
        "bankname" => "اسم البنك",
        "beneficiaryname" => "اسم المستفيد من الحساب",
        "iban" => "رقم الآيبان (IBAN)",
        "accountnumber" => "رقم الحساب البنكي",
        "swiftcode" => "رمز السويفت (SWIFT)",
        "countryid" => "الدولة",
        "regionid" => "المحافظة / المنطقة",
        "latitude" or "longitude" => "موقع الخريطة",
        _ => key,
    };

    private static string ResolveFieldStep(string key) => key.ToLowerInvariant() switch
    {
        "address" or "headquartersaddressar" or "latitude" or "longitude" or "countryid" or "regionid" or "website" or "instagramhandle" or "contactphone" or "contactemail" or "primarycontactname" or "primarycontacttitle" or "primarycontactphone" or "primarycontactemail" => "location",
        "bankname" or "beneficiaryname" or "iban" or "accountnumber" or "swiftcode" => "regions",
        _ => "business",
    };

    private static string TranslateDocumentType(DocumentType type) => type switch
    {
        DocumentType.CommercialRegistration => "السجل التجاري",
        DocumentType.CommercialLicense => "الترخيص التجاري",
        DocumentType.FoodLicense => "ترخيص الغذاء",
        DocumentType.AuthorizedSignatoryId => "البطاقة المدنية للمفوض بالتوقيع",
        DocumentType.AuthorizedSignatoryAuthorization => "اعتماد التوقيع",
        DocumentType.BankIbanProof => "شهادة الآيبان / الحساب البنكي",
        DocumentType.ProfileImage => "شعار المطعم",
        DocumentType.BranchLicense => "ترخيص الفرع",
        DocumentType.BranchPhoto => "صورة الفرع",
        _ => type.ToString(),
    };
}
