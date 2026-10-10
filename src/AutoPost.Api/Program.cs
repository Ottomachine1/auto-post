using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddAutoPost(Registration.Connection);
builder.Services.AddAuthentication("session").AddCookie("session", o =>
{
    o.Cookie.Name = "atlas-session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddSignalR();
builder.Services.AddHostedService<ChangeRelay>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Environment.GetEnvironmentVariable("KEYS_PATH") ?? "data/keys"));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    // Containers only expose loopback on the host; explicitly trust that bridge, not arbitrary clients.
    if (System.Net.IPAddress.TryParse(Environment.GetEnvironmentVariable("TRUSTED_PROXY"), out var proxy)) o.KnownProxies.Add(proxy);
});
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<Store>().Database.MigrateAsync();
    Console.WriteLine("Database migration complete."); return;
}
var admin = Connectors.Env("ADMIN_TOKEN");
if (admin.Length < 32) throw new InvalidOperationException("ADMIN_TOKEN 必须至少32字符");
app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' ws: wss:; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/hubs")) ctx.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { detail = ex.Message }); } }
    catch (Exception) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { detail = "服务暂不可用，请检查系统状态" }); } }
});
app.UseAuthentication();
app.Use(async (ctx, next) =>
{
    var supplied = ctx.Request.Headers.Authorization.ToString();
    if (supplied.StartsWith("Bearer ") && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(admin)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied[7..]))))
    { ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "bearer")); }
    var origin = ctx.Request.Headers.Origin.ToString();
    if ((ctx.Request.Path.StartsWithSegments("/hubs") || ctx.Request.Method != "GET") && origin != "" && origin != $"{ctx.Request.Scheme}://{ctx.Request.Host}") { ctx.Response.StatusCode = 403; return; }
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.Identity.AuthenticationType != "bearer" && ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    { try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); } catch (AntiforgeryValidationException) { ctx.Response.StatusCode = 403; return; } }
    await next();
});
app.UseAuthorization();
var loginAttempts = new System.Collections.Concurrent.ConcurrentDictionary<string, (long Start, int Count)>();
app.MapPost("/api/session", async (Login input, HttpContext ctx, IAntiforgery csrf) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var attempt = loginAttempts.AddOrUpdate(ip, (Clock.Now, 1), (_, old) => Clock.Now - old.Start > 60 ? (Clock.Now, 1) : (old.Start, old.Count + 1));
    if (attempt.Count > 10) return Results.StatusCode(429);
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(admin)), SHA256.HashData(Encoding.UTF8.GetBytes(input.Token)))) return Results.Unauthorized();
    await ctx.SignInAsync("session", new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "session")));
    return Results.Ok(new { ok = true });
});
app.MapGet("/api/health", async (Store db) => { var ready = await db.Database.CanConnectAsync(); return Results.Json(new { status = ready ? "ok" : "unavailable", mode = Registration.Demo ? "demo" : "live" }, statusCode: ready ? 200 : 503); });
var api = app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async (ctx, next) =>
{
    if (ctx.HttpContext.Request.Method == "GET") return await next(ctx);
    var db = ctx.HttpContext.RequestServices.GetRequiredService<Store>();
    await using var tx = await db.Database.BeginTransactionAsync(); await db.Lock();
    var result = await next(ctx); await db.SaveChangesAsync(); await tx.CommitAsync(); return result;
});
api.MapGet("/session", (HttpContext ctx, IAntiforgery csrf) => new { csrfToken = csrf.GetAndStoreTokens(ctx).RequestToken });
api.MapDelete("/session", async (HttpContext ctx) => { await ctx.SignOutAsync("session"); return Results.Ok(); });
api.MapGet("/events", async (Store db, string? source, string? q, int? limit) =>
    (await Query(db, source, q, null).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync()).Select(EventDto));
