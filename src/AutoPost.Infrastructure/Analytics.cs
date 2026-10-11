using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
namespace AutoPost.Infrastructure;
public static class Analytics
{
    public sealed record Window(long From,long To,int BucketSeconds,string TimeBasis="collectedAt",string Interval="[from,to)");
    public sealed record Count(string Key,int Value);
    public sealed record Bucket(long From,int Reports,int ScoredReports,double? AverageScore);
    public sealed record Channel(string Key,bool Configured,string Mode,int Deliveries,Count[] Statuses);
    public sealed record Report(Window Window,int Reports,int AnalysedReports,int ScoredReports,double? AverageScore,Count[] Sources,Count[] Topics,Count[] Processing,Channel[] Channels,Bucket[] Timeline,int UnknownSentimentReports,long AsOf);
    public static Window Resolve(string? range,long? from,long? to,long now)
    {
        long start,end;
        if(range=="custom") {if(from is null||to is null)throw new ArgumentException("自定义范围需要起止时间");start=from.Value;end=to.Value;}
        else {var seconds=(range??"24h") switch {"1h"=>3600,"24h"=>86400,"7d"=>604800,"30d"=>2592000,_=>throw new ArgumentException("时间范围不支持")};end=now;start=end-seconds;}
        if(start<0||end<=start||end>now+300||end-start>90L*86400)throw new ArgumentException("时间范围须为有效的过去区间且不超过90天");
        var span=end-start;return new(start,end,span<=3600?300:span<=86400?3600:span<=604800?21600:86400);
    }
    public static async Task<Report> Read(Store db,Window window,CancellationToken ct)
    {
        // Read-only consistent snapshot; no scoring or model tasks are created by viewing analytics.
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var events=db.Events.AsNoTracking().Where(e=>e.Demo==Registration.Demo&&e.CollectedAt>=window.From&&e.CollectedAt<window.To);
        var reports=await events.CountAsync(ct);
        var analysed=await events.CountAsync(e=>db.Analyses.Any(a=>a.EventId==e.Id),ct);
        var sources=await events.GroupBy(e=>e.Source).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).Select(g=>new Count(g.Key,g.Count())).ToArrayAsync(ct);
        var topics=await events.GroupBy(e=>e.Category).OrderByDescending(g=>g.Count()).ThenBy(g=>g.Key).Select(g=>new Count(g.Key,g.Count())).ToArrayAsync(ct);
        var processing=await events.GroupBy(e=>e.AnalysisStatus).Select(g=>new Count(g.Key,g.Count())).ToArrayAsync(ct);
        // One latest snapshot per event, with deterministic tie break for timestamps sharing a second.
        var latest=EventFilters.LatestScores(db);
        var scored=from e in events join s in latest on e.Id equals s.EventId select new {e.CollectedAt,s.Score};
        var scoredCount=await scored.CountAsync(ct);var average=await scored.Select(s=>(double?)s.Score).AverageAsync(ct);
        var reportBuckets=await events.GroupBy(e=>Math.Floor((double)(e.CollectedAt-window.From)/window.BucketSeconds)).Select(g=>new {Index=g.Key,Count=g.Count()}).ToListAsync(ct);
        var scoreBuckets=await scored.GroupBy(s=>Math.Floor((double)(s.CollectedAt-window.From)/window.BucketSeconds)).Select(g=>new {Index=g.Key,Count=g.Count(),Average=g.Average(s=>(double)s.Score)}).ToListAsync(ct);
        var timeline=Enumerable.Range(0,(int)Math.Ceiling((double)(window.To-window.From)/window.BucketSeconds)).Select(i=>new Bucket(window.From+(long)i*window.BucketSeconds,reportBuckets.SingleOrDefault(b=>b.Index==i)?.Count??0,scoreBuckets.SingleOrDefault(b=>b.Index==i)?.Count??0,scoreBuckets.SingleOrDefault(b=>b.Index==i)?.Average)).ToArray();
        var deliveries=from delivery in db.Deliveries.AsNoTracking() join draft in db.Drafts.AsNoTracking() on delivery.DraftId equals draft.Id where draft.Demo==Registration.Demo&&delivery.CreatedAt>=window.From&&delivery.CreatedAt<window.To select delivery;
        var deliveryCounts=await deliveries.GroupBy(d=>new {d.Channel,d.Status}).Select(g=>new {g.Key.Channel,g.Key.Status,Count=g.Count()}).ToListAsync(ct);
        var channels=Connectors.Channels.Select(channel=>new Channel(channel,Connectors.Configured(channel),channel is "x" or "telegram" or "binance"?"api":"manual_export",deliveryCounts.Where(d=>d.Channel==channel).Sum(d=>d.Count),deliveryCounts.Where(d=>d.Channel==channel).Select(d=>new Count(d.Status,d.Count)).ToArray())).ToArray();
        await tx.CommitAsync(ct);
        // Existing model schema has no sentiment field; do not infer sentiment from importance or prose.
        return new(window,reports,analysed,scoredCount,average,sources,topics,processing,channels,timeline,reports,Clock.Now);
    }
}
