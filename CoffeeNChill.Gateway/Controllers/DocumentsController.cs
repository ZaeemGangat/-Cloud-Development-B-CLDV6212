using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace CoffeeNChill.Gateway.Controllers;

/// <summary>
/// Staff operational documents (recipe sheets, cleaning manuals, policies) in the
/// "staff-docs" Blob container. Every endpoint requires an Admin or Staff token.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize(Roles = Roles.AdminOrStaff)]
[Tags("3. Staff Documents")]
public class DocumentsController : ControllerBase
{
    /// <summary>20 MB upload limit.</summary>
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".md", ".png", ".jpg", ".jpeg"
    };

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly IDocumentStore _documents;

    public DocumentsController(IDocumentStore documents) => _documents = documents;

    /// <summary>Upload a staff document (Admin/Staff).</summary>
    /// <remarks>
    /// Send as multipart/form-data with the file in the `file` field.
    /// Allowed types: PDF, Office documents, text/CSV/Markdown and images, up to 20 MB.
    /// Uploading a file with an existing name replaces it.
    /// </remarks>
    /// <response code="201">Uploaded.</response>
    /// <response code="400">Missing, empty or disallowed file.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType(typeof(StaffDocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload([FromForm] DocumentUploadRequest request, CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiError("Attach a non-empty file in the 'file' form field."));
        }

        // Path.GetFileName strips any directory parts ("../../x.pdf" → "x.pdf").
        var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(fileName))
        {
            return BadRequest(new ApiError("The uploaded file has no name."));
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(fileName)))
        {
            return BadRequest(new ApiError($"File type not allowed. Allowed: {string.Join(", ", AllowedExtensions.Order())}."));
        }

        var contentType = ContentTypes.TryGetContentType(fileName, out var detected) ? detected : "application/octet-stream";

        await using (var stream = file.OpenReadStream())
        {
            await _documents.UploadAsync(fileName, stream, contentType, cancellationToken);
        }

        var response = new StaffDocumentResponse
        {
            FileName = fileName,
            SizeInBytes = file.Length,
            ContentType = contentType,
            LastModified = DateTimeOffset.UtcNow
        };
        return CreatedAtAction(nameof(Download), new { fileName }, response);
    }

    /// <summary>List all staff documents (Admin/Staff).</summary>
    /// <response code="200">Documents in staff-docs.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IEnumerable<StaffDocumentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _documents.ListAsync(cancellationToken));

    /// <summary>Download a staff document (Admin/Staff).</summary>
    /// <param name="fileName">e.g. "barista-recipe-sheet.txt"</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The file.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    /// <response code="404">No such document.</response>
    [HttpGet("{fileName}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string fileName, CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(fileName);
        var document = await _documents.OpenReadAsync(safeName, cancellationToken);
        return document is null
            ? NotFound(new ApiError($"Document '{safeName}' was not found."))
            : File(document.Value.Content, document.Value.ContentType, safeName);
    }

    /// <summary>Delete a staff document (Admin/Staff).</summary>
    /// <param name="fileName">e.g. "barista-recipe-sheet.txt"</param>
    /// <param name="cancellationToken"></param>
    /// <response code="204">Deleted.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    /// <response code="404">No such document.</response>
    [HttpDelete("{fileName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string fileName, CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(fileName);
        return await _documents.DeleteAsync(safeName, cancellationToken)
            ? NoContent()
            : NotFound(new ApiError($"Document '{safeName}' was not found."));
    }
}
