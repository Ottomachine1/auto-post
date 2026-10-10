using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<Store>().UseNpgsql(Registration.Connection).Options;
await using var db = new Store(options);
if (args.Length == 0) throw new ArgumentException("Commands: migrate | seed-demo | import-sqlite <path> | benchmark");
if (args[0] == "migrate") { await db.Database.MigrateAsync(); return; }
if (args[0] == "feed-catalog")
{
    if (args.Length < 2) throw new ArgumentException("feed-catalog <json> [validate] [enable-valid]");
    var sources = Json.Read<Source[]>(await File.ReadAllTextAsync(args[1]));
    foreach (var source in sources)
        if (!await db.Sources.AnyAsync(s => s.Id == source.Id)) { source.Enabled = false; db.Sources.Add(source); }
    await db.SaveChangesAsync();
    if (args.Contains("validate"))
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Registration.AddAutoPost(services, Registration.Connection);
        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var factory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IHttpClientFactory>(provider);
        await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (entry, ct) =>
        {
            await using var checkDb = new Store(options); var source = await checkDb.Sources.SingleAsync(s => s.Id == entry.Id, ct);
            var valid = await new FeedReader(factory).Validate(source, ct);
            source.Enabled = valid && args.Contains("enable-valid"); source.NextRun = Clock.Now;
            await checkDb.SaveChangesAsync(ct);
            Console.WriteLine(Json.Write(new { source.Id, source.Name, source.HttpStatus, source.Validation, source.Error, source.LatestPublishedAt, source.Enabled }));
        });
    }
    var ids = sources.Select(s => s.Id).ToArray();
    var report = await db.Sources.AsNoTracking().Where(s => ids.Contains(s.Id)).OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync();
    await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("FEED_REPORT_PATH") ?? Path.Combine(Path.GetTempPath(), "feed-validation-report.json"), Json.Write(report));
    Console.WriteLine($"Candidates={report.Count}; valid={report.Count(s => s.Validation == "valid")}; enabled={report.Count(s => s.Enabled)}"); return;
}
if (args[0] == "seed-demo")
{
    var titles = new[] { "全球宏观观察：政策预期与风险资产", "比特币生态观察：资金流向与网络活动", "AI 与加密基础设施：关注叙事变化", "全球能源：供需与市场传导" };
    for (var i = 0; i < titles.Length; i++)
    {
        var id = "dotnet-demo-" + i;
        if (await db.Events.AnyAsync(e => e.Id == id)) continue;
        db.Events.Add(new Event { Id = id, Source = "demo", SourceId = id, Title = titles[i], Body = "演示样例，不代表真实新闻。接入授权来源后将显示真实事件。", Category = i == 1 ? "加密" : i == 2 ? "社交" : "宏观", PublishedAt = Clock.Now - i * 120, Demo = true, Fingerprint = id, GroupId = id }); db.Mark("event", id);
    }
    await db.SaveChangesAsync(); Console.WriteLine("Demo seed complete."); return;
}
if (args[0] == "import-sqlite")
{
    if (args.Length != 2 || !File.Exists(args[1])) throw new ArgumentException("Provide existing SQLite file; stop legacy service before import.");
    await using var sqlite = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(args[1]), Mode = SqliteOpenMode.ReadOnly }.ToString());
    await sqlite.OpenAsync();
    await using var tx = await db.Database.BeginTransactionAsync(); await db.Lock();
    var counts = new Dictionary<string, int>();
    foreach (var table in new[] { "events", "analyses", "drafts", "deliveries", "audit", "source_health" })
    {
        using var command = sqlite.CreateCommand(); command.CommandText = "SELECT * FROM " + table;
        await using var reader = await command.ExecuteReaderAsync(); var count = 0;
        while (await reader.ReadAsync())
        {
            count++; string S(string key) => reader.IsDBNull(reader.GetOrdinal(key)) ? "" : reader[key].ToString()!;
            long L(string key) => long.Parse(S(key));
            var rowId = S(table == "analyses" ? "event_id" : table == "source_health" ? "source" : "id");
            var draftId = table == "deliveries" ? S("draft_id") : "";
            switch (table)
            {
                case "events":
                    if (!await db.Events.AnyAsync(e => e.Id == rowId))
                    { var id = S("id"); db.Events.Add(new Event { Id = id, Source = S("source"), SourceId = S("source_id"), Title = S("title"), Body = Network.Text(S("body")), Url = Network.Canonical(S("url")), Category = S("category"), PublishedAt = L("published_at"), CollectedAt = L("collected_at"), Demo = L("demo") != 0, GroupId = id, Fingerprint = Network.Hash(S("title") + S("body")) }); }
                    break;
                case "analyses":
                    if (!await db.Analyses.AnyAsync(a => a.EventId == rowId)) db.Analyses.Add(new Analysis { Id = "legacy-" + S("event_id"), EventId = S("event_id"), Result = S("result"), Model = "legacy-unknown", PromptVersion = "legacy", CreatedAt = L("created_at") });
                    break;
                case "drafts":
                    if (!await db.Drafts.AnyAsync(d => d.Id == rowId))
                    {
                        var d = new Draft { Id = S("id"), Content = S("content"), Channels = S("channels"), EventId = S("event_id") is { Length: > 0 } eid ? eid : null, Status = S("status"), Revision = (int)L("revision"), Quarantined = true, CreatedAt = L("created_at"), UpdatedAt = L("updated_at") };
                        db.Drafts.Add(d); db.Versions.Add(new DraftVersion { DraftId = d.Id, Revision = d.Revision, Content = d.Content, Channels = d.Channels, CreatedAt = d.UpdatedAt });
                    }
                    break;
                case "deliveries":
                    if (!await db.Deliveries.AnyAsync(d => d.Id == rowId))
                    { var revision = await db.Drafts.Where(d => d.Id == draftId).Select(d => d.Revision).FirstOrDefaultAsync(); db.Deliveries.Add(new Delivery { Id = S("id"), DraftId = S("draft_id"), Revision = revision, Channel = S("channel"), Status = S("status") == "sending" ? "unknown" : S("status"), RemoteId = S("remote_id"), Error = S("error"), CreatedAt = L("created_at"), UpdatedAt = L("updated_at") }); }
                    break;
                case "audit":
                    if (!await db.Audits.AnyAsync(a => a.Id == rowId)) db.Audits.Add(new Audit { Id = S("id"), Action = S("action"), Target = S("target"), Detail = "legacy import", CreatedAt = L("created_at") });
                    break;
                case "source_health":
                    var sid = "legacy-" + Network.Hash(S("source"))[..16];
                    if (!await db.Sources.AnyAsync(s => s.Id == sid)) db.Sources.Add(new Source { Id = sid, Name = S("source"), Address = S("source"), Enabled = false, Status = S("status"), Error = S("error"), LastSuccess = S("status") == "connected" ? L("updated_at") : null });
                    break;
            }
            await db.SaveChangesAsync();
        }
        counts[table] = count;
    }
    db.Mark("import", "sqlite", Json.Write(counts)); await db.SaveChangesAsync(); await tx.CommitAsync();
    Console.WriteLine(Json.Write(new { sourceCounts = counts, destinationCounts = new { events = await db.Events.CountAsync(), analyses = await db.Analyses.CountAsync(), drafts = await db.Drafts.CountAsync(), deliveries = await db.Deliveries.CountAsync() }, note = "Imported drafts quarantined; no jobs or automatic publication created." })); return;
}
if (args[0] == "benchmark")
{
    // Run only against a disposable benchmark database, never a production database.
    if (Environment.GetEnvironmentVariable("ALLOW_BENCHMARK") != "yes") throw new InvalidOperationException("Requires ALLOW_BENCHMARK=yes and a dedicated database.");
    await db.Database.ExecuteSqlRawAsync("""
INSERT INTO "Events" SELECT 'bench-'||i,'benchmark',i::text,'Benchmark event '||i,'Reference load data','','宏观',1700000000+i,1700000000+i,true,i::text,i::text,'pending' FROM generate_series(1,100000) i ON CONFLICT DO NOTHING;
""");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var samples = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
    {
        await using var scoped = new Store(options); var values = new List<double>();
        for (var i = 0; i < 20; i++) { var sw = System.Diagnostics.Stopwatch.StartNew(); await scoped.Events.AsNoTracking().Where(e => e.Demo).OrderByDescending(e => e.PublishedAt).ThenByDescending(e => e.Id).Take(50).ToListAsync(); values.Add(sw.Elapsed.TotalMilliseconds); }
        return values;
    }));
    var ordered = samples.SelectMany(x => x).Order().ToArray();
    Console.WriteLine(Json.Write(new { events = 100000, clients = 20, queries = ordered.Length, p50Ms = ordered[ordered.Length / 2], p95Ms = ordered[(int)(ordered.Length * .95)], elapsedMs = watch.ElapsedMilliseconds, memoryBytes = Environment.WorkingSet, scope = "database queries only; browser/SignalR load is measured separately" })); return;
}
throw new ArgumentException("Unknown command");
