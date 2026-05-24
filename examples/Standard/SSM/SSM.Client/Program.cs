using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

// Example – SSM Parameter Store Client
//
// This project reads parameters from the SSM Parameter Store
// managed by the local Kite (started by SSM.Emulator).
//
// Make sure SSM.Emulator is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";

var credentials = new BasicAWSCredentials("test", "test");
using var ssmClient = new AmazonSimpleSystemsManagementClient(credentials, new AmazonSimpleSystemsManagementConfig
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region
});

Console.WriteLine("Reading parameters from SSM Parameter Store...");
Console.WriteLine();

var parameterNames = new[]
{
    "/myapp/database/connection-string",
    "/myapp/api/key",
    "/myapp/feature-flags"
};

// Get multiple parameters in a single request
var response = await ssmClient.GetParametersAsync(new GetParametersRequest
{
    Names = [.. parameterNames],
    WithDecryption = true
});

foreach (var parameter in response.Parameters)
{
    Console.WriteLine($"Name:  {parameter.Name}");
    Console.WriteLine($"Type:  {parameter.Type}");
    Console.WriteLine($"Value: {parameter.Value}");
    Console.WriteLine();
}

if (response.InvalidParameters.Count > 0)
{
    Console.WriteLine("Invalid (not found) parameters:");
    foreach (var name in response.InvalidParameters)
        Console.WriteLine($"  {name}");
}

Console.WriteLine("SSM parameter reads complete.");
