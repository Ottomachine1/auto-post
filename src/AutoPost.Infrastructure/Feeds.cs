using System.Net;
using System.Net.Http.Headers;
using System.ServiceModel.Syndication;
using System.Xml;
using AutoPost.Core;

namespace AutoPost.Infrastructure;

public sealed class FeedFailure(string code, int delay = 60) : Exception(code)
{
    public string Code { get; } = code;
    public int Delay { get; } = Math.Clamp(delay, 30, 86400);
}
public sealed record FeedDocument(SyndicationFeed? Feed, bool NotModified);
public sealed class FeedReader(IHttpClientFactory factory)
{
    public async Task<FeedDocument> Fetch(Source source, bool conditional, CancellationToken ct)
    {
        source.HttpStatus = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, Network.Validate(source.Address));
        request.Headers.UserAgent.ParseAdd("AutoPostIntelligence/0.3 (+https://auto-post.maxson.cc)");
        request.Headers.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml;q=0.9");
        if (conditional && source.ETag != "") request.Headers.TryAddWithoutValidation("If-None-Match", source.ETag);
        if (conditional && source.LastModified != "") request.Headers.TryAddWithoutValidation("If-Modified-Since", source.LastModified);
        using var response = await factory.CreateClient("rss").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        source.HttpStatus = (int)response.StatusCode; source.CheckedAt = Clock.Now;
        if (response.StatusCode == HttpStatusCode.NotModified && conditional)
        {
            if (source.LatestPublishedAt == null || source.LatestPublishedAt < Clock.Now - source.FreshnessDays * 86400L) throw new FeedFailure("stale_feed");
            return new(null, true);
        }
        if (!response.IsSuccessStatusCode)
        {
            var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60;
            throw new FeedFailure((int)response.StatusCode is 401 or 403 ? "access_restricted" : (int)response.StatusCode is >= 300 and < 400 ? "redirect_requires_review" : "http_" + (int)response.StatusCode, (int)retry);
        }
        if (response.Content.Headers.ContentLength > 2_000_000) throw new FeedFailure("response_too_large");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        { if (bytes.Length + read > 2_000_000) throw new FeedFailure("response_too_large"); bytes.Write(buffer, 0, read); }
        bytes.Position = 0;
        SyndicationFeed feed;
        try
        {
            using var xml = XmlReader.Create(bytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
            feed = SyndicationFeed.Load(xml) ?? throw new FeedFailure("invalid_feed");
        }
        catch (Exception e) when (e is XmlException or FormatException or ArgumentException) { throw new FeedFailure("invalid_xml"); }
        var items = feed.Items.Take(500).ToArray(); feed.Items = items;
        source.LatestPublishedAt = items.Select(i => i.PublishDate != default ? i.PublishDate : i.LastUpdatedTime).Where(t => t != default).Select(t => (long?)t.ToUnixTimeSeconds()).DefaultIfEmpty().Max();
        if (items.Length == 0) throw new FeedFailure("empty_feed");
        if (source.LatestPublishedAt == null) throw new FeedFailure("missing_dates");
        if (source.LatestPublishedAt < Clock.Now - source.FreshnessDays * 86400L) throw new FeedFailure("stale_feed");
        if (source.LatestPublishedAt > Clock.Now + 86400) throw new FeedFailure("future_dates");
        source.ETag = response.Headers.ETag?.ToString() ?? "";
        source.LastModified = response.Content.Headers.LastModified?.ToString("R") ?? "";
        source.Validation = "valid";
        return new(feed, false);
    }
    public async Task<bool> Validate(Source source, CancellationToken ct)
    {
        try
        {
            await Fetch(source, false, ct); source.Status = "validated"; source.Error = null;
            source.ConsecutiveFailures = 0; source.SuspendedUntil = 0; return true;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        { Failure(source, e); source.Validation = "failed"; source.Enabled = false; return false; }
    }
    public static void Failure(Source s, Exception error)
    {
        s.CheckedAt = Clock.Now; s.ConsecutiveFailures++;
        var delay = Math.Max(error is FeedFailure f ? f.Delay : 60, Math.Min(86400, 60 * (1 << Math.Min(s.ConsecutiveFailures, 10))));
        s.NextRun = Clock.Now + delay;
        s.SuspendedUntil = s.ConsecutiveFailures >= 3 ? s.NextRun : 0;
        s.Status = s.ConsecutiveFailures >= 3 ? "suspended" : "error";
        s.Error = error is FeedFailure failure ? failure.Code : error is OperationCanceledException ? "timeout" : error is HttpRequestException http ? "network_" + http.HttpRequestError.ToString().ToLowerInvariant() : "parse_error";
    }
}
public static class NewsTopics
{
    public static string Url(string topic, string language = "en", string region = "US")
    {
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 300 || language is not ("en" or "zh-CN") || region is not ("US" or "CN" or "GB")) throw new ArgumentException("关键词或语言地区无效");
        var edition = language == "zh-CN" ? "zh-Hans" : "en";
        return "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(topic.Trim()) + "&hl=" + (language == "en" ? "en-" + region : language) + "&gl=" + region + "&ceid=" + region + ":" + edition;
    }
}
