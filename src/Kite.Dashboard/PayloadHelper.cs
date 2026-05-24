namespace Kite.Dashboard;

public static class PayloadHelper
{
    // Only allow simple alphanumeric/dash/underscore category names to prevent path traversal.
    private static readonly System.Text.RegularExpressions.Regex SafeCategory =
        new(@"^[a-zA-Z0-9_\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string GetPayloadsDir(string category)
    {
        if (!SafeCategory.IsMatch(category))
            throw new ArgumentException($"Invalid payload category: '{category}'.", nameof(category));

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "payloads", category);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static List<string> GetPayloadFiles(string category)
    {
        var dir = GetPayloadsDir(category);
        return Directory.GetFiles(dir, "*.json")
            .OrderBy(f => f)
            .ToList();
    }

    public static async Task SavePayloadAsync(string category, string content)
    {
        var dir = GetPayloadsDir(category);
        // Include milliseconds and a short GUID fragment to avoid second-level collisions.
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"payload_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{shortId}.json";
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, content);
    }
}
