using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.S3Events;
using Kite.Core;
using Kite.Core.Models;
using Kite.Lambda.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kite.S3;

public class S3Service
{
    private readonly IServiceRegistry _registry;
    private readonly LambdaExecutor _executor;
    private readonly ILogger<S3Service> _logger;

    public S3Service(IServiceRegistry registry, LambdaExecutor executor, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _executor = executor;
        _logger = loggerFactory.CreateLogger<S3Service>();
    }

    public async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method.ToUpper();
        var query = context.Request.Query;

        _logger.LogDebug("[DEBUG] S3 HandleAsync - Method: {Method}, Path: {Path}", method, path);

        // Parse bucket and key from path
        var segments = path.TrimStart('/').Split('/', 2);
        var bucketName = segments.Length > 0 ? segments[0] : string.Empty;
        var key = segments.Length > 1 ? segments[1] : string.Empty;

        _logger.LogDebug("[DEBUG] S3 operation - Bucket: {Bucket}, Key: {Key}, QueryParams: {QueryCount}", bucketName ?? "<none>", key ?? "<none>", query.Count);

        using var activity = KiteActivitySource.S3.StartActivity($"s3.{method.ToLower()}");
        activity?.SetTag("s3.bucket", bucketName);
        activity?.SetTag("s3.key", key);

        if (string.IsNullOrEmpty(bucketName))
        {
            await HandleListBuckets(context);
            return;
        }

        // Bucket-level operations
        if (string.IsNullOrEmpty(key))
        {
            if (query.ContainsKey("notification"))
            {
                if (method == "PUT") await HandlePutBucketNotification(context, bucketName);
                else await HandleGetBucketNotification(context, bucketName);
                return;
            }

            if (query.ContainsKey("list-type"))
            {
                await HandleListObjectsV2(context, bucketName);
                return;
            }

            if (method == "PUT") await HandleCreateBucket(context, bucketName);
            else if (method == "DELETE") await HandleDeleteBucket(context, bucketName);
            else await HandleListObjectsV2(context, bucketName);
            return;
        }

