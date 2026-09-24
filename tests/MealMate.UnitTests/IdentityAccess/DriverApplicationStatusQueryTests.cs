using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverApplicationStatusQueryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public DriverApplicationStatusQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _messages = new FakeLocalizedMessageProvider();
    }

    private sealed class FakeLocalizedMessageProvider : ILocalizedMessageProvider
    {
        public CultureInfo CurrentCulture => new("ar");

        public string GetMessage(string code, string? fallback = null, params object[] arguments)
        {
            return fallback ?? code;
        }
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_WhenPendingRestaurant_ShouldReturnStage1WithRestaurantStatus()
    {
        // Arrange
        var restId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = restId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96591112233",
            RestaurantApprovalStatus = VerificationStatus.Submitted,
            AdminApprovalStatus = VerificationStatus.Submitted,
            Status = VerificationStatus.Submitted
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new GetDriverApplicationStatusQueryHandler(_db, _messages);

        // Act
        var result = await handler.Handle(
            new GetDriverApplicationStatusQuery(Phone: null, RegistrationId: reg.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Stage.Should().Be(1);
        dto.RestaurantApprovalStatus.Should().Be("Submitted");
        dto.AdminApprovalStatus.Should().Be("Submitted");
        dto.CanResubmit.Should().BeFalse();
        dto.IsApproved.Should().BeFalse();
        dto.RequestedByRole.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenRestaurantApprovedPendingAdmin_ShouldReturnStage1WithUpdatedSubtitle()
    {
        // Arrange
        var restId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = restId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96591112233",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.Submitted,
            Status = VerificationStatus.UnderReview
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new GetDriverApplicationStatusQueryHandler(_db, _messages);

        // Act
        var result = await handler.Handle(
            new GetDriverApplicationStatusQuery(Phone: null, RegistrationId: reg.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Stage.Should().Be(1);
        dto.RestaurantApprovalStatus.Should().Be("Approved");
        dto.AdminApprovalStatus.Should().Be("Submitted");
        dto.Subtitle.Should().Contain("الإدارة");
    }

    [Fact]
    public async Task Handle_WhenNeedsChangesByAdmin_ShouldReturnStage2AndIdentifyAdminRole()
    {
        // Arrange
        var restId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = restId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96591112233",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.NeedsChanges,
            Status = VerificationStatus.NeedsChanges,
            ChangeRequestNotes = "الرجاء إعادة رفع صورة رخصة القيادة بشكل أوضح"
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new GetDriverApplicationStatusQueryHandler(_db, _messages);

        // Act
        var result = await handler.Handle(
            new GetDriverApplicationStatusQuery(Phone: null, RegistrationId: reg.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Stage.Should().Be(2);
        dto.CanResubmit.Should().BeTrue();
        dto.ChangeRequestNotes.Should().Be("الرجاء إعادة رفع صورة رخصة القيادة بشكل أوضح");
        dto.RequestedByRole.Should().Be("Admin");
    }

    [Fact]
    public async Task Handle_WhenNeedsChangesByRestaurant_ShouldReturnStage2AndIdentifyRestaurantRole()
    {
        // Arrange
        var restId = Guid.NewGuid();
        var reg = new DriverRegistration
        {
            RestaurantId = restId,
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96591112233",
            RestaurantApprovalStatus = VerificationStatus.NeedsChanges,
            AdminApprovalStatus = VerificationStatus.Submitted,
            Status = VerificationStatus.NeedsChanges,
            ChangeRequestNotes = "يرجى تعديل نوع المركبة"
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new GetDriverApplicationStatusQueryHandler(_db, _messages);

        // Act
        var result = await handler.Handle(
            new GetDriverApplicationStatusQuery(Phone: null, RegistrationId: reg.Id),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Stage.Should().Be(2);
        dto.CanResubmit.Should().BeTrue();
        dto.RequestedByRole.Should().Be("Restaurant");
    }

    [Fact]
    public async Task Handle_WhenApproved_ShouldReturnStage4AndIsApprovedTrue()
    {
        // Arrange
        var reg = new DriverRegistration
        {
            FullNameAr = "محمد أحمد",
            FullNameEn = "Mohamed Ahmed",
            Phone = "+96591112233",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.Approved,
            Status = VerificationStatus.Approved
        };
        _db.DriverRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        var handler = new GetDriverApplicationStatusQueryHandler(_db, _messages);

        // Act
        var result = await handler.Handle(
            new GetDriverApplicationStatusQuery(Phone: "+96591112233", RegistrationId: null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Stage.Should().Be(4);
        dto.IsApproved.Should().BeTrue();
    }
}
