using AutoPost.Core;
using AutoPost.Infrastructure;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
namespace AutoPost.Tests;
public sealed class AnalyticsUnitTests
{
 [Fact] public void RangesAreBoundedAndExplicit(){var w=Analytics.Resolve("1h",null,null,1000000);Assert.Equal(996400,w.From);Assert.Equal(300,w.BucketSeconds);Assert.Equal("collectedAt",w.TimeBasis);Assert.Throws<ArgumentException>(()=>Analytics.Resolve("all",null,null,1000000));Assert.Throws<ArgumentException>(()=>Analytics.Resolve("custom",10,10,1000000));Assert.Throws<ArgumentException>(()=>Analytics.Resolve("custom",0,10000000,10000000));}
}
public sealed partial class WorkflowTests
{
 [Fact] public async Task AnalyticsCountsLatestScoresWithoutDuplicatesOrModeLeak(){
  await using var db=Db();const long start=1000000;
  db.Events.AddRange(new Event{Id="a",Source="alpha",SourceId="a",Category="AI",CollectedAt=start,PublishedAt=1},new Event{Id="b",Source="beta",SourceId="b",Category="AI",CollectedAt=start+300,AnalysisStatus="completed"},new Event{Id="c",Source="alpha",SourceId="c",Category="宏观",CollectedAt=start+3599},new Event{Id="outside",SourceId="o",CollectedAt=start+3600},new Event{Id="demo",SourceId="d",CollectedAt=start,Demo=true});
  db.Analyses.Add(new Analysis{EventId="b"});
  db.ValueScores.AddRange(new ValueScore{Id="s1",EventId="a",InputHash="1",Score=20,CreatedAt=10},new ValueScore{Id="s2",EventId="a",InputHash="2",Score=80,CreatedAt=20},new ValueScore{Id="s3",EventId="a",InputHash="3",Score=90,CreatedAt=20},new ValueScore{Id="s4",EventId="b",InputHash="4",Score=50,CreatedAt=20});
  db.Drafts.AddRange(new Draft{Id="draft-live"},new Draft{Id="draft-demo",Demo=true});db.Deliveries.AddRange(new Delivery{DraftId="draft-live",Channel="x",Revision=1,CreatedAt=start,Status="unknown"},new Delivery{DraftId="draft-demo",Channel="x",Revision=1,CreatedAt=start,Status="sent"});
  await db.SaveChangesAsync();var report=await Analytics.Read(db,Analytics.Resolve("custom",start,start+3600,start+3600),default);
  Assert.Equal(1,report.Channels.Single(c=>c.Key=="x").Deliveries);Assert.Equal("unknown",report.Channels.Single(c=>c.Key=="x").Statuses.Single().Key);Assert.Equal(3,report.Reports);Assert.Equal(1,report.AnalysedReports);Assert.Equal(2,report.ScoredReports);Assert.Equal(70,report.AverageScore);Assert.Equal(12,report.Timeline.Length);Assert.Equal(3,report.Timeline.Sum(x=>x.Reports));Assert.Equal(2,report.Sources.Single(x=>x.Key=="alpha").Value);Assert.Equal(2,report.Topics.Single(x=>x.Key=="AI").Value);Assert.Equal(3,report.UnknownSentimentReports);Assert.Empty(db.Jobs);Assert.Equal(4,db.ValueScores.Count());
 }
 [Fact] public async Task AnalyticsApiRequiresAuthAndRejectsInvalidWindows(){using var app=new WebApplicationFactory<Program>();using var client=app.CreateClient();Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await client.GetAsync("/api/analytics?range=1h")).StatusCode);client.DefaultRequestHeaders.Authorization=new("Bearer",Token);Assert.False((await client.GetAsync("/api/analytics?range=custom&from=10&to=5")).IsSuccessStatusCode);var report=await client.GetFromJsonAsync<Analytics.Report>("/api/analytics?range=24h");Assert.NotNull(report);Assert.Equal(0,report.Reports);Assert.Null(report.AverageScore);Assert.All(report.Timeline,b=>Assert.Equal(0,b.Reports));}
}
