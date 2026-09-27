using FluentAssertions;

namespace MealMate.IntegrationTests;

/// <summary>
/// Integration tests use Testcontainers.PostgreSql to spin up a real database.
/// This placeholder keeps the project compiling until the first real fixture is
/// added; Docker is required to run the database-backed tests.
public class PlaceholderTests
{
    [Fact]
    public void IntegrationTestAssembly_LoadsSuccessfully()
    {
        typeof(PlaceholderTests).Assembly.Should().NotBeNull();
    }
}
