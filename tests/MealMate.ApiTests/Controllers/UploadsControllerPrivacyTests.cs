using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
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
/// delivery staff), and legacy keys without a metadata record remain readable to
/// authenticated callers only (documented gap until a backfill).
/// </summary>
public class UploadsControllerPrivacyTests
{
    private const string ProofKey = "uploads/drivers/delivery/proof-123.jpg";

    private static (UploadsController Controller, IFileStorage Storage, AppDbContext Db) Build(
        Guid? callerUserId = null, string[]? roles = null)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("jpeg-bytes")));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton<IAppDbContext>(db);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (callerUserId.HasValue)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, callerUserId.Value.ToString()) };
            foreach (var role in roles ?? Array.Empty<string>())
            {
                claims.Add(new Claim(ClaimsIdentity.DefaultRoleClaimType, role));
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        var controller = new UploadsController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, storage, db);
    }

    [Fact]
    public async Task Read_DeliveryProof_Anonymous_Is_Unauthorized()
    {
        var (controller, storage, db) = Build();

        var result = await controller.Read(ProofKey, storage, db, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Read_DeliveryProof_RecordOwnedBySomeoneElse_Is_Forbidden()
    {
        var (controller, storage, db) = Build(callerUserId: Guid.NewGuid());
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = ProofKey,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(ProofKey, storage, db, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Read_DeliveryProof_OwnUploader_Is_Allowed()
    {
        var uploaderId = Guid.NewGuid();
        var (controller, storage, db) = Build(callerUserId: uploaderId);
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = ProofKey,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            UploadedByUserId = uploaderId,
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(ProofKey, storage, db, CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>();
    }

    [Fact]
    public async Task Read_DeliveryProof_DeliveryStaff_Is_Allowed_Even_For_Foreign_Upload()
    {
        var (controller, storage, db) = Build(
            callerUserId: Guid.NewGuid(),
            roles: new[] { AppRoles.DeliveryManager });
        db.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = ProofKey,
            FileName = "proof.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            UploadedByUserId = Guid.NewGuid(),
            UploadedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Read(ProofKey, storage, db, CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>();
    }

    [Fact]
    public async Task Read_DeliveryProof_Legacy_Key_Without_Record_Is_Available_To_Authenticated_Caller()
    {
        var (controller, storage, db) = Build(callerUserId: Guid.NewGuid());

        var result = await controller.Read(ProofKey, storage, db, CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>();
    }

    [Fact]
    public async Task Read_Public_Meal_Image_Stayed_Anonymous()
    {
        var (controller, storage, db) = Build(callerUserId: null);

        var result = await controller.Read(
            "uploads/restaurant/meals/meal-1.jpg", storage, db, CancellationToken.None);

        result.Should().BeOfType<FileStreamResult>();
    }
}
