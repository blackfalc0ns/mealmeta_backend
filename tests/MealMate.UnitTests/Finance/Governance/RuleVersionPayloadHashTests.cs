using System.Text.Json;
using FluentAssertions;
using MealMate.Domain.Foundation;
using Xunit;

namespace MealMate.UnitTests.Finance.Governance;

public sealed class RuleVersionPayloadHashTests
{
    [Fact]
    public void JsonWhitespacePropertyOrderAndNumberNotationDoNotChangePayloadHash()
    {
        var compact = RuleVersionPayloadHash.Compute("{\"maxFees\":40,\"minimumExecutedDays\":3}");
        var formatted = RuleVersionPayloadHash.Compute("{ \"minimumExecutedDays\" : 3.0, \"maxFees\" : 4e1 }");

        formatted.Should().Be(compact);
    }

    [Fact]
    public void DuplicatePropertiesAreRejectedBeforePersistenceCanDiscardThem()
    {
        FluentActions.Invoking(() => RuleVersionPayloadHash.Compute("{\"maxFees\":4,\"maxFees\":40}"))
            .Should().Throw<JsonException>();
    }
}
