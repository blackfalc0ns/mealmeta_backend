using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class SupportIssuesSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        // 1. Check if complaints for Capital already exist to avoid duplicate seeding
        var hasCapitalComplaints = await db.Complaints.AnyAsync(
            c => c.Title.Contains("العاصمة") || c.Description.Contains("العاصمة") || c.Description.Contains("شرق"),
            cancellationToken);

        if (hasCapitalComplaints)
        {
            logger.LogInformation("Support issues and complaints for Capital already seeded. Skipping seeder.");
            return;
        }

        logger.LogInformation("Starting rich seeding for Dispatcher Support Issues & Complaints across Kuwait areas...");

        var now = DateTime.UtcNow;

        // 2. Ensure at least one RestaurantProfile exists
        var restaurants = await db.RestaurantProfiles.ToListAsync(cancellationToken);
        if (restaurants.Count == 0)
        {
            var defaultRestaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "شركة دايت كير الكويت للأغذية الصحية",
                LegalNameEn = "Diet Care Kuwait Healthy Food Co.",
                TradeNameAr = "مطعم دايت كير",
                TradeNameEn = "Diet Care Restaurant",
                CommercialLicenseNumber = "CR-102938",
                ContactPhone = "+96522001122",
                ContactEmail = "support@dietcare.kw",
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true,
                ActivatedAtUtc = now,
                CreatedAtUtc = now
            };
            db.RestaurantProfiles.Add(defaultRestaurant);
            await db.SaveChangesAsync(cancellationToken);
            restaurants.Add(defaultRestaurant);
        }

        var primaryRestaurant = restaurants[0];

        // 3. Ensure Realistic Driver Profiles & Registrations exist
        var existingDrivers = await db.DriverProfiles.ToListAsync(cancellationToken);

        var driverDefs = new[]
        {
            (
                Ar: "أحمد السعيد",
                En: "Ahmed Al-Saeed",
                Phone: "+96550123456",
                Plate: "KW-4412",
                Type: "سيارة",
                Model: "تويوتا كامري",
                Color: "أبيض",
                License: "L-10254",
                IsAvailable: true
            ),
            (
                Ar: "محمد العنزي",
                En: "Mohammed Al-Enzi",
                Phone: "+96550234567",
                Plate: "KW-9821",
                Type: "سيارة",
                Model: "نيسان سنترا",
                Color: "فضي",
                License: "L-30819",
                IsAvailable: false
            ),
            (
                Ar: "سالم الحربي",
                En: "Salem Al-Harbi",
                Phone: "+96550345678",
                Plate: "KW-1190",
                Type: "دراجة نارية",
                Model: "هوندا توصيل",
                Color: "أسود",
                License: "L-77412",
                IsAvailable: false
            ),
            (
                Ar: "طارق المنصور",
                En: "Tariq Al-Mansoor",
                Phone: "+96550456789",
                Plate: "KW-6634",
                Type: "سيارة",
                Model: "هوندا سيفيك",
                Color: "أزرق",
                License: "L-55209",
                IsAvailable: true
            ),
            (
                Ar: "فهد العازمي",
                En: "Fahad Al-Azmi",
                Phone: "+96550567890",
                Plate: "KW-8841",
                Type: "سيارة",
                Model: "هيونداي إلنترا",
                Color: "رمادي",
                License: "L-66120",
                IsAvailable: true
            )
        };

        var seededDrivers = new List<DriverProfile>();

        foreach (var def in driverDefs)
        {
            var driver = existingDrivers.FirstOrDefault(d => d.FullNameAr == def.Ar || d.FullNameEn == def.En);
            if (driver == null)
            {
                var driverUserId = Guid.NewGuid();
                driver = new DriverProfile
                {
                    UserId = driverUserId,
                    RestaurantId = primaryRestaurant.Id,
                    FullNameAr = def.Ar,
                    FullNameEn = def.En,
                    NationalId = $"29{Random.Shared.Next(10000000, 99999999)}",
                    LicenseNumber = def.License,
                    VehiclePlate = def.Plate,
                    VehicleType = def.Type,
                    VehicleModel = def.Model,
                    VerificationStatus = VerificationStatus.Approved,
                    IsAvailable = def.IsAvailable,
                    ActivatedAtUtc = now.AddDays(-30),
                    CreatedAtUtc = now.AddDays(-30)
                };
                db.DriverProfiles.Add(driver);
                await db.SaveChangesAsync(cancellationToken);

                var reg = new DriverRegistration
                {
                    SubmittedByUserId = driverUserId,
                    RestaurantId = primaryRestaurant.Id,
                    FullNameAr = def.Ar,
                    FullNameEn = def.En,
                    Phone = def.Phone,
                    LicenseNumber = def.License,
                    VehiclePlate = def.Plate,
                    VehicleType = def.Type,
                    VehicleModel = def.Model,
                    VehicleColor = def.Color,
                    Status = VerificationStatus.Approved,
                    ResultingDriverProfileId = driver.Id,
                    CreatedAtUtc = now.AddDays(-30)
                };
                db.DriverRegistrations.Add(reg);
                await db.SaveChangesAsync(cancellationToken);
            }
            seededDrivers.Add(driver);
        }

        // 4. Ensure Realistic Customer Profiles exist
        var customerDefs = new[]
        {
            (Name: "محمد الفضلي", Address: "مدينة الكويت، شرق، برج الراية، الدور 18"),
            (Name: "دلال السبيعي", Address: "مدينة الكويت، القبلة، مجمع الصالحية، مكتب 402"),
            (Name: "سارة المطيري", Address: "مدينة الكويت، برج الحمراء، الدور 28"),
            (Name: "جاسم العتيبي", Address: "السالمية، قطعة 4، شارع 12، منزل 15"),
            (Name: "فاطمة الكندري", Address: "السالمية، شارع سالم المبارك، برج الثريا"),
            (Name: "عبدالله الشمري", Address: "حولي، شارع تونس، مجمع 8، شقة 12"),
            (Name: "ريم الدوسري", Address: "الجابرية، قطعة 1أ، شارع 105"),
            (Name: "يوسف المطيري", Address: "الفروانية، قطعة 1، شارع حبيب مناور"),
            (Name: "نور الصباح", Address: "خيطان، قطعة 3، شارع 45")
        };

        var seededCustomerIds = new List<Guid>();
        foreach (var c in customerDefs)
        {
            var customerUserId = Guid.NewGuid();
            var customerProfile = new CustomerProfile
            {
                UserId = customerUserId,
                DeliveryAddressLine = c.Address,
                OnboardingCompleted = true,
                PreferredLanguage = "ar",
                DietaryPattern = "Balanced Diet",
                CreatedAtUtc = now.AddDays(-20)
            };
            db.CustomerProfiles.Add(customerProfile);
            seededCustomerIds.Add(customerUserId);
        }
        await db.SaveChangesAsync(cancellationToken);

        // 5. Create Trips and TripStops across Kuwait areas
        var tripCodes = new[] { "TRP-DISP-001", "TRP-DISP-002", "TRP-DISP-003", "TRP-DISP-004" };
        var existingTrips = await db.Trips.Where(t => tripCodes.Contains(t.Code)).ToListAsync(cancellationToken);

        var tripCapital = existingTrips.FirstOrDefault(t => t.Code == "TRP-DISP-001");
        if (tripCapital == null)
        {
            tripCapital = new Trip
            {
                RestaurantId = primaryRestaurant.Id,
                Code = "TRP-DISP-001",
                Status = TripStatus.InProgress,
                PlannedDate = DateOnly.FromDateTime(now),
                ShiftName = "وردية العاصمة الصباحية",
                Notes = "توزيع وجبات أبراج ومكاتب العاصمة - شرق والقبلة",
                CreatedAtUtc = now.AddHours(-4)
            };
            tripCapital.DriverAssignments.Add(new DriverAssignment
            {
                DriverProfileId = seededDrivers[0].Id,
                AssignedAtUtc = now.AddHours(-4),
                Status = "Assigned",
                CreatedAtUtc = now.AddHours(-4)
            });
            db.Trips.Add(tripCapital);
            await db.SaveChangesAsync(cancellationToken);
        }

        var tripSalmiya = existingTrips.FirstOrDefault(t => t.Code == "TRP-DISP-002");
        if (tripSalmiya == null)
        {
            tripSalmiya = new Trip
            {
                RestaurantId = primaryRestaurant.Id,
                Code = "TRP-DISP-002",
                Status = TripStatus.InProgress,
                PlannedDate = DateOnly.FromDateTime(now),
                ShiftName = "وردية السالمية والبدع",
                Notes = "توزيع وجبات السالمية السكنية والتجارية",
                CreatedAtUtc = now.AddHours(-3)
            };
            tripSalmiya.DriverAssignments.Add(new DriverAssignment
            {
                DriverProfileId = seededDrivers[1].Id,
                AssignedAtUtc = now.AddHours(-3),
                Status = "Assigned",
                CreatedAtUtc = now.AddHours(-3)
            });
            db.Trips.Add(tripSalmiya);
            await db.SaveChangesAsync(cancellationToken);
        }

        var tripHawally = existingTrips.FirstOrDefault(t => t.Code == "TRP-DISP-003");
        if (tripHawally == null)
        {
            tripHawally = new Trip
            {
                RestaurantId = primaryRestaurant.Id,
                Code = "TRP-DISP-003",
                Status = TripStatus.InProgress,
                PlannedDate = DateOnly.FromDateTime(now),
                ShiftName = "وردية حولي والجابرية",
                Notes = "توزيع وجبات حولي والجابرية وبيان",
                CreatedAtUtc = now.AddHours(-5)
            };
            tripHawally.DriverAssignments.Add(new DriverAssignment
            {
                DriverProfileId = seededDrivers[2].Id,
                AssignedAtUtc = now.AddHours(-5),
                Status = "Assigned",
                CreatedAtUtc = now.AddHours(-5)
            });
            db.Trips.Add(tripHawally);
            await db.SaveChangesAsync(cancellationToken);
        }

        var tripFarwaniya = existingTrips.FirstOrDefault(t => t.Code == "TRP-DISP-004");
        if (tripFarwaniya == null)
        {
            tripFarwaniya = new Trip
            {
                RestaurantId = primaryRestaurant.Id,
                Code = "TRP-DISP-004",
                Status = TripStatus.InProgress,
                PlannedDate = DateOnly.FromDateTime(now),
                ShiftName = "وردية الفروانية وخيطان",
                Notes = "توزيع وجبات الفروانية وخيطان والعارضية",
                CreatedAtUtc = now.AddHours(-6)
            };
            tripFarwaniya.DriverAssignments.Add(new DriverAssignment
            {
                DriverProfileId = seededDrivers[3].Id,
                AssignedAtUtc = now.AddHours(-6),
                Status = "Assigned",
                CreatedAtUtc = now.AddHours(-6)
            });
            db.Trips.Add(tripFarwaniya);
            await db.SaveChangesAsync(cancellationToken);
        }

        // 6. Define Issues Catalog with multi-restaurant assignment
        var issuesCatalog = new[]
        {
            // === CAPITAL (العاصمة) ===
            new
            {
                Area = "capital",
                ZoneAr = "العاصمة",
                ZoneEn = "Capital",
                BoxCode = "#BX-4091",
                CustomerName = "محمد الفضلي",
                CustomerPhone = "+96590112233",
                Category = "SevereDelay",
                Title = "تأخير شديد في تسليم الوجبة - العاصمة، شرق",
                Description = "السائق متأخر أكثر من 45 دقيقة عن موعد التسليم المحدد في برج الراية، والعميل لديه اجتماع عمل ومستاء للغاية.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddMinutes(-35),
                DriverIndex = 0, // Ahmed Al-Saeed
                Trip = tripCapital,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/delay_traffic_01.jpg",
                    "https://cdn.mealmate.app/evidence/delay_route_02.jpg"
                }
            },
            new
            {
                Area = "capital",
                ZoneAr = "العاصمة",
                ZoneEn = "Capital",
                BoxCode = "#BX-1025",
                CustomerName = "سارة المطيري",
                CustomerPhone = "+96590223344",
                Category = "CustomerUnavailable",
                Title = "العميل غير متاح ولا يرد على الاتصال - العاصمة، برج الحمراء",
                Description = "السائق وصل إلى برج الحمراء، الدور 28، وحاول الاتصال بالعميل 4 مرات على مدار 15 دقيقة دون استجابة.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddHours(-2),
                DriverIndex = 1, // Mohammed Al-Enzi
                Trip = tripCapital,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/unreachable_door_01.jpg"
                }
            },
            new
            {
                Area = "capital",
                ZoneAr = "العاصمة",
                ZoneEn = "Capital",
                BoxCode = "#BX-3342",
                CustomerName = "دلال السبيعي",
                CustomerPhone = "+96590334455",
                Category = "VehicleBreakdown",
                Title = "تعطل دراجة التوصيل - العاصمة، القبلة",
                Description = "انفجار إطار دراجة التوصيل بالقرب من مجمع الصالحية في القبلة، والسائق غير قادر على إكمال باقي المسار ويحتاج سائق بديل فوراً.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddDays(-1).AddHours(-3),
                DriverIndex = 2, // Salem Al-Harbi
                Trip = tripCapital,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/bike_puncture_01.jpg",
                    "https://cdn.mealmate.app/evidence/bike_puncture_02.jpg"
                }
            },
            new
            {
                Area = "capital",
                ZoneAr = "العاصمة",
                ZoneEn = "Capital",
                BoxCode = "#BX-5521",
                CustomerName = "عمر الخالدي",
                CustomerPhone = "+96590445566",
                Category = "AddressIssue",
                Title = "مشكلة في العنوان ورقم المبنى - العاصمة، دسمان",
                Description = "العنوان المسجل يشير إلى شارع الخليج العربي لكن رقم القسيمة والمبنى مفقود، جاري التحقق من خدمة العملاء.",
                Status = ComplaintStatus.UnderReview,
                SubmittedAt = now.AddDays(-2).AddHours(-1),
                DriverIndex = 3, // Tariq Al-Mansoor
                Trip = tripCapital,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/address_missing_01.jpg"
                }
            },
            new
            {
                Area = "capital",
                ZoneAr = "العاصمة",
                ZoneEn = "Capital",
                BoxCode = "#BX-2901",
                CustomerName = "أروى الرشيدي",
                CustomerPhone = "+96590556677",
                Category = "DamagedBox",
                Title = "بوكس تالف جزئياً - العاصمة، المرقاب",
                Description = "تسرب صلصة داخل الصندوق بسبب مطب حاد، تم تعويض العميل بوجبة بديلة فوراً وقبول التسوية.",
                Status = ComplaintStatus.Resolved,
                SubmittedAt = now.AddDays(-4),
                DriverIndex = 0, // Ahmed Al-Saeed
                Trip = tripCapital,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/box_damaged_01.jpg",
                    "https://cdn.mealmate.app/evidence/box_damaged_02.jpg"
                }
            },

            // === SALMIYA (السالمية) ===
            new
            {
                Area = "salmiya",
                ZoneAr = "السالمية",
                ZoneEn = "Salmiya",
                BoxCode = "#BX-8821",
                CustomerName = "جاسم العتيبي",
                CustomerPhone = "+96590667788",
                Category = "DamagedBox",
                Title = "صندوق وجبة مكسور - السالمية، قطعة 4",
                Description = "استلم العميل الصندوق مكسوراً من الزاوية مع انسكاب العصير الداخلي، يطالب بإعادة إرسال الوجبة.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddHours(-1),
                DriverIndex = 1, // Mohammed Al-Enzi
                Trip = tripSalmiya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/broken_box_01.jpg",
                    "https://cdn.mealmate.app/evidence/broken_box_02.jpg"
                }
            },
            new
            {
                Area = "salmiya",
                ZoneAr = "السالمية",
                ZoneEn = "Salmiya",
                BoxCode = "#BX-6110",
                CustomerName = "فاطمة الكندري",
                CustomerPhone = "+96590778899",
                Category = "CustomerUnavailable",
                Title = "العميل غير متاح - السالمية، شارع سالم المبارك",
                Description = "حارس البناية رفض الاستلام وهاتف العميل مغلق منذ 20 دقيقة.",
                Status = ComplaintStatus.UnderReview,
                SubmittedAt = now.AddDays(-3),
                DriverIndex = 2, // Salem Al-Harbi
                Trip = tripSalmiya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/building_closed_01.jpg"
                }
            },
            new
            {
                Area = "salmiya",
                ZoneAr = "السالمية",
                ZoneEn = "Salmiya",
                BoxCode = "#BX-7452",
                CustomerName = "مها الغانم",
                CustomerPhone = "+96590889900",
                Category = "SevereDelay",
                Title = "تأخير بسبب ازدحام شارع البلاجات - السالمية",
                Description = "تأخر السائق 35 دقيقة بسبب حادث مروري، تم إبلاغ العميل وتسليم الطلب واعتذار رسمي.",
                Status = ComplaintStatus.Resolved,
                SubmittedAt = now.AddDays(-5),
                DriverIndex = 3, // Tariq Al-Mansoor
                Trip = tripSalmiya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/traffic_jam_01.jpg"
                }
            },

            // === HAWALLY (حولي) ===
            new
            {
                Area = "hawally",
                ZoneAr = "حولي",
                ZoneEn = "Hawally",
                BoxCode = "#BX-9043",
                CustomerName = "عبدالله الشمري",
                CustomerPhone = "+96590990011",
                Category = "VehicleBreakdown",
                Title = "تعطل المحرك - حولي، شارع تونس",
                Description = "ارتفاع حرارة المحرك وتوقف السيارة أمام مجمع المحاصيل، بانتظار سائق بديل لنقل الوجبات.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddHours(-3),
                DriverIndex = 0, // Ahmed Al-Saeed
                Trip = tripHawally,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/car_engine_01.jpg"
                }
            },
            new
            {
                Area = "hawally",
                ZoneAr = "حولي",
                ZoneEn = "Hawally",
                BoxCode = "#BX-4489",
                CustomerName = "ريم الدوسري",
                CustomerPhone = "+96590001122",
                Category = "AddressIssue",
                Title = "عنوان ناقص ورقم المنزل مفقود - الجابرية، قطعة 1أ",
                Description = "رقم المنزل غير مذكور في العنوان المسجل والسائق ينتظر عند مدخل القطعة.",
                Status = ComplaintStatus.UnderReview,
                SubmittedAt = now.AddDays(-2).AddHours(-4),
                DriverIndex = 1, // Mohammed Al-Enzi
                Trip = tripHawally,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/street_view_01.jpg"
                }
            },
            new
            {
                Area = "hawally",
                ZoneAr = "حولي",
                ZoneEn = "Hawally",
                BoxCode = "#BX-1198",
                CustomerName = "بدر المنصور",
                CustomerPhone = "+96590112244",
                Category = "CustomerUnavailable",
                Title = "العميل في النادي الرياضي - بيان، قطعة 7",
                Description = "تم التواصل مع العميل وإعادة جدولة التسليم للنوبة المسائية وتم التسليم بنجاح.",
                Status = ComplaintStatus.Resolved,
                SubmittedAt = now.AddDays(-6),
                DriverIndex = 2, // Salem Al-Harbi
                Trip = tripHawally,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/gym_dropoff_01.jpg"
                }
            },

            // === FARWANIYA (الفروانية) ===
            new
            {
                Area = "farwaniya",
                ZoneAr = "الفروانية",
                ZoneEn = "Farwaniya",
                BoxCode = "#BX-7723",
                CustomerName = "يوسف المطيري",
                CustomerPhone = "+96590223355",
                Category = "CustomerUnavailable",
                Title = "العميل خارج المنزل - الفروانية، قطعة 1",
                Description = "العميل طلب ترك الصندوق عند الباب لكن نظام العمارة يمنع ترك الطرود دون تسليم مباشر.",
                Status = ComplaintStatus.Submitted,
                SubmittedAt = now.AddHours(-4),
                DriverIndex = 3, // Tariq Al-Mansoor
                Trip = tripFarwaniya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/building_gate_01.jpg"
                }
            },
            new
            {
                Area = "farwaniya",
                ZoneAr = "الفروانية",
                ZoneEn = "Farwaniya",
                BoxCode = "#BX-8834",
                CustomerName = "نور الصباح",
                CustomerPhone = "+96590334466",
                Category = "SevereDelay",
                Title = "تأخير في التوصيل - خيطان، قطعة 3",
                Description = "تأخر السائق أثناء توزيع الوجبات المدرسية الصباحية بسبب أعمال صيانة الطرق في خيطان.",
                Status = ComplaintStatus.UnderReview,
                SubmittedAt = now.AddDays(-3).AddHours(-1),
                DriverIndex = 0, // Ahmed Al-Saeed
                Trip = tripFarwaniya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/roadworks_01.jpg"
                }
            },
            new
            {
                Area = "farwaniya",
                ZoneAr = "الفروانية",
                ZoneEn = "Farwaniya",
                BoxCode = "#BX-3912",
                CustomerName = "هند العتيبي",
                CustomerPhone = "+96590445577",
                Category = "DamagedBox",
                Title = "تلف الغلاف الخارجي - العارضية، قطعة 2",
                Description = "تم استبدال الوجبة خلال 30 دقيقة وقبول الاعتذار وحل الشكوى بالكامل.",
                Status = ComplaintStatus.Resolved,
                SubmittedAt = now.AddDays(-12),
                DriverIndex = 2, // Salem Al-Harbi
                Trip = tripFarwaniya,
                Photos = new[]
                {
                    "https://cdn.mealmate.app/evidence/damaged_seal_01.jpg"
                }
            }
        };

        // 7. Seed for each restaurant in the system so any logged-in DeliveryManager finds data
        int seq = 1;
        foreach (var restaurant in restaurants)
        {
            foreach (var item in issuesCatalog)
            {
                var assignedDriver = seededDrivers[item.DriverIndex % seededDrivers.Count];
                var customerId = seededCustomerIds[(seq - 1) % seededCustomerIds.Count];

                // Create TripStop for problem details linking
                var stop = new TripStop
                {
                    TripId = item.Trip.Id,
                    OrderCode = item.BoxCode,
                    CustomerMaskedId = $"CUST-****{Math.Abs(item.BoxCode.GetHashCode()) % 9000 + 1000}",
                    CustomerName = item.CustomerName,
                    CustomerPhone = item.CustomerPhone,
                    ZoneNameAr = item.ZoneAr,
                    ZoneNameEn = item.ZoneEn,
                    MealSummaryAr = "وجبة كيتو متوازنة + سلطة سيزر خفيفة",
                    MealSummaryEn = "Keto Balanced Meal + Light Caesar Salad",
                    BoxCount = 1,
                    SlotLabel = "الغداء",
                    SequenceNumber = seq++,
                    Status = item.Status == ComplaintStatus.Resolved ? TripStopStatus.Delivered : (item.Category == "SevereDelay" || item.Category == "VehicleBreakdown" ? TripStopStatus.Failed : TripStopStatus.Pending),
                    FailureReason = item.Status != ComplaintStatus.Resolved ? item.Title : null,
                    DeliveredAtUtc = item.Status == ComplaintStatus.Resolved ? item.SubmittedAt.AddMinutes(45) : null,
                    CreatedAtUtc = item.SubmittedAt
                };
                db.TripStops.Add(stop);
                await db.SaveChangesAsync(cancellationToken);

                // Create Complaint
                var complaint = new Complaint
                {
                    RestaurantId = restaurant.Id,
                    CustomerUserId = customerId,
                    DriverProfileId = assignedDriver.Id,
                    OrderId = stop.Id,
                    Category = item.Category,
                    Title = item.Title,
                    Description = item.Description,
                    Status = item.Status,
                    SubmittedAtUtc = item.SubmittedAt,
                    CreatedAtUtc = item.SubmittedAt
                };

                foreach (var photoUrl in item.Photos)
                {
                    complaint.EvidencePhotos.Add(new ComplaintEvidencePhoto
                    {
                        StoragePath = photoUrl,
                        ContentType = "image/jpeg",
                        UploadedAtUtc = item.SubmittedAt
                    });
                }

                db.Complaints.Add(complaint);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Successfully seeded {Count} rich Dispatcher Support Issues across Kuwait governorates!", issuesCatalog.Length * restaurants.Count);
    }
}
