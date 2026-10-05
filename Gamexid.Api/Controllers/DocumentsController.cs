using Gamexid.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/documents")]
public class DocumentsController(PdfInvoiceExtractor extractor) : ControllerBase
{
    [HttpPost("extract"), EnableRateLimiting("pdf")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> Extract(IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 5 or > PdfInvoiceExtractor.MaxBytes || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Seleccioná un PDF de hasta 10 MB." });
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        try { return Ok(extractor.Extract(buffer.ToArray(), ct)); }
        catch (InvalidDataException e) { return UnprocessableEntity(new { mensaje = e.Message }); }
        catch (Exception e) when (e is not OperationCanceledException && e is not OutOfMemoryException)
        {
            return UnprocessableEntity(new { mensaje = "No se pudo interpretar el PDF. Puede estar protegido, dañado o tener un formato no compatible." });
        }
    }
}
