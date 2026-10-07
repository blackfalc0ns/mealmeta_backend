using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallV2WireContractApiTests
{
    [Fact]
    public async Task RoleAndSequenceRemainConsistentAcrossInitiationSnapshotAndAnswer()
    {
        await using var host = await VoiceCallTestHost.CreateAsync(extraSettings:
            new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });
        await host.RegisterDeviceAsync(host.DriverDeviceA, "contract-driver");
        await host.RegisterDeviceAsync(host.CustomerDeviceA, "contract-customer");
        var initiate = await host.DriverDeviceA.PostAsJsonAsync("/api/v2/voice-calls/initiate",
            new { tripStopId = host.TripStopId, clientRequestId = Guid.NewGuid() });
        initiate.EnsureSuccessStatusCode();
        var created = (await initiate.Content.ReadFromJsonAsync<JsonElement>());
        var callId = created.GetProperty("callId").GetGuid();
        var snapshot = await host.DriverDeviceA.GetFromJsonAsync<JsonElement>($"/api/v2/voice-calls/{callId}");
        snapshot.GetProperty("localParticipantRole").GetString().Should().Be(created.GetProperty("localParticipantRole").GetString());
        var answer = await host.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", null);
        answer.EnsureSuccessStatusCode();
        var accepted = await answer.Content.ReadFromJsonAsync<JsonElement>();
        accepted.GetProperty("sequence").GetInt64().Should().Be(created.GetProperty("sequence").GetInt64() + 1);
        accepted.GetProperty("localParticipantRole").GetString().Should().Be("Customer");
    }
}
