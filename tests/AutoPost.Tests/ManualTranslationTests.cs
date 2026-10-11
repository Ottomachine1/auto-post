using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace AutoPost.Tests;
public sealed partial class WorkflowTests
{
    [Fact]
    public async Task MySqlUpgradePreservesEventsAndCancelsOnlyLegacyTranslationOnce()
    {
        await using var db=Db();
        if (!db.MySql) return;
        var item=new Event {Title="Preserved original",Body="Existing data"}; db.Events.Add(item);
        var legacy=new Job {Kind="translate",Target=item.Id}; db.Jobs.Add(legacy); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE `Translations`; DELETE FROM `AutoPostSchemaVersions` WHERE `Version`=9;");
        await db.Migrate(); db.ChangeTracker.Clear();
        Assert.Equal("Preserved original",(await db.Events.SingleAsync()).Title);
        Assert.Equal("cancelled",(await db.Jobs.SingleAsync()).Status);
        await ManualTranslations.Request(db,"标题","正文","zh","zh",default); await db.SaveChangesAsync();
        await db.Migrate(); db.ChangeTracker.Clear();
        Assert.Equal("pending",(await db.Jobs.SingleAsync(j=>j.Kind=="translate_manual")).Status);
    }
    [Fact]
    public async Task ManualTranslationCachesExactContentAndNeverOverwritesOriginal()
    {
        await using var db = Db();
        var first = await ManualTranslations.Request(db,"中文标题","原始中文内容","zh","zh",default);
        await db.SaveChangesAsync();
        await ManualTranslations.Execute(db,first.Id,factory,default); await db.SaveChangesAsync();
        Assert.Equal("native",first.Status); Assert.Equal("原始中文内容",first.TranslatedBody);
        var again=await ManualTranslations.Request(db,"中文标题","原始中文内容","zh","zh",default);
        Assert.Equal(first.Id,again.Id); Assert.Equal(0,factory.Calls);
        Assert.Equal(1,await db.Jobs.CountAsync(j=>j.Kind=="translate_manual"));
        var changed=await ManualTranslations.Request(db,"中文标题","已修改的中文内容","zh","zh",default);
        Assert.NotEqual(first.Id,changed.Id); Assert.Equal("原始中文内容",first.Body);
        await Assert.ThrowsAsync<ArgumentException>(()=>ManualTranslations.Request(db,"Title","Body","en","invalid",default));
    }
    [Fact]
    public async Task ManualCacheIsIsolatedByMode()
    {
        await using var db=Db();
        var live=await ManualTranslations.Request(db,"标题","正文","zh","zh",default); await db.SaveChangesAsync();
        Environment.SetEnvironmentVariable("APP_MODE","demo");
        try {
            var demo=await ManualTranslations.Request(db,"标题","正文","zh","zh",default);
            Assert.NotEqual(live.Id,demo.Id); Assert.True(demo.Demo); Assert.False(live.Demo);
        } finally { Environment.SetEnvironmentVariable("APP_MODE","live"); }
    }
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
