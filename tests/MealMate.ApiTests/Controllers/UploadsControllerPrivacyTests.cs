using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Privacy rules for reading delivery-proof files under uploads/drivers/delivery/:
/// anonymous access is rejected, upload-record ownership applies (uploader or
/// delivery staff), and legacy keys without a metadata record are rejected fail-closed.
/// </summary>
public class UploadsControllerPrivacyTests
{
    private const string ProofKey = "uploads/drivers/delivery/proof-123.jpg";
    private static readonly byte[] ValidJpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46];

    private static (UploadsController Controller, IFileStorage Storage, AppDbContext Db, SupportEvidenceScanService EvidenceScans, DriverEvidenceAccessService StaffAccess) Build(
        Guid? callerUserId = null, string[]? roles = null, UserType userType = UserType.Driver, Guid? restaurantId = null)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(ValidJpeg));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton<IAppDbContext>(db);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var currentUser = Substitute.For<ICurrentUser>();
        if (callerUserId.HasValue)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, callerUserId.Value.ToString()),
                new(AppClaimTypes.UserId, callerUserId.Value.ToString()),
            };
            foreach (var role in roles ?? Array.Empty<string>())
            {
                claims.Add(new Claim(ClaimsIdentity.DefaultRoleClaimType, role));
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            currentUser.IsAuthenticated.Returns(true);
            currentUser.UserId.Returns(callerUserId.Value);
            currentUser.UserType.Returns(userType);
            currentUser.RestaurantId.Returns(restaurantId);
        }
        else
        {
            currentUser.IsAuthenticated.Returns(false);
            currentUser.UserId.Returns((Guid?)null);
        }

        var scanner = Substitute.For<IEvidenceMalwareScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new EvidenceScanResult(EvidenceScanVerdict.Clean, "clamd", FailureCode: "clean"));
        var receipts = Substitute.For<ISupportEvidenceScanReceiptStore>();
        receipts.GetOrScanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Func<CancellationToken, Task<EvidenceScanResult>>>(), Arg.Any<CancellationToken>())
            .Returns(new EvidenceScanResult(EvidenceScanVerdict.Clean, "clamd", FailureCode: "clean"));
        var evidenceScans = new SupportEvidenceScanService(storage, scanner, receipts);
        var staffAccess = new DriverEvidenceAccessService(db, currentUser);

        var controller = new UploadsController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, storage, db, evidenceScans, staffAccess);
    }

    [Theory]
    [InlineData(ProofKey)]
    [InlineData("uploads/drivers/issue-evidence/evidence-123.jpg")]
    public async Task Read_DeliveryProof_Anonymous_Is_Unauthorized(string key)
    {
        var (controller, storage, db, evidenceScans, staffAccess) = Build();
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = key,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = ValidJpeg.Length,
            Scope = "drivers",
            Folder = key.Contains("delivery") ? "delivery" : "issue-evidence",
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(key, storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Theory]
    [InlineData(ProofKey)]
    [InlineData("uploads/drivers/issue-evidence/evidence-123.jpg")]
    public async Task Read_DeliveryProof_RecordOwnedBySomeoneElse_Is_Forbidden(string key)
    {
        var (controller, storage, db, evidenceScans, staffAccess) = Build(callerUserId: Guid.NewGuid());
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = key,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = ValidJpeg.Length,
            Scope = "drivers",
            Folder = key.Contains("delivery") ? "delivery" : "issue-evidence",
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(key, storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(ProofKey)]
    [InlineData("uploads/drivers/issue-evidence/evidence-123.jpg")]
    public async Task Read_DeliveryProof_OwnUploader_Is_Allowed(string key)
    {
        var uploaderId = Guid.NewGuid();
        var (controller, storage, db, evidenceScans, staffAccess) = Build(callerUserId: uploaderId);
        db.Users.Add(new ApplicationUser
        {
            Id = uploaderId,
            AccountStatus = AccountStatus.Active,
            UserName = "driver",
            Email = "driver@local"
        });
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = key,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = ValidJpeg.Length,
            Scope = "drivers",
            Folder = key.Contains("delivery") ? "delivery" : "issue-evidence",
            UploadedByUserId = uploaderId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(key, storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<FileContentResult>();
    }

    [Theory]
    [InlineData(ProofKey)]
    [InlineData("uploads/drivers/issue-evidence/evidence-123.jpg")]
    public async Task Read_DeliveryProof_DeliveryStaff_Is_Allowed_Even_For_Foreign_Upload(string key)
    {
        var staffId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var (controller, storage, db, evidenceScans, staffAccess) = Build(
            callerUserId: staffId,
            roles: new[] { AppRoles.DeliveryManager },
            userType: UserType.DeliveryManager,
            restaurantId: restaurantId);

        db.Users.Add(new ApplicationUser
        {
            Id = staffId,
            AccountStatus = AccountStatus.Active,
            UserType = UserType.DeliveryManager,
            RestaurantId = restaurantId,
            UserName = "staff",
            Email = "staff@local"
        });

        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = key,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = ValidJpeg.Length,
            Scope = "drivers",
            Folder = key.Contains("delivery") ? "delivery" : "issue-evidence",
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });

        var complaint = new Complaint
        {
            RestaurantId = restaurantId,
            Category = "delivery",
            Title = "Complaint",
            Description = "Desc",
            Status = ComplaintStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Complaints.Add(complaint);
        db.Set<ComplaintEvidencePhoto>().Add(new ComplaintEvidencePhoto
        {
            StoragePath = key,
            Complaint = complaint,
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var result = await controller.Read(key, storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<FileContentResult>();
    }

    [Fact]
    public async Task Read_DeliveryProof_Legacy_Key_Without_Record_Is_NotFound()
    {
        var (controller, storage, db, evidenceScans, staffAccess) = Build(callerUserId: Guid.NewGuid());

        var result = await controller.Read(ProofKey, storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Read_Public_Meal_Image_Stayed_Anonymous()
    {
        var (controller, storage, db, evidenceScans, staffAccess) = Build(callerUserId: null);

        var result = await controller.Read(
            "uploads/restaurant/meals/meal-1.jpg", storage, db, evidenceScans, staffAccess, CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>();
    }
}