api.MapGet("/events/page", async (Store db, string? source, string? q, string? category, string? cursor) =>
{
    IQueryable<Event> query = Query(db, source, q, category);
    if (cursor != null)
    {
        var split = cursor.Split(':', 2);
        if (split.Length != 2 || !long.TryParse(split[0], out var stamp)) throw new ArgumentException("分页游标无效");
        var id = split[1]; query = query.Where(e => e.PublishedAt < stamp || e.PublishedAt == stamp && string.Compare(e.Id, id) < 0);
    }
    var rows = await query.Take(51).ToListAsync(); var page = rows.Take(50).ToList();
    return new { items = page.Select(EventDto), nextCursor = rows.Count > 50 ? page[^1].PublishedAt + ":" + page[^1].Id : null };
});
api.MapGet("/events/{id}", async (string id, Store db) =>
{
    var item = await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Demo == Registration.Demo);
    if (item == null) return Results.NotFound();
    return Results.Ok(new { item = EventDto(item), analysis = await db.Analyses.FirstOrDefaultAsync(a => a.EventId == id), related = (await db.Events.Where(e => e.GroupId == item.GroupId && e.Demo == Registration.Demo).Take(30).ToListAsync()).Select(EventDto), drafts = await db.Drafts.Where(d => d.EventId == id && d.Demo == Registration.Demo).ToListAsync() });
});
api.MapPost("/events/{id}/analysis", async (string id, Store db) =>
{
    var item = await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Demo == Registration.Demo) ?? throw new ArgumentException("事件不存在");
    var cached = await db.Analyses.FirstOrDefaultAsync(a => a.EventId == id);
    if (cached != null) return Results.Content(cached.Result, "application/json");
    if (item.Demo) throw new InvalidOperationException("演示事件不调用真实模型");
    item.AnalysisStatus = "pending"; var job = await db.Enqueue("analyse", id); return Results.Accepted(value: new { taskId = job.Id, status = "queued" });
});
api.MapGet("/changes", async (Store db, long? after) => await db.Changes.AsNoTracking().Where(c => c.Id > (after ?? 0)).OrderBy(c => c.Id).Take(200).ToListAsync());
api.MapGet("/stream", async (HttpContext ctx, IServiceScopeFactory scopes) =>
{
    ctx.Response.ContentType = "text/event-stream"; ctx.Response.Headers["X-Accel-Buffering"] = "no";
    long.TryParse(ctx.Request.Headers["Last-Event-ID"], out var after);
    while (!ctx.RequestAborted.IsCancellationRequested)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<Store>();
        var changes = await db.Changes.Where(c => c.Id > after).OrderBy(c => c.Id).Take(200).ToListAsync(ctx.RequestAborted);
        if (changes.Count > 0)
        { after = changes[^1].Id; var rows = await Query(db, null, null, null).Take(50).ToListAsync(ctx.RequestAborted); await ctx.Response.WriteAsync($"id: {after}\nevent: events\ndata: {Json.Write(rows.Select(EventDto))}\n\n", ctx.RequestAborted); }
        else await ctx.Response.WriteAsync(": heartbeat\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted); await Task.Delay(1500, ctx.RequestAborted);
    }
});
api.MapGet("/status", async (Store db) => new
{
    mode = Registration.Demo ? "demo" : "live",
    sources = await db.Sources.ToListAsync(),
    settings = await db.Settings.SingleAsync(),
    ai_configured = Connectors.Env("OPENAI_API_KEY") != "",
    channels = Connectors.Channels.Select(c => new { id = c, label = c, mode = c is "x" or "telegram" ? "api" : "manual_export", configured = Connectors.Configured(c) }),
    events = await db.Events.CountAsync(e => e.Demo == Registration.Demo),
    queue = await db.Jobs.GroupBy(j => j.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(),
    usage = await db.Budgets.Where(b => b.Id.EndsWith(Clock.Day)).ToListAsync()
});
api.MapPut("/settings", async (SettingsInput input, Store db) =>
{
    if (input.AnalysisDailyLimit is < 0 or > 10000 || input.ChannelDailyLimit is < 0 or > 10000) throw new ArgumentException("限额应在0至10000之间");
    var setting = await db.Settings.SingleAsync(); setting.AutoPaused = input.AutoPaused; setting.AnalysisDailyLimit = input.AnalysisDailyLimit; setting.ChannelDailyLimit = input.ChannelDailyLimit;
    db.Mark("settings", "global"); return Results.Ok(setting);
});
api.MapGet("/drafts", async (Store db) =>
{
    var drafts = await db.Drafts.Where(d => d.Demo == Registration.Demo || d.Quarantined).OrderByDescending(d => d.CreatedAt).Take(100).ToListAsync();
    var ids = drafts.Select(d => d.Id).ToArray(); var deliveries = await db.Deliveries.Where(d => ids.Contains(d.DraftId)).ToListAsync();
    return drafts.Select(d => new { d.Id, d.Content, channels = Json.Read<string[]>(d.Channels), event_id = d.EventId, d.Status, d.Revision, d.ApprovedRevision, d.Demo, d.Quarantined, d.RuleId, d.RuleVersion, created_at = d.CreatedAt, deliveries = deliveries.Where(x => x.DraftId == d.Id) });
});
api.MapPost("/drafts", async (DraftInput input, Store db, Pipeline pipeline) =>
{
    ValidateDraft(input);
    if (input.EventId != null && !await db.Events.AnyAsync(e => e.Id == input.EventId && e.Demo == Registration.Demo)) throw new ArgumentException("来源事件不存在");
    var draft = pipeline.NewDraft(input.Content, input.Channels, input.EventId, Registration.Demo); return Results.Ok(new { draft.Id, draft.Status });
});
api.MapPut("/drafts/{id}", async (string id, DraftInput input, Store db, Pipeline pipeline) =>
{
    ValidateDraft(input); var draft = await GetDraft(db, id);
    if (input.Revision != draft.Revision) throw new InvalidOperationException("草稿版本已变化，请刷新");
    if (await db.Deliveries.AnyAsync(d => d.DraftId == id && (d.Status == "sending" || d.Status == "queued" || d.Status == "unknown"))) throw new InvalidOperationException("请先处理正在发送或结果不明的投递");
    draft.Content = input.Content; draft.Channels = Json.Write(input.Channels); draft.Revision++; draft.ApprovedRevision = null; draft.Status = "draft"; draft.RuleId = null; draft.RuleVersion = null; draft.UpdatedAt = Clock.Now;
    pipeline.Snapshot(draft); db.Mark("draft_edited", id, "revision=" + draft.Revision); return Results.Ok(draft);
});
api.MapGet("/drafts/{id}/versions", async (string id, Store db) => { await GetDraft(db, id); return await db.Versions.Where(v => v.DraftId == id).OrderByDescending(v => v.Revision).ToListAsync(); });
api.MapPost("/drafts/{id}/approve", async (string id, int? revision, Store db) =>
{
    var d = await GetDraft(db, id); if (d.Quarantined || d.Status != "draft") throw new InvalidOperationException("隔离草稿或当前状态不可审核");
    if (revision != d.Revision) throw new InvalidOperationException("审核版本已变化或未指定，请刷新并审核明确版本");
    d.ApprovedRevision = d.Revision; d.Status = "approved"; db.Mark("approved", id, "revision=" + d.Revision); return Results.Ok(new { status = d.Status });
});
api.MapPost("/drafts/{id}/publish", async (string id, Store db, Pipeline pipeline) => { var d = await GetDraft(db, id); await pipeline.QueuePublish(d, null, default); return Results.Accepted(value: new { status = "queued" }); });
api.MapPost("/drafts/preview", (DraftInput input) =>
{
    ValidateDraft(input); return input.Channels.Select(c => new { channel = c, content = input.Content, error = Connectors.ContentError(c, input.Content), length = c == "x" ? Connectors.XLength(input.Content) : input.Content.EnumerateRunes().Count(), mode = c is "x" or "telegram" ? "api" : "manual_export" });
});
api.MapPost("/deliveries/{id}/retry", async (string id, Store db) =>
{
    var d = await db.Deliveries.FindAsync(id) ?? throw new ArgumentException("投递不存在"); await GetDraft(db, d.DraftId);
    if (d.Status is not ("failed" or "blocked")) throw new InvalidOperationException("只有明确失败或被规则拦截的投递允许重试");
    d.Status = "queued"; d.Error = null; await db.Enqueue("publish", id); db.Mark("delivery_retry", id); return Results.Accepted();
});
api.MapPost("/deliveries/{id}/resolve", async (string id, ResolveInput input, Store db) =>
{
    var d = await db.Deliveries.FindAsync(id) ?? throw new ArgumentException("投递不存在"); await GetDraft(db, d.DraftId);
    if (d.Status != "unknown" || input.Status is not ("published" or "failed") || string.IsNullOrWhiteSpace(input.Note) || input.Status == "published" && string.IsNullOrWhiteSpace(input.RemoteId)) throw new ArgumentException("需核验说明，已发布还需远端ID");
    d.Status = input.Status; d.RemoteId = input.RemoteId; d.Error = input.Note; d.UpdatedAt = Clock.Now; db.Mark("delivery_resolved", id, Json.Write(input)); return Results.Ok(d);
});
api.MapGet("/rules", async (Store db) => (await db.Rules.ToListAsync()).Select(RuleDto));
api.MapPost("/rules", (RuleInput input, Store db) => { var r = new Rule(); ApplyRule(r, input); r.Enabled = false; db.Rules.Add(r); db.Mark("rule_created", r.Id); return Results.Ok(RuleDto(r)); });
api.MapPut("/rules/{id}", async (string id, RuleInput input, Store db) => { var r = await db.Rules.FindAsync(id) ?? throw new ArgumentException("规则不存在"); ApplyRule(r, input); r.Version++; db.Mark("rule_updated", id, "version=" + r.Version); return Results.Ok(RuleDto(r)); });
api.MapDelete("/rules/{id}", async (string id, Store db) => { var r = await db.Rules.FindAsync(id); if (r != null) db.Rules.Remove(r); db.Mark("rule_deleted", id); return Results.Ok(); });
api.MapPost("/rules/{id}/test", async (string id, Store db) =>
{
    var r = await db.Rules.FindAsync(id) ?? throw new ArgumentException("规则不存在");
    var enabled = r.Enabled; r.Enabled = true;
    var rows = await db.Events.Where(e => e.Demo == Registration.Demo).OrderByDescending(e => e.PublishedAt).Take(50).ToListAsync();
    var result = rows.Select(e => new { e.Id, e.Title, reason = AutoPost.Core.Rules.Rejection(r, e, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Hour) ?? "匹配（仍需通过配额及渠道检查）" }).ToList();
    r.Enabled = enabled; return Results.Ok(result);
});
api.MapGet("/sources", async (Store db) => await db.Sources.ToListAsync());
api.MapPost("/sources", (SourceInput input, Store db) => { var s = new Source(); ApplySource(s, input); db.Sources.Add(s); db.Mark("source_created", s.Id); return Results.Ok(s); });
api.MapPut("/sources/{id}", async (string id, SourceInput input, Store db) =>
{
    var s = await db.Sources.FindAsync(id) ?? throw new ArgumentException("来源不存在");
    if (await db.Jobs.AnyAsync(j => j.Target == id && j.Kind == "collect" && j.Status == "running")) throw new InvalidOperationException("采集中，请稍后修改来源");
    if (s.Address != input.Address) { s.Cursor = ""; s.PageToken = ""; s.PendingCursor = ""; }
    ApplySource(s, input); db.Mark("source_updated", id); return Results.Ok(s);
});
api.MapPost("/collect", async (Store db) => { if (Registration.Demo) throw new InvalidOperationException("演示模式不采集真实数据"); foreach (var s in await db.Sources.Where(s => s.Enabled).ToListAsync()) await db.Enqueue("collect", s.Id); return Results.Accepted(value: new { status = "queued" }); });
api.MapGet("/tasks", async (Store db) => await db.Jobs.OrderByDescending(j => j.DueAt).Take(100).ToListAsync());
api.MapGet("/audit", async (Store db) => await db.Audits.OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync());
app.MapHub<EventsHub>("/hubs/events").RequireAuthorization();
app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");
await app.RunAsync();

static IOrderedQueryable<Event> Query(Store db, string? source, string? q, string? category) => db.Events.AsNoTracking().Where(e => e.Demo == Registration.Demo && (source == null || source == "" || e.Source == source) && (category == null || category == "" || e.Category == category) && (q == null || q == "" || EF.Functions.ILike(e.Title, "%" + q + "%") || EF.Functions.ILike(e.Body, "%" + q + "%"))).OrderByDescending(e => e.PublishedAt).ThenByDescending(e => e.Id);
static object EventDto(Event e) => new { e.Id, e.Source, source_id = e.SourceId, e.Title, e.Body, e.Url, e.Category, published_at = e.PublishedAt, collected_at = e.CollectedAt, e.Demo, e.GroupId, e.AnalysisStatus };
static async Task<Draft> GetDraft(Store db, string id) => await db.Drafts.SingleOrDefaultAsync(d => d.Id == id && (d.Demo == Registration.Demo || d.Quarantined)) ?? throw new ArgumentException("草稿不存在");
static void ValidateDraft(DraftInput input)
{
    if (string.IsNullOrWhiteSpace(input.Content) || input.Content.Length > 10000 || input.Channels.Length is < 1 or > 5 || input.Channels.Any(c => !Connectors.Channels.Contains(c))) throw new ArgumentException("请填写有效内容并选择渠道");
}
static object RuleDto(Rule r) => new { r.Id, r.Name, r.Enabled, r.Version, sources = Json.Read<string[]>(r.Sources), keywords = Json.Read<string[]>(r.Keywords), channels = Json.Read<string[]>(r.Channels), r.Account, r.DailyLimit, r.CooldownMinutes, r.StartHour, r.EndHour };
static void ApplyRule(Rule r, RuleInput input)
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Sources.Length == 0 || input.Sources.Any(string.IsNullOrWhiteSpace) || input.Keywords.Any(string.IsNullOrWhiteSpace) || input.Channels.Length == 0 || input.Channels.Any(c => c is not ("x" or "telegram")) || input.DailyLimit is < 1 or > 1000 || input.CooldownMinutes is < 1 or > 1440 || input.StartHour is < 0 or > 23 || input.EndHour is < 1 or > 24 || input.StartHour == input.EndHour || input.Account != "default") throw new ArgumentException("规则需有效来源、X/Telegram渠道、时间窗口及default账号");
    r.Name = input.Name; r.Enabled = input.Enabled; r.Sources = Json.Write(input.Sources); r.Keywords = Json.Write(input.Keywords); r.Channels = Json.Write(input.Channels.Distinct().ToArray()); r.Account = input.Account; r.DailyLimit = input.DailyLimit; r.CooldownMinutes = input.CooldownMinutes; r.StartHour = input.StartHour; r.EndHour = input.EndHour;
}
static void ApplySource(Source s, SourceInput input)
{
    if (input.Kind is not ("rss" or "x") || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Address) || input.IntervalSeconds is < 30 or > 86400) throw new ArgumentException("来源配置无效");
    if (input.Kind == "rss") Network.Validate(input.Address);
    s.Kind = input.Kind; s.Name = input.Name; s.Address = input.Address; s.Enabled = input.Enabled; s.IntervalSeconds = input.IntervalSeconds; s.NextRun = Clock.Now;
}
public sealed record Login(string Token);
public sealed record DraftInput(string Content, string[] Channels, string? EventId = null, int? Revision = null);
public sealed record SettingsInput(bool AutoPaused, int AnalysisDailyLimit = 100, int ChannelDailyLimit = 20);
public sealed record RuleInput(string Name, string[] Sources, string[] Keywords, string[] Channels, bool Enabled = false, string Account = "default", int DailyLimit = 10, int CooldownMinutes = 15, int StartHour = 0, int EndHour = 24);
public sealed record SourceInput(string Kind, string Name, string Address, bool Enabled = false, int IntervalSeconds = 60);
public sealed record ResolveInput(string Status, string? RemoteId, string Note);
public sealed class EventsHub : Hub;
sealed class ChangeRelay(IServiceScopeFactory scopes, IHubContext<EventsHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        long after = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<Store>();
                var changes = await db.Changes.AsNoTracking().Where(c => c.Id > after).OrderBy(c => c.Id).Take(200).ToListAsync(ct);
                if (changes.Count > 0) { await hub.Clients.All.SendAsync("changes", changes, ct); after = changes[^1].Id; }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            await Task.Delay(1500, ct);
        }
    }
}
public partial class Program;
