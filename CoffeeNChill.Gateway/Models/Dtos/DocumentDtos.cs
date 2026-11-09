using System.ComponentModel.DataAnnotations;

namespace CoffeeNChill.Gateway.Models.Dtos;

/// <summary>multipart/form-data body for POST /api/documents.</summary>
public class DocumentUploadRequest
{
    /// <summary>The operational file (recipe sheet, cleaning manual, policy...).</summary>
    [Required]
    public IFormFile File { get; set; } = default!;
}

/// <summary>A stored staff document.</summary>
public class StaffDocumentResponse
{
    public string FileName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset? LastModified { get; set; }
}

/// <summary>Standard error body.</summary>
public class ApiError
{
    public string Message { get; set; } = string.Empty;

    public ApiError() { }
    public ApiError(string message) => Message = message;
}
