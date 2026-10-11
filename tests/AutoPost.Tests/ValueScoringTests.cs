using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace AutoPost.Tests;
public sealed class ValueScoringUnitTests
{
    [Fact]
    public void ScoresAreBoundedExplainUncertaintyAndDecay()
    {
        var weights=ValueScoring.Weights(new Settings().ScoreWeightsJson);var now=Clock.Now;
        var item=new Event {Title="Bitcoin ETF",Body=new string('x',100),Url="https://example.com/story",PublishedAt=now,Author="Newsroom"};
        var fresh=ValueScoring.Evaluate(item,null,1,new(0,0,0),null,weights,1,now);
        Assert.InRange(fresh.Score,0,100);Assert.Equal("pending",fresh.Verification);Assert.Null(fresh.Model);Assert.Null(fresh.Traffic);Assert.Equal(50,fresh.Components["source"].Value);
        var old=ValueScoring.Evaluate(item,null,16,new(0,0,0),null,weights,1,now+86400);
        Assert.True(old.Score<fresh.Score);Assert.True(old.Components["novelty"].Value<fresh.Components["novelty"].Value);
        item.PublishedEstimated=true;Assert.Equal(50,ValueScoring.Evaluate(item,null,1,new(0,0,0),null,weights,1,now).Components["freshness"].Value);
        Assert.Throws<ArgumentException>(()=>ValueScoring.Weights("{\"source\":100}"));
    }
    [Fact]
    public void ExistingModelAndHumanFeedbackAreTrackedWithoutTruthClaim()
    {
        var weights=ValueScoring.Weights(new Settings().ScoreWeightsJson);var item=new Event {PublishedAt=Clock.Now};
        var analysis=new Analysis {Model="test-model",PromptVersion="test-v1",Result=Json.Write(new AnalysisResult("summary",[],[],"draft",Importance:100))};
        var detail=ValueScoring.Evaluate(item,analysis,1,new(1,100,Clock.Now),new ValueFeedback {Rating=100},weights,2,Clock.Now);
        Assert.Equal("test-model",detail.Model);Assert.Equal(100,detail.Components["impact"].Value);Assert.InRange(detail.Components["source"].Value,50,60);Assert.True(detail.FeedbackAdjustment>0);Assert.Equal(2,detail.WeightVersion);Assert.Equal("pending",detail.Verification);
        analysis.Result="{\"importance\":\"unavailable\"}";
        Assert.Null(ValueScoring.Evaluate(item,analysis,1,new(0,0,0),null,weights,2,Clock.Now).Model);
    }
}
public sealed partial class WorkflowTests
{
    [Fact]
    public async Task ValueScoreBatchPersistsCacheAndSeparatesModes()
    {
        await using var db=Db();var item=new Event {Source="score-source",SourceId="score-1",Title="Bitcoin ETF",PublishedAt=Clock.Now,GroupId="score-group"};db.Events.Add(item);await db.SaveChangesAsync();
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock();var scores=await ValueScoring.Batch(db,[item.Id],default);Assert.InRange(scores[item.Id].Score,0,100);await db.SaveChangesAsync();await tx.CommitAsync();}
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Lock();await ValueScoring.Batch(db,[item.Id],default);await db.SaveChangesAsync();await tx.CommitAsync();}
        Assert.Equal(1,await db.ValueScores.CountAsync());Assert.Equal(0,factory.Calls);
        db.Events.Add(new Event {Id="score-demo",Demo=true,Source="demo",SourceId="score-demo"});await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(()=>ValueScoring.Batch(db,["score-demo"],default));
    }
    [Fact]
    public async Task ValueMigrationUpgradesExistingMySqlWithoutLosingEvents()
    {
        await using var db=Db();if(!db.MySql)return;
        db.Events.Add(new Event {Id="pre-scoring",Source="migration",SourceId="pre-scoring",Title="Preserved"});await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE `ValueScores`; DROP TABLE `ValueFeedback`; ALTER TABLE `Settings` DROP COLUMN `ScoreWeightsJson`, DROP COLUMN `ScoreWeightsVersion`");
        db.ChangeTracker.Clear();await db.Migrate();await db.Migrate();
        Assert.Equal("Preserved",(await db.Events.SingleAsync()).Title);Assert.Equal(100,ValueScoring.Weights((await db.Settings.SingleAsync()).ScoreWeightsJson).Values.Sum());Assert.Empty(await db.ValueScores.ToListAsync());
    }
    [Fact]
    public async Task ScoreApiValidatesWeightsAndUpsertsFeedback()
    {
        await using(var db=Db()){db.Events.Add(new Event {Id="score-api-event",Source="score-api-source",SourceId="score-api-event",PublishedAt=Clock.Now});await db.SaveChangesAsync();}
        using var app=new WebApplicationFactory<Program>();using var client=app.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await client.GetAsync("/api/scoring/profile")).StatusCode);client.DefaultRequestHeaders.Authorization=new("Bearer",Token);
        (await client.PostAsJsonAsync("/api/events/scores",new {ids=new[]{"score-api-event"}})).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/scoring/feedback",new {eventId="score-api-event",rating=80,sourceRating=90,note="First"})).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/scoring/feedback",new {eventId="score-api-event",rating=20,sourceRating=30,note="Revised"})).EnsureSuccessStatusCode();
        await using(var db=Db()){Assert.Equal(1,await db.ValueFeedback.CountAsync());Assert.Equal(20,(await db.ValueFeedback.SingleAsync()).Rating);}
        var bad=await client.PutAsJsonAsync("/api/scoring/profile",new {weights=new {source=100},expectedVersion=1});Assert.False(bad.IsSuccessStatusCode);
        var weights=new {source=20,freshness=20,impact=20,novelty=20,evidence=20};
        (await client.PutAsJsonAsync("/api/scoring/profile",new {weights,expectedVersion=1})).EnsureSuccessStatusCode();
        Assert.False((await client.PutAsJsonAsync("/api/scoring/profile",new {weights,expectedVersion=1})).IsSuccessStatusCode);
        using var profile=JsonDocument.Parse(await client.GetStringAsync("/api/scoring/profile"));Assert.Equal(2,profile.RootElement.GetProperty("version").GetInt32());
        using var scores=JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/events/scores",new {ids=new[]{"score-api-event"}})).Content.ReadAsStringAsync());Assert.Equal(2,scores.RootElement.GetProperty("score-api-event").GetProperty("weightVersion").GetInt32());
    }
}
