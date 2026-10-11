namespace AutoPost.Core;

public sealed class AgentSession : Entity
{
    public string Title { get; set; } = "新情报会话";
    public bool Demo { get; set; }
    public long CreatedAt { get; set; } = Clock.Now;
    public long UpdatedAt { get; set; } = Clock.Now;
}
public sealed class AgentMessage : Entity
{
    public string SessionId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Action { get; set; } = "latest";
    public string? EventId { get; set; }
    public string Status { get; set; } = "queued";
    public string Progress { get; set; } = "等待执行";
    public string Result { get; set; } = "{}";
    public string Model { get; set; } = "";
    public string Usage { get; set; } = "{}";
    public bool CancelRequested { get; set; }
    public long CreatedAt { get; set; } = Clock.Now;
    public long UpdatedAt { get; set; } = Clock.Now;
}
public sealed record AgentCitation(string Id, string Title, string Url, string Source, long PublishedAt, long CollectedAt);
public sealed record AgentAnswer(string Summary, string[] Facts, string[] Reports, string[] Predictions, string[] Rumors, string[] Uncertainties, AgentCitation[] Sources, long AsOf, string? DraftId = null, string? Draft = null);
