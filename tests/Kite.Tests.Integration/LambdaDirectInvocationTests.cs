using System.Text;
using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests that directly invoke a Lambda function via the official AWS SDK,
/// pointed at the local Kite instance.
/// </summary>
[Collection("EmulatorCollection")]
public class LambdaDirectInvocationTests
{
    private readonly EmulatorFixture _fixture;

    public LambdaDirectInvocationTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InvokeLambda_WithStringPayload_ReturnsEchoResponse()
    {
        // Arrange
        var payload = JsonSerializer.Serialize("hello from AWS SDK");

        // Act
        var request = new InvokeRequest
        {
            FunctionName = EmulatorFixture.EchoFunctionName,
            InvocationType = InvocationType.RequestResponse,
            Payload = payload
        };

        var response = await _fixture.LambdaClient.InvokeAsync(request);

        // Assert
        response.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.FunctionError.Should().BeNullOrEmpty();

        using var reader = new StreamReader(response.Payload, Encoding.UTF8);
        var responseBody = await reader.ReadToEndAsync();
        responseBody.Should().Contain("echo:");
    }

    [Fact]
    public async Task InvokeLambda_Async_ReturnsAccepted()
    {
        // Arrange
        var payload = JsonSerializer.Serialize("async invocation test");

        // Act
        var request = new InvokeRequest
        {
            FunctionName = EmulatorFixture.EchoFunctionName,
            InvocationType = InvocationType.Event,
            Payload = payload
        };

        var response = await _fixture.LambdaClient.InvokeAsync(request);

        // Assert – async ("Event") invocations return HTTP 202
        ((int)response.HttpStatusCode).Should().Be(202);
    }

    [Fact]
    public async Task ListFunctions_ReturnsRegisteredFunctions()
    {
        // Act
        var response = await _fixture.LambdaClient.ListFunctionsAsync();

        // Assert
        response.Functions.Should().Contain(f => f.FunctionName == EmulatorFixture.EchoFunctionName);
        response.Functions.Should().Contain(f => f.FunctionName == EmulatorFixture.SqsProcessorFunctionName);
    }
}
