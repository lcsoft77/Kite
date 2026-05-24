using System.Text;
using Amazon.S3.Model;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Integration;

/// <summary>
/// Integration tests for S3 bucket and object operations via the official AWS SDK,
/// pointed at the local Kite instance.
/// </summary>
[Collection("EmulatorCollection")]
public class S3BucketTests
{
    private readonly EmulatorFixture _fixture;

    public S3BucketTests(EmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutObject_ThenGetObject_RoundTripsData()
    {
        // Arrange
        const string key = "roundtrip-test/hello.txt";
        const string content = "Hello from integration test!";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var expectedETag = $"\"{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(contentBytes)).ToLowerInvariant()}\"";

        // Act – upload
        var putRequest = new PutObjectRequest
        {
            BucketName = EmulatorFixture.TestBucketName,
            Key = key,
            ContentBody = content,
            ContentType = "text/plain"
        };
        var putResponse = await _fixture.S3Client.PutObjectAsync(putRequest);
        putResponse.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        putResponse.ETag.Should().Be(expectedETag, "emulator ETag must be MD5 of the uploaded content bytes");

        // Act – download
        var getRequest = new GetObjectRequest
        {
            BucketName = EmulatorFixture.TestBucketName,
            Key = key
        };
        using var getResponse = await _fixture.S3Client.GetObjectAsync(getRequest);

        // Assert
        getResponse.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        getResponse.ETag.Should().Be(expectedETag, "ETag returned by GET must match the one returned by PUT");
        using var reader = new StreamReader(getResponse.ResponseStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        body.Should().Be(content);
    }

    [Fact]
    public async Task PutObject_ThenDeleteObject_ObjectNoLongerExists()
    {
        // Arrange
        const string key = "delete-test/to-delete.txt";

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = EmulatorFixture.TestBucketName,
            Key = key,
            ContentBody = "delete me"
        });

        // Act
        var deleteResponse = await _fixture.S3Client.DeleteObjectAsync(
            EmulatorFixture.TestBucketName, key);

        deleteResponse.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Assert – subsequent GET should return 404
        var act = async () => await _fixture.S3Client.GetObjectAsync(
            EmulatorFixture.TestBucketName, key);
        await act.Should().ThrowAsync<Amazon.S3.AmazonS3Exception>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListObjectsV2_AfterPuttingMultipleObjects_ReturnsAllObjects()
    {
        // Arrange – upload several objects under a unique prefix so the test is isolated
        var prefix = $"list-test/{Guid.NewGuid():N}/";
        var keys = Enumerable.Range(1, 3).Select(i => $"{prefix}file-{i}.txt").ToList();

        foreach (var k in keys)
        {
            await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = EmulatorFixture.TestBucketName,
                Key = k,
                ContentBody = $"content of {k}"
            });
        }

        // Act
        var listResponse = await _fixture.S3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = EmulatorFixture.TestBucketName,
            Prefix = prefix
        });

        // Assert
        listResponse.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        listResponse.S3Objects.Should().HaveCount(keys.Count);
        listResponse.S3Objects.Select(o => o.Key).Should().BeEquivalentTo(keys);
    }

    [Fact]
    public async Task HeadObject_ForExistingObject_ReturnsMetadata()
    {
        // Arrange
        const string key = "head-test/metadata.bin";
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var expectedETag = $"\"{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(data)).ToLowerInvariant()}\"";

        await _fixture.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = EmulatorFixture.TestBucketName,
            Key = key,
            InputStream = new MemoryStream(data),
            ContentType = "application/octet-stream"
        });

        // Act
        var headResponse = await _fixture.S3Client.GetObjectMetadataAsync(
            EmulatorFixture.TestBucketName, key);

        // Assert – verify both size and the exact ETag to guard against aws-chunked regressions
        headResponse.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        headResponse.ContentLength.Should().Be(data.Length);
        headResponse.ETag.Should().Be(expectedETag, "ETag must be the MD5 of the raw uploaded bytes");
    }

    [Fact]
    public async Task GetObject_NonExistentKey_Returns404()
    {
        // Act + Assert
        var act = async () => await _fixture.S3Client.GetObjectAsync(
            EmulatorFixture.TestBucketName, "does-not-exist/missing.txt");

        await act.Should().ThrowAsync<Amazon.S3.AmazonS3Exception>()
            .Where(ex => ex.StatusCode == System.Net.HttpStatusCode.NotFound);
    }
}
