namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Response DTO for a single file listed from the "staff-docs" Azure File Share.
/// </summary>
public class StaffDocumentResponse
{
    public string FileName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}
