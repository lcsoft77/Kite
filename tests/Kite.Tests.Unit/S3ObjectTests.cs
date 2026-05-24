using Kite.Core.Models;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class S3ObjectTests
{
    [Fact]
    public void S3Bucket_AddAndRetrieveObject_Works()
    {
        var bucket = new S3Bucket { Name = "test-bucket" };
        var obj = new S3Object
        {
            Key = "my/key.json",
            ContentType = "application/json",
            Data = System.Text.Encoding.UTF8.GetBytes("{\"test\": true}"),
            ETag = "\"abc123\""
        };

        bucket.Objects["my/key.json"] = obj;
        bucket.Objects.TryGetValue("my/key.json", out var retrieved).Should().BeTrue();
        retrieved!.ContentType.Should().Be("application/json");
        retrieved.ETag.Should().Be("\"abc123\"");
    }

    [Fact]
    public void S3Object_DefaultValues_AreCorrect()
    {
        var obj = new S3Object();
        obj.ContentType.Should().Be("application/octet-stream");
        obj.Data.Should().BeEmpty();
        obj.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void S3Bucket_DefaultValues_AreCorrect()
    {
        var bucket = new S3Bucket();
        bucket.Name.Should().BeEmpty();
        bucket.Objects.Should().BeEmpty();
        bucket.NotificationConfiguration.Should().BeNull();
        bucket.RequestLog.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void S3Bucket_NotifyChange_FiresEvent()
    {
        var bucket = new S3Bucket { Name = "test-bucket" };
        var fired = false;
        bucket.OnChange += () => fired = true;

        bucket.NotifyChange();

        fired.Should().BeTrue();
    }

    [Fact]
    public void S3Bucket_RemoveObject_Works()
    {
        var bucket = new S3Bucket { Name = "test-bucket" };
        bucket.Objects["key1"] = new S3Object { Key = "key1", Data = new byte[] { 1 } };

        bucket.Objects.TryRemove("key1", out _).Should().BeTrue();
        bucket.Objects.Should().BeEmpty();
    }

    [Fact]
    public void S3Bucket_NonExistentKey_ReturnsFalse()
    {
        var bucket = new S3Bucket { Name = "test-bucket" };

        bucket.Objects.TryGetValue("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void S3Bucket_MultipleObjects_Works()
    {
        var bucket = new S3Bucket { Name = "test-bucket" };
        bucket.Objects["a"] = new S3Object { Key = "a" };
        bucket.Objects["b"] = new S3Object { Key = "b" };
        bucket.Objects["c"] = new S3Object { Key = "c" };

        bucket.Objects.Should().HaveCount(3);
    }

    [Fact]
    public void S3Object_Key_DefaultEmpty()
    {
        var obj = new S3Object();
        obj.Key.Should().BeEmpty();
    }

    [Fact]
    public void S3Object_ETag_DefaultEmpty()
    {
        var obj = new S3Object();
        obj.ETag.Should().BeEmpty();
    }

    [Fact]
    public void S3NotificationConfiguration_LambdaConfigurations_InitiallyEmpty()
    {
        var config = new S3NotificationConfiguration();
        config.LambdaFunctionConfigurations.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void S3LambdaNotification_DefaultValues_AreCorrect()
    {
        var notification = new S3LambdaNotification();
        notification.LambdaFunctionArn.Should().BeEmpty();
        notification.Events.Should().NotBeNull().And.BeEmpty();
    }
}
