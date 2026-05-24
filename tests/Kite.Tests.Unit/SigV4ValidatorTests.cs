using Kite.Core.Auth;
using FluentAssertions;
using Xunit;

namespace Kite.Tests.Unit;

public class SigV4ValidatorTests
{
    private const string ValidHeader = "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20240101/us-east-1/s3/aws4_request, SignedHeaders=host;x-amz-date, Signature=abc123";

    [Fact]
    public void Validate_WithCorrectAccessKeyId_ReturnsTrue()
    {
        SigV4Validator.Validate(ValidHeader, "AKIAIOSFODNN7EXAMPLE").Should().BeTrue();
    }

    [Fact]
    public void Validate_WithWrongAccessKeyId_ReturnsFalse()
    {
        SigV4Validator.Validate(ValidHeader, "WRONGKEY").Should().BeFalse();
    }

    [Fact]
    public void Validate_WithNullHeader_ReturnsFalse()
    {
        SigV4Validator.Validate(null, "AKIAIOSFODNN7EXAMPLE").Should().BeFalse();
    }

    [Fact]
    public void ExtractServiceName_ExtractsS3()
    {
        SigV4Validator.ExtractServiceName(ValidHeader).Should().Be("s3");
    }

    [Fact]
    public void ExtractServiceName_ExtractsSqs()
    {
        var header = "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/sqs/aws4_request, SignedHeaders=host, Signature=sig";
        SigV4Validator.ExtractServiceName(header).Should().Be("sqs");
    }

    [Fact]
    public void ExtractServiceName_ExtractsEvents()
    {
        var header = "AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1/events/aws4_request, SignedHeaders=host, Signature=sig";
        SigV4Validator.ExtractServiceName(header).Should().Be("events");
    }

    [Fact]
    public void ExtractServiceName_WithNullHeader_ReturnsNull()
    {
        SigV4Validator.ExtractServiceName(null).Should().BeNull();
    }

    [Fact]
    public void Validate_WithEmptyString_ReturnsFalse()
    {
        SigV4Validator.Validate("", "AKIAIOSFODNN7EXAMPLE").Should().BeFalse();
    }

    [Fact]
    public void Validate_WithNoCredentialPrefix_ReturnsFalse()
    {
        SigV4Validator.Validate("AWS4-HMAC-SHA256 SignedHeaders=host, Signature=abc123", "test").Should().BeFalse();
    }

    [Fact]
    public void ExtractServiceName_WithEmptyHeader_ReturnsNull()
    {
        SigV4Validator.ExtractServiceName("").Should().BeNull();
    }

    [Fact]
    public void ExtractServiceName_WithNoCredentialPrefix_ReturnsNull()
    {
        SigV4Validator.ExtractServiceName("AWS4-HMAC-SHA256 SignedHeaders=host").Should().BeNull();
    }

    [Fact]
    public void ExtractServiceName_WithShortCredential_ReturnsNull()
    {
        // Credential with fewer than 4 parts — no service name
        SigV4Validator.ExtractServiceName("AWS4-HMAC-SHA256 Credential=test/20240101/us-east-1").Should().BeNull();
    }

    [Fact]
    public void Validate_WithCredentialNoComma_StillWorks()
    {
        // Authorization header where Credential is the last element (no trailing comma)
        var header = "AWS4-HMAC-SHA256 Credential=AKID/20240101/us-east-1/s3/aws4_request";
        SigV4Validator.Validate(header, "AKID").Should().BeTrue();
    }
}
