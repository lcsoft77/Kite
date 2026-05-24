namespace Kite.Core.Models;

public class SsmParameter
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
    public int Version { get; set; } = 1;
    public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;
}
