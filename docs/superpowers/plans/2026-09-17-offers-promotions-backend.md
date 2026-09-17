# Offers & Promotions Module Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and integrate the production-grade Offers & Promotions backend module in MealMate strictly mirroring the Clean Architecture, CQRS, PostgreSQL persistence, and domain invariants established in the Collaborative Campaigns module.

**Architecture:** Bounded context `MealMate.Domain.Offers` paired with vertical slices in `MealMate.Application.Features.Offers`, EF Core persistence configurations in `MealMate.Persistence`, RESTful controllers in `MealMate.Api`, periodic worker jobs in `MealMate.Worker`, and comprehensive xUnit/FluentAssertions testing.

**Tech Stack:** .NET 8, C# 12, PostgreSQL via EF Core, MediatR, FluentValidation, xUnit, FluentAssertions.

**Spec / Architectural Reference:** [2026-09-15-campaign-management-v2.md](file:///f:/MealMate/backend/docs/superpowers/plans/2026-09-15-campaign-management-v2.md) & [CollaborativeCampaigns Architecture](file:///f:/MealMate/backend/src/MealMate.Domain/CollaborativeCampaigns/Campaign.cs)

## Global Constraints

- Root entities inherit `AuditableEntity : BaseEntity, IAuditableEntity, ISoftDeletable` with `Guid Id`.
- Append-only or financial/audit entities inherit `BaseEntity` (never soft-deleted).
- All monetary amounts use owned `Money` value objects with KWD precision `(18, 3)`.
- All percentage values use `Percentage` value objects with `.HasPercentageConversion()` (fraction `0.000` to `1.000`).
- Bilanguage string pairs: `NameAr`, `NameEn`, `DescriptionAr`, `DescriptionEn` directly on the entity.
- Controller endpoints inherit `ApiControllerBase` and return standardized `Result<T>` ProblemDetails envelopes.
- Time handling injected via `IDateTimeProvider` (never raw `DateTime.UtcNow`).
- Idempotency & outbox event patterns adhered to without creating redundant secondary infrastructure.

---

### Task 1: Offers Domain Enums, Exceptions, and Events

**Files:**
- Create: `src/MealMate.Domain/Offers/Enums/OfferEnums.cs`
- Create: `src/MealMate.Domain/Offers/Events/OfferDomainEvents.cs`
- Create: `src/MealMate.Domain/Offers/Exceptions/OfferExceptions.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferEnumsAndEventsTests.cs`

**Interfaces:**
- Produces:
  - `enum OfferStatus { Draft = 1, Scheduled = 2, Active = 3, Paused = 4, Stopped = 5, Ended = 6, Cancelled = 7 }`
  - `enum OfferType { StandardDiscount = 1, FirstSubscriptionPromotion = 2, RenewalReward = 3, TierUpgradeIncentive = 4 }`
  - `enum DiscountType { Percentage = 1, FixedAmount = 2, FreeDelivery = 3 }`
  - `enum OfferFundingMode { PlatformFull = 1, RestaurantFull = 2, Shared = 3 }`
  - `sealed record OfferStatusChangedDomainEvent(Guid OfferId, OfferStatus OldStatus, OfferStatus NewStatus, string ActorId, string Reason) : DomainEvent;`
  - `sealed record OfferRedeemedDomainEvent(Guid OfferId, Guid RedemptionId, Guid CustomerId, Guid SubscriptionId, decimal DiscountAmount, DateTime RedeemedAtUtc) : DomainEvent;`
  - `class InvalidOfferStateTransitionException(OfferStatus from, OfferStatus to) : DomainException;`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferEnumsAndEventsTests.cs
using FluentAssertions;
using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferEnumsAndEventsTests
{
    [Fact]
    public void OfferEnums_ContainExpectedValues()
    {
        ((int)OfferStatus.Draft).Should().Be(1);
        ((int)OfferStatus.Active).Should().Be(3);
        ((int)OfferStatus.Stopped).Should().Be(5);
        ((int)OfferStatus.Ended).Should().Be(6);
        ((int)OfferStatus.Cancelled).Should().Be(7);

        ((int)OfferFundingMode.Shared).Should().Be(3);
    }

    [Fact]
    public void OfferStatusChangedDomainEvent_SetsPropertiesCorrectly()
    {
        var offerId = Guid.NewGuid();
        var evt = new OfferStatusChangedDomainEvent(offerId, OfferStatus.Draft, OfferStatus.Scheduled, "admin-1", "Approved");

        evt.OfferId.Should().Be(offerId);
        evt.OldStatus.Should().Be(OfferStatus.Draft);
        evt.NewStatus.Should().Be(OfferStatus.Scheduled);
        evt.ActorId.Should().Be("admin-1");
        evt.Reason.Should().Be("Approved");
        evt.Should().BeAssignableTo<DomainEvent>();
    }

    [Fact]
    public void InvalidOfferStateTransitionException_HasDescriptiveMessage()
    {
        var ex = new InvalidOfferStateTransitionException(OfferStatus.Draft, OfferStatus.Active);
        ex.Message.Should().Contain("Draft").And.Contain("Active");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferEnumsAndEventsTests"`
Expected: Compilation failure (types not found).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Domain/Offers/Enums/OfferEnums.cs
namespace MealMate.Domain.Offers.Enums;

public enum OfferStatus
{
    Draft = 1,
    Scheduled = 2,
    Active = 3,
    Paused = 4,
    Stopped = 5,
    Ended = 6,
    Cancelled = 7
}

public enum OfferType
{
    StandardDiscount = 1,
    FirstSubscriptionPromotion = 2,
    RenewalReward = 3,
    TierUpgradeIncentive = 4
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
    FreeDelivery = 3
}

public enum OfferFundingMode
{
    PlatformFull = 1,
    RestaurantFull = 2,
    Shared = 3
}
```

```csharp
// src/MealMate.Domain/Offers/Events/OfferDomainEvents.cs
using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Domain.Offers.Events;

public sealed record OfferStatusChangedDomainEvent(
    Guid OfferId,
    OfferStatus OldStatus,
    OfferStatus NewStatus,
    string ActorId,
    string Reason
) : DomainEvent;

public sealed record OfferRedeemedDomainEvent(
    Guid OfferId,
    Guid RedemptionId,
    Guid CustomerId,
    Guid SubscriptionId,
    decimal DiscountAmount,
    DateTime RedeemedAtUtc
) : DomainEvent;
```

```csharp
// src/MealMate.Domain/Offers/Exceptions/OfferExceptions.cs
using MealMate.Domain.Exceptions;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Domain.Offers.Exceptions;

public sealed class InvalidOfferStateTransitionException : DomainException
{
    public InvalidOfferStateTransitionException(OfferStatus from, OfferStatus to)
        : base("Offer.InvalidStateTransition", $"Cannot transition offer from status '{from}' to '{to}'.")
    {
    }
}

public sealed class OfferEligibilityException : DomainException
{
    public OfferEligibilityException(string code, string message)
        : base(code, message)
    {
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferEnumsAndEventsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Domain/Offers/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): add domain enums, domain events, and domain exceptions"
```

---

### Task 2: Offer Entity and Finite State Machine Lifecycle

**Files:**
- Create: `src/MealMate.Domain/Offers/Offer.cs`
- Create: `src/MealMate.Domain/Offers/OfferProgram.cs`
- Create: `src/MealMate.Domain/Offers/OfferBundle.cs`
- Create: `src/MealMate.Domain/Offers/OfferRestaurant.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferLifecycleTests.cs`

**Interfaces:**
- Produces:
  - `class Offer : AuditableEntity`
  - `Result CanTransitionTo(OfferStatus targetStatus)`
  - `void TransitionTo(OfferStatus targetStatus, string actorId, string reason)`
  - Transition invariants:
    - `Draft` -> `Scheduled`, `Cancelled`
    - `Scheduled` -> `Active`, `Cancelled`
    - `Active` -> `Paused`, `Stopped`, `Ended`
    - `Paused` -> `Active`, `Stopped`, `Cancelled`
    - `Stopped` -> `Ended` (terminal for new redemptions)
    - `Ended`, `Cancelled` are strict terminal states

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferLifecycleTests.cs
using FluentAssertions;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferLifecycleTests
{
    [Fact]
    public void NewOffer_DefaultsToDraft()
    {
        var offer = new Offer();
        offer.Status.Should().Be(OfferStatus.Draft);
    }

    [Theory]
    [InlineData(OfferStatus.Scheduled, true)]
    [InlineData(OfferStatus.Cancelled, true)]
    [InlineData(OfferStatus.Active, false)]
    [InlineData(OfferStatus.Paused, false)]
    [InlineData(OfferStatus.Stopped, false)]
    [InlineData(OfferStatus.Ended, false)]
    public void DraftOffer_TransitionValidation(OfferStatus target, bool expectedSuccess)
    {
        var offer = new Offer();
        offer.CanTransitionTo(target).IsSuccess.Should().Be(expectedSuccess);
    }

    [Fact]
    public void TransitionTo_Throws_WhenInvalid()
    {
        var offer = new Offer();
        var act = () => offer.TransitionTo(OfferStatus.Active, "admin-user", "Direct launch");
        act.Should().Throw<InvalidOfferStateTransitionException>();
    }

    [Fact]
    public void ValidTransitions_UpdateStatus_AndEmitDomainEvent()
    {
        var offer = new Offer();
        offer.TransitionTo(OfferStatus.Scheduled, "admin-1", "Schedule launch");

        offer.Status.Should().Be(OfferStatus.Scheduled);
        offer.DomainEvents.Should().ContainSingle(e => e is OfferStatusChangedDomainEvent);

        var evt = (OfferStatusChangedDomainEvent)offer.DomainEvents.Single();
        evt.OldStatus.Should().Be(OfferStatus.Draft);
        evt.NewStatus.Should().Be(OfferStatus.Scheduled);
        evt.ActorId.Should().Be("admin-1");
        evt.Reason.Should().Be("Schedule launch");
    }

    [Fact]
    public void Stopped_IsTerminalForNewRedemptions_CanOnlyTransitionToEnded()
    {
        var offer = new Offer();
        offer.TransitionTo(OfferStatus.Scheduled, "admin", "Scheduled");
        offer.TransitionTo(OfferStatus.Active, "admin", "Live");
        offer.TransitionTo(OfferStatus.Stopped, "admin", "Halted");

        offer.CanTransitionTo(OfferStatus.Active).IsSuccess.Should().BeFalse();
        offer.CanTransitionTo(OfferStatus.Cancelled).IsSuccess.Should().BeFalse();
        offer.CanTransitionTo(OfferStatus.Ended).IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferLifecycleTests"`
Expected: FAIL (Offer class not defined).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Domain/Offers/Offer.cs
using MealMate.Domain.Common;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Offers.Events;
using MealMate.Domain.Offers.Exceptions;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Offers;

public sealed class Offer : AuditableEntity
{
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? BannerImageUrl { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Draft;
    public OfferType Type { get; set; } = OfferType.StandardDiscount;
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public OfferFundingMode FundingMode { get; set; } = OfferFundingMode.PlatformFull;

    public Percentage? DiscountRate { get; set; }
    public Money? DiscountAmount { get; set; }
    public Percentage? PlatformShareRate { get; set; }
    public Percentage? RestaurantShareRate { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }

    public Money? MinOrderAmount { get; set; }
    public int? MinSubscriptionDays { get; set; }
    public int? MaxSubscriptionDays { get; set; }

    public int? TotalUsageLimit { get; set; }
    public int CurrentUsageCount { get; set; }
    public int PerCustomerLimit { get; set; } = 1;

    public CustomerSegmentFilter CustomerSegment { get; set; } = CustomerSegmentFilter.All;
    public string? AllowedGovernoratesJson { get; set; }

    public Guid? CampaignId { get; set; }

    public ICollection<OfferProgram> EligiblePrograms { get; set; } = new List<OfferProgram>();
    public ICollection<OfferBundle> EligibleBundles { get; set; } = new List<OfferBundle>();
    public ICollection<OfferRestaurant> EligibleRestaurants { get; set; } = new List<OfferRestaurant>();
    public ICollection<OfferRedemption> Redemptions { get; set; } = new List<OfferRedemption>();
    public ICollection<OfferAuditLog> AuditLogs { get; set; } = new List<OfferAuditLog>();

    public Result CanTransitionTo(OfferStatus targetStatus)
    {
        if (Status == targetStatus)
        {
            return Result.Failure($"Offer is already in state '{targetStatus}'.");
        }

        var isAllowed = Status switch
        {
            OfferStatus.Draft => targetStatus is OfferStatus.Scheduled or OfferStatus.Cancelled,
            OfferStatus.Scheduled => targetStatus is OfferStatus.Active or OfferStatus.Cancelled,
            OfferStatus.Active => targetStatus is OfferStatus.Paused or OfferStatus.Stopped or OfferStatus.Ended,
            OfferStatus.Paused => targetStatus is OfferStatus.Active or OfferStatus.Stopped or OfferStatus.Cancelled,
            OfferStatus.Stopped => targetStatus is OfferStatus.Ended,
            OfferStatus.Ended => false,
            OfferStatus.Cancelled => false,
            _ => false
        };

        return isAllowed
            ? Result.Success()
            : Result.Failure($"Transition from '{Status}' to '{targetStatus}' is not allowed.");
    }

    public void TransitionTo(OfferStatus targetStatus, string actorId, string reason)
    {
        var check = CanTransitionTo(targetStatus);
        if (check.IsFailure)
        {
            throw new InvalidOfferStateTransitionException(Status, targetStatus);
        }

        var oldStatus = Status;
        Status = targetStatus;

        Raise(new OfferStatusChangedDomainEvent(Id, oldStatus, targetStatus, actorId, reason));
    }
}
```

```csharp
// src/MealMate.Domain/Offers/OfferProgram.cs
using MealMate.Domain.Common;

namespace MealMate.Domain.Offers;

public sealed class OfferProgram : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;
    public Guid ProgramId { get; set; }
}

// src/MealMate.Domain/Offers/OfferBundle.cs
public sealed class OfferBundle : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;
    public Guid BundleId { get; set; }
}

// src/MealMate.Domain/Offers/OfferRestaurant.cs
public sealed class OfferRestaurant : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;
    public Guid RestaurantId { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferLifecycleTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Domain/Offers/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): implement Offer domain aggregate root and state machine"
```

---

### Task 3: OfferRedemption and OfferAuditLog Append-Only Entities

**Files:**
- Create: `src/MealMate.Domain/Offers/OfferRedemption.cs`
- Create: `src/MealMate.Domain/Offers/OfferAuditLog.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferRedemptionAndAuditTests.cs`

**Interfaces:**
- Produces:
  - `class OfferRedemption : BaseEntity` (append-only, tracks monetary funding breakdown per redemption)
  - `class OfferAuditLog : BaseEntity` (immutable state audit)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferRedemptionAndAuditTests.cs
using FluentAssertions;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferRedemptionAndAuditTests
{
    [Fact]
    public void OfferRedemption_InitializesWithRequiredValues()
    {
        var offerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        var redemption = new OfferRedemption
        {
            OfferId = offerId,
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            DiscountAppliedAmount = Money.Kwd(5.000m),
            PlatformFundedAmount = Money.Kwd(3.000m),
            RestaurantFundedAmount = Money.Kwd(2.000m),
            RedeemedAtUtc = DateTime.UtcNow
        };

        redemption.OfferId.Should().Be(offerId);
        redemption.DiscountAppliedAmount.Amount.Should().Be(5.000m);
        redemption.PlatformFundedAmount.Amount.Should().Be(3.000m);
        redemption.RestaurantFundedAmount.Amount.Should().Be(2.000m);
    }

    [Fact]
    public void OfferAuditLog_StoresDiffsAndReasons()
    {
        var log = new OfferAuditLog
        {
            OfferId = Guid.NewGuid(),
            ActorId = "admin-1",
            Action = "OfferStatusChanged",
            BeforeStateJson = "{\"Status\":\"Draft\"}",
            AfterStateJson = "{\"Status\":\"Scheduled\"}",
            JustificationReason = "Approved by marketing head"
        };

        log.ActorId.Should().Be("admin-1");
        log.JustificationReason.Should().Be("Approved by marketing head");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferRedemptionAndAuditTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Domain/Offers/OfferRedemption.cs
using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Offers;

public sealed class OfferRedemption : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;

    public Guid CustomerId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid? PromoCodeId { get; set; }

    public Money DiscountAppliedAmount { get; set; } = default!;
    public Money PlatformFundedAmount { get; set; } = default!;
    public Money RestaurantFundedAmount { get; set; } = default!;

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
}
```

```csharp
// src/MealMate.Domain/Offers/OfferAuditLog.cs
using MealMate.Domain.Common;

namespace MealMate.Domain.Offers;

public sealed class OfferAuditLog : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;

    public string ActorId { get; set; } = default!;
    public string Action { get; set; } = default!;

    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public string? DetailsJson { get; set; }
    public string? JustificationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferRedemptionAndAuditTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Domain/Offers/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): add OfferRedemption and OfferAuditLog append-only entities"
```

---

### Task 4: EF Core Persistence Configurations & AppDbContext Mapping

**Files:**
- Create: `src/MealMate.Persistence/Configurations/Offers/OffersConfigurations.cs`
- Modify: `src/MealMate.Application/Common/Abstractions/IAppDbContext.cs`
- Modify: `src/MealMate.Persistence/AppDbContext.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferPersistenceModelTests.cs`

**Interfaces:**
- Produces:
  - EF Core mappings for `Offers`, `OfferPrograms`, `OfferBundles`, `OfferRestaurants`, `OfferRedemptions`, `OfferAuditLogs`.
  - Precision `(18, 3)` on money values, Percentage conversions on rates.
  - Foreign key relations with `DeleteBehavior.Restrict`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferPersistenceModelTests.cs
using FluentAssertions;
using MealMate.Domain.Offers;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferPersistenceModelTests
{
    [Fact]
    public void AppDbContext_RegistersOfferDbSets()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "OfferModelTestDb")
            .Options;

        using var context = new AppDbContext(options);

        context.Offers.Should().NotBeNull();
        context.OfferRedemptions.Should().NotBeNull();
        context.OfferAuditLogs.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferPersistenceModelTests"`
Expected: Compilation failure (`context.Offers` does not exist).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Persistence/Configurations/Offers/OffersConfigurations.cs
using MealMate.Domain.Offers;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.Offers;

public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.BannerImageUrl).HasMaxLength(1000);

        builder.Property(x => x.DiscountRate).HasPercentageConversion();
        builder.OwnsMoney(x => x.DiscountAmount, "DiscountAmount", required: false);

        builder.Property(x => x.PlatformShareRate).HasPercentageConversion();
        builder.Property(x => x.RestaurantShareRate).HasPercentageConversion();
        builder.OwnsMoney(x => x.MinOrderAmount, "MinOrderAmount", required: false);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => x.CampaignId);
    }
}

