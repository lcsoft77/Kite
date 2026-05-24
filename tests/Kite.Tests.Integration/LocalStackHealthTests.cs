using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests that verify the LocalStack-compatible health endpoint
/// returns the expected JSON shape and service statuses.
/// </summary>
[Collection("EmulatorCollection")]
public class LocalStackHealthTests
{
    private readonly EmulatorFixture _fixture;

    public LocalStackHealthTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLocalStackHealth_ReturnsOkWithExpectedShape()
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{_fixture.ServiceUrl}/_localstack/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("services", out var services).Should().BeTrue("services key must be present");
        root.TryGetProperty("version", out _).Should().BeTrue("version key must be present");

        foreach (var serviceName in new[] { "sqs", "s3", "lambda", "events", "ssm", "ecs", "apigateway" })
        {
            services.TryGetProperty(serviceName, out var status).Should().BeTrue($"service '{serviceName}' must be listed");
            status.GetString().Should().Be("running", $"service '{serviceName}' status should be 'running'");
        }
    }
}
