namespace Kite.Core.Auth;

public static class SigV4Validator
{
    public static bool Validate(string? authorizationHeader, string expectedAccessKeyId)
    {
        if (string.IsNullOrEmpty(authorizationHeader))
            return false;

        var credential = ExtractCredential(authorizationHeader);
        if (credential is null)
            return false;

        var parts = credential.Split('/');
        return parts.Length >= 1 && parts[0] == expectedAccessKeyId;
    }

    public static string? ExtractServiceName(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader))
            return null;

        var credential = ExtractCredential(authorizationHeader);
        if (credential is null)
            return null;

        // Format: AKID/date/region/service/aws4_request
        var parts = credential.Split('/');
        return parts.Length >= 4 ? parts[3] : null;
    }

    private static string? ExtractCredential(string authorizationHeader)
    {
        // AWS4-HMAC-SHA256 Credential=AKID/date/region/service/aws4_request, SignedHeaders=..., Signature=...
        const string credentialPrefix = "Credential=";
        var credIdx = authorizationHeader.IndexOf(credentialPrefix, StringComparison.OrdinalIgnoreCase);
        if (credIdx < 0)
            return null;

        var start = credIdx + credentialPrefix.Length;
        var end = authorizationHeader.IndexOf(',', start);
        return end < 0
            ? authorizationHeader[start..].Trim()
            : authorizationHeader[start..end].Trim();
    }
}
