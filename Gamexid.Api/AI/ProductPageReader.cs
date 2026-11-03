using System.Net;
using System.Text.RegularExpressions;
using Gamexid.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Gamexid.Api.AI;

public class ProductPageReader(HttpClient http, IMemoryCache cache)
{
    public async Task<AiProductCard> ReadAsync(GamingCityProduct product, CancellationToken ct)
    {
        var card = new AiProductCard(product.Id, product.Name, product.Code, product.Description, null, product.WebUrl);
        if (product.WebUrl == null) return card;
        if (!IsSafePage(product.WebUrl, product.Id)) return card with { WebUrl = null };
        var uri = new Uri(product.WebUrl);
        var key = "product-photo:" + product.WebUrl;
        if (cache.TryGetValue<string>(key, out var image)) return card with { ImageUrl = string.IsNullOrEmpty(image) ? null : image };
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_500_000) {
                cache.Set(key, "", TimeSpan.FromMinutes(2));
                return card;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, ct)) > 0)
            {
                if (buffer.Length + read > 1_500_000) return card;
                buffer.Write(bytes, 0, read);
            }
            var html = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            foreach (Match tag in Regex.Matches(html, "<meta\\s[^>]{1,3000}>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150)))
            {
                if (!Regex.IsMatch(tag.Value, "property\\s*=\\s*[\"']og:image[\"']", RegexOptions.IgnoreCase)) continue;
                var match = Regex.Match(tag.Value, "content\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                var url = WebUtility.HtmlDecode(match.Groups[1].Value);
                if (IsSafeImage(url))
                {
                    cache.Set(key, url, TimeSpan.FromHours(1));
                    return card with { ImageUrl = url };
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or RegexMatchTimeoutException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested) { }
        cache.Set(key, "", TimeSpan.FromMinutes(2));
        return card;
    }

    public static bool IsSafePage(string url, string id) => Uri.TryCreate(url, UriKind.Absolute, out var page) &&
        page.Scheme == "https" && page.Host is "www.gamingcity.com.ar" or "gamingcity.com.ar" &&
        page.IsDefaultPort && page.UserInfo.Length == 0 && page.Query.Length == 0 && page.Fragment.Length == 0 &&
        page.AbsolutePath.EndsWith("--det--" + id, StringComparison.Ordinal);

    public static bool IsSafeImage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var image) &&
        image.Scheme == "https" && image.Host is "www.gamingcity.com.ar" or "gamingcity.com.ar" &&
        image.IsDefaultPort && image.UserInfo.Length == 0 && image.Query.Length == 0 && image.Fragment.Length == 0 &&
        Regex.IsMatch(image.AbsolutePath, @"^/images/[a-zA-Z0-9_./-]+\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase);
}
