using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace AutoPost.Tests;
public sealed partial class WorkflowTests
{
 [Fact] public async Task QuarantineCancelsQueuedWorkPreservesContentAndRestoreRequiresReview(){
  await using var db=Db();var item=new Event{Id="moderated",SourceId="moderated",Body="Original retained"};db.Events.Add(item);var pipeline=new Pipeline(db,new Connectors(factory,db));var draft=pipeline.NewDraft("Draft",["x"],item.Id,false);draft.ApprovedRevision=1;draft.Status="approved";db.Deliveries.Add(new Delivery{Id="moderated-delivery",DraftId=draft.Id,Revision=1,Channel="x"});db.Jobs.AddRange(new Job{Kind="analyse",Target=item.Id},new Job{Kind="publish",Target="moderated-delivery"},new Job{Kind="collect",Target="source"});await db.SaveChangesAsync();
  await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock();await EventModeration.Change(db,item.Id,true,0,"low_value","Human review",default);await db.SaveChangesAsync();await tx.CommitAsync();}
  Assert.True(item.IsQuarantined);Assert.Null(draft.ApprovedRevision);Assert.Equal("blocked",(await db.Deliveries.SingleAsync()).Status);Assert.Equal(2,await db.Jobs.CountAsync(j=>j.Status=="cancelled"));Assert.Equal(1,await db.Jobs.CountAsync(j=>j.Kind=="collect"&&j.Status=="pending"));await pipeline.Analyse(item.Id,default);Assert.Equal(0,factory.Calls);Assert.Equal("来源情报已隔离",await pipeline.PublishRejection(await db.Deliveries.SingleAsync(),draft,default));await Assert.ThrowsAsync<InvalidOperationException>(()=>pipeline.QueuePublish(draft,null,default));
  await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock();await EventModeration.Change(db,item.Id,false,1,"incorrect_filter","Restore after review",default);await db.SaveChangesAsync();await tx.CommitAsync();}
  Assert.False(item.IsQuarantined);Assert.Equal(2,item.ModerationVersion);Assert.Equal("Original retained",item.Body);Assert.Null(draft.ApprovedRevision);Assert.Equal("blocked",(await db.Deliveries.SingleAsync()).Status);Assert.Equal(2,await db.Audits.CountAsync(a=>a.Action=="event_moderated"));Assert.Single(await db.Versions.ToListAsync());
 }
 [Theory][InlineData("sending")][InlineData("unknown")]
 public async Task QuarantineRefusesInFlightOrUncertainDeliveries(string status){await using var db=Db();var item=new Event{SourceId="uncertain"};db.Events.Add(item);var draft=new Draft{EventId=item.Id};db.Drafts.Add(draft);db.Deliveries.Add(new Delivery{DraftId=draft.Id,Channel="x",Status=status});await db.SaveChangesAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>EventModeration.Change(db,item.Id,true,0,"spam","Review required",default));Assert.False(item.IsQuarantined);Assert.Equal(0,item.ModerationVersion);}
 [Fact] public async Task QuarantineApiFiltersAndRestoresWithVersionAudit(){
  await using(var db=Db()){db.Events.Add(new Event{Id="api-moderated",SourceId="api-moderated",Title="Visible original"});await db.SaveChangesAsync();}
  using var app=new WebApplicationFactory<Program>();using var client=app.CreateClient();Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await client.GetAsync("/api/events/api-moderated/moderation")).StatusCode);client.DefaultRequestHeaders.Authorization=new("Bearer",Token);
  (await client.PostAsJsonAsync("/api/events/api-moderated/moderation",new{quarantine=true,expectedVersion=0,reason="spam",note="Human decision"})).EnsureSuccessStatusCode();
  using(var visible=JsonDocument.Parse(await client.GetStringAsync("/api/events/page")))Assert.Empty(visible.RootElement.GetProperty("items").EnumerateArray());
  using(var hidden=JsonDocument.Parse(await client.GetStringAsync("/api/events/page?visibility=quarantined")))Assert.Equal("api-moderated",hidden.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
  Assert.False((await client.PostAsJsonAsync("/api/events/api-moderated/moderation",new{quarantine=false,expectedVersion=0,reason="reviewed",note="Stale"})).IsSuccessStatusCode);
  Assert.False((await client.PostAsJsonAsync("/api/events/api-moderated/analysis",new{})).IsSuccessStatusCode);
  (await client.PostAsJsonAsync("/api/events/api-moderated/moderation",new{quarantine=false,expectedVersion=1,reason="incorrect_filter",note="Reviewed original"})).EnsureSuccessStatusCode();
  using var history=JsonDocument.Parse(await client.GetStringAsync("/api/events/api-moderated/moderation"));Assert.Equal(2,history.RootElement.GetProperty("history").GetArrayLength());
 }
 [Fact] public async Task AnalysisFinishingAfterQuarantineCannotCreateDraftOrDelivery(){
  await using var db=Db();var item=new Event{Source="rss",SourceId="analysis-race",Title="Bitcoin"};db.Events.Add(item);(await db.Settings.SingleAsync()).AutoPaused=false;db.Rules.Add(new Rule{Enabled=true,Sources="[\"rss\"]",Channels="[\"x\"]"});await db.SaveChangesAsync();
  factory.Responses.Enqueue(Json.Write(new{choices=new[]{new{message=new{content=Json.Write(new AnalysisResult("summary",[],[],"draft",[],"news"))}}}}));
  factory.BeforeReply=async ct=>{await using var other=Db();await using var tx=await other.Database.BeginTransactionAsync(ct);await other.Lock(ct);await EventModeration.Change(other,item.Id,true,0,"low_value","While model was running",ct);await other.SaveChangesAsync(ct);await tx.CommitAsync(ct);};
  await new Pipeline(db,new Connectors(factory,db)).Analyse(item.Id,default);Assert.True(item.IsQuarantined);Assert.Single(await db.Analyses.ToListAsync());Assert.Empty(await db.Drafts.ToListAsync());Assert.Empty(await db.Deliveries.ToListAsync());
 }
 [Fact] public async Task AgentFinishingAfterQuarantineCannotCommitDraft(){
  Environment.SetEnvironmentVariable("OPENAI_MODEL","test-model");await using var db=Db();var item=new Event{SourceId="agent-race",Title="Bitcoin"};db.Events.Add(item);await db.SaveChangesAsync();var(message,job)=await AgentTask(db,"draft","Bitcoin");AgentResponse();
  factory.BeforeReply=async ct=>{await using var other=Db();await using var tx=await other.Database.BeginTransactionAsync(ct);await other.Lock(ct);await EventModeration.Change(other,item.Id,true,0,"spam","While Agent was running",ct);await other.SaveChangesAsync(ct);await tx.CommitAsync(ct);};var pipeline=new Pipeline(db,new Connectors(factory,db));await new AgentService(db,new Connectors(factory,db),pipeline).Run(job,default);db.ChangeTracker.Clear();Assert.Equal("failed",(await db.AgentMessages.FindAsync(message.Id))!.Status);Assert.Empty(await db.Drafts.ToListAsync());
 }
 [Fact] public async Task EventModerationMigrationUpgradesExistingMySql(){await using var db=Db();if(!db.MySql)return;db.Events.Add(new Event{Id="before-moderation",SourceId="before-moderation",Body="Keep original"});await db.SaveChangesAsync();await db.Database.ExecuteSqlRawAsync("DROP INDEX `IX_Events_Demo_IsQuarantined_PublishedAt_Id` ON `Events`; ALTER TABLE `Events` DROP COLUMN `IsQuarantined`, DROP COLUMN `ModerationVersion`, DROP COLUMN `ModerationReason`, DROP COLUMN `QuarantinedAt`");db.ChangeTracker.Clear();await db.Migrate();await db.Migrate();var item=await db.Events.SingleAsync();Assert.Equal("Keep original",item.Body);Assert.False(item.IsQuarantined);Assert.Equal(0,item.ModerationVersion);}

}
