using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyGroupPersistenceTests
{
    [Fact]
    public async Task CanPersistGroupWithManagerMember()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Draft,
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 24,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.NotStarted,
        };
        group.Members.Add(member);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var loaded = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        loaded.Name.Should().Be("عائلة أحمد");
        loaded.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager);
    }
}