using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class AdminRestaurantReviewTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantChangeReviewService _service;

    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public AdminRestaurantReviewTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _currentUser = Substitute.For<ICurrentUser>();
        _audit = Substitute.For<IAuditLogger>();
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(new DateTime(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc));
        _notifier = Substitute.For<IRegistrationReviewNotifier>();

        _service = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);

        SeedData();
    }

    private void SeedData()
    {
        var reg = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            TradeNameAr = "مطعم شاورما الأصلي",
            TradeNameEn = "Original Shawarma",
            LegalNameAr = "شركة شاورما الأصلي",
            LegalNameEn = "Original Shawarma Co",
            ContactPhone = "96590000002",
            ContactEmail = "shawarma@example.com",
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = _restaurantId,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(reg, _registrationId);
        _db.RestaurantRegistrations.Add(reg);

        var profile = new RestaurantProfile
        {
            UserId = _ownerUserId,
            TradeNameAr = "مطعم شاورما الأصلي",
            TradeNameEn = "Original Shawarma",
            LegalNameAr = "شركة شاورما الأصلي",
            LegalNameEn = "Original Shawarma Co",
            ContactPhone = "96590000002",
            ContactEmail = "shawarma@example.com",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            ApprovedVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        typeof(BaseEntity).GetProperty("Id")!.SetValue(profile, _restaurantId);
        _db.RestaurantProfiles.Add(profile);

        _db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ListChangeRequests_WhenFilteredByCategoryAndStatus_ReturnsMatchingItems()
    {
        _currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);

        var req1 = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "تعديل 1" }),
            ChangedFieldsJson = JsonSerializer.Serialize(new[] { "TradeNameAr" }),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        var req2 = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Capacity,
            Status = VerificationStatus.Approved,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { MaxDailyOrders = 100 }),
            ChangedFieldsJson = JsonSerializer.Serialize(new[] { "MaxDailyOrders" }),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        _db.RestaurantSettingsChangeRequests.AddRange(req1, req2);
        await _db.SaveChangesAsync(CancellationToken.None);

        var query = new ListRestaurantChangeRequestsQuery(
            RestaurantId: _restaurantId,
            Category: RestaurantChangeCategory.Profile,
            Status: VerificationStatus.Submitted);

        var handler = new ListRestaurantChangeRequestsQueryHandler(_service);
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Category.Should().Be(RestaurantChangeCategory.Profile);
        result.Value.Items[0].Status.Should().Be(VerificationStatus.Submitted);
    }

    [Fact]
    public async Task GetChangeRequestDetail_ReturnsLiveAndProposedValuesWithMaskedSensitiveBank()
    {
        _currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);
        // Non-approver viewing detail should see masked IBAN
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        _currentUser.UserType.Returns(UserType.Restaurant);

        var req = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new
            {
                LegalNameAr = "شركة مقترحة",
                LegalNameEn = "Proposed Co",
                TradeNameAr = "مطعم مقترح",
                TradeNameEn = "Proposed Restaurant",
                ContactPhone = "96590000002",
                ContactEmail = "shawarma@example.com",
                Iban = "KW81CBKU0000000000001234567890",
                BankAccountNumber = "1234567890"
            }),
            ChangedFieldsJson = JsonSerializer.Serialize(new[] { "Iban", "LegalNameAr" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(req);
        await _db.SaveChangesAsync(CancellationToken.None);

        var query = new GetRestaurantChangeRequestDetailQuery(req.Id);
        var handler = new GetRestaurantChangeRequestDetailQueryHandler(_service);
        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProposedPayloadJson.Should().NotContain("1234567890");
        result.Value.ProposedPayloadJson.Should().Contain("****7890");
        result.Value.BaseVersion.Should().Be(1);
        result.Value.ChangedFields.Should().Contain("Iban");
    }

    [Fact]
    public async Task Decide_WhenNeedsChangesWithoutReason_ReturnsValidationError()
    {
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.UserType.Returns(UserType.Admin);

        var req = new RestaurantSettingsChangeRequest
        {
            RestaurantId = _restaurantId,
            RegistrationId = _registrationId,
            Category = RestaurantChangeCategory.Profile,
            Status = VerificationStatus.Submitted,
            BaseVersion = 1,
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(new { TradeNameAr = "مطعم مقترح" }),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantSettingsChangeRequests.Add(req);
        await _db.SaveChangesAsync(CancellationToken.None);

        var command = new DecideRestaurantChangeCommand(req.Id, "NeedsChanges", Reason: null, ExpectedVersion: 1);
        var result = await _service.DecideChangeAsync(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }
}
