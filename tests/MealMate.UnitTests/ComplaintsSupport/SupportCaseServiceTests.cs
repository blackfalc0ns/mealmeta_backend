using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using NSubstitute;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class SupportCaseServiceTests
{
    [Fact]
    public async Task GenericCreateRejectsDriverOperationalReportsUntilOperationalAdapterIsUsed()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Driver);
        var db = Substitute.For<IAppDbContext>();
        var service = CreateService(db, currentUser);

        var result = await service.CreateAsync(new CreateSupportCaseRequest(
            SupportCaseChannel.Driver,
            SupportCaseKind.OperationalReport,
            "delivery",
            "Order issue",
            "Delivery complaint",
            "driver-report-1"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.OperationalRouteRequired);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MutationsWithoutAuthenticatedActorAreRejectedBeforePersistence()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.UserId.Returns((Guid?)null);
        var db = Substitute.For<IAppDbContext>();
        var service = CreateService(db, currentUser);

        var result = await service.CreateAsync(new CreateSupportCaseRequest(
            SupportCaseChannel.Customer,
            SupportCaseKind.Complaint,
            "order",
            "Missing item",
            "Something is missing",
            "anonymous-key"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(SupportCaseProblemCodes.AuthenticationRequired);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static SupportCaseService CreateService(IAppDbContext db, ICurrentUser currentUser)
    {
        var storage = Substitute.For<IFileStorage>();
        var scanner = Substitute.For<IEvidenceMalwareScanner>();
        var receipts = Substitute.For<ISupportEvidenceScanReceiptStore>();
        var evidenceScans = new MealMate.Application.Common.Storage.SupportEvidenceScanService(storage, scanner, receipts);
        return new(db, Substitute.For<IUnitOfWork>(), currentUser, TimeProvider.System,
            new InlineTransactionExecutor(), Substitute.For<IApplicationLockManager>(), storage, evidenceScans);
    }

    private sealed class InlineTransactionExecutor : IApplicationTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
