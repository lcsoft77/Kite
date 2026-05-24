using Kite.Core;
using Kite.Host;

// Example – Kite Host (S3 → Lambda)
//
// This project starts the Kite with:
//   • An S3 bucket named "uploads"
//   • The FileProcessorFunction Lambda triggered on every object upload
//
// Run this project first, then run S3.Client to upload files.

var lambdaDll = Path.Combine(AppContext.BaseDirectory, "S3.Lambda.dll");

var emulator = KiteBuilder.Create()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .AddS3Bucket("uploads")
    .AddLambda("file-processor", lb => lb
        .WithHandler("S3.Lambda::S3.Lambda.FileProcessorFunction::Handle")
        .WithDll(lambdaDll))
    .AddS3Trigger("uploads", "file-processor")
    .Build()
    .UseHost();

Console.WriteLine("Kite running on http://localhost:4566");
Console.WriteLine("S3 bucket:  http://localhost:4566/uploads");
Console.WriteLine("Press Ctrl+C to stop.");

await emulator.RunAsync();