        // Object-level operations
        switch (method)
        {
            case "PUT":
                await HandlePutObject(context, bucketName, key);
                break;
            case "GET":
                await HandleGetObject(context, bucketName, key);
                break;
            case "DELETE":
                await HandleDeleteObject(context, bucketName, key);
                break;
            case "HEAD":
                await HandleHeadObject(context, bucketName, key);
                break;
            default:
                context.Response.StatusCode = 405;
                break;
        }
    }

    private async Task HandleListBuckets(HttpContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<ListAllMyBucketsResult xmlns=""http://s3.amazonaws.com/doc/2006-03-01/"">");
        sb.AppendLine(@"  <Owner><ID>owner</ID><DisplayName>owner</DisplayName></Owner>");
        sb.AppendLine("  <Buckets>");
        foreach (var bucket in _registry.GetAllS3Buckets())
            sb.AppendLine($"    <Bucket><Name>{bucket.Name}</Name><CreationDate>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}</CreationDate></Bucket>");
        sb.AppendLine("  </Buckets>");
        sb.AppendLine("</ListAllMyBucketsResult>");

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandleCreateBucket(HttpContext context, string bucketName)
    {
        _logger.LogDebug("[ENTRY] HandleCreateBucket - BucketName: {BucketName}", bucketName);
        
        if (_registry.GetS3Bucket(bucketName) is null)
            _registry.RegisterS3Bucket(new S3Bucket { Name = bucketName });

        _logger.LogInformation("CreateBucket completed - BucketName: {BucketName}", bucketName);

        context.Response.Headers["Location"] = $"/{bucketName}";
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(@"<?xml version=""1.0"" encoding=""UTF-8""?><CreateBucketConfiguration/>");
    }

    private async Task HandleDeleteBucket(HttpContext context, string bucketName)
    {
        _logger.LogDebug("[ENTRY] HandleDeleteBucket - BucketName: {BucketName}", bucketName);
        
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null)
        {
            _logger.LogError("[ERROR] DeleteBucket failed - Bucket not found: {BucketName}", bucketName);
            await WriteXmlError(context, 404, "NoSuchBucket", $"The specified bucket does not exist: {bucketName}");
            return;
        }
        
        _logger.LogInformation("DeleteBucket completed - BucketName: {BucketName}", bucketName);
        context.Response.StatusCode = 204;
        await context.Response.CompleteAsync();
    }

    private async Task HandlePutObject(HttpContext context, string bucketName, string key)
    {
        _logger.LogDebug("[ENTRY] HandlePutObject - BucketName: {BucketName}, Key: {Key}", bucketName, key);
        
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null)
        {
            _logger.LogError("[ERROR] PutObject failed - Bucket not found: {BucketName}", bucketName);
            await WriteXmlError(context, 404, "NoSuchBucket", $"The specified bucket does not exist: {bucketName}");
            return;
        }

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var rawData = ms.ToArray();

        // The AWS SDK for .NET uses S3 streaming chunked encoding when the header
        // x-amz-content-sha256 equals "STREAMING-AWS4-HMAC-SHA256-PAYLOAD".
        // Unlike HTTP Transfer-Encoding: chunked, Kestrel does NOT decode this format
        // automatically (it arrives as Content-Encoding: aws-chunked). We must decode it
        // manually so the stored bytes and ETag match what the SDK expects.
        var contentSha256 = context.Request.Headers.TryGetValue("x-amz-content-sha256", out var sha256Header)
            ? sha256Header.ToString() : string.Empty;

        var data = contentSha256 is "STREAMING-AWS4-HMAC-SHA256-PAYLOAD" or "STREAMING-AWS4-ECDSA-P256-SHA256-PAYLOAD"
            ? DecodeAwsChunkedBody(rawData)
            : rawData;

        var etag = $"\"{Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant()}\"";

        var metadata = new Dictionary<string, string>();
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
                metadata[header.Key[11..]] = header.Value.ToString();
        }

        var obj = new S3Object
        {
            Key = key,
            ContentType = context.Request.ContentType ?? "application/octet-stream",
            Data = data,
            Metadata = metadata,
            LastModified = DateTime.UtcNow,
            ETag = etag
        };

        bucket.Objects[key] = obj;
        _logger.LogInformation("S3 PutObject: s3://{Bucket}/{Key} ({Size} bytes)", bucketName, key, data.Length);
        LogHelpers.AddRequestLog(bucket.RequestLog, "PutObject", $"s3://{bucketName}/{key} ({data.Length} bytes)");
        bucket.NotifyChange();

        context.Response.Headers["ETag"] = etag;
        context.Response.StatusCode = 200;

        // Trigger notifications
        await FireS3NotificationAsync(bucket, key, "s3:ObjectCreated:Put", obj);
    }

    /// <summary>
    /// Decodes an AWS S3 streaming chunked body (Content-Encoding: aws-chunked).
    /// Each chunk is formatted as: {hex-size}[;chunk-signature={sig}]\r\n{data}\r\n
    /// The body is terminated by an empty chunk: 0[;chunk-signature={sig}]\r\n\r\n
    /// Returns rawData unchanged if the framing cannot be fully parsed, so the caller
    /// always gets a usable (non-silently-truncated) byte array.
    /// </summary>
    private static byte[] DecodeAwsChunkedBody(byte[] rawData)
    {
        var result = new MemoryStream();
        int pos = 0;
        bool sawTerminalChunk = false;

        while (pos < rawData.Length)
        {
            // Find end of chunk header line (\r\n).
            // Use i + 1 < rawData.Length to guarantee both rawData[i] and rawData[i+1] are in bounds.
            int crlfPos = -1;
            for (int i = pos; i + 1 < rawData.Length; i++)
            {
                if (rawData[i] == '\r' && rawData[i + 1] == '\n')
                {
                    crlfPos = i;
                    break;
                }
            }

            if (crlfPos < 0)
                return rawData; // Malformed body – fall back to raw bytes

            // Chunk header: "{hex-size}" or "{hex-size};chunk-signature={sig}"
            var header = Encoding.ASCII.GetString(rawData, pos, crlfPos - pos);
            var semicolonIdx = header.IndexOf(';');
            var hexSize = semicolonIdx >= 0 ? header[..semicolonIdx] : header;

            if (!int.TryParse(hexSize.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int chunkSize))
                return rawData; // Malformed chunk size – fall back to raw bytes

            pos = crlfPos + 2; // Advance past \r\n

            if (chunkSize == 0)
            {
                // Terminal empty chunk – decoding succeeded
                sawTerminalChunk = true;
                break;
            }

            // Guard against pos being at or beyond the end of the buffer
            if (pos >= rawData.Length || rawData.Length - pos < chunkSize)
                return rawData; // Truncated input – fall back to raw bytes

            result.Write(rawData, pos, chunkSize);
            pos += chunkSize + 2; // Advance past data and trailing \r\n
        }

        // Only return decoded data when the terminal 0-size chunk was reached
        return sawTerminalChunk ? result.ToArray() : rawData;
    }

    private async Task HandleGetObject(HttpContext context, string bucketName, string key)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { await WriteXmlError(context, 404, "NoSuchBucket", $"The specified bucket does not exist"); return; }

        if (!bucket.Objects.TryGetValue(key, out var obj))
        {
            await WriteXmlError(context, 404, "NoSuchKey", $"The specified key does not exist: {key}");
            return;
        }

        using var activity = KiteActivitySource.S3.StartActivity("s3.get");
        activity?.SetTag("s3.bucket", bucketName);
        activity?.SetTag("s3.key", key);

        context.Response.ContentType = obj.ContentType;
        context.Response.Headers["ETag"] = obj.ETag;
        context.Response.Headers["Last-Modified"] = obj.LastModified.ToString("R");
        context.Response.Headers["Content-Length"] = obj.Data.Length.ToString();
        foreach (var (k, v) in obj.Metadata)
            context.Response.Headers[$"x-amz-meta-{k}"] = v;

        LogHelpers.AddRequestLog(bucket.RequestLog, "GetObject", $"s3://{bucketName}/{key}");
        bucket.NotifyChange();
        await context.Response.Body.WriteAsync(obj.Data);
    }

    private async Task HandleDeleteObject(HttpContext context, string bucketName, string key)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { await WriteXmlError(context, 404, "NoSuchBucket", $"Bucket not found"); return; }

        bucket.Objects.TryRemove(key, out var deleted);

        using var activity = KiteActivitySource.S3.StartActivity("s3.delete");
        activity?.SetTag("s3.bucket", bucketName);
        activity?.SetTag("s3.key", key);

        if (deleted is null)
        {
            LogHelpers.AddRequestLog(bucket.RequestLog, "DeleteObject", $"s3://{bucketName}/{key}", 404);
            bucket.NotifyChange();
            context.Response.StatusCode = 404;
            await WriteXmlError(context, 404, "NoSuchKey", $"The specified key does not exist.");
            return;
        }

        await FireS3NotificationAsync(bucket, key, "s3:ObjectRemoved:Delete", deleted);

        LogHelpers.AddRequestLog(bucket.RequestLog, "DeleteObject", $"s3://{bucketName}/{key}", 204);
        bucket.NotifyChange();

        context.Response.StatusCode = 204;
        await context.Response.CompleteAsync();
    }

    private async Task HandleHeadObject(HttpContext context, string bucketName, string key)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { context.Response.StatusCode = 404; return; }

        if (!bucket.Objects.TryGetValue(key, out var obj)) { context.Response.StatusCode = 404; return; }

        context.Response.ContentType = obj.ContentType;
        context.Response.Headers["ETag"] = obj.ETag;
        context.Response.Headers["Last-Modified"] = obj.LastModified.ToString("R");
        context.Response.Headers["Content-Length"] = obj.Data.Length.ToString();
        foreach (var (k, v) in obj.Metadata)
            context.Response.Headers[$"x-amz-meta-{k}"] = v;
    }

    private async Task HandleListObjectsV2(HttpContext context, string bucketName)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { await WriteXmlError(context, 404, "NoSuchBucket", $"Bucket not found"); return; }

        var prefix = context.Request.Query.TryGetValue("prefix", out var p) ? p.ToString() : string.Empty;
        var objects = bucket.Objects.Values
            .Where(o => string.IsNullOrEmpty(prefix) || o.Key.StartsWith(prefix))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine($@"<ListBucketResult xmlns=""http://s3.amazonaws.com/doc/2006-03-01/"">");
        sb.AppendLine($"  <Name>{bucketName}</Name>");
        sb.AppendLine($"  <Prefix>{prefix}</Prefix>");
        sb.AppendLine($"  <KeyCount>{objects.Count}</KeyCount>");
        sb.AppendLine($"  <MaxKeys>1000</MaxKeys>");
        sb.AppendLine($"  <IsTruncated>false</IsTruncated>");
        foreach (var obj in objects)
        {
            sb.AppendLine("  <Contents>");
            sb.AppendLine($"    <Key>{System.Security.SecurityElement.Escape(obj.Key)}</Key>");
            sb.AppendLine($"    <LastModified>{obj.LastModified:yyyy-MM-ddTHH:mm:ss.fffZ}</LastModified>");
            sb.AppendLine($"    <ETag>{obj.ETag}</ETag>");
            sb.AppendLine($"    <Size>{obj.Data.Length}</Size>");
            sb.AppendLine("    <StorageClass>STANDARD</StorageClass>");
            sb.AppendLine("  </Contents>");
        }
        sb.AppendLine("</ListBucketResult>");

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task HandlePutBucketNotification(HttpContext context, string bucketName)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { await WriteXmlError(context, 404, "NoSuchBucket", "Bucket not found"); return; }

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        // Simple XML parsing for notification config
        var config = new S3NotificationConfiguration();
        if (body.Contains("LambdaFunctionArn"))
        {
            // Parse lambda notification configs from XML
            var matches = System.Text.RegularExpressions.Regex.Matches(body,
                @"<LambdaFunctionConfiguration>.*?<CloudFunction>(.*?)</CloudFunction>.*?<Event>(.*?)</Event>.*?</LambdaFunctionConfiguration>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                config.LambdaFunctionConfigurations.Add(new S3LambdaNotification
                {
                    LambdaFunctionArn = match.Groups[1].Value,
                    Events = new List<string> { match.Groups[2].Value }
                });
            }
        }

        bucket.NotificationConfiguration = config;
        context.Response.StatusCode = 200;
        await context.Response.CompleteAsync();
    }

    private async Task HandleGetBucketNotification(HttpContext context, string bucketName)
    {
        var bucket = _registry.GetS3Bucket(bucketName);
        if (bucket is null) { await WriteXmlError(context, 404, "NoSuchBucket", "Bucket not found"); return; }

        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine("<NotificationConfiguration>");
        if (bucket.NotificationConfiguration is not null)
        {
            foreach (var n in bucket.NotificationConfiguration.LambdaFunctionConfigurations)
            {
                sb.AppendLine("  <LambdaFunctionConfiguration>");
                sb.AppendLine($"    <CloudFunction>{n.LambdaFunctionArn}</CloudFunction>");
                foreach (var evt in n.Events)
                    sb.AppendLine($"    <Event>{evt}</Event>");
                sb.AppendLine("  </LambdaFunctionConfiguration>");
            }
        }
        sb.AppendLine("</NotificationConfiguration>");

        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(sb.ToString());
    }

    private async Task FireS3NotificationAsync(S3Bucket bucket, string key, string eventName, S3Object obj)
    {
        if (bucket.NotificationConfiguration is null) return;

        foreach (var notification in bucket.NotificationConfiguration.LambdaFunctionConfigurations)
        {
            var matches = notification.Events.Any(e =>
                e == eventName ||
                (e.EndsWith("*") && eventName.StartsWith(e[..^1])));

            if (!matches) continue;

            var functionName = notification.LambdaFunctionArn.Split(':').Last();
            var function = _registry.GetLambda(functionName);
            if (function is null) continue;

            var s3Event = new S3Event
            {
                Records = new List<S3Event.S3EventNotificationRecord>
                {
                    new()
                    {
                        EventName = eventName,
                        EventSource = "aws:s3",
                        AwsRegion = _registry.Region,
                        EventTime = DateTime.UtcNow,
                        S3 = new S3Event.S3Entity
                        {
                            Bucket = new S3Event.S3BucketEntity { Name = bucket.Name, Arn = $"arn:aws:s3:::{bucket.Name}" },
                            Object = new S3Event.S3ObjectEntity { Key = key, Size = obj.Data.Length, ETag = obj.ETag }
                        }
                    }
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(s3Event);
                await _executor.InvokeAsync(functionName, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "S3 notification failed for function {Function}", functionName);
            }
        }
    }

    private static async Task WriteXmlError(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Error>
  <Code>{code}</Code>
  <Message>{message}</Message>
  <RequestId>{Guid.NewGuid()}</RequestId>
</Error>");
    }
}
