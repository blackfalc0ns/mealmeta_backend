using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.Support;

public sealed class SupportCaseAttachmentHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    private readonly SupportEvidenceTestScanner _scanner;
    public SupportCaseAttachmentHttpTests(OrderLifecycleEndToEndFixture fixture)
    {
        _fixture = fixture;
        _scanner = fixture.EnableSupportEvidenceTestScanner();
        _scanner.Reset();
    }

    [Fact]
    public async Task AttachmentUploadIsEncryptedPrivateAndReadableOnlyThroughVisibleMessage()
    {
        var customerId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var uploaded = await UploadPdfAsync(customer);
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var attachment = await uploaded.Content.ReadFromJsonAsync<SupportCaseService.SupportCaseAttachmentUploadDto>();
        attachment.Should().NotBeNull();
        attachment!.StorageKey.Should().StartWith("documents/support/evidence/");
        (await uploaded.Content.ReadAsStringAsync()).Should().NotContain("readUrl");
        var encryptedPath = Path.Combine(_fixture.SupportStorageRoot, attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        var encryptedBytes = await File.ReadAllBytesAsync(encryptedPath);
        encryptedBytes[..4].Should().Equal("MMDE"u8.ToArray());
        System.Text.Encoding.Latin1.GetString(encryptedBytes).Should().NotContain("%PDF-1.7");
        (await customer.GetAsync($"/api/v1/uploads/content?key={Uri.EscapeDataString(attachment.StorageKey)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var created = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Evidence", "Private evidence test", "attachment-case"));
        var supportCase = await created.Content.ReadFromJsonAsync<SupportCaseDto>();
        var messageResponse = await customer.PostAsJsonAsync($"/api/v1/support/cases/{supportCase!.Id}/messages",
            new AddSupportMessageRequest("See attached document", supportCase.Version, "attachment-message", AttachmentKeys: [attachment.StorageKey]));
        messageResponse.StatusCode.Should().Be(HttpStatusCode.OK, await messageResponse.Content.ReadAsStringAsync());
        var messageCase = await messageResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        var metadata = messageCase!.Messages.Single(x => x.Body == "See attached document").Attachments.Single();
        metadata.FileName.Should().Be("evidence.pdf");
        metadata.ContentType.Should().Be("application/pdf");
        metadata.ReadPath.Should().NotContain(attachment.StorageKey);
        var content = await customer.GetAsync(metadata.ReadPath);
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Headers.CacheControl!.NoStore.Should().BeTrue();
        content.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal("%PDF-1.7\nprivate support evidence\n"u8.ToArray());

        using var other = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        (await other.GetAsync(metadata.ReadPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AttachmentRejectsMismatchedSignatureAndForeignUploaderKey()
    {
        var ownerId = Guid.NewGuid();
        using var owner = _fixture.CreateClientForUser(new TokenUser(ownerId, UserType.Customer, ["Customer"], []));
        using var mismatch = new MultipartFormDataContent();
        var bad = new ByteArrayContent("not a PDF"u8.ToArray());
        bad.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        mismatch.Add(bad, "file", "wrong.pdf");
        (await owner.PostAsync("/api/v1/support/attachments", mismatch)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var uploaded = await UploadPdfAsync(owner);
        var attachment = await uploaded.Content.ReadFromJsonAsync<SupportCaseService.SupportCaseAttachmentUploadDto>();
        using var other = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var created = await other.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Foreign file", "Not yours", "foreign-attachment-case"));
        var supportCase = await created.Content.ReadFromJsonAsync<SupportCaseDto>();
        var response = await other.PostAsJsonAsync($"/api/v1/support/cases/{supportCase!.Id}/messages",
            new AddSupportMessageRequest("Foreign attachment", supportCase.Version, "foreign-attachment-message", AttachmentKeys: [attachment!.StorageKey]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(SupportCaseProblemCodes.AttachmentUnsupported);
    }

    [Fact]
    public async Task RevokedInternalPermissionHidesMessageAndBlocksItsAttachmentDespiteOldToken()
    {
        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var created = await customer.PostAsJsonAsync("/api/v1/support/cases", new CreateSupportCaseRequest(
            SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest, "account", "Internal evidence", "Requester context", "internal-attachment-case"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var supportCase = await created.Content.ReadFromJsonAsync<SupportCaseDto>();

        using var support = _fixture.CreateClientForUser(new TokenUser(adminId, UserType.Admin, ["SupportAdmin"],
            ["support:view", "support.internal"], null, [scopeId]));
        using var uploadForm = new MultipartFormDataContent();
        var pdf = new ByteArrayContent("%PDF-1.7\nprivate support evidence\n"u8.ToArray());
        pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadForm.Add(pdf, "file", "internal.pdf");
        var uploaded = await support.PostAsync("/api/v1/support/attachments", uploadForm);
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var attachment = await uploaded.Content.ReadFromJsonAsync<SupportCaseService.SupportCaseAttachmentUploadDto>();

        Guid messageId;
        await using (var db = _fixture.CreateDbContext())
        {
            db.AccessScopes.Add(new AccessScope { Id = scopeId, Name = "Support global", ScopeType = AccessScopeType.Global });
            db.UserScopes.Add(new UserScope { UserId = adminId, AccessScopeId = scopeId });
            await db.SaveChangesAsync();
            await SupportTestIdentity.SeedAsync(db, new TokenUser(adminId, UserType.Admin, ["SupportAdmin"],
                ["support:view", "support.internal"], null, [scopeId]));
            var message = new SupportCaseMessage
            {
                SupportCaseId = supportCase!.Id, AuthorUserId = adminId, Visibility = SupportCaseMessageVisibility.Internal,
                Body = "Private staff note", AttachmentKeys = [attachment!.StorageKey], CreatedAtUtc = DateTime.UtcNow,
            };
            db.SupportCaseMessages.Add(message);
            await db.SaveChangesAsync();
            messageId = message.Id;

            var roleId = await db.UserRoles.Where(x => x.UserId == adminId).Select(x => x.RoleId).SingleAsync();
            var permissionId = await db.Permissions.Where(x => x.Code == "support.internal").Select(x => x.Id).SingleAsync();
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == roleId && x.PermissionId == permissionId));
            await db.SaveChangesAsync();
        }

        var detail = await support.GetFromJsonAsync<SupportCaseDto>($"/api/v1/support/cases/{supportCase!.Id}");
        detail.Should().NotBeNull();
        detail!.Messages.Should().NotContain(x => x.Body == "Private staff note");
        (await support.GetAsync($"/api/v1/support/cases/{supportCase.Id}/messages/{messageId}/attachments/0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(EvidenceScanVerdict.Rejected, HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(EvidenceScanVerdict.Unavailable, HttpStatusCode.UnprocessableEntity, "Unavailable")]
    public async Task ScannerVerdictPersistsReceiptAndNeverServesRejectedOrUnavailableEvidence(
        EvidenceScanVerdict verdict, HttpStatusCode expectedStatus, string expectedReceipt)
    {
        _scanner.Reset();
        _scanner.SetVerdict(verdict);
        var uploaderId = Guid.NewGuid();
        using var uploader = _fixture.CreateClientForUser(new TokenUser(uploaderId, UserType.Customer, ["Customer"], []));

        var upload = await UploadPdfAsync(uploader);
        upload.StatusCode.Should().Be(expectedStatus, await upload.Content.ReadAsStringAsync());
        (await upload.Content.ReadAsStringAsync()).Should().NotContain("%PDF-1.7");

        Guid uploadRecordId;
        string storageKey;
        var caseId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext())
        {
            var record = await db.UploadFileRecords.AsNoTracking().SingleAsync(x => x.UploadedByUserId == uploaderId
                && x.Scope == "support" && x.Folder == "evidence");
            uploadRecordId = record.Id;
            storageKey = record.StorageKey;
            var receipt = await db.SupportEvidenceScanReceipts.AsNoTracking().SingleAsync(x => x.UploadFileRecordId == record.Id);
            receipt.Verdict.Should().Be(expectedReceipt);
            receipt.Provider.Should().Be("test-only");

            var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest,
                SupportCaseOwnerTeam.PlatformSupport, "account", "Scanner privacy", "Test-only evidence reference.", uploaderId, DateTime.UtcNow);
            caseId = supportCase.Id;
            db.SupportCases.Add(supportCase);
            db.SupportCaseMessages.Add(new SupportCaseMessage { Id = messageId, SupportCaseId = caseId, AuthorUserId = uploaderId,
                Visibility = SupportCaseMessageVisibility.Participant, Body = "Attached rejected evidence", AttachmentKeys = [storageKey], CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        (await uploader.GetAsync($"/api/v1/uploads/content?key={Uri.EscapeDataString(storageKey)}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await uploader.GetAsync($"/api/v1/support/cases/{caseId:D}/messages/{messageId:D}/attachments/0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var db = _fixture.CreateDbContext())
            (await db.SupportEvidenceScanReceipts.CountAsync(x => x.UploadFileRecordId == uploadRecordId)).Should().BeGreaterThan(0);
        _scanner.Calls.Should().BeGreaterThan(0);
        _scanner.Reset();
    }

    private static async Task<HttpResponseMessage> UploadPdfAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.7\nprivate support evidence\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "evidence.pdf");
        return await client.PostAsync("/api/v1/support/attachments", form);
    }
}
