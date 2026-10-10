using System.Net;
using System.Net.Http.Json;
using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace AutoPost.Tests;

public sealed class WorkflowTests : IAsyncLifetime
{
    private const string Token = "integration-test-token-at-least-32-characters";
    private static readonly string Connection = Environment.GetEnvironmentVariable("TEST_DATABASE_URL") ?? "Host=127.0.0.1;Port=55432;Database=autopost_tests;Username=postgres;Password=autopost-local-only";
    private Store Db() => new((DbContextOptions<Store>)Registration.Configure(new DbContextOptionsBuilder<Store>(),Connection).Options);
    private readonly HttpClientFactory factory = new();
    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DATABASE_URL", Connection); Environment.SetEnvironmentVariable("APP_MODE", "live"); Environment.SetEnvironmentVariable("ADMIN_TOKEN", Token);
        Environment.SetEnvironmentVariable("X_USER_ACCESS_TOKEN", "mock"); Environment.SetEnvironmentVariable("OPENAI_API_KEY", "mock");
        await using var db = Db(); await db.Database.EnsureDeletedAsync(); await db.Migrate();
    }
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact] public async Task MigrationIsRepeatable() { await using var db = Db(); await db.Migrate(); Assert.True((await db.Settings.SingleAsync()).AutoPaused); }
    [Theory]
    [InlineData("https://127.0.0.1/rss")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("http://example.com/feed")]
    [InlineData("https://user:pass@example.com/")]
    public void RejectUnsafeRss(string url) => Assert.Throws<InvalidOperationException>(() => Network.Validate(url));
    [Fact] public void CanonicalUrl() => Assert.Equal("https://example.com/article?a=1", Network.Canonical("https://example.com/article?utm_source=x&a=1#part"));
    [Fact] public void XWeightedLength() { Assert.Equal(4, Connectors.XLength("中文")); Assert.Equal(2,Connectors.XLength("👨‍👩‍👧‍👦")); Assert.Equal(2,Connectors.XLength("👍🏽")); Assert.Equal(1,Connectors.XLength("e\u0301")); Assert.Equal(25, Connectors.XLength("a https://example.com/very/long/path")); Assert.NotNull(Connectors.ContentError("x", new string('中', 141))); }
    [Fact]
    public void RulesDefaultClosedAndDemoBlocked()
    {
        var rule = new Rule { Sources = "[\"rss\"]" }; var e = new Event { Source = "rss" };
        Assert.Equal("规则未启用", Rules.Rejection(rule, e, 12)); rule.Enabled = true; e.Demo = true; Assert.Equal("演示数据不能自动发布", Rules.Rejection(rule, e, 12));
    }
    [Fact]
    public void RuleOvernightAndKeyword()
    {
        var r = new Rule { Enabled = true, Sources = "[\"rss\"]", Keywords = "[\"bitcoin\"]", StartHour = 22, EndHour = 6 };
        var e = new Event { Source = "rss", Title = "BITCOIN update" };
        Assert.Null(Rules.Rejection(r, e, 23)); Assert.NotNull(Rules.Rejection(r, e, 12)); e.Source = "other"; Assert.Equal("来源不在白名单", Rules.Rejection(r, e, 23));
    }
    [Fact]
    public async Task DedupRetainsRelatedSources()
    {
        await using var db = Db(); var connectors = new Connectors(factory, db);
        await connectors.AddEvent(new Event { Source = "one", SourceId = "1", Title = "same", Url = "https://example.com/a?utm_source=x" }, default);
        await connectors.AddEvent(new Event { Source = "one", SourceId = "1", Title = "same" }, default);
        await connectors.AddEvent(new Event { Source = "two", SourceId = "2", Title = "same", Url = "https://example.com/a" }, default);
        Assert.Equal(2, await db.Events.CountAsync()); Assert.Single(await db.Events.Select(e => e.GroupId).Distinct().ToListAsync()); Assert.Equal(2, await db.Jobs.CountAsync());
    }
    [Fact]
    public async Task ConcurrentClaimsAreUnique()
    {
        await using (var db = Db()) { for (var i = 0; i < 10; i++) db.Jobs.Add(new Job { Kind = "analyse", Target = i.ToString() }); await db.SaveChangesAsync(); }
        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ => { await using var db = Db(); return (await new Pipeline(db, new Connectors(factory, db)).Claim(default))?.Id; }));
        Assert.Equal(10, ids.Distinct().Count()); Assert.All(ids, id => Assert.NotNull(id));
    }
    [Fact]
    public async Task ExpiredLeaseRecoversAndSendingBecomesUnknown()
    {
        await using var db = Db(); db.Jobs.Add(new Job { Kind = "analyse", Target = "e", Status = "running", LeaseUntil = Clock.Now - 10 });
        db.Deliveries.Add(new Delivery { DraftId = "d", Channel = "x", Status = "sending" }); await db.SaveChangesAsync();
        var job = await new Pipeline(db, new Connectors(factory, db)).Claim(default); db.ChangeTracker.Clear();
        Assert.NotNull(job); Assert.Equal("unknown", (await db.Deliveries.SingleAsync()).Status);
    }
    [Fact]
    public async Task UnknownNeverResubmits()
    {
        await using var db = Db(); var delivery = new Delivery { DraftId = "d", Channel = "x", Status = "unknown" }; db.Deliveries.Add(delivery); await db.SaveChangesAsync();
        await new Pipeline(db, new Connectors(factory, db)).Publish(delivery.Id, default); Assert.Equal(0, factory.Calls);
    }
    [Fact]
    public async Task BudgetAndPauseRejectBeforeSending()
    {
        await using var db = Db(); var p = new Pipeline(db, new Connectors(factory, db)); var item = new Event { Source = "rss" }; db.Events.Add(item);
        var rule = new Rule { Enabled = true, Sources = "[\"rss\"]", Channels = "[\"x\"]" }; db.Rules.Add(rule);
        var draft = p.NewDraft("hello", ["x"], item.Id, false); draft.ApprovedRevision = 1;
        var del = new Delivery { DraftId = draft.Id, Revision = 1, Channel = "x", RuleId = rule.Id, RuleVersion = 1 }; db.Deliveries.Add(del); await db.SaveChangesAsync();
        Assert.Equal("自动发布已暂停", await p.PublishRejection(del, draft, default));
        (await db.Settings.SingleAsync()).AutoPaused = false; db.Budgets.Add(new Budget { Id = "channel:x:" + Clock.Day, Used = 20 }); await db.SaveChangesAsync();
        Assert.Equal("渠道今日限额已用完", await p.PublishRejection(del, draft, default)); Assert.Equal(0, factory.Calls);
    }
    [Fact]
    public async Task AnalysisBudgetExhaustedLeavesQueued()
    {
        await using var db = Db(); var item = new Event { Source = "rss" }; db.Events.Add(item); db.Budgets.Add(new Budget { Id = "analysis:" + Clock.Day, Used = 100 }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<RetryLater>(() => new Pipeline(db, new Connectors(factory, db)).Analyse(item.Id, default)); Assert.Equal(0, factory.Calls);
    }
    [Fact]
    public async Task XPaginationAndCursorCommit()
    {
        await using var db = Db(); var source = new Source { Kind = "x", Address = "bitcoin", Enabled = true, Cursor = "10" }; db.Sources.Add(source); await db.SaveChangesAsync();
        Environment.SetEnvironmentVariable("X_BEARER_TOKEN", "mock");
        factory.Responses.Enqueue("""{"data":[{"id":"12","text":"a","created_at":"2026-10-10T00:00:00Z"}],"meta":{"next_token":"page2"}}""");
        factory.Responses.Enqueue("""{"data":[{"id":"11","text":"b","created_at":"2026-10-10T00:00:00Z"}],"meta":{}}""");
        await new Connectors(factory, db).Collect(source, default);
        Assert.Equal("12", source.Cursor); Assert.Equal("", source.PageToken); Assert.Equal(2, await db.Events.CountAsync());
        Assert.Contains("since_id=10", factory.Urls[1]); Assert.Contains("next_token=page2", factory.Urls[1]);
    }
    [Fact]
    public async Task RateLimitKeepsCursor()
    {
        await using var db = Db(); var s = new Source { Kind = "x", Address = "bitcoin", Cursor = "10", PageToken = "page2" }; db.Sources.Add(s); await db.SaveChangesAsync();
        Environment.SetEnvironmentVariable("X_BEARER_TOKEN", "mock"); factory.Status = HttpStatusCode.TooManyRequests;
        await Assert.ThrowsAsync<RetryLater>(() => new Connectors(factory, db).Collect(s, default)); Assert.Equal("10", s.Cursor); Assert.Equal("page2", s.PageToken);
    }
    [Fact]
    public async Task MalformedModelDoesNotCreateAnalysis()
    {
        await using var db = Db(); factory.Responses.Enqueue("""{"choices":[{"message":{"content":"{}"}}]}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Connectors(factory, db).Analyse(new Event(), default)); Assert.Empty(await db.Analyses.ToListAsync());
    }
    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    public async Task AmbiguousPlatformResponse(int status) { factory.Status = (HttpStatusCode)status; await using var db = Db(); await Assert.ThrowsAsync<Uncertain>(() => new Connectors(factory, db).Deliver("x", "hello", default)); }
    [Fact]
    public async Task ApiAuthEditingAndIsolation()
    {
        using var app = new WebApplicationFactory<Program>(); using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var created = await client.PostAsJsonAsync("/api/drafts", new { content = "hello", channels = new[] { "x" } }); created.EnsureSuccessStatusCode();
        var d = await created.Content.ReadFromJsonAsync<IdResult>();
        (await client.PostAsync($"/api/drafts/{d!.Id}/approve?revision=1", null)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/drafts/{d.Id}", new { content = "edited", channels = new[] { "x" }, revision = 1 })).EnsureSuccessStatusCode();
        await using var db = Db(); var draft = await db.Drafts.FindAsync(d.Id); Assert.Equal(2, draft!.Revision); Assert.Null(draft.ApprovedRevision); Assert.Equal(2, await db.Versions.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/drafts/{d.Id}/publish", null)).StatusCode);
        db.Events.Add(new Event { Id = "demo-event", Source = "demo", SourceId = "demo-event", Demo = true }); await db.SaveChangesAsync();
        Assert.DoesNotContain("demo-event", await client.GetStringAsync("/api/events"));
    }
    [Fact]
    public async Task ChangesPersistWithBusinessTransaction()
    {
        await using var db = Db(); await using (var tx = await db.Database.BeginTransactionAsync()) { db.Events.Add(new Event { Source = "rss", SourceId = "1" }); db.Mark("event", "1"); await db.SaveChangesAsync(); await tx.RollbackAsync(); }
        Assert.Equal(0, await db.Events.CountAsync()); Assert.Equal(0, await db.Changes.CountAsync());
    }
    [Fact]
    public async Task CookieWritesRequireCsrfAndForeignOriginsAreRejected()
    {
        using var app = new WebApplicationFactory<Program>(); using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await client.PostAsJsonAsync("/api/session", new { token = Token })).EnsureSuccessStatusCode();
        var csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/session");
        var input = new { autoPaused = true, analysisDailyLimit = 100, channelDailyLimit = 20 };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings", input)).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("csrfToken").GetString());
        (await client.PutAsJsonAsync("/api/settings", input)).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Origin", "https://other.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null)).StatusCode);
    }
    [Fact]
    public async Task CursorPagesDoNotSkipEqualTimestamps()
    {
        await using var db = Db();
        for (var i = 0; i < 65; i++) db.Events.Add(new Event { Id = i.ToString("D4"), Source = "rss", SourceId = i.ToString(), PublishedAt = 123 });
        await db.SaveChangesAsync();
        using var app = new WebApplicationFactory<Program>(); using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var first = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/events/page");
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        var next = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/events/page?cursor=" + first.GetProperty("nextCursor").GetString());
        Assert.Equal(15, next.GetProperty("items").GetArrayLength());
    }
    [Fact]
    public async Task EnabledRuleProducesOneVersionBoundPublication()
    {
        await using var db = Db(); var settings = await db.Settings.SingleAsync(); settings.AutoPaused = false;
        var item = new Event { Source = "rss", SourceId = "one", Title = "bitcoin update" }; db.Events.Add(item);
        var rule = new Rule { Enabled = true, Sources = "[\"rss\"]", Keywords = "[\"bitcoin\"]", Channels = "[\"x\"]" }; db.Rules.Add(rule); await db.SaveChangesAsync();
        factory.Responses.Enqueue(Json.Write(new { choices = new[] { new { message = new { content = Json.Write(new AnalysisResult("摘要", ["推测"], ["未核验"], "Verified source update", ["来源标题"], "加密")) } } }, usage = new { total_tokens = 20 } }));
        var p = new Pipeline(db, new Connectors(factory, db)); await p.Analyse(item.Id, default);
        var draft = await db.Drafts.SingleAsync(); Assert.Equal(1, draft.ApprovedRevision);
        var delivery = await db.Deliveries.SingleAsync(); Assert.Equal(rule.Version, delivery.RuleVersion);
        factory.Responses.Enqueue("""{"data":{"id":"remote-1"}}"""); await p.Publish(delivery.Id, default); await p.Publish(delivery.Id, default);
        Assert.Equal(2, factory.Calls); Assert.Equal("published", delivery.Status);
    }
    [Fact]
    public async Task DisabledRuleNeverAuthorizesSending()
    {
        await using var db = Db(); (await db.Settings.SingleAsync()).AutoPaused = false;
        var item = new Event { Source = "rss", SourceId = "one" }; db.Events.Add(item); db.Rules.Add(new Rule { Sources = "[\"rss\"]", Channels = "[\"x\"]" }); await db.SaveChangesAsync();
        factory.Responses.Enqueue(Json.Write(new { choices = new[] { new { message = new { content = Json.Write(new AnalysisResult("摘要", [], [], "Draft", [], "新闻")) } } } }));
        await new Pipeline(db, new Connectors(factory, db)).Analyse(item.Id, default);
        Assert.Empty(await db.Deliveries.ToListAsync()); Assert.Null((await db.Drafts.SingleAsync()).ApprovedRevision);
    }
    [Fact]
    public void GoogleTopicsEncodeAndRejectInvalidLocale()
    {
        var url = NewsTopics.Url("gold & oil / CPI", "zh-CN", "CN");
        Assert.Contains("q=gold%20%26%20oil%20%2F%20CPI", url); Assert.Contains("ceid=CN:zh-Hans", url);
        Assert.Throws<ArgumentException>(() => NewsTopics.Url("", "en", "US"));
        Assert.Throws<ArgumentException>(() => NewsTopics.Url("gold", "bad", "US"));
    }
    [Fact]
    public async Task FeedValidationAndConditionalNotModified()
    {
        var source = new Source { Address = "https://example.com/feed" };
        var now = DateTimeOffset.UtcNow.ToString("R");
        factory.Responses.Enqueue($"<rss version='2.0'><channel><title>News</title><link>https://example.com</link><description>News</description><item><guid>1</guid><title>Story</title><pubDate>{now}</pubDate></item></channel></rss>");
        Assert.True(await new FeedReader(factory).Validate(source, default)); Assert.Equal("valid", source.Validation);
        source.ETag = "\"v1\""; factory.Status = HttpStatusCode.NotModified;
        Assert.True((await new FeedReader(factory).Fetch(source, true, default)).NotModified);
    }
    [Fact]
    public async Task InvalidFeedCannotBeEnabledAndBacksOff()
    {
        var source = new Source { Address = "https://example.com/feed", Enabled = true };
        factory.Responses.Enqueue("<html>Blocked</html>");
        Assert.False(await new FeedReader(factory).Validate(source, default)); Assert.False(source.Enabled);
        var first = source.NextRun; FeedReader.Failure(source, new FeedFailure("http_429", 500)); FeedReader.Failure(source, new FeedFailure("http_503"));
        Assert.Equal("suspended", source.Status); Assert.True(source.SuspendedUntil > first); Assert.Equal(3, source.ConsecutiveFailures);
    }
    [Fact]
    public void SimilarityDoesNotTreatUnrelatedStoriesAsIdentical()
    {
        Assert.True(EventSimilarity.Title("Bitcoin ETF receives regulatory approval today", "Today Bitcoin ETF receives regulatory approval") > .88);
        Assert.Equal(0, EventSimilarity.Title("Bitcoin price rises", "New AI chips released"));
        Assert.Equal(1, EventSimilarity.Cosine([1, 0, 0], [1, 0, 0])); Assert.Equal(0, EventSimilarity.Cosine([float.NaN], [1]));
    }
    [Fact]
    public async Task RestrictedFeedAndStaleFeedFailPreflight()
    {
        var source = new Source { Address = "https://example.com/feed" };
        factory.Status = HttpStatusCode.Forbidden;
        Assert.False(await new FeedReader(factory).Validate(source, default)); Assert.Equal("access_restricted", source.Error);
        factory.Status = HttpStatusCode.OK;
        factory.Responses.Enqueue("<rss version='2.0'><channel><title>Old</title><link>https://example.com</link><description>Old</description><item><title>Old story</title><pubDate>Mon, 01 Jan 2024 12:00:00 GMT</pubDate></item></channel></rss>");
        Assert.False(await new FeedReader(factory).Validate(source, default)); Assert.Equal("stale_feed", source.Error);
    }
    [Fact]
    public async Task EnablingNewFeedQueuesPreflightInsteadOfCollecting()
    {
        using var app = new WebApplicationFactory<Program>(); using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var response = await client.PostAsJsonAsync("/api/sources", new { kind = "rss", name = "Test", address = "https://example.com/feed", enabled = true, category = "macro", priority = 90 });
        response.EnsureSuccessStatusCode();
        await using var db = Db(); var source = await db.Sources.SingleAsync();
        Assert.False(source.Enabled); Assert.True(source.EnableAfterValidation);
        Assert.Equal("validate:" + source.Id, (await db.Jobs.SingleAsync()).Target);
    }
    [Fact]
    public async Task AtomFeedPreservesAuthorAndLanguage()
    {
        await using var db = Db(); var source = new Source { Address = "https://example.com/atom", Enabled = true, Category = "technology" }; db.Sources.Add(source); await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow.ToString("O");
        factory.Responses.Enqueue($"<feed xmlns='http://www.w3.org/2005/Atom' xml:lang='en'><title>AI</title><id>urn:feed</id><updated>{now}</updated><entry><id>urn:item</id><title>AI model released</title><updated>{now}</updated><author><name>Research Lab</name></author><summary>New research</summary><link href='https://example.com/research'/></entry></feed>");
        await new Connectors(factory, db).Collect(source, default);
        var item = await db.Events.SingleAsync(); Assert.Equal("Research Lab", item.Author); Assert.Equal("en", item.Language); Assert.Equal("科技", item.Category); Assert.Equal("New research", item.OriginalSummary);
    }
    [Fact]
    public async Task MediaDraftRevisionPreservesImagesAndInvalidatesApproval()
    {
        using var app = new WebApplicationFactory<Program>(); using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jY9sAAAAASUVORK5CYII=");
        var uploaded = await client.PostAsJsonAsync("/api/media",new {data=Convert.ToBase64String(png)}); uploaded.EnsureSuccessStatusCode();
        var media = (await uploaded.Content.ReadFromJsonAsync<IdResult>())!;
        var created = await client.PostAsJsonAsync("/api/drafts",new {content="Photo",channels=new[]{"x"},media=new[]{new Attachment(media.Id,"描述")}}); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<IdResult>())!.Id;
        (await client.PostAsync("/api/drafts/"+id+"/approve?revision=1",null)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/drafts/"+id,new {content="Photo edited",channels=new[]{"x"},revision=1,media=new[]{new Attachment(media.Id,"new")}})).EnsureSuccessStatusCode();
        await using var db=Db(); var draft=await db.Drafts.SingleAsync(); Assert.Null(draft.ApprovedRevision); Assert.Equal(2,draft.Revision);
        Assert.Equal("描述",Json.Read<Attachment[]>((await db.Versions.SingleAsync(v=>v.Revision==1)).MediaJson)[0].Alt);
        Assert.Equal("new",Json.Read<Attachment[]>(draft.MediaJson)[0].Alt);
        Assert.Equal(png,await client.GetByteArrayAsync("/api/media/"+media.Id));
    }
    [Fact]
    public async Task MediaIsolationAndUnsupportedChannels()
    {
        await using var db=Db(); var asset=new MediaAsset {Demo=true};db.Media.Add(asset);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(()=>MediaFiles.Validate(db,[new Attachment(asset.Id)]));
        Assert.NotNull(MediaFiles.ChannelError("telegram",[new Attachment("id")]));
        Assert.Throws<ArgumentException>(()=>MediaFiles.Detect(System.Text.Encoding.UTF8.GetBytes("<svg><script>bad</script></svg>")));
    }
    [Fact]
    public async Task BinanceRejectionAndAmbiguousSubmissionNeverSucceed()
    {
        Environment.SetEnvironmentVariable("BINANCE_SQUARE_OPENAPI_KEY","mock");await using var db=Db();var connectors=new Connectors(factory,db);
        factory.Responses.Enqueue("{\"code\":\"220003\",\"message\":\"bad key\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>connectors.Deliver("binance","hello",default));
        factory.Status=HttpStatusCode.GatewayTimeout;
        await Assert.ThrowsAsync<Uncertain>(()=>connectors.Deliver("binance","hello",default));
        factory.Status=HttpStatusCode.OK;factory.Responses.Enqueue("{\"code\":\"000000\",\"data\":{\"id\":\"123\"}}");
        Assert.Equal(("published","123"),await connectors.Deliver("binance","hello",default));
    }
    [Fact]
    public async Task XMediaUploadsBeforePostingWithExactText()
    {
        await using var db=Db();var asset=new MediaAsset {ContentType="image/png",Data="aGVsbG8="};db.Media.Add(asset);await db.SaveChangesAsync();
        factory.Responses.Enqueue("{\"data\":{\"id\":\"media1\"}}");factory.Responses.Enqueue("{\"data\":{\"id\":\"post1\"}}");
        var result=await new Connectors(factory,db).Deliver("x","Exact text https://example.com",default,[new Attachment(asset.Id)]);
        Assert.Equal("post1",result.Remote);Assert.EndsWith("/2/media/upload",factory.Urls[0]);Assert.EndsWith("/2/tweets",factory.Urls[1]); Assert.Equal("Exact text https://example.com",Json.Read<System.Text.Json.JsonElement>(factory.Bodies[1]).GetProperty("text").GetString());
    }
    [Fact]
    public async Task BinanceImageUploadUsesProcessedUrlAndPreservesText()
    {
        Environment.SetEnvironmentVariable("BINANCE_SQUARE_OPENAPI_KEY","mock");await using var db=Db();var asset=new MediaAsset {ContentType="image/png",Data="aGVsbG8="};db.Media.Add(asset);await db.SaveChangesAsync();
        factory.Responses.Enqueue("{\"code\":\"000000\",\"data\":{\"presignedUrl\":\"https://example.com/upload\",\"fileTicket\":\"ticket1\"}}");
        factory.Responses.Enqueue("{}");factory.Responses.Enqueue("{\"code\":\"000000\",\"data\":{\"status\":1,\"imageUrl\":\"https://example.com/processed.png\"}}");
        factory.Responses.Enqueue("{\"code\":\"000000\",\"data\":{\"id\":\"post1\"}}");
        Assert.Equal("post1",(await new Connectors(factory,db).Deliver("binance","Exact #BTC $BTC",default,[new Attachment(asset.Id)])).Remote);
        var body=Json.Read<System.Text.Json.JsonElement>(factory.Bodies[3]);Assert.Equal("Exact #BTC $BTC",body.GetProperty("bodyTextOnly").GetString());Assert.Equal("https://example.com/processed.png",body.GetProperty("imageList")[0].GetString());
    }
    public sealed record IdResult(string Id);
}
public sealed class HttpClientFactory : IHttpClientFactory
{
    public List<string> Bodies { get; } = []; public int Calls; public List<string> Urls { get; } = []; public Queue<string> Responses { get; } = []; public HttpStatusCode Status = HttpStatusCode.OK;
    public HttpClient CreateClient(string name) => new(new Handler(this));
    private sealed class Handler(HttpClientFactory owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            owner.Calls++; owner.Urls.Add(request.RequestUri!.ToString()); owner.Bodies.Add(request.Content==null?"":await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(owner.Status) { Content = new StringContent(owner.Responses.Count > 0 ? owner.Responses.Dequeue() : "{}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
