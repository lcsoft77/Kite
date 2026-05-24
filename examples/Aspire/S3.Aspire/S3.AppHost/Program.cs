using Kite.Aspire;

// Example – .NET Aspire App Host with Kite (S3 → Lambda)
//
// This project uses Kite.Aspire to start the emulator in-process inside the
// Aspire App Host.  Every emulator component (Lambda, S3 bucket) appears as its own
// resource row in the Aspire dashboard with integrated logs and a Restart button for
// hot-reloading the Lambda assembly from disk.
//
// Run this project (it starts the Aspire dashboard automatically), then run
// S3.Client to upload files to the bucket.

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the Lambda DLL from the AppHost output directory.
// The S3.Lambda project is referenced, so its DLL is copied alongside this binary.
var lambdaDll = Path.Combine(AppContext.BaseDirectory, "S3.Lambda.dll");

builder
    .AddKite()
    .WithRegion("eu-west-1")
    .WithCredentials("test", "test")
    .WithS3Bucket("uploads")
    .WithLambdaFunction(
        name: "file-processor",
        dllPath: lambdaDll,
        handler: "S3.Lambda::S3.Lambda.FileProcessorFunction::Handle")
    .WithS3Trigger(bucketName: "uploads", functionName: "file-processor");

builder.Build().Run();
