using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

// Example – S3 File Uploader
//
// This project uploads a series of text files to the "uploads" S3 bucket
// managed by the local Kite (started by S3.Emulator).
//
// Make sure S3.Emulator is running before executing this project.

const string emulatorUrl = "http://localhost:4566";
const string region = "eu-west-1";
const string bucketName = "uploads";

var credentials = new BasicAWSCredentials("test", "test");
using var s3Client = new AmazonS3Client(credentials, new AmazonS3Config
{
    ServiceURL = emulatorUrl,
    AuthenticationRegion = region,
    ForcePathStyle = true
});

Console.WriteLine($"Uploading files to bucket '{bucketName}'...");
Console.WriteLine();

for (int i = 1; i <= 5; i++)
{
    var key = $"documents/file-{i:D2}.txt";
    var content = $"Content of document {i}, uploaded at {DateTime.UtcNow:O}";

    var response = await s3Client.PutObjectAsync(new PutObjectRequest
    {
        BucketName = bucketName,
        Key = key,
        ContentBody = content
    });

    Console.WriteLine($"Uploaded '{key}': ETag = {response.ETag}");
}

Console.WriteLine();
Console.WriteLine("All 5 files uploaded. Check S3.Emulator logs to see them being processed.");
