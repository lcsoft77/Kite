using Kite.Core;
using Kite.Host;

// Example – Kite Host (SSM Parameter Store)
//
// This project starts the Kite with pre-seeded SSM parameters:
//   • /myapp/database/connection-string  (String)
//   • /myapp/api/key                     (SecureString)
//   • /myapp/feature-flags               (StringList)
//
// Run this project first, then run SSM.Client to read the parameters.

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddSsmParameter("/myapp/database/connection-string", "Server=db;Database=myapp;User=admin;Password=secret", "String")
    .AddSsmParameter("/myapp/api/key", "super-secret-api-key-12345", "SecureString")
    .AddSsmParameter("/myapp/feature-flags", "dark-mode,new-dashboard,beta-search", "StringList")
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("SSM parameters:");
Console.WriteLine("  /myapp/database/connection-string");
Console.WriteLine("  /myapp/api/key");
Console.WriteLine("  /myapp/feature-flags");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
