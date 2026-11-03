using Gamexid.Api.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/catalog/photos"), Authorize]
public class CatalogPhotosController(ProductPageReader pages) : ControllerBase
{
    [HttpGet("{id}"), EnableRateLimiting("photos")]
    public async Task<IActionResult> Get(string id, string webUrl, CancellationToken ct)
    {
        if (id.Length is < 1 or > 20 || id.Any(c => c is < '0' or > '9') ||
            webUrl.Length > 1500 || !ProductPageReader.IsSafePage(webUrl, id))
            return BadRequest(new { mensaje = "Ficha de producto inválida." });
        var product = new GamingCityProduct(id, "", "", "", "", null, null, WebUrl: webUrl);
        var card = await pages.ReadAsync(product, ct);
        return Ok(new { card.ImageUrl });
    }
}
