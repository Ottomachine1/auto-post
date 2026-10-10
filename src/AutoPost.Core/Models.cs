using System.Text.Json;

namespace AutoPost.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
}
public abstract class Entity { public string Id { get; set; } = Guid.NewGuid().ToString("N"); }
public sealed class Event : Entity
{
    public string Author { get; set; } = "";
    public string Language { get; set; } = "und";
    public string OriginalSummary { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string EvidenceKey { get; set; } = "";
    public string Relation { get; set; } = "unverified_report";
    public bool PublishedEstimated { get; set; }
    public string Embedding { get; set; } = "[]";
    public string EmbeddingModel { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "新闻";
    public long PublishedAt { get; set; }
    public long CollectedAt { get; set; } = Clock.Now;
    public bool Demo { get; set; }
    public string Fingerprint { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string AnalysisStatus { get; set; } = "pending";
}
public sealed class Analysis : Entity
{
    public string EventId { get; set; } = "";
    public string Result { get; set; } = "{}";
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "v2";
    public int DurationMs { get; set; }
    public string Usage { get; set; } = "{}";
    public long CreatedAt { get; set; } = Clock.Now;
}
public sealed class Draft : Entity
{
    public string Content { get; set; } = "";
    public string Channels { get; set; } = "[]";
    public string? EventId { get; set; }
    public string Status { get; set; } = "draft";
    public int Revision { get; set; } = 1;
    public int? ApprovedRevision { get; set; }
    public bool Demo { get; set; }
    public bool Quarantined { get; set; }
    public string? RuleId { get; set; }
    public int? RuleVersion { get; set; }
    public long CreatedAt { get; set; } = Clock.Now;
    public long UpdatedAt { get; set; } = Clock.Now;
}
public sealed class DraftVersion : Entity
{
    public string DraftId { get; set; } = "";
    public int Revision { get; set; }
    public string Content { get; set; } = "";
    public string Channels { get; set; } = "[]";
    public long CreatedAt { get; set; } = Clock.Now;
}
public sealed class Delivery : Entity
{
    public string DraftId { get; set; } = "";
    public int Revision { get; set; }
    public string Channel { get; set; } = "";
    public string Account { get; set; } = "default";
    public string Status { get; set; } = "queued";
    public string? RemoteId { get; set; }
    public string? Error { get; set; }
    public string? RuleId { get; set; }
    public int? RuleVersion { get; set; }
    public int Attempts { get; set; }
    public long CreatedAt { get; set; } = Clock.Now;
    public long UpdatedAt { get; set; } = Clock.Now;
}
public sealed class Rule : Entity
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public int Version { get; set; } = 1;
    public string Sources { get; set; } = "[]";
    public string Keywords { get; set; } = "[]";
    public string Channels { get; set; } = "[]";
    public string Account { get; set; } = "default";
    public int DailyLimit { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 15;
    public int StartHour { get; set; }
    public int EndHour { get; set; } = 24;
}
public sealed class Source : Entity
{
    public string Category { get; set; } = "world";
    public int Priority { get; set; } = 50;
    public string Topic { get; set; } = "";
    public string Language { get; set; } = "en";
    public string Region { get; set; } = "US";
    public string Publisher { get; set; } = "";
    public string ETag { get; set; } = "";
    public string LastModified { get; set; } = "";
    public int ConsecutiveFailures { get; set; }
    public long SuspendedUntil { get; set; }
    public long? CheckedAt { get; set; }
    public long? LatestPublishedAt { get; set; }
    public int? HttpStatus { get; set; }
    public string Validation { get; set; } = "unchecked";
    public bool EnableAfterValidation { get; set; }
    public int FreshnessDays { get; set; } = 30;
    public string Kind { get; set; } = "rss";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public long NextRun { get; set; }
    public string Cursor { get; set; } = "";
    public string PageToken { get; set; } = "";
    public string PendingCursor { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public long? LastSuccess { get; set; }
}
public sealed class Job : Entity
{
    public int Priority { get; set; } = 50;
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public string Status { get; set; } = "pending";
    public long DueAt { get; set; } = Clock.Now;
    public long LeaseUntil { get; set; }
    public string? LeaseOwner { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}
public sealed class Change
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public long CreatedAt { get; set; } = Clock.Now;
}
public sealed class Audit : Entity
{
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
    public long CreatedAt { get; set; } = Clock.Now;
}
public sealed class Settings
{
    public int Id { get; set; } = 1;
    public bool AutoPaused { get; set; } = true;
    public int AnalysisDailyLimit { get; set; } = 100;
    public int ChannelDailyLimit { get; set; } = 20;
    public long WorkerHeartbeat { get; set; }
}
public sealed class Budget
{
    public string Id { get; set; } = "";
    public int Used { get; set; }
}
public static class Clock
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static string Day => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");
    public static long DayStart => new DateTimeOffset(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
}
public sealed record AnalysisResult(string Summary, string[] Implications, string[] Uncertainties, string Draft, string[]? Evidence = null, string? Category = null, int Importance = 0, string ClaimType = "media_report", string[]? Facts = null, string[]? Reports = null, string[]? Predictions = null, string[]? Rumors = null, string[]? SuggestedChannels = null, string Verification = "unverified");
public static class Rules
{
    public static string? Rejection(Rule rule, Event item, int hour)
    {
        if (!rule.Enabled) return "规则未启用";
        if (item.Demo) return "演示数据不能自动发布";
        if (!Json.Read<string[]>(rule.Sources).Contains(item.Source)) return "来源不在白名单";
        var words = Json.Read<string[]>(rule.Keywords);
        if (words.Length > 0 && !words.Any(w => (item.Title + " " + item.Body).Contains(w, StringComparison.OrdinalIgnoreCase))) return "关键词不匹配";
        var inWindow = rule.StartHour < rule.EndHour ? hour >= rule.StartHour && hour < rule.EndHour : hour >= rule.StartHour || hour < rule.EndHour;
        return inWindow ? null : "不在发布时间窗口";
    }
}
