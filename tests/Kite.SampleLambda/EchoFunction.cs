using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Kite.SampleLambda;

/// <summary>
/// Simple Lambda handler used for integration tests.
/// Accepts a string payload and returns it prefixed with "echo: ".
/// </summary>
public class EchoFunction
{
    public string Handle(string input) => $"echo: {input}";
}
