using MealMate.Domain.ComplaintsSupport;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class SupportCaseDomainTests
{
    [Fact]
    public void NewCaseCannotBeClosedWithoutResolution()
    {
        var supportCase = CreateCase();

        var transitioned = supportCase.TryTransitionTo(SupportCaseStatus.Closed);

        Assert.False(transitioned);
        Assert.Equal(SupportCaseStatus.New, supportCase.Status);
    }

    [Fact]
    public void ResolvedCaseCanBeReopenedWithoutChangingItsOwner()
    {
        var supportCase = CreateCase();
        supportCase.Assign(Guid.NewGuid(), supportCase.RequesterUserId, DateTime.UtcNow);
        supportCase.TryTransitionTo(SupportCaseStatus.Acknowledged);
        supportCase.TryTransitionTo(SupportCaseStatus.InProgress);
        supportCase.TryTransitionTo(SupportCaseStatus.Resolved);
        var ownerTeam = supportCase.OwnerTeam;
        var assignedTo = supportCase.AssignedToUserId;

        var transitioned = supportCase.TryTransitionTo(SupportCaseStatus.InProgress);

        Assert.True(transitioned);
        Assert.Equal(SupportCaseStatus.InProgress, supportCase.Status);
        Assert.Equal(ownerTeam, supportCase.OwnerTeam);
        Assert.Equal(assignedTo, supportCase.AssignedToUserId);
    }

    [Fact]
    public void PartyTaskCompletionDoesNotTransferCaseOwnership()
    {
        var supportCase = CreateCase();
        var task = new SupportCaseTask
        {
            SupportCaseId = supportCase.Id,
            RecipientType = SupportCaseTaskRecipientType.Restaurant,
        };

        task.Complete(Guid.NewGuid(), "Evidence submitted", DateTime.UtcNow);

        Assert.Equal(SupportCaseTaskStatus.Completed, task.Status);
        Assert.Equal(SupportCaseOwnerTeam.PlatformSupport, supportCase.OwnerTeam);
    }

    [Fact]
    public void WaitingOnRequesterCannotBeClosedWithoutAnExplicitResolution()
    {
        var supportCase = CreateCase();
        supportCase.TryTransitionTo(SupportCaseStatus.Acknowledged);
        supportCase.TryTransitionTo(SupportCaseStatus.WaitingOnRequester);

        Assert.False(supportCase.TryTransitionTo(SupportCaseStatus.Closed));
    }

    [Fact]
    public void TaskRejectsCompletionWithoutPersistedActorOrUtcTime()
    {
        var task = new SupportCaseTask();

        Assert.False(task.Complete(Guid.Empty, "Result", DateTime.UtcNow));
        Assert.False(task.Complete(Guid.NewGuid(), "Result", DateTime.Now));
    }

    [Fact]
    public void TaskRejectsCompletionResultOverPersistenceLimit()
    {
        var task = new SupportCaseTask();

        Assert.False(task.Complete(Guid.NewGuid(), new string('x', 4001), DateTime.UtcNow));
    }

    [Fact]
    public void CaseFactoryRejectsUndefinedClassificationAndUnpairedLink()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportCase.Create(
            (SupportCaseChannel)999, SupportCaseKind.Complaint, SupportCaseOwnerTeam.PlatformSupport,
            "order", "Subject", "Body", Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => SupportCase.Create(
            SupportCaseChannel.Customer, SupportCaseKind.Complaint, SupportCaseOwnerTeam.PlatformSupport,
            "order", "Subject", "Body", Guid.NewGuid(), DateTime.UtcNow,
            relatedResourceType: "Order"));
    }
    private static SupportCase CreateCase() => SupportCase.Create(
        SupportCaseChannel.Customer,
        SupportCaseKind.Complaint,
        SupportCaseOwnerTeam.PlatformSupport,
        "order",
        "Missing item",
        "One item was missing from the delivery.",
        Guid.NewGuid(),
        DateTime.UtcNow);
}
