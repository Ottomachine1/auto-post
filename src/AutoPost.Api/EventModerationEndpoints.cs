using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
public static class EventModerationEndpoints
{
 public sealed record Input(bool Quarantine,int ExpectedVersion,string Reason,string Note);
 public static void MapEventModeration(this RouteGroupBuilder api){
  api.MapPost("/events/{id}/moderation",async(string id,Input input,Store db,CancellationToken ct)=>Results.Ok(await EventModeration.Change(db,id,input.Quarantine,input.ExpectedVersion,input.Reason,input.Note,ct)));
  api.MapGet("/events/{id}/moderation",async(string id,Store db,CancellationToken ct)=>{var item=await db.Events.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id&&e.Demo==Registration.Demo,ct)??throw new ArgumentException("情报不存在");return Results.Ok(new{item.IsQuarantined,item.ModerationVersion,item.ModerationReason,item.QuarantinedAt,history=await db.Audits.Where(a=>a.Target==id&&a.Action=="event_moderated").OrderByDescending(a=>a.CreatedAt).ThenByDescending(a=>a.Id).Take(100).ToListAsync(ct)});});
 }
}
