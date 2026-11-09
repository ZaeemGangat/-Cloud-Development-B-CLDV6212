using System.Net;
using Azure.Storage.Blobs;
using CoffeeNChill.Functions.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CoffeeNChill.Functions.Functions;

/// <summary>
/// HTTP-triggered functions that replace the CoffeeNChill filing cabinet:
/// barista recipe sheets, cleaning manuals and health &amp; safety policies are
/// streamed to/from the "staff-docs" Azure Blob Storage container.
///
/// The brief asks for an Azure File Share here, but Azurite - the local
/// storage emulator this project runs against - only emulates Blob, Queue
/// and Table storage; it does not support Azure Files at all (confirmed in
/// Microsoft's own docs: https://learn.microsoft.com/azure/storage/common/storage-use-azurite).
/// Blob Storage is used instead so upload/list/download genuinely work end
/// to end locally; the same approach (streaming to/from a client registered
/// in DI) would work unchanged against a real Azure File Share on an actual
/// Azure Storage account.
/// </summary>
public class DocumentFunctions
{
    private readonly BlobContainerClient _documentsContainer;
    private readonly ILogger<DocumentFunctions> _logger;

    public DocumentFunctions(BlobContainerClient documentsContainer, ILogger<DocumentFunctions> logger)
    {
        _documentsContainer = documentsContainer;
        _logger = logger;
    }

    // POST /api/documents/upload  (multipart/form-data, file field e.g. "file")
    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> UploadStaffDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
        {
            return await BadRequest(req, "A multipart/form-data Content-Type header is required.");
        }

        MediaTypeHeaderValue mediaType;
        try
        {
            mediaType = MediaTypeHeaderValue.Parse(contentTypeValues.First());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse Content-Type header on UploadStaffDocument");
            return await BadRequest(req, "Content-Type header is malformed.");
        }

        if (!mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) || !mediaType.Boundary.HasValue)
        {
            return await BadRequest(req, "Request must be multipart/form-data with a boundary.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return await BadRequest(req, "Content-Type is missing a multipart boundary.");
        }

        var reader = new MultipartReader(boundary, req.Body);

        string? savedFileName = null;
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync()) is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition)
                || !contentDisposition.DispositionType.Equals("form-data")
                || string.IsNullOrEmpty(contentDisposition.FileName.Value))
            {
                continue; // not a file part (e.g. a plain text form field) - skip it
            }

            var fileName = Path.GetFileName(HeaderUtilities.RemoveQuotes(contentDisposition.FileName).Value);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await section.Body.CopyToAsync(buffer);
            buffer.Position = 0;

            var blobClient = _documentsContainer.GetBlobClient(fileName);
            await blobClient.UploadAsync(buffer, overwrite: true);

            savedFileName = fileName;
            break; // one document per upload request
        }

        if (savedFileName is null)
        {
            return await BadRequest(req, "No file found in the multipart/form-data body. Attach the file under a form field (e.g. 'file').");
        }

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new { fileName = savedFileName, message = "Document uploaded to staff-docs." }, HttpStatusCode.Created);
        return response;
    }

    // GET /api/documents
    [Function("ListStaffDocuments")]
    public async Task<HttpResponseData> ListStaffDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequestData req)
    {
        var files = new List<StaffDocumentResponse>();

        await foreach (var blobItem in _documentsContainer.GetBlobsAsync())
        {
            files.Add(new StaffDocumentResponse
            {
                FileName = blobItem.Name,
                SizeInBytes = blobItem.Properties.ContentLength ?? 0,
                LastModified = blobItem.Properties.LastModified
            });
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(files, HttpStatusCode.OK);
        return response;
    }

    // GET /api/documents/download/{fileName}
    [Function("DownloadStaffDocument")]
    public async Task<HttpResponseData> DownloadStaffDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/download/{fileName}")] HttpRequestData req,
        string fileName)
    {
        var blobClient = _documentsContainer.GetBlobClient(fileName);

        if (!await blobClient.ExistsAsync())
        {
            return await NotFound(req, $"Document '{fileName}' was not found in staff-docs.");
        }

        var download = (await blobClient.DownloadStreamingAsync()).Value;

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", string.IsNullOrEmpty(download.Details.ContentType) ? "application/octet-stream" : download.Details.ContentType);
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        await download.Content.CopyToAsync(response.Body);
        return response;
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new ApiError { Message = message }, HttpStatusCode.BadRequest);
        return response;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.NotFound);
        await response.WriteAsJsonAsync(new ApiError { Message = message }, HttpStatusCode.NotFound);
        return response;
    }
}
