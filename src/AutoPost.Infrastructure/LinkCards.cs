using System.Net;
using System.Text.RegularExpressions;

namespace AutoPost.Infrastructure;
public static partial class LinkCards
{
    public static async Task<object> Read(string url, IHttpClientFactory factory, CancellationToken ct)
    {
        Network.Validate(url);
        using var response = await factory.CreateClient("rss").GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml")) throw new ArgumentException("此链接不是网页，可保留原始链接发布");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk,ct)) > 0) { if(buffer.Length+count>262144) break; await buffer.WriteAsync(chunk.AsMemory(0,count),ct); }
        var html = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        var title = Title().Match(html).Groups[1].Value;
        var description = "";
        foreach (Match tag in Meta().Matches(html)) {
            var fields = Attribute().Matches(tag.Value).ToDictionary(m=>m.Groups[1].Value.ToLowerInvariant(),m=>WebUtility.HtmlDecode(m.Groups[3].Value),StringComparer.OrdinalIgnoreCase);
            var name = fields.GetValueOrDefault("property") ?? fields.GetValueOrDefault("name");
            if(name=="og:title") title=fields.GetValueOrDefault("content")??title;
            if(name is "description" or "og:description") description=fields.GetValueOrDefault("content")??description;
        }
        return new {url,title=Network.Text(WebUtility.HtmlDecode(title))[..Math.Min(Network.Text(WebUtility.HtmlDecode(title)).Length,300)],description=Network.Text(description)[..Math.Min(Network.Text(description).Length,500)]};
    }
    [GeneratedRegex(@"<title[^>]*>(.*?)</title>",RegexOptions.IgnoreCase|RegexOptions.Singleline,1000)] private static partial Regex Title();
    [GeneratedRegex(@"<meta\s[^>]*>",RegexOptions.IgnoreCase,1000)] private static partial Regex Meta();
    [GeneratedRegex("([a-zA-Z-]+)\\s*=\\s*([\"'])(.*?)\\2",RegexOptions.Singleline,1000)] private static partial Regex Attribute();
}
