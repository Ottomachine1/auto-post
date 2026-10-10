using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AutoPost.Core;
namespace AutoPost.Infrastructure;

public static class LocalTranslation
{
    public static bool NativeChinese(string text)
    {
        var han = Regex.Matches(text, "[\\p{IsCJKUnifiedIdeographs}]").Count;
        var latin = Regex.Matches(text, "[a-zA-Z]").Count;
        return han > 0 && han >= latin;
    }
    public static async Task Run(Event item, IHttpClientFactory factory, CancellationToken ct)
    {
        if (NativeChinese(item.Title) && (string.IsNullOrWhiteSpace(item.Body) || NativeChinese(item.Body)))
        {
            item.ChineseTitle = item.Title; item.ChineseBody = item.Body;
            item.TranslationStatus = "native"; item.TranslationEngine = "original-zh"; return;
        }
        var endpoint = Connectors.Env("TRANSLATION_URL");
        if (endpoint == "") throw new RetryLater(300);
        var source = item.Language.Split('-')[0].ToLowerInvariant();
        if (source is "" or "und") source = "auto";
        // Chinese feeds can contain English articles; translate those through English.
        if (source == "zh") source = "en";
        using var response = await factory.CreateClient("translation").PostAsJsonAsync(endpoint.TrimEnd('/') + "/translate", new { title = item.Title, body = item.Body, source }, ct);
        if ((int)response.StatusCode is 429 or 503) throw new RetryLater(60);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Result>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty translation");
        if (result.Engine != "argos-offline" || string.IsNullOrWhiteSpace(result.Title) || (item.Body.Length > 0 && string.IsNullOrWhiteSpace(result.Body))) throw new InvalidOperationException("Invalid translation");
        item.ChineseTitle = result.Title; item.ChineseBody = result.Body;
        item.TranslationStatus = "completed"; item.TranslationEngine = result.Engine;
    }
    private sealed record Result(string Title, string Body, string Engine);
}
