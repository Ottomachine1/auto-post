using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
namespace AutoPost.Infrastructure;

public static class ManualTranslations
{
    public static string Hash(string title, string body, string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(new { title, body, source }))));
    public static string[] AllowedTargets => (Connectors.Env("TRANSLATION_TARGETS") is { Length: > 0 } value ? value : "zh").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(t => t is "zh" or "en" or "ja" or "ko" or "fr" or "de" or "es" or "ru" or "ar").Distinct().ToArray();
    public static async Task<Translation> Request(Store db, string title, string body, string source, string target, CancellationToken ct)
    {
        if (title is null || body is null || source is null || target is null) throw new ArgumentException("翻译参数不能为空");
        if (!AllowedTargets.Contains(target)) throw new ArgumentException("目标语言未配置本地模型");
        if (title.Length > 1000 || body.Length > 25000 || string.IsNullOrWhiteSpace(title + body)) throw new ArgumentException("翻译内容为空或过长");
        source = source.Split('-')[0].ToLowerInvariant(); if (source is "und" or "") source = "auto";
        var hash = Hash(title, body, source);
        var cached = await db.Translations.SingleOrDefaultAsync(t => t.ContentHash == hash && t.TargetLanguage == target && t.Demo == Registration.Demo, ct);
        if (cached is { Status: "completed" or "native" }) return cached;
        if (cached == null)
        {
            cached = new Translation { ContentHash = hash, Title = title, Body = body, SourceLanguage = source, TargetLanguage = target, Demo = Registration.Demo };
            db.Translations.Add(cached);
        }
        cached.Status = "pending"; cached.Error = null; cached.UpdatedAt = Clock.Now;
        var job = await db.Enqueue("translate_manual", cached.Id, ct); job.Priority = 100; job.DueAt = Clock.Now;
        db.Mark("translation_requested", cached.Id, "target=" + target); return cached;
    }
    public static async Task<Capabilities> Languages(IHttpClientFactory factory, CancellationToken ct)
    {
        var url = Connectors.Env("TRANSLATION_URL");
        if (url == "") return new([], false, "argos-offline");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try {
            var health = await factory.CreateClient("translation").GetFromJsonAsync<Health>(url.TrimEnd('/') + "/health", timeout.Token);
            var targets = health?.Engine == "argos-offline" ? AllowedTargets.Intersect(health.Targets ?? []).ToArray() : [];
            return new(targets, targets.Length > 0, "argos-offline");
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) {
            if (ct.IsCancellationRequested) throw;
            return new([], false, "argos-offline");
        }
    }
    public sealed record Capabilities(string[] Targets, bool Configured, string Engine);
    private sealed record Health(string Engine, string[]? Targets);
    public static async Task Execute(Store db, string id, IHttpClientFactory factory, CancellationToken ct)
    {
        var item = await db.Translations.FindAsync([id], ct);
        if (item == null || item.Demo != Registration.Demo || item.Status is "completed" or "native") return;
        if (item.TargetLanguage == "zh" && (string.IsNullOrWhiteSpace(item.Title) || LocalTranslation.NativeChinese(item.Title)) && (string.IsNullOrWhiteSpace(item.Body) || LocalTranslation.NativeChinese(item.Body)))
        {
            item.TranslatedTitle = item.Title; item.TranslatedBody = item.Body; item.Status = "native"; item.Engine = "original-zh";
        }
        else
        {
            if (Registration.Demo) throw new InvalidOperationException("演示模式不调用真实翻译服务");
            var url = Connectors.Env("TRANSLATION_URL"); if (url == "") throw new RetryLater(60);
            item.Status = "running"; item.UpdatedAt = Clock.Now; await db.SaveChangesAsync(ct);
            using var content = new StringContent(Json.Write(new { title = item.Title, body = item.Body, source = "auto", target = item.TargetLanguage }), Encoding.UTF8, "application/json");
            using var response = await factory.CreateClient("translation").PostAsync(url.TrimEnd('/') + "/translate", content, ct);
            if ((int)response.StatusCode is 429 or 503) throw new RetryLater(60);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Result>(cancellationToken: ct) ?? throw new InvalidOperationException("翻译响应为空");
            if (result.Engine != "argos-offline" || (item.Title.Trim() != "" && string.IsNullOrWhiteSpace(result.Title)) || (item.Body.Trim() != "" && string.IsNullOrWhiteSpace(result.Body))) throw new InvalidOperationException("翻译响应无效");
            item.TranslatedTitle = result.Title; item.TranslatedBody = result.Body; item.Status = "completed"; item.Engine = result.Engine;
        }
        item.Error = null; item.UpdatedAt = Clock.Now; db.Mark("translation", item.Id);
    }
    private sealed record Result(string Title, string Body, string Engine);
}
