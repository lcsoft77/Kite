using Amazon.SimpleSystemsManagement.Model;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests for SSM Parameter Store operations via the official AWS SDK,
/// pointed at the local Kite instance.
/// </summary>
[Collection("EmulatorCollection")]
public class SsmParameterTests
{
    private readonly EmulatorFixture _fixture;

    public SsmParameterTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetParameter_PreConfiguredParameter_ReturnsExpectedValue()
    {
        // Act
        var response = await _fixture.SsmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = EmulatorFixture.SsmParameterName
        });

        // Assert
        response.Parameter.Should().NotBeNull();
        response.Parameter.Name.Should().Be(EmulatorFixture.SsmParameterName);
        response.Parameter.Value.Should().Be(EmulatorFixture.SsmParameterValue);
    }

    [Fact]
    public async Task PutParameter_ThenGetParameter_RoundTripsValue()
    {
        // Arrange – use a unique name to avoid cross-test collisions
        var paramName = $"/integration/dynamic/{Guid.NewGuid():N}";
        const string paramValue = "dynamic-value-123";

        // Act – store
        var putResponse = await _fixture.SsmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = paramName,
            Value = paramValue,
            Type = Amazon.SimpleSystemsManagement.ParameterType.String
        });
        putResponse.Version.Should().BeGreaterThan(0);

        // Act – retrieve
        var getResponse = await _fixture.SsmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = paramName
        });

        // Assert
        getResponse.Parameter.Value.Should().Be(paramValue);
        getResponse.Parameter.Name.Should().Be(paramName);
    }

    [Fact]
    public async Task GetParametersByPath_ReturnsAllParametersUnderPrefix()
    {
        // Arrange – write two parameters under a unique sub-path
        var prefix = $"/integration/path-test/{Guid.NewGuid():N}";
        var names = new[] { $"{prefix}/a", $"{prefix}/b" };

        foreach (var n in names)
        {
            await _fixture.SsmClient.PutParameterAsync(new PutParameterRequest
            {
                Name = n,
                Value = $"value-for-{n}",
                Type = Amazon.SimpleSystemsManagement.ParameterType.String
            });
        }

        // Act
        var pathResponse = await _fixture.SsmClient.GetParametersByPathAsync(
            new GetParametersByPathRequest { Path = prefix });

        // Assert
        pathResponse.Parameters.Should().HaveCount(names.Length);
        pathResponse.Parameters.Select(p => p.Name).Should().BeEquivalentTo(names);
    }

    [Fact]
    public async Task GetParameter_NonExistentName_ThrowsParameterNotFound()
    {
        // Arrange – use a unique name to ensure the parameter was never created
        var paramName = $"/integration/does-not-exist/{Guid.NewGuid():N}";

        // Act + Assert
        var act = async () => await _fixture.SsmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = paramName
        });

        await act.Should().ThrowAsync<ParameterNotFoundException>();
    }

    [Fact]
    public async Task PutParameter_OverwriteExisting_UpdatesValue()
    {
        // Arrange
        var paramName = $"/integration/overwrite/{Guid.NewGuid():N}";

        await _fixture.SsmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = paramName,
            Value = "original",
            Type = Amazon.SimpleSystemsManagement.ParameterType.String
        });

        // Act – overwrite
        await _fixture.SsmClient.PutParameterAsync(new PutParameterRequest
        {
            Name = paramName,
            Value = "updated",
            Type = Amazon.SimpleSystemsManagement.ParameterType.String,
            Overwrite = true
        });

        // Assert
        var getResponse = await _fixture.SsmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = paramName
        });
        getResponse.Parameter.Value.Should().Be("updated");
    }
}
