using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.Common;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;

public class AdminOperationalPoliciesHandlerTests
{
    private readonly IOperationalPolicyService _policyService;
    private readonly IDateTimeProvider _clock;
    private readonly DateTime _utcNow = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new(2026, 8, 20);

    public AdminOperationalPoliciesHandlerTests()
    {
        _policyService = Substitute.For<IOperationalPolicyService>();
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_utcNow);
        _clock.TodayUtc.Returns(_today);
    }

    [Fact]
    public async Task GetOperationalPoliciesQueryHandler_ReturnsSettingsAndPreview()
    {
        // Arrange
        var settings = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 10m),
            Freeze: new FreezePolicySettings()
        );
        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _policyService.CalculateSelectionDeadlineUtc(Arg.Any<DateOnly>(), Arg.Any<OperationalPolicySettings>())
            .Returns(new DateTime(2026, 8, 25, 21, 0, 0, DateTimeKind.Utc));

        var handler = new GetOperationalPoliciesQueryHandler(_policyService, _clock);

        // Act
        var result = await handler.Handle(new GetOperationalPoliciesQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Cutoff.CutoffLeadHours.Should().Be(48);
        result.Value.Cancellation.FeeValue.Should().Be(10m);
        result.Value.Simulation.Should().NotBeNull();
        result.Value.Simulation!.GrossRefund.Should().Be(60.000m); // 20 days * 3.000 KWD
        result.Value.Simulation.CancellationFee.Should().Be(6.000m); // 10% of 60
        result.Value.Simulation.NetRefund.Should().Be(54.000m); // 60 - 6
    }

    [Fact]
    public async Task UpdateOperationalPoliciesCommandHandler_CallsServiceAndReturnsUpdatedDto()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var newSettings = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 24),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.FixedAmount, FeeValue: 4m),
            Freeze: new FreezePolicySettings()
        );
        _policyService.UpdateSettingsAsync(newSettings, adminId, Arg.Any<CancellationToken>())
            .Returns(Result<OperationalPolicySettings>.Success(newSettings));
        _policyService.CalculateSelectionDeadlineUtc(Arg.Any<DateOnly>(), Arg.Any<OperationalPolicySettings>())
            .Returns(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc));

        var handler = new UpdateOperationalPoliciesCommandHandler(_policyService, _clock);

        // Act
        var result = await handler.Handle(new UpdateOperationalPoliciesCommand(newSettings, adminId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Cutoff.CutoffLeadHours.Should().Be(24);
        result.Value.Cancellation.FeeType.Should().Be(CancellationFeeType.FixedAmount);
        result.Value.Simulation!.CancellationFee.Should().Be(4.000m);
        result.Value.Simulation.NetRefund.Should().Be(56.000m); // 60 - 4
    }

    [Fact]
    public async Task PreviewOperationalPoliciesQueryHandler_CalculatesDynamicCustomPreview()
    {
        // Arrange
        var customSettings = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 20m),
            Freeze: new FreezePolicySettings()
        );
        var sampleDate = new DateOnly(2026, 9, 1);
        var expectedDeadline = new DateTime(2026, 8, 28, 21, 0, 0, DateTimeKind.Utc);

        _policyService.CalculateSelectionDeadlineUtc(sampleDate, customSettings)
            .Returns(expectedDeadline);

        var handler = new PreviewOperationalPoliciesQueryHandler(_policyService, _clock);
        var query = new PreviewOperationalPoliciesQuery(
            Settings: customSettings,
            SampleServiceDate: sampleDate,
            SampleSubscriptionPrice: 120.000m,
            SampleTotalDays: 30,
            SampleRemainingDays: 15
        );

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var preview = result.Value;
        preview.SampleDailyPrice.Should().Be(4.000m); // 120 / 30
        preview.GrossRefund.Should().Be(60.000m); // 15 * 4
        preview.CancellationFee.Should().Be(12.000m); // 20% of 60
        preview.NetRefund.Should().Be(48.000m); // 60 - 12
        preview.CutoffDeadlineUtc.Should().Be(expectedDeadline);
    }

    [Fact]
    public void UpdateOperationalPoliciesCommandValidator_ValidatesConstraints()
    {
        // Arrange
        var validator = new UpdateOperationalPoliciesCommandValidator();

        // Invalid: CutoffLeadHours = 0
        var invalidCutoff = new UpdateOperationalPoliciesCommand(
            new OperationalPolicySettings(
                Cutoff: new CutoffPolicySettings(CutoffLeadHours: 0),
                Cancellation: new CancellationPolicySettings(),
                Freeze: new FreezePolicySettings()
            ),
            Guid.NewGuid());

        var resultCutoff = validator.Validate(invalidCutoff);
        resultCutoff.IsValid.Should().BeFalse();

        // Invalid: Percentage fee > 100
        var invalidFee = new UpdateOperationalPoliciesCommand(
            new OperationalPolicySettings(
                Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48),
                Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 150m),
                Freeze: new FreezePolicySettings()
            ),
            Guid.NewGuid());

        var resultFee = validator.Validate(invalidFee);
        resultFee.IsValid.Should().BeFalse();

        // Valid
        var valid = new UpdateOperationalPoliciesCommand(
            new OperationalPolicySettings(
                Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48),
                Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 10m),
                Freeze: new FreezePolicySettings()
            ),
            Guid.NewGuid());

        var resultValid = validator.Validate(valid);
        resultValid.IsValid.Should().BeTrue();
    }
}
