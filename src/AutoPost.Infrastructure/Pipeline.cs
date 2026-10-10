using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoPost.Infrastructure;

public sealed class Pipeline(Store db, Connectors connectors)
{
    public async Task Schedule(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Lock(ct);
        var settings = await db.Settings.SingleAsync(ct);
        settings.WorkerHeartbeat = Clock.Now;
        if (!Registration.Demo)
        {
            foreach (var source in await db.Sources.Where(s => s.Enabled && s.NextRun <= Clock.Now).ToListAsync(ct)) await db.Enqueue("collect", source.Id, ct);
            if (Connectors.Env("OPENAI_API_KEY") != "")
                foreach (var item in await db.Events.Where(e => !e.Demo && e.AnalysisStatus == "pending").OrderBy(e => e.CollectedAt).Take(100).ToListAsync(ct)) await db.Enqueue("analyse", item.Id, ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<Job?> Claim(CancellationToken ct, string? kind = null)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Jobs\" SET \"Status\"='pending', \"LeaseOwner\"=NULL WHERE \"Status\"='running' AND \"LeaseUntil\" < {Clock.Now}", ct);
        if (kind is null or "publish") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Deliveries\" SET \"Status\"='unknown',\"Error\"='进程中断，请人工核验',\"UpdatedAt\"={Clock.Now} WHERE \"Status\"='sending'", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.Jobs.FromSqlInterpolated($"SELECT * FROM \"Jobs\" WHERE \"Status\"='pending' AND \"DueAt\" <= {Clock.Now} AND ({kind}::text IS NULL OR \"Kind\"={kind}) ORDER BY \"DueAt\" FOR UPDATE SKIP LOCKED LIMIT 1").FirstOrDefaultAsync(ct);
        if (job != null)
        {
            job.Status = "running"; job.Attempts++; job.LeaseUntil = Clock.Now + 1800; job.LeaseOwner = Environment.MachineName + ":" + Environment.ProcessId;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return job;
    }
    public async Task<bool> RunOnce(CancellationToken ct, string kind = "analyse")
    {
        // A session lock guarantees one active worker even if a second container starts.
        await using var leader = new NpgsqlConnection(Registration.Connection);
        await leader.OpenAsync(ct);
        var laneLock = kind switch { "collect" => 794122, "analyse" => 794123, "publish" => 794124, _ => throw new ArgumentException("Unknown lane") };
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(" + laneLock + ")", leader);
        if (!Equals(await command.ExecuteScalarAsync(ct), true)) return false;
        try
        {
            await Schedule(ct);
            var job = await Claim(ct, kind); if (job == null) return false;
            try
            {
                switch (job.Kind)
                {
                    case "collect":
                        var source = await db.Sources.FindAsync([job.Target], ct);
                        if (source is { Enabled: true }) await connectors.Collect(source, ct);
                        break;
                    case "analyse": await Analyse(job.Target, ct); break;
                    case "publish": await Publish(job.Target, ct); break;
                }
                job.Status = "done"; job.Error = null;
            }
            catch (RetryLater e) { job.Status = "pending"; job.DueAt = Clock.Now + e.Seconds; job.Error = "等待配额恢复"; }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                job.Status = job.Attempts < 5 && job.Kind != "publish" ? "pending" : "failed";
                job.DueAt = Clock.Now + Math.Min(3600, 30 * (1 << Math.Min(job.Attempts, 6)));
                job.Error = "执行失败，请检查配置、平台权限或响应格式";
                if (job.Kind == "collect" && await db.Sources.FindAsync([job.Target], ct) is { } source)
                { source.Status = "error"; source.Error = job.Error; source.NextRun = job.DueAt; }
                if (job.Kind == "analyse" && await db.Events.FindAsync([job.Target], ct) is { } item)
                    item.AnalysisStatus = job.Status == "failed" ? "failed" : "pending";
            }
            db.Mark("task", job.Id, job.Status); await db.SaveChangesAsync(ct); return true;
        }
        finally { await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(" + laneLock + ")", leader); await release.ExecuteScalarAsync(CancellationToken.None); }
    }
    public async Task Analyse(string id, CancellationToken ct)
    {
        var item = await db.Events.FindAsync([id], ct);
        if (item == null || item.Demo || Registration.Demo) return;
        if (await db.Analyses.AnyAsync(a => a.EventId == id, ct)) { item.AnalysisStatus = "completed"; await db.SaveChangesAsync(ct); return; }
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Lock(ct);
            var settings = await db.Settings.SingleAsync(ct);
            var key = "analysis:" + Clock.Day;
            var budget = await db.Budgets.FindAsync([key], ct);
            if ((budget?.Used ?? 0) >= settings.AnalysisDailyLimit) throw new RetryLater((int)(Clock.DayStart + 86400 - Clock.Now));
            if (budget == null) { budget = new Budget { Id = key }; db.Budgets.Add(budget); }
            budget.Used++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        var result = await connectors.Analyse(item, ct);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Lock(ct);
            db.Analyses.Add(result); item.AnalysisStatus = "completed";
            var parsed = Json.Read<AnalysisResult>(result.Result);
            item.Category = parsed.Category ?? item.Category;
            var matched = false;
            foreach (var rule in await db.Rules.Where(r => r.Enabled).ToListAsync(ct))
            {
                if (Core.Rules.Rejection(rule, item, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Hour) != null) continue;
                matched = true;
                if (await db.Drafts.AnyAsync(d => d.EventId == id && d.RuleId == rule.Id, ct)) continue;
                var draft = NewDraft(parsed.Draft, Json.Read<string[]>(rule.Channels), id, false);
                draft.RuleId = rule.Id; draft.RuleVersion = rule.Version;
                var settings = await db.Settings.SingleAsync(ct);
                if (!settings.AutoPaused && Json.Read<string[]>(rule.Channels).All(c => Connectors.Configured(c) && Connectors.ContentError(c, draft.Content) == null))
                {
                    draft.ApprovedRevision = draft.Revision; draft.Status = "approved";
                    await QueuePublish(draft, rule, ct);
                }
            }
            if (!matched && !await db.Drafts.AnyAsync(d => d.EventId == id, ct)) NewDraft(parsed.Draft, ["telegram"], id, false);
            db.Mark("analysis", id); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
    public Draft NewDraft(string content, string[] channels, string? eventId, bool demo)
    {
        var draft = new Draft { Content = content, Channels = Json.Write(channels.Distinct().ToArray()), EventId = eventId, Demo = demo };
        db.Drafts.Add(draft);
        Snapshot(draft); db.Mark("draft_created", draft.Id); return draft;
    }
    public void Snapshot(Draft draft) => db.Versions.Add(new DraftVersion { DraftId = draft.Id, Revision = draft.Revision, Content = draft.Content, Channels = draft.Channels });
    public async Task QueuePublish(Draft draft, Rule? rule, CancellationToken ct)
    {
        if (Registration.Demo || draft.Demo || draft.Quarantined) throw new InvalidOperationException("演示或隔离草稿禁止发布");
        if (draft.ApprovedRevision != draft.Revision) throw new InvalidOperationException("请审核当前版本");
        foreach (var channel in Json.Read<string[]>(draft.Channels))
        {
            if (await db.Deliveries.AnyAsync(d => d.DraftId == draft.Id && d.Revision == draft.Revision && d.Channel == channel, ct)) continue;
            var delivery = new Delivery { DraftId = draft.Id, Revision = draft.Revision, Channel = channel, RuleId = rule?.Id, RuleVersion = rule?.Version, Account = rule?.Account ?? "default", Status = channel is "x" or "telegram" ? "queued" : "manual_required" };
            db.Deliveries.Add(delivery);
            if (delivery.Status == "queued") await db.Enqueue("publish", delivery.Id, ct);
        }
        db.Mark("publish_queued", draft.Id, "revision=" + draft.Revision);
    }
    public async Task Publish(string id, CancellationToken ct)
    {
        var delivery = await db.Deliveries.FindAsync([id], ct);
        if (delivery == null || delivery.Status != "queued") return;
        var draft = await db.Drafts.FindAsync([delivery.DraftId], ct);
        if (draft == null) return;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Lock(ct);
            var rejection = await PublishRejection(delivery, draft, ct);
            if (rejection != null)
            {
                delivery.Status = "blocked"; delivery.Error = rejection;
                db.Mark("delivery", id, rejection); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            await Spend("channel:" + delivery.Channel + ":" + Clock.Day, ct);
            if (delivery.RuleId != null) await Spend("rule:" + delivery.RuleId + ":" + delivery.Channel + ":" + Clock.Day, ct);
            delivery.Status = "sending"; delivery.Attempts++; delivery.UpdatedAt = Clock.Now;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            var result = await connectors.Deliver(delivery.Channel, draft.Content, ct);
            delivery.Status = result.Status; delivery.RemoteId = result.Remote; delivery.Error = null;
        }
        catch (Uncertain) { delivery.Status = "unknown"; delivery.Error = "结果不明确，请核验平台，禁止自动重发"; }
        catch (Exception) when (!ct.IsCancellationRequested) { delivery.Status = "failed"; delivery.Error = "平台明确拒绝或配置无效，请核对权限和内容"; }
        delivery.UpdatedAt = Clock.Now;
        db.Mark("delivery", id, delivery.Status); await db.SaveChangesAsync(CancellationToken.None);
        var states = await db.Deliveries.Where(d => d.DraftId == draft.Id && d.Revision == draft.Revision).Select(d => d.Status).ToListAsync(CancellationToken.None);
        draft.Status = states.All(s => s == "published") ? "published" : "partial";
        await db.SaveChangesAsync(CancellationToken.None);
    }
    private async Task Spend(string key, CancellationToken ct)
    {
        var budget = await db.Budgets.FindAsync([key], ct);
        if (budget == null) { budget = new Budget { Id = key }; db.Budgets.Add(budget); }
        budget.Used++;
    }
    public async Task<string?> PublishRejection(Delivery delivery, Draft draft, CancellationToken ct)
    {
        if (Registration.Demo || draft.Demo || draft.Quarantined) return "演示或隔离内容禁止发布";
        if (draft.Revision != delivery.Revision || draft.ApprovedRevision != draft.Revision) return "草稿版本未审核";
        if (Connectors.ContentError(delivery.Channel, draft.Content) is { } error) return error;
        if (!Connectors.Configured(delivery.Channel)) return "渠道未配置";
        var settings = await db.Settings.SingleAsync(ct);
        if (((await db.Budgets.FindAsync(["channel:" + delivery.Channel + ":" + Clock.Day], ct))?.Used ?? 0) >= settings.ChannelDailyLimit) return "渠道今日限额已用完";
        if (delivery.RuleId != null)
        {
            if (settings.AutoPaused) return "自动发布已暂停";
            var rule = await db.Rules.FindAsync([delivery.RuleId], ct);
            if (rule == null || rule.Version != delivery.RuleVersion) return "规则版本已变更";
            var item = await db.Events.FindAsync([draft.EventId!], ct);
            if (item == null) return "来源事件缺失";
            if (Core.Rules.Rejection(rule, item, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Hour) is { } reason) return reason;
            if (((await db.Budgets.FindAsync(["rule:" + rule.Id + ":" + delivery.Channel + ":" + Clock.Day], ct))?.Used ?? 0) >= rule.DailyLimit) return "规则今日限额已用完";
            if (await db.Deliveries.AnyAsync(d => d.Id != delivery.Id && d.RuleId == rule.Id && d.Channel == delivery.Channel && d.Attempts > 0 && d.UpdatedAt > Clock.Now - rule.CooldownMinutes * 60, ct)) return "规则冷却中";
        }
        return null;
    }
}
