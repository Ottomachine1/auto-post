using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;
namespace AutoPost.Tests;
public sealed class EventFilterUnitTests
{
 [Fact] public void InvalidCombinationsFailBeforeQuerying(){using var db=new Store(new DbContextOptionsBuilder<Store>().Options);var query=Array.Empty<Event>().AsQueryable();Assert.Throws<ArgumentException>(()=>EventFilters.Apply(db,query,null,2,1,null,null,null));Assert.Throws<ArgumentException>(()=>EventFilters.Apply(db,query,"zh<script>",null,null,null,null,null));Assert.Throws<ArgumentException>(()=>EventFilters.Apply(db,query,null,null,null,80,20,null));Assert.Throws<ArgumentException>(()=>EventFilters.Apply(db,query,null,null,null,0,null,"unscored"));}
}
public sealed partial class WorkflowTests
{
 [Fact] public async Task EventFiltersUseLatestSnapshotsPreserveCursorAndDoNotScore(){
  await using(var db=Db()){
   for(var i=0;i<52;i++){var id="filter-"+i.ToString("D3");db.Events.Add(new Event{Id=id,Source="filter-source",SourceId=id,Language=i==0?"ZH":"zh",CollectedAt=10000+i,PublishedAt=20000+i});db.ValueScores.Add(new ValueScore{Id="score-"+id,EventId=id,InputHash=id,Score=90,CreatedAt=1,DetailJson=Json.Write(ValueScoring.Evaluate(new Event(),null,1,new(0,0,0),null,ValueScoring.Weights(new Settings().ScoreWeightsJson),1,Clock.Now) with{Score=90})});}
   db.Events.AddRange(new Event{Id="revised",SourceId="revised",Language="zh",CollectedAt=10000,PublishedAt=30000},new Event{Id="unscored",SourceId="unscored",Language="zh",CollectedAt=10000,PublishedAt=30001},new Event{Id="filter-demo",SourceId="demo",Demo=true,Language="zh",CollectedAt=10000});
   db.ValueScores.AddRange(new ValueScore{Id="revised-old",EventId="revised",InputHash="old",Score=99,CreatedAt=1},new ValueScore{Id="revised-new",EventId="revised",InputHash="new",Score=20,CreatedAt=2});await db.SaveChangesAsync();
  }
  using var app=new WebApplicationFactory<Program>();using var client=app.CreateClient();client.DefaultRequestHeaders.Authorization=new("Bearer",Token);
  const string path="/api/events/page?language=zh&from=10000&to=11000&minScore=80&maxScore=100";
  using var first=JsonDocument.Parse(await client.GetStringAsync(path));var items=first.RootElement.GetProperty("items");Assert.Equal(50,items.GetArrayLength());var cursor=first.RootElement.GetProperty("nextCursor").GetString()!;
  using var second=JsonDocument.Parse(await client.GetStringAsync(path+"&cursor="+Uri.EscapeDataString(cursor)));var tail=second.RootElement.GetProperty("items");Assert.Equal(2,tail.GetArrayLength());Assert.Empty(items.EnumerateArray().Select(e=>e.GetProperty("id").GetString()).Intersect(tail.EnumerateArray().Select(e=>e.GetProperty("id").GetString())));
  using var missing=JsonDocument.Parse(await client.GetStringAsync("/api/events/page?language=zh&scoreState=unscored"));Assert.Equal("unscored",missing.RootElement.GetProperty("items")[0].GetProperty("id").GetString());Assert.Equal(1,missing.RootElement.GetProperty("items").GetArrayLength());
  using var snapshots=JsonDocument.Parse(await client.GetStringAsync("/api/scoring/snapshots?ids=filter-000,unscored"));Assert.Equal(90,snapshots.RootElement.GetProperty("filter-000").GetProperty("score").GetInt32());Assert.False(snapshots.RootElement.TryGetProperty("unscored",out _));
  Assert.False((await client.GetAsync("/api/scoring/snapshots?ids=filter-demo")).IsSuccessStatusCode);Assert.False((await client.GetAsync("/api/events/page?minScore=101")).IsSuccessStatusCode);
  await using var check=Db();Assert.Equal(54,await check.ValueScores.CountAsync());Assert.Empty(await check.Jobs.ToListAsync());
 }
}
