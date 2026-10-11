using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
public static class ValueScoringEndpoints
{
    public sealed record BatchInput(string[] Ids);
    public sealed record ProfileInput(Dictionary<string,int> Weights,int ExpectedVersion);
    public sealed record FeedbackInput(string EventId,int Rating,int? SourceRating,string? Note);
    public static void MapValueScoring(this RouteGroupBuilder api)
    {
        api.MapGet("/scoring/snapshots",async(string ids,Store db,CancellationToken ct)=>{
            var keys=ids.Split(',');if(keys.Length is <1 or >200||keys.Any(string.IsNullOrWhiteSpace)||keys.Distinct().Count()!=keys.Length)throw new ArgumentException("请选择1至200条不重复情报");
            if(await db.Events.CountAsync(e=>keys.Contains(e.Id)&&e.Demo==Registration.Demo,ct)!=keys.Length)throw new ArgumentException("情报不存在或模式不符");
            var snapshots=await EventFilters.LatestScores(db).Where(s=>keys.Contains(s.EventId)).ToListAsync(ct);
            return Results.Ok(snapshots.ToDictionary(s=>s.EventId,s=>Json.Read<ValueScoring.Detail>(s.DetailJson)));
        });
        api.MapPost("/events/scores",async(BatchInput input,Store db,CancellationToken ct)=>Results.Ok(await ValueScoring.Batch(db,input.Ids,ct)));
        api.MapGet("/scoring/profile",async(Store db,CancellationToken ct)=>{
            var settings=await db.Settings.SingleAsync(ct);
            return Results.Ok(new {weights=ValueScoring.Weights(settings.ScoreWeightsJson),version=settings.ScoreWeightsVersion,algorithm="value-v1",traffic=(int?)null});
        });
        api.MapPut("/scoring/profile",async(ProfileInput input,Store db,CancellationToken ct)=>{
            var weights=ValueScoring.Weights(Json.Write(input.Weights));var settings=await db.Settings.SingleAsync(ct);
            if(settings.ScoreWeightsVersion!=input.ExpectedVersion)throw new InvalidOperationException("评分权重版本已变化，请刷新");
            settings.ScoreWeightsJson=Json.Write(weights);settings.ScoreWeightsVersion++;
            db.Mark("score_weights","settings",Json.Write(new {version=settings.ScoreWeightsVersion,weights}));
            return Results.Ok(new {version=settings.ScoreWeightsVersion,weights});
        });
        api.MapPost("/scoring/feedback",async(FeedbackInput input,Store db,CancellationToken ct)=>{
            if(input.Rating is <0 or >100||input.SourceRating is <0 or >100||(input.Note?.Length??0)>2000)throw new ArgumentException("评价必须在0至100之间，说明最多2000字");
            var item=await db.Events.SingleOrDefaultAsync(e=>e.Id==input.EventId&&e.Demo==Registration.Demo,ct)??throw new ArgumentException("情报不存在");
            var feedback=await db.ValueFeedback.SingleOrDefaultAsync(f=>f.EventId==item.Id&&f.Demo==item.Demo,ct);
            var previous=feedback==null?null:Json.Write(feedback);
            if(feedback==null){feedback=new ValueFeedback {EventId=item.Id,SourceId=item.Source,Demo=item.Demo};db.ValueFeedback.Add(feedback);}
            feedback.Rating=input.Rating;feedback.SourceRating=input.SourceRating;feedback.Note=input.Note??"";feedback.UpdatedAt=Clock.Now;
            db.Mark("score_feedback",item.Id,Json.Write(new {previous,rating=input.Rating,sourceRating=input.SourceRating,note=input.Note}));
            return Results.Ok(new {feedback.Id});
        });
        api.MapGet("/scoring/feedback/{id}",async(string id,Store db,CancellationToken ct)=>
            Results.Ok(await db.ValueFeedback.AsNoTracking().SingleOrDefaultAsync(f=>f.EventId==id&&f.Demo==Registration.Demo,ct)));
    }
}
