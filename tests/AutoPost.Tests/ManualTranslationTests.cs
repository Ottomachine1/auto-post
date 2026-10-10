using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace AutoPost.Tests;
public sealed partial class WorkflowTests
{
    [Fact]
    public async Task SchedulingCollectedEventsDoesNotCreateTranslationRequests()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");
        await using var db = Db();
        db.Events.Add(new Event { Title = "Foreign report", Body = "Original English content.", Language = "en" });
        await db.SaveChangesAsync();
        await new Pipeline(db, new Connectors(factory, db)).Schedule(default);
        Assert.False(await db.Jobs.AnyAsync(j => j.Kind == "translate" || j.Kind == "translate_manual"));
        Assert.Equal(0, factory.Calls);
        var item = await db.Events.SingleAsync(); Assert.Equal("Foreign report", item.Title); Assert.Empty(item.ChineseTitle);
    }
}
