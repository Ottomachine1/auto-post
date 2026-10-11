using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AutoPost.Infrastructure;

public static class ValueScoring
{
    public static readonly string[] Dimensions=["source","freshness","impact","novelty","evidence"];
    public sealed record Dimension(double Value,string Reason);
    public sealed record Detail(int Score,Dictionary<string,Dimension> Components,Dictionary<string,int> Weights,int WeightVersion,string Algorithm,string? Model,string? PromptVersion,string Verification,double FeedbackAdjustment,int? Traffic,long AsOf);
    public sealed record SourceSignal(int Samples,double Total,long UpdatedAt);
    public static Dictionary<string,int> Weights(string json)
    {
        var values=Json.Read<Dictionary<string,int>>(json);
        if(values is null||values.Count!=5||Dimensions.Any(d=>!values.ContainsKey(d))||values.Values.Any(v=>v<0||v>100)||values.Values.Sum()!=100)
            throw new ArgumentException("五项评分权重必须在0至100之间，合计100");
        return values;
    }
    public static Detail Evaluate(Event item,Analysis? analysis,int groupSize,SourceSignal signal,ValueFeedback? feedback,Dictionary<string,int> weights,int version,long now)
    {
        Weights(Json.Write(weights));
        AnalysisResult? ai=null;
        try{if(analysis!=null){using var document=JsonDocument.Parse(analysis.Result);if(document.RootElement.TryGetProperty("importance",out var importance)&&importance.ValueKind==JsonValueKind.Number&&importance.TryGetInt32(out var value)&&value is >=0 and <=100)ai=Json.Read<AnalysisResult>(analysis.Result);}}catch(JsonException){}
        if(ai?.Importance is <0 or >100)ai=null;
        var source=(250+signal.Total)/(5+signal.Samples);
        var validTime=!item.PublishedEstimated&&item.PublishedAt>0&&item.PublishedAt<=now+300;
        var age=Math.Max(0,now-item.PublishedAt)/3600d;
        var fresh=validTime?100*Math.Exp(-age/24):50;
        var text=(item.Title+" "+item.Body).ToLowerInvariant();
        var terms=new[]{"bitcoin","ethereum","solana","fomc","federal reserve","etf","inflation","stablecoin","openai","deepseek","nvidia","比特币","以太坊","稳定币","美联储","人工智能","芯片","监管","战争","黑客"};
        var matches=terms.Count(text.Contains);
        var impact=ai?.Importance is >=0 and <=100?ai.Importance:Math.Min(70,30+matches*10);
        var novelty=100d/Math.Sqrt(Math.Max(1,groupSize));
        var url=Uri.TryCreate(item.Url,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http";
        var evidence=(url?40:0)+(validTime?20:0)+(item.Body.Length>=80?25:0)+(!string.IsNullOrWhiteSpace(item.Author)?15:0);
        var components=new Dictionary<string,Dimension>{
            ["source"]=new(Math.Round(source,2),signal.Samples==0?"来源可信度待验证，采用50分中性先验；网站流量未知":$"{signal.Samples}条人工来源评价，与5条50分中性先验平滑；不代表事实核验"),
            ["freshness"]=new(Math.Round(fresh,2),validTime?$"距来源发布时间{age:F1}小时，24小时指数衰减":"发布时间缺失、估计或异常，采用50分；待验证"),
            ["impact"]=new(impact,ai!=null?"采用已有模型重要性输出，不额外调用模型；属于潜在影响判断":$"本地关注词匹配{matches}项，规则估分；尚无模型判断"),
            ["novelty"]=new(Math.Round(novelty,2),$"当前事件组包含{Math.Max(1,groupSize)}条采样报道，重复越多新颖性越低；不代表独立证据"),
            ["evidence"]=new(evidence,"按来源链接、可用发布时间、摘要长度、作者字段评估元数据完整度；不是事实验证")};
        var baseline=components.Sum(p=>p.Value.Value*weights[p.Key]/100);
        var adjustment=feedback==null?0:(feedback.Rating-baseline)*0.2;
        var score=(int)Math.Round(Math.Clamp(baseline+adjustment,0,100));
        return new(score,components,weights,version,"value-v1",ai==null?null:analysis!.Model,ai==null?null:analysis!.PromptVersion,"pending",Math.Round(adjustment,2),null,now);
    }
    // Caller owns transaction and global write lock. One batch, never one external model call per item.
    public static async Task<Dictionary<string,Detail>> Batch(Store db,string[] ids,CancellationToken ct)
    {
        if(ids is null||ids.Length is <1 or >200||ids.Any(string.IsNullOrWhiteSpace)||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("请选择1至200条不重复情报");
        var items=await db.Events.AsNoTracking().Where(e=>ids.Contains(e.Id)&&e.Demo==Registration.Demo).ToListAsync(ct);
        if(items.Count!=ids.Length)throw new ArgumentException("情报不存在或模式不符");
        var settings=await db.Settings.SingleAsync(ct);var weights=Weights(settings.ScoreWeightsJson);
        var sources=items.Select(e=>e.Source).Distinct().ToArray();var groups=items.Where(e=>e.GroupId!="").Select(e=>e.GroupId).Distinct().ToArray();
        var analyses=await db.Analyses.AsNoTracking().Where(a=>ids.Contains(a.EventId)).ToDictionaryAsync(a=>a.EventId,ct);
        var feedback=await db.ValueFeedback.AsNoTracking().Where(f=>sources.Contains(f.SourceId)&&f.Demo==Registration.Demo).ToListAsync(ct);
        var counts=await db.Events.AsNoTracking().Where(e=>groups.Contains(e.GroupId)&&e.Demo==Registration.Demo).GroupBy(e=>e.GroupId).Select(g=>new {Id=g.Key,Count=g.Count()}).ToDictionaryAsync(g=>g.Id,g=>g.Count,ct);
        var now=Clock.Now;var bucket=now/3600*3600;
        var details=new Dictionary<string,Detail>();var hashes=new Dictionary<string,string>();
        foreach(var item in items)
        {
            var ratings=feedback.Where(f=>f.SourceId==item.Source&&f.SourceRating.HasValue).ToArray();
            var signal=new SourceSignal(ratings.Length,ratings.Sum(f=>f.SourceRating!.Value),ratings.Select(f=>f.UpdatedAt).DefaultIfEmpty().Max());
            analyses.TryGetValue(item.Id,out var analysis);var own=feedback.SingleOrDefault(f=>f.EventId==item.Id);
            var detail=Evaluate(item,analysis,counts.GetValueOrDefault(item.GroupId,1),signal,own,weights,settings.ScoreWeightsVersion,now);
            var input=Json.Write(new {item.Title,item.Body,item.Url,item.Author,item.PublishedAt,item.PublishedEstimated,bucket,weights,settings.ScoreWeightsVersion,analysis,groupSize=counts.GetValueOrDefault(item.GroupId,1),signal,own});
            hashes[item.Id]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));details[item.Id]=detail;
        }
        var hashValues=hashes.Values.ToArray();
        var cached=await db.ValueScores.AsNoTracking().Where(s=>ids.Contains(s.EventId)&&hashValues.Contains(s.InputHash)&&s.Demo==Registration.Demo).ToListAsync(ct);
        foreach(var item in items)
        {
            var old=cached.SingleOrDefault(s=>s.EventId==item.Id&&s.InputHash==hashes[item.Id]);
            if(old!=null)details[item.Id]=Json.Read<Detail>(old.DetailJson);
            else db.ValueScores.Add(new ValueScore {EventId=item.Id,Demo=item.Demo,InputHash=hashes[item.Id],Score=details[item.Id].Score,DetailJson=Json.Write(details[item.Id])});
        }
        return details;
    }
}