public sealed class OfferProgramConfiguration : IEntityTypeConfiguration<OfferProgram>
{
    public void Configure(EntityTypeBuilder<OfferProgram> builder)
    {
        builder.ToTable("OfferPrograms");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.ProgramId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligiblePrograms)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferBundleConfiguration : IEntityTypeConfiguration<OfferBundle>
{
    public void Configure(EntityTypeBuilder<OfferBundle> builder)
    {
        builder.ToTable("OfferBundles");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.BundleId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligibleBundles)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferRestaurantConfiguration : IEntityTypeConfiguration<OfferRestaurant>
{
    public void Configure(EntityTypeBuilder<OfferRestaurant> builder)
    {
        builder.ToTable("OfferRestaurants");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OfferId, x.RestaurantId }).IsUnique();

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.EligibleRestaurants)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferRedemptionConfiguration : IEntityTypeConfiguration<OfferRedemption>
{
    public void Configure(EntityTypeBuilder<OfferRedemption> builder)
    {
        builder.ToTable("OfferRedemptions");
        builder.HasKey(x => x.Id);

        builder.OwnsRequiredMoney(x => x.DiscountAppliedAmount, "DiscountAppliedAmount");
        builder.OwnsRequiredMoney(x => x.PlatformFundedAmount, "PlatformFundedAmount");
        builder.OwnsRequiredMoney(x => x.RestaurantFundedAmount, "RestaurantFundedAmount");

        builder.HasIndex(x => x.OfferId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.RedeemedAtUtc);

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.Redemptions)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OfferAuditLogConfiguration : IEntityTypeConfiguration<OfferAuditLog>
{
    public void Configure(EntityTypeBuilder<OfferAuditLog> builder)
    {
        builder.ToTable("OfferAuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.JustificationReason).HasMaxLength(1000);

        builder.HasIndex(x => x.OfferId);
        builder.HasIndex(x => x.CreatedAtUtc);

        builder.HasOne(x => x.Offer)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Add DbSets to `IAppDbContext` and `AppDbContext`:
```csharp
DbSet<Offer> Offers { get; }
DbSet<OfferProgram> OfferPrograms { get; }
DbSet<OfferBundle> OfferBundles { get; }
DbSet<OfferRestaurant> OfferRestaurants { get; }
DbSet<OfferRedemption> OfferRedemptions { get; }
DbSet<OfferAuditLog> OfferAuditLogs { get; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferPersistenceModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Persistence/ src/MealMate.Application/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): add ef core configurations and register offer sets in IAppDbContext"
```

---

### Task 5: Offer Eligibility Engine

**Files:**
- Create: `src/MealMate.Application/Features/Offers/Services/IOfferEligibilityEngine.cs`
- Create: `src/MealMate.Application/Features/Offers/Services/OfferEligibilityEngine.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferEligibilityEngineTests.cs`

**Interfaces:**
- Produces:
  - `sealed record OfferEligibilityContext(Guid CustomerId, int PriorSubscriptionCount, int CustomerOfferRedemptionCount, decimal CartAmount, int DurationDays, string? DeliveryGovernorate, Guid? ProgramId, Guid? BundleId, Guid? RestaurantId);`
  - `sealed record OfferEligibilityResult(bool IsEligible, decimal CalculatedDiscountAmount, IReadOnlyList<string> IneligibilityReasons);`
  - `IOfferEligibilityEngine.Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferEligibilityEngineTests.cs
using FluentAssertions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferEligibilityEngineTests
{
    private readonly OfferEligibilityEngine _engine = new();

    [Fact]
    public void Evaluate_ReturnsIneligible_WhenOfferNotActive()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        };

        var context = new OfferEligibilityContext(Guid.NewGuid(), 0, 0, 50m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, context, DateTime.UtcNow);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("not active"));
    }

    [Fact]
    public void Evaluate_EnforcesCustomerSegmentFilter()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            CustomerSegment = CustomerSegmentFilter.NewCustomersOnly,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.10m)
        };

        var existingCustomerCtx = new OfferEligibilityContext(Guid.NewGuid(), 2, 0, 50m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, existingCustomerCtx, DateTime.UtcNow);

        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().Contain(r => r.Contains("new customers only"));
    }

    [Fact]
    public void Evaluate_CalculatesCorrectDiscount_ForEligibleCustomer()
    {
        var offer = new Offer
        {
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            CustomerSegment = CustomerSegmentFilter.All,
            DiscountType = DiscountType.Percentage,
            DiscountRate = Percentage.FromFraction(0.15m),
            PerCustomerLimit = 1
        };

        var eligibleCtx = new OfferEligibilityContext(Guid.NewGuid(), 1, 0, 100m, 20, "Hawalli", null, null, null);
        var result = _engine.Evaluate(offer, eligibleCtx, DateTime.UtcNow);

        result.IsEligible.Should().BeTrue();
        result.CalculatedDiscountAmount.Should().Be(15.000m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferEligibilityEngineTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Application/Features/Offers/Services/IOfferEligibilityEngine.cs
using MealMate.Domain.Offers;

namespace MealMate.Application.Features.Offers.Services;

public sealed record OfferEligibilityContext(
    Guid CustomerId,
    int PriorSubscriptionCount,
    int CustomerOfferRedemptionCount,
    decimal CartAmount,
    int DurationDays,
    string? DeliveryGovernorate,
    Guid? ProgramId,
    Guid? BundleId,
    Guid? RestaurantId
);

public sealed record OfferEligibilityResult(
    bool IsEligible,
    decimal CalculatedDiscountAmount,
    IReadOnlyList<string> IneligibilityReasons
);

public interface IOfferEligibilityEngine
{
    OfferEligibilityResult Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc);
}
```

```csharp
// src/MealMate.Application/Features/Offers/Services/OfferEligibilityEngine.cs
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using System.Text.Json;

namespace MealMate.Application.Features.Offers.Services;

public sealed class OfferEligibilityEngine : IOfferEligibilityEngine
{
    public OfferEligibilityResult Evaluate(Offer offer, OfferEligibilityContext context, DateTime evaluationTimeUtc)
    {
        var reasons = new List<string>();

        // 1. Status Check
        if (offer.Status != OfferStatus.Active)
        {
            reasons.Add($"Offer is not active (status is '{offer.Status}').");
        }

        // 2. Dates Check
        if (evaluationTimeUtc < offer.StartsAtUtc || evaluationTimeUtc > offer.EndsAtUtc)
        {
            reasons.Add("Current time is outside the offer validity period.");
        }

        // 3. Customer Segment Check
        if (offer.CustomerSegment == CustomerSegmentFilter.NewCustomersOnly && context.PriorSubscriptionCount > 0)
        {
            reasons.Add("Offer is restricted to new customers only.");
        }
        else if (offer.CustomerSegment == CustomerSegmentFilter.ExistingCustomersOnly && context.PriorSubscriptionCount == 0)
        {
            reasons.Add("Offer is restricted to existing subscribers only.");
        }

        // 4. Per Customer Usage Limit
        if (context.CustomerOfferRedemptionCount >= offer.PerCustomerLimit)
        {
            reasons.Add($"Customer has reached redemption limit ({offer.PerCustomerLimit}) for this offer.");
        }

        // 5. Total Usage Limit
        if (offer.TotalUsageLimit.HasValue && offer.CurrentUsageCount >= offer.TotalUsageLimit.Value)
        {
            reasons.Add("Offer has reached its maximum total redemptions.");
        }

        // 6. Minimum Order Amount Check
        if (offer.MinOrderAmount != null && context.CartAmount < offer.MinOrderAmount.Amount)
        {
            reasons.Add($"Cart amount ({context.CartAmount} KWD) is below the minimum required ({offer.MinOrderAmount.Amount} KWD).");
        }

        // 7. Duration Bounds
        if (offer.MinSubscriptionDays.HasValue && context.DurationDays < offer.MinSubscriptionDays.Value)
        {
            reasons.Add($"Subscription duration ({context.DurationDays} days) is less than the required minimum ({offer.MinSubscriptionDays.Value} days).");
        }
        if (offer.MaxSubscriptionDays.HasValue && context.DurationDays > offer.MaxSubscriptionDays.Value)
        {
            reasons.Add($"Subscription duration ({context.DurationDays} days) exceeds maximum allowed ({offer.MaxSubscriptionDays.Value} days).");
        }

        // 8. Geographic Area Check
        if (!string.IsNullOrWhiteSpace(offer.AllowedGovernoratesJson) && !string.IsNullOrWhiteSpace(context.DeliveryGovernorate))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(offer.AllowedGovernoratesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(context.DeliveryGovernorate, StringComparer.OrdinalIgnoreCase))
                {
                    reasons.Add($"Delivery governorate '{context.DeliveryGovernorate}' is outside the offer scope.");
                }
            }
            catch
            {
                // Fallback safe ignore bad json
            }
        }

        if (reasons.Count > 0)
        {
            return new OfferEligibilityResult(false, 0m, reasons);
        }

        // Calculate Discount
        decimal discount = 0m;
        if (offer.DiscountType == DiscountType.Percentage && offer.DiscountRate != null)
        {
            discount = Math.Round(context.CartAmount * offer.DiscountRate.Value, 3, MidpointRounding.AwayFromZero);
        }
        else if (offer.DiscountType == DiscountType.FixedAmount && offer.DiscountAmount != null)
        {
            discount = Math.Min(context.CartAmount, Math.Round(offer.DiscountAmount.Amount, 3, MidpointRounding.AwayFromZero));
        }

        return new OfferEligibilityResult(true, discount, Array.Empty<string>());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferEligibilityEngineTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/Offers/Services/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): implement IOfferEligibilityEngine and calculation logic"
```

---

### Task 6: CQRS Commands: CreateDraft, UpdateDraft, and TransitionStatus

**Files:**
- Create: `src/MealMate.Application/Features/Offers/DTOs/OfferDtos.cs`
- Create: `src/MealMate.Application/Features/Offers/Commands/CreateOfferDraftCommand.cs`
- Create: `src/MealMate.Application/Features/Offers/Commands/UpdateOfferDraftCommand.cs`
- Create: `src/MealMate.Application/Features/Offers/Commands/TransitionOfferStatusCommand.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferCommandTests.cs`

**Interfaces:**
- Produces:
  - `CreateOfferDraftCommand(...) : ICommand<OfferDto>`
  - `UpdateOfferDraftCommand(...) : ICommand<OfferDto>`
  - `TransitionOfferStatusCommand(...) : ICommand<OfferDto>`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferCommandTests.cs
using FluentAssertions;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferCommandTests
{
    [Fact]
    public async Task CreateOfferDraftCommandHandler_CreatesValidDraft()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("OfferCommandsDb")
            .Options;

        using var db = new AppDbContext(options);
        var handler = new CreateOfferDraftCommandHandler(db);

        var command = new CreateOfferDraftCommand(
            NameAr: "عرض تجريبي",
            NameEn: "Promo Offer",
            Type: OfferType.StandardDiscount,
            DiscountType: DiscountType.Percentage,
            FundingMode: OfferFundingMode.PlatformFull,
            DiscountRate: 0.20m,
            DiscountAmount: null,
            PlatformShareRate: 0.20m,
            RestaurantShareRate: 0m,
            StartsAtUtc: DateTime.UtcNow.AddDays(1),
            EndsAtUtc: DateTime.UtcNow.AddDays(10));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Status.Should().Be(OfferStatus.Draft);
        result.Value.NameEn.Should().Be("Promo Offer");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferCommandTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Application/Features/Offers/DTOs/OfferDtos.cs
using MealMate.Domain.Enums;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Application.Features.Offers.DTOs;

public sealed record OfferDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? BannerImageUrl,
    OfferStatus Status,
    OfferType Type,
    DiscountType DiscountType,
    OfferFundingMode FundingMode,
    decimal? DiscountRate,
    decimal? DiscountAmountKwd,
    decimal? PlatformShareRate,
    decimal? RestaurantShareRate,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal? MinOrderAmountKwd,
    int? MinSubscriptionDays,
    int? MaxSubscriptionDays,
    int? TotalUsageLimit,
    int CurrentUsageCount,
    int PerCustomerLimit,
    CustomerSegmentFilter CustomerSegment,
    Guid? CampaignId
);
```

```csharp
// src/MealMate.Application/Features/Offers/Commands/CreateOfferDraftCommand.cs
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record CreateOfferDraftCommand(
    string NameAr,
    string NameEn,
    string? DescriptionAr = null,
    string? DescriptionEn = null,
    string? BannerImageUrl = null,
    OfferType Type = OfferType.StandardDiscount,
    DiscountType DiscountType = DiscountType.Percentage,
    OfferFundingMode FundingMode = OfferFundingMode.PlatformFull,
    decimal? DiscountRate = null,
    decimal? DiscountAmount = null,
    decimal? PlatformShareRate = null,
    decimal? RestaurantShareRate = null,
    DateTime StartsAtUtc = default,
    DateTime EndsAtUtc = default,
    decimal? MinOrderAmount = null,
    int? MinSubscriptionDays = null,
    int? MaxSubscriptionDays = null,
    int? TotalUsageLimit = null,
    int PerCustomerLimit = 1,
    CustomerSegmentFilter CustomerSegment = CustomerSegmentFilter.All,
    string? AllowedGovernoratesJson = null,
    Guid? CampaignId = null,
    List<Guid>? ProgramIds = null,
    List<Guid>? BundleIds = null,
    List<Guid>? RestaurantIds = null
) : ICommand<OfferDto>;

public sealed class CreateOfferDraftCommandValidator : AbstractValidator<CreateOfferDraftCommand>
{
    public CreateOfferDraftCommandValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAtUtc).LessThan(x => x.EndsAtUtc);
        RuleFor(x => x.PerCustomerLimit).GreaterThan(0);
    }
}

public sealed class CreateOfferDraftCommandHandler : ICommandHandler<CreateOfferDraftCommand, OfferDto>
{
    private readonly IAppDbContext _context;

    public CreateOfferDraftCommandHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(CreateOfferDraftCommand request, CancellationToken cancellationToken)
    {
        var offer = new Offer
        {
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            BannerImageUrl = request.BannerImageUrl?.Trim(),
            Status = OfferStatus.Draft,
            Type = request.Type,
            DiscountType = request.DiscountType,
            FundingMode = request.FundingMode,
            DiscountRate = request.DiscountRate.HasValue ? Percentage.FromFraction(request.DiscountRate.Value) : null,
            DiscountAmount = request.DiscountAmount.HasValue ? Money.Kwd(request.DiscountAmount.Value) : null,
            PlatformShareRate = request.PlatformShareRate.HasValue ? Percentage.FromFraction(request.PlatformShareRate.Value) : null,
            RestaurantShareRate = request.RestaurantShareRate.HasValue ? Percentage.FromFraction(request.RestaurantShareRate.Value) : null,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            MinOrderAmount = request.MinOrderAmount.HasValue ? Money.Kwd(request.MinOrderAmount.Value) : null,
            MinSubscriptionDays = request.MinSubscriptionDays,
            MaxSubscriptionDays = request.MaxSubscriptionDays,
            TotalUsageLimit = request.TotalUsageLimit,
            PerCustomerLimit = request.PerCustomerLimit,
            CustomerSegment = request.CustomerSegment,
            AllowedGovernoratesJson = request.AllowedGovernoratesJson,
            CampaignId = request.CampaignId
        };

        if (request.ProgramIds != null)
        {
            foreach (var pId in request.ProgramIds.Distinct())
            {
                offer.EligiblePrograms.Add(new OfferProgram { Offer = offer, ProgramId = pId });
            }
        }

        if (request.BundleIds != null)
        {
            foreach (var bId in request.BundleIds.Distinct())
            {
                offer.EligibleBundles.Add(new OfferBundle { Offer = offer, BundleId = bId });
            }
        }

        if (request.RestaurantIds != null)
        {
            foreach (var rId in request.RestaurantIds.Distinct())
            {
                offer.EligibleRestaurants.Add(new OfferRestaurant { Offer = offer, RestaurantId = rId });
            }
        }

        _context.Offers.Add(offer);
        await _context.SaveChangesAsync(cancellationToken);

        return MapOfferDto(offer);
    }

    public static OfferDto MapOfferDto(Offer o) => new(
        o.Id,
        o.NameAr,
        o.NameEn,
        o.DescriptionAr,
        o.DescriptionEn,
        o.BannerImageUrl,
        o.Status,
        o.Type,
        o.DiscountType,
        o.FundingMode,
        o.DiscountRate?.Value,
        o.DiscountAmount?.Amount,
        o.PlatformShareRate?.Value,
        o.RestaurantShareRate?.Value,
        o.StartsAtUtc,
        o.EndsAtUtc,
        o.MinOrderAmount?.Amount,
        o.MinSubscriptionDays,
        o.MaxSubscriptionDays,
        o.TotalUsageLimit,
        o.CurrentUsageCount,
        o.PerCustomerLimit,
        o.CustomerSegment,
        o.CampaignId
    );
}
```

Implement `UpdateOfferDraftCommand` and `TransitionOfferStatusCommand` in their corresponding files following identical patterns.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/Offers/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): implement CreateOfferDraft, UpdateOfferDraft, and TransitionStatus commands"
```

---

### Task 7: CQRS Queries and Reporting

**Files:**
- Create: `src/MealMate.Application/Features/Offers/Queries/GetOfferByIdQuery.cs`
- Create: `src/MealMate.Application/Features/Offers/Queries/GetOffersListQuery.cs`
- Create: `src/MealMate.Application/Features/Offers/Queries/GetCustomerAvailableOffersQuery.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferQueriesTests.cs`

**Interfaces:**
- Produces:
  - `GetOfferByIdQuery(Guid Id) : IQuery<OfferDto>`
  - `GetOffersListQuery(OfferStatus? Status, PageRequest Page) : IQuery<PagedResult<OfferDto>>`
  - `GetCustomerAvailableOffersQuery(Guid CustomerId, ...) : IQuery<IReadOnlyList<OfferDto>>`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferQueriesTests.cs
using FluentAssertions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferQueriesTests
{
    [Fact]
    public async Task GetOffersListQuery_ReturnsPagedResult()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("OfferQueriesDb")
            .Options;

        using var db = new AppDbContext(options);
        db.Offers.Add(new Offer
        {
            NameAr = "عرض 1",
            NameEn = "Offer 1",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        });
        await db.SaveChangesAsync();

        var handler = new GetOffersListQueryHandler(db);
        var result = await handler.Handle(new GetOffersListQuery(OfferStatus.Active, new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferQueriesTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Implement `GetOfferByIdQueryHandler`, `GetOffersListQueryHandler`, and `GetCustomerAvailableOffersQueryHandler`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferQueriesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/Offers/Queries/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): implement CQRS queries for offers and customer eligibility"
```

---

### Task 8: API Controllers (Admin, Customer, Restaurant)

**Files:**
- Create: `src/MealMate.Api/Controllers/Admin/AdminOffersController.cs`
- Create: `src/MealMate.Api/Controllers/Customer/CustomerOffersController.cs`
- Create: `src/MealMate.Api/Controllers/Restaurant/RestaurantOffersController.cs`
- Modify: `src/MealMate.Application/Common/Security/PermissionCodes.cs` (add `OffersManage = "offers:manage"`)
- Test: `tests/MealMate.UnitTests/Offers/OffersControllerTests.cs`

**Interfaces:**
- Produces:
  - `AdminOffersController` under `api/v1/admin/offers`
  - `CustomerOffersController` under `api/v1/customer/offers`
  - `RestaurantOffersController` under `api/v1/restaurant/offers`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OffersControllerTests.cs
using FluentAssertions;
using MealMate.Api.Controllers.Admin;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OffersControllerTests
{
    [Fact]
    public void AdminOffersController_HasAuthorizeAndManagePermission()
    {
        var authAttr = typeof(AdminOffersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        PermissionCodes.OffersManage.Should().Be("offers:manage");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OffersControllerTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/MealMate.Api/Controllers/Admin/AdminOffersController.cs
using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Offers.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/offers")]
public sealed class AdminOffersController : ApiControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Create([FromBody] CreateOfferDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleCreated(result, $"api/v1/admin/offers/{result.Value?.Id}");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOfferDraftCommand command, CancellationToken cancellationToken)
    {
        if (id != command.OfferId)
        {
            command = command with { OfferId = id };
        }
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/transition")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionOfferStatusCommand command, CancellationToken cancellationToken)
    {
        if (id != command.OfferId)
        {
            command = command with { OfferId = id };
        }
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> List([FromQuery] OfferStatus? status, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOffersListQuery(status, page), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOfferByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }
}
```

Implement `CustomerOffersController` and `RestaurantOffersController`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OffersControllerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Api/Controllers/ src/MealMate.Application/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): add Admin, Customer, and Restaurant Offers API controllers"
```

---

### Task 9: Lifecycle Scheduler Service & Background Worker

**Files:**
- Create: `src/MealMate.Application/Features/Offers/Services/IOfferLifecycleSchedulerService.cs`
- Create: `src/MealMate.Application/Features/Offers/Services/OfferLifecycleSchedulerService.cs`
- Create: `src/MealMate.Worker/Jobs/OfferLifecycleSchedulerJob.cs`
- Test: `tests/MealMate.UnitTests/Offers/OfferLifecycleSchedulerTests.cs`

**Interfaces:**
- Produces:
  - `IOfferLifecycleSchedulerService.ProcessLifecycleTransitionsAsync(CancellationToken ct)`
  - Automatic transition from `Scheduled` -> `Active` when `StartsAtUtc <= now`
  - Automatic transition from `Active` / `Paused` -> `Ended` when `EndsAtUtc <= now`
  - Schedular worker job inheriting `ScheduledJob` running every 1 minute.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/MealMate.UnitTests/Offers/OfferLifecycleSchedulerTests.cs
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferLifecycleSchedulerTests
{
    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_ActivatesScheduledOffers()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("OfferSchedulerDb")
            .Options;

        using var db = new AppDbContext(options);
        var clock = Substitute.For<IDateTimeProvider>();
        var now = DateTime.UtcNow;
        clock.UtcNow.Returns(now);

        var offer = new Offer
        {
            NameAr = "مجدول",
            NameEn = "Scheduled",
            Status = OfferStatus.Scheduled,
            StartsAtUtc = now.AddMinutes(-5),
            EndsAtUtc = now.AddDays(5)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var service = new OfferLifecycleSchedulerService(db, clock);
        var result = await service.ProcessLifecycleTransitionsAsync();

        result.ActivatedCount.Should().Be(1);
        offer.Status.Should().Be(OfferStatus.Active);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferLifecycleSchedulerTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Implement `OfferLifecycleSchedulerService` and `OfferLifecycleSchedulerJob` mirroring `CampaignLifecycleSchedulerJob`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MealMate.UnitTests --filter "FullyQualifiedName~OfferLifecycleSchedulerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MealMate.Application/Features/Offers/Services/ src/MealMate.Worker/Jobs/ tests/MealMate.UnitTests/Offers/
git commit -m "feat(offers): add OfferLifecycleSchedulerService and worker job"
```

---

### Task 10: Full Solution Verification & Campaigns Alignment Audit

- [ ] **Step 1: Run complete test suite**
Run: `dotnet test tests/MealMate.UnitTests`
Expected: All tests pass.

- [ ] **Step 2: Build complete solution**
Run: `dotnet build MealMate.sln -c Release`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Verification commit & documentation update**
Update walkthrough and architecture alignment documentation.
