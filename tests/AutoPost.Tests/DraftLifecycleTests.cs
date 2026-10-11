using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
namespace AutoPost.Tests;
public sealed partial class WorkflowTests
{
    [Fact]
    public async Task DraftManagementApiPaginatesTrashAndRestoresFreshVersion()
    {
        using var app=new WebApplicationFactory<Program>();using var client=app.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await client.GetAsync("/api/drafts/page")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",Token);
        var response=await client.PostAsJsonAsync("/api/drafts",new {content="Management fixture",channels=new[]{"x"}});response.EnsureSuccessStatusCode();
        var created=await response.Content.ReadFromJsonAsync<IdResult>();
        (await client.PostAsJsonAsync("/api/drafts/bulk",new {action="trash",items=new[]{new {id=created!.Id,revision=1}}})).EnsureSuccessStatusCode();
        using var selection=JsonDocument.Parse(await client.GetStringAsync("/api/drafts/selection?view=trash&q=Management&channel=x"));
        Assert.Equal(created!.Id,selection.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
        using var emptySelection=JsonDocument.Parse(await client.GetStringAsync("/api/drafts/selection?view=active&q=Management"));
        Assert.Equal(0,emptySelection.RootElement.GetProperty("items").GetArrayLength());
        using var trash=JsonDocument.Parse(await client.GetStringAsync("/api/drafts/page?view=trash&q=Management&channel=x"));Assert.Equal(1,trash.RootElement.GetProperty("total").GetInt32());
        using var active=JsonDocument.Parse(await client.GetStringAsync("/api/drafts/page"));Assert.Equal(0,active.RootElement.GetProperty("total").GetInt32());
        (await client.PostAsJsonAsync("/api/drafts/bulk",new {action="restore",items=new[]{new {id=created!.Id,revision=1}}})).EnsureSuccessStatusCode();
        using var restored=JsonDocument.Parse(await client.GetStringAsync("/api/drafts/page"));Assert.Equal(2,restored.RootElement.GetProperty("items")[0].GetProperty("revision").GetInt32());
    }
    [Fact]
    public async Task TrashingCancelsQueuePreservesHistoryAndRestoreNeedsReview()
    {
        await using var db=Db();var pipeline=new Pipeline(db,new Connectors(factory,db));
        var item=new Event {Title="Original source"};db.Events.Add(item);
        var draft=pipeline.NewDraft("Draft text",["x"],item.Id,false);draft.ApprovedRevision=draft.Revision;
        await db.SaveChangesAsync(); await pipeline.QueuePublish(draft,null,default);await db.SaveChangesAsync();
        await DraftLifecycle.Apply(db,[new(draft.Id,draft.Revision)],"trash",default);await db.SaveChangesAsync();
        Assert.NotNull(draft.DeletedAt);Assert.Equal(1,await db.Events.CountAsync());Assert.Equal(1,await db.Versions.CountAsync());
        Assert.Equal("cancelled",(await db.Deliveries.SingleAsync()).Status);Assert.Equal("cancelled",(await db.Jobs.SingleAsync()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>pipeline.QueuePublish(draft,null,default));
        await DraftLifecycle.Apply(db,[new(draft.Id,draft.Revision)],"restore",default);await db.SaveChangesAsync();
        Assert.Null(draft.DeletedAt);Assert.Null(draft.ApprovedRevision);Assert.Equal("draft",draft.Status);
        Assert.Equal(2,draft.Revision);Assert.Equal(2,await db.Versions.CountAsync());
        Assert.Equal(0,factory.Calls);
    }
    [Fact]
    public async Task UnknownDeliveryAndStaleBatchCannotBeHidden()
    {
        await using var db=Db();var pipeline=new Pipeline(db,new Connectors(factory,db));
        var first=pipeline.NewDraft("First",["x"],null,false);var second=pipeline.NewDraft("Second",["x"],null,false);
        db.Deliveries.Add(new Delivery {DraftId=first.Id,Revision=first.Revision,Channel="x",Status="unknown"});await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>DraftLifecycle.Apply(db,[new(first.Id,first.Revision)],"trash",default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>DraftLifecycle.Apply(db,[new(first.Id,first.Revision),new(second.Id,99)],"trash",default));
        Assert.Null(first.DeletedAt);Assert.Null(second.DeletedAt);
    }
}
