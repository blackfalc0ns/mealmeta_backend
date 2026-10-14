using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.ApiTests.FamilyManager;

[Collection("OrderLifecycleE2E")]
public sealed class FamilyManagementFlowPostgresHttpTests(OrderLifecycleEndToEndFixture fixture)
    : IClassFixture<OrderLifecycleEndToEndFixture>
{
    [Fact]
    public async Task Manager_can_manage_a_funded_family_member_and_complete_a_calendar_day_over_http()
    {
        var now = DateTime.UtcNow.AddSeconds(-1);
        using var clock = fixture.Clock.AdvanceTo(now);
        var serviceDate = DateOnly.FromDateTime(now).AddDays(5);
        await using var db = fixture.CreateDbContext();
        var seed = await OrderLifecycleTestDataSeeder.SeedAsync(db, serviceDate);

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = seed.CustomerUser.Id,
            Name = "E2E family",
            Status = FamilyGroupStatus.Active,
            PaidAtUtc = now.AddDays(-1),
            CurrencyCode = "KWD"
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "Before edit",
            RelationshipCode = "child",
            Gender = FamilyMemberGender.Female,
            AgeYears = 16,
            NutritionalGoalCode = "maintain",
            ActivityLevel = FamilyActivityLevel.Active,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = seed.Program.Id,
            BundleId = seed.Bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 6,
            StartDate = DateOnly.FromDateTime(now)
        };
        group.Members.Add(member);
        db.Add(group);
        await db.SaveChangesAsync();
        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = seed.CustomerUser.Id,
            FamilyGroupId = group.Id,
            ProgramId = seed.Program.Id,
            BundleId = seed.Bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 6,
            StartDate = DateOnly.FromDateTime(now),
            EndDate = serviceDate,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(30m, "KWD"),
            PaymentVerifiedAtUtc = now.AddDays(-1),
            FulfillmentCompletedAtUtc = now.AddDays(-1),
            DeliveryAddress = seed.CustomerAddress.AddressLine
        };
        member.MemberSubscriptionId = subscription.Id;
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        subscription.FamilyMemberId = member.Id;
        await db.SaveChangesAsync();
        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = 1,
            EligibilityCheckedAtUtc = now.AddDays(-1)
        };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = seed.RestaurantA.Id,
            ProgramId = seed.Program.Id,
            BundleId = seed.Bundle.Id,
            RegionId = seed.Region.Id,
            Tier = RestaurantTier.Basic,
            NameAr = seed.RestaurantA.TradeNameAr,
            NameEn = seed.RestaurantA.TradeNameEn,
            IsInitiallySelected = true,
            CapturedAtUtc = now.AddDays(-1)
        };
        var menuVersionId = await db.MenuPlanVersions.Where(x => x.MenuPlan.RestaurantId == seed.RestaurantA.Id)
            .Select(x => x.Id).SingleAsync();
        const string slotRequirementsJson = "[{\"slot\":\"lunch\",\"count\":1}]";
        var mealsJson = JsonSerializer.Serialize(new[] { new
        {
            mealId = seed.MealA.Id,
            nameAr = seed.MealA.NameAr,
            nameEn = seed.MealA.NameEn,
            descriptionAr = seed.MealA.NameAr,
            descriptionEn = seed.MealA.NameEn,
            slot = "lunch",
            sortOrder = 1,
            calories = seed.MealA.Calories,
            proteinG = seed.MealA.ProteinG,
            carbsG = seed.MealA.CarbsG,
            fatG = seed.MealA.FatG,
            presentAllergenCodes = Array.Empty<string>(),
            mayContainAllergenCodes = Array.Empty<string>(),
            dietaryPreferenceCodes = Array.Empty<string>(),
            ingredients = Array.Empty<object>()
        }});
        var daySnapshot = new SubscriptionRestaurantDaySnapshot
        {
            RestaurantSnapshotId = restaurant.Id,
            SubscriptionId = subscription.Id,
            RestaurantId = seed.RestaurantA.Id,
            ServiceDate = serviceDate,
            MenuPlanVersionId = menuVersionId,
            SlotRequirementsJson = slotRequirementsJson,
            MealsJson = mealsJson,
            ContentHash = FrozenSnapshotIntegrity.ComputeContentHash(slotRequirementsJson, mealsJson),
            CapturedAtUtc = now.AddDays(-1)
        };
        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id,
            PayerCustomerUserId = seed.CustomerUser.Id,
            IdempotencyKey = "family-management-e2e-paid",
            Status = FamilyCheckoutBatchStatus.Paid,
            PaidAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-2),
            PaymentAnchorSubscriptionId = subscription.Id,
            CurrencyCode = "KWD",
            FinalPrice = 30m
        };
        var batchLine = new FamilyCheckoutBatchLine
        {
            BatchId = batch.Id,
            FamilyMemberId = member.Id,
            SubscriptionId = subscription.Id,
            CheckoutSelected = true,
            ProgramId = seed.Program.Id,
            BundleId = seed.Bundle.Id,
            Tier = RestaurantTier.Basic,
            DurationDays = 6,
            StartDate = subscription.StartDate,
            DeliveryAddressId = seed.CustomerAddress.Id,
            AddressLine = seed.CustomerAddress.AddressLine,
            RegionId = seed.Region.Id,
            ResolvedRestaurantId = seed.RestaurantA.Id,
            CurrencyCode = "KWD",
            PriceBeforeFamilyDiscount = 30m,
            NetPaidAmount = 30m,
            CreatedAtUtc = now.AddDays(-2)
        };
        db.AddRange(pool, restaurant, daySnapshot,
            new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewCalendar = true, SelectRestaurant = true, EditDailyMeals = true, ConfirmMeals = true },
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = seed.CustomerUser.Id, HasSeparateAccount = false }, batch, batchLine);
        db.FoodPreferenceDefinitions.Add(new FoodPreferenceDefinition
        {
            Code = "E2E_VEGETARIAN",
            NameAr = "نباتي للاختبار",
            NameEn = "E2E vegetarian",
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.SubscriptionDays.Add(new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = serviceDate,
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = now.AddDays(2),
            DeliveryAddressId = seed.CustomerAddress.Id,
            DeliveryAddressLine = seed.CustomerAddress.AddressLine
        });
        await db.SaveChangesAsync();

        var futureStart = DateOnly.FromDateTime(now).AddDays(17);
        var futureMember = new FamilyMember
        {
            FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Future funded member",
            RelationshipCode = "child", Gender = FamilyMemberGender.Female, AgeYears = 12,
            NutritionalGoalCode = "maintain", ActivityLevel = FamilyActivityLevel.Active,
            SetupStatus = FamilyMemberSetupStatus.Ready, ProgramId = seed.Program.Id, BundleId = seed.Bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 2, StartDate = futureStart
        };
        var futureSubscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = seed.CustomerUser.Id, FamilyGroupId = group.Id, FamilyMemberId = futureMember.Id,
            ProgramId = seed.Program.Id, BundleId = seed.Bundle.Id, SelectedTier = RestaurantTier.Basic,
            DurationDays = 2, StartDate = futureStart, EndDate = futureStart.AddDays(1),
            Status = SubscriptionStatus.Paid, PaidAmount = new Money(17m, "KWD"),
            PaymentVerifiedAtUtc = now.AddDays(-1), DeliveryAddress = seed.CustomerAddress.AddressLine
        };
        db.FamilyMembers.Add(futureMember);
        await db.SaveChangesAsync();
        futureSubscription.FamilyMemberId = futureMember.Id;
        db.Subscriptions.Add(futureSubscription);
        await db.SaveChangesAsync();
        futureMember.MemberSubscriptionId = futureSubscription.Id;
        var futureBatch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id, PayerCustomerUserId = seed.CustomerUser.Id,
            IdempotencyKey = "family-management-e2e-future-paid", Status = FamilyCheckoutBatchStatus.Paid,
            PaidAtUtc = now.AddDays(-1), CreatedAtUtc = now.AddDays(-1),
            PaymentAnchorSubscriptionId = subscription.Id, CurrencyCode = "KWD", FinalPrice = 17m,
            Lines = { new FamilyCheckoutBatchLine
            {
                FamilyMemberId = futureMember.Id, SubscriptionId = futureSubscription.Id, CheckoutSelected = true,
                ProgramId = seed.Program.Id, BundleId = seed.Bundle.Id, Tier = RestaurantTier.Basic,
                DurationDays = 2, StartDate = futureStart, DeliveryAddressId = seed.CustomerAddress.Id,
                AddressLine = seed.CustomerAddress.AddressLine, RegionId = seed.Region.Id,
                ResolvedRestaurantId = seed.RestaurantA.Id, CurrencyCode = "KWD",
                PriceBeforeFamilyDiscount = 17m, NetPaidAmount = 17m, CreatedAtUtc = now.AddDays(-1)
            } }
        };
        db.AddRange(futureBatch,
            new SubscriptionDay { SubscriptionId = futureSubscription.Id, ServiceDate = futureStart, Status = SubscriptionDayStatus.Open },
            new SubscriptionDay { SubscriptionId = futureSubscription.Id, ServiceDate = futureStart.AddDays(1), Status = SubscriptionDayStatus.Open });
        await db.SaveChangesAsync();

        using var manager = fixture.CreateClientForUser(new TokenUser(seed.CustomerUser.Id, UserType.Customer, ["Customer"], []));
        using var outsider = fixture.CreateClientForUser(new TokenUser(seed.DispatcherA.Id, UserType.Customer, ["Customer"], []));

        using (var futureHome = await manager.GetAsync("/api/v1/customer/family/home"))
        {
            Assert.Equal(HttpStatusCode.OK, futureHome.StatusCode);
            var body = await futureHome.Content.ReadFromJsonAsync<JsonElement>();
            var future = body.GetProperty("members").EnumerateArray()
                .Single(x => x.GetProperty("memberId").GetGuid() == futureMember.Id);
            Assert.Equal(JsonValueKind.Null, future.GetProperty("memberSubscriptionId").ValueKind);
            Assert.Equal(futureSubscription.Id, future.GetProperty("upcomingSubscriptionId").GetGuid());
            Assert.Equal(2, future.GetProperty("remainingDays").GetInt32());
            Assert.Equal("PaidPreparingCalendar", future.GetProperty("lifecycle").GetProperty("displayStatus").GetString());
            Assert.Equal("Pending", future.GetProperty("lifecycle").GetProperty("fulfillmentStatus").GetString());
            Assert.Equal("Paid", future.GetProperty("lifecycle").GetProperty("serviceStatus").GetString());
        }
        using (var futureList = await manager.GetAsync("/api/v1/customer/family/members"))
        {
            Assert.Equal(HttpStatusCode.OK, futureList.StatusCode);
            var body = await futureList.Content.ReadFromJsonAsync<JsonElement>();
            var future = body.EnumerateArray().Single(x => x.GetProperty("memberId").GetGuid() == futureMember.Id);
            Assert.Equal(JsonValueKind.Null, future.GetProperty("memberSubscriptionId").ValueKind);
            Assert.Equal(futureSubscription.Id, future.GetProperty("upcomingSubscriptionId").GetGuid());
            Assert.Equal(2, future.GetProperty("remainingDays").GetInt32());
            Assert.Equal("PaidPreparingCalendar", future.GetProperty("lifecycle").GetProperty("displayStatus").GetString());
        }
        using (var futureDetail = await manager.GetAsync($"/api/v1/customer/family/members/{futureMember.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, futureDetail.StatusCode);
            var body = await futureDetail.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, body.GetProperty("memberSubscriptionId").ValueKind);
            Assert.Equal(futureSubscription.Id, body.GetProperty("upcomingSubscriptionId").GetGuid());
            Assert.Equal(2, body.GetProperty("remainingDays").GetInt32());
            Assert.False(body.GetProperty("canRenew").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("renewalPath").ValueKind);
            Assert.Equal(JsonValueKind.Null, body.GetProperty("todayNutrition").ValueKind);
            Assert.Equal("PaidPreparingCalendar", body.GetProperty("lifecycle").GetProperty("displayStatus").GetString());
        }

        using (var home = await manager.GetAsync("/api/v1/customer/family/home"))
        {
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            var homeBody = await home.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(member.Id, homeBody.GetProperty("members")[0].GetProperty("memberId").GetGuid());
            Assert.Equal(subscription.Id, homeBody.GetProperty("members")[0].GetProperty("memberSubscriptionId").GetGuid());
            Assert.False(homeBody.GetProperty("wallet").GetProperty("available").GetBoolean());
        }
        using (var members = await manager.GetAsync("/api/v1/customer/family/members"))
            Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        using (var detail = await manager.GetAsync($"/api/v1/customer/family/members/{member.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(subscription.Id, detailBody.GetProperty("memberSubscriptionId").GetGuid());
            Assert.Equal(1, detailBody.GetProperty("remainingDays").GetInt32());
        }

        using (var savedDietary = await manager.PutAsJsonAsync($"/api/v1/customer/family-subscription-flow/members/{member.Id}/dietary", new
        {
            dietaryPreferenceCodes = new[] { "E2E_VEGETARIAN" }
        }))
            Assert.Equal(HttpStatusCode.OK, savedDietary.StatusCode);
        using (var readDietary = await manager.GetAsync($"/api/v1/customer/family-subscription-flow/members/{member.Id}/dietary"))
        {
            Assert.Equal(HttpStatusCode.OK, readDietary.StatusCode);
            var dietary = await readDietary.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("E2E_VEGETARIAN", dietary.GetProperty("selectedCodes").EnumerateArray().Select(x => x.GetString()));
        }

        using (var added = await manager.PostAsJsonAsync("/api/v1/customer/family-subscription-flow/members", new
        {
            displayName = "Deferred member",
            relationshipCode = "child",
            gender = "Male",
            ageYears = 12,
            deliveryAddressId = (Guid?)null,
            phone = (string?)null,
            occupation = (string?)null
        }))
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        await using (var verifyNew = fixture.CreateDbContext())
        {
            var addedMember = await verifyNew.FamilyMembers.SingleAsync(x => x.DisplayName == "Deferred member");
            Assert.Equal(string.Empty, addedMember.NutritionalGoalCode);
            Assert.Equal(FamilyActivityLevel.Sedentary, addedMember.ActivityLevel);
            Assert.Null(addedMember.MemberSubscriptionId);
            Assert.Equal(0, await verifyNew.FamilyCheckoutBatchLines.CountAsync(x => x.FamilyMemberId == addedMember.Id));
        }

        var edit = await manager.PutAsJsonAsync($"/api/v1/customer/family-subscription-flow/members/{member.Id}", new
        {
            displayName = "After edit", relationshipCode = "child", gender = "Female", ageYears = 17,
            deliveryAddressId = seed.CustomerAddress.Id, phone = (string?)null, occupation = "Student"
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        await using (var verify = fixture.CreateDbContext())
        {
            var saved = await verify.FamilyMembers.FindAsync(member.Id);
            Assert.Equal("maintain", saved!.NutritionalGoalCode);
            Assert.Equal(FamilyActivityLevel.Active, saved.ActivityLevel);
            Assert.Equal("After edit", saved.DisplayName);
        }

        using (var access = await manager.PutAsJsonAsync($"/api/v1/customer/family/members/{member.Id}/access", new { isPaused = true }))
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var pausedManagerAction = await manager.PostAsJsonAsync(
            $"/api/v1/customer/subscriptions/{subscription.Id}/days/{serviceDate:yyyy-MM-dd}/restaurant",
            new { restaurantId = seed.RestaurantA.Id }))
            Assert.Equal(HttpStatusCode.OK, pausedManagerAction.StatusCode);
        using (var access = await manager.PutAsJsonAsync($"/api/v1/customer/family/members/{member.Id}/access", new { isPaused = false }))
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);

        using (var deniedAr = await LocalizedGet(outsider, $"/api/v1/customer/family/members/{member.Id}", "ar"))
        using (var deniedEn = await LocalizedGet(outsider, $"/api/v1/customer/family/members/{member.Id}", "en"))
        using (var deniedArAgain = await LocalizedGet(outsider, $"/api/v1/customer/family/members/{member.Id}", "ar"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedAr.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deniedEn.StatusCode);
            var ar = await deniedAr.Content.ReadFromJsonAsync<JsonElement>();
            var en = await deniedEn.Content.ReadFromJsonAsync<JsonElement>();
            var arAgain = await deniedArAgain.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("family.not_manager", ar.GetProperty("code").GetString());
            Assert.NotEqual(ar.GetProperty("detail").GetString(), en.GetProperty("detail").GetString());
            Assert.Equal(ar.GetProperty("detail").GetString(), arAgain.GetProperty("detail").GetString());
        }

        var date = serviceDate.ToString("yyyy-MM-dd");
        using (var outsiderDay = await outsider.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}"))
            Assert.Equal(HttpStatusCode.Forbidden, outsiderDay.StatusCode);
        using (var outsiderEdit = await outsider.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/restaurant", new { restaurantId = seed.RestaurantA.Id }))
            Assert.Equal(HttpStatusCode.Forbidden, outsiderEdit.StatusCode);
        using (var calendar = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/calendar"))
        {
            Assert.Equal(HttpStatusCode.OK, calendar.StatusCode);
            Assert.Contains(serviceDate.ToString("yyyy-MM-dd"), await calendar.Content.ReadAsStringAsync());
        }
        using (var day = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}"))
            Assert.Equal(HttpStatusCode.OK, day.StatusCode);
        using (var restaurants = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/restaurants"))
            Assert.Equal(HttpStatusCode.OK, restaurants.StatusCode);
        using (var availableMeals = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/restaurants/{seed.RestaurantA.Id}/meals?slot=lunch"))
        {
            Assert.Equal(HttpStatusCode.OK, availableMeals.StatusCode);
            Assert.Contains(seed.MealA.Id.ToString(), await availableMeals.Content.ReadAsStringAsync());
        }
        using (var address = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/delivery-address"))
        {
            Assert.Equal(HttpStatusCode.OK, address.StatusCode);
            Assert.Contains(seed.CustomerAddress.AddressLine, await address.Content.ReadAsStringAsync());
        }

        using (var incomplete = await manager.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/complete", new { idempotencyKey = "family-day-complete" }))
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        using (var selectRestaurant = await manager.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/restaurant", new { restaurantId = seed.RestaurantA.Id }))
            Assert.Equal(HttpStatusCode.OK, selectRestaurant.StatusCode);
        using (var slotMeal = await manager.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/meals/slot", new { mealId = seed.MealA.Id, slot = "lunch" }))
            Assert.Equal(HttpStatusCode.OK, slotMeal.StatusCode);
        using (var completed = await manager.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/complete", new { idempotencyKey = "family-day-complete" }))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            var body = await completed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("selectedMeals").GetArrayLength());
            Assert.Equal(seed.MealA.Id, body.GetProperty("selectedMeals")[0].GetProperty("mealId").GetGuid());
        }
        using (var confirmed = await manager.PostAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/confirm", null))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using (var reload = await manager.GetAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}"))
        {
            Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
            var body = await reload.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("selectedMeals").GetArrayLength());
            Assert.Equal(seed.MealA.Id, body.GetProperty("selectedMeals")[0].GetProperty("mealId").GetGuid());
            Assert.Equal("CustomerSelected", body.GetProperty("status").GetString());
        }
        using (var replay = await manager.PostAsJsonAsync($"/api/v1/customer/subscriptions/{subscription.Id}/days/{date}/complete", new { idempotencyKey = "family-day-complete" }))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("selectedMeals").GetArrayLength());
            Assert.Equal(seed.MealA.Id, body.GetProperty("selectedMeals")[0].GetProperty("mealId").GetGuid());
        }
        await using (var verify = fixture.CreateDbContext())
        {
            var savedDay = await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == subscription.Id && x.ServiceDate == serviceDate);
            Assert.Equal(SubscriptionDayStatus.CustomerSelected, savedDay.Status);
            Assert.Single(await verify.DailyMealSelections.Where(x => x.SubscriptionDayId == savedDay.Id).ToListAsync());
            Assert.Single(await verify.DailyRestaurantSelections.Where(x => x.SubscriptionDayId == savedDay.Id).ToListAsync());
            Assert.False(await verify.RestaurantDailyOrders.AnyAsync(x => x.SubscriptionDayId == savedDay.Id));
        }
    }

    private static Task<HttpResponseMessage> LocalizedGet(HttpClient client, string route, string language)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("Accept-Language", language);
        return client.SendAsync(request);
    }
}
