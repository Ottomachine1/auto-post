using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;

// Explicit offline transfer. Stop both writers first. Existing rows are never overwritten.
internal static class DatabaseTransfer
{
    private static readonly Type[] Tables = [typeof(Event),typeof(Analysis),typeof(Draft),typeof(DraftVersion),typeof(Delivery),typeof(Rule),typeof(Source),typeof(Job),typeof(Change),typeof(Audit),typeof(Settings),typeof(Budget),typeof(MediaAsset),typeof(AgentSession),typeof(AgentMessage)];
    public static async Task Run(Store db,string command,string file)
    {
        var document = new Dictionary<string,JsonElement>();
        if(command=="export-db") {
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            foreach(var type in Tables) document[type.Name]=await Read(db,type);
            await tx.CommitAsync();
            await File.WriteAllTextAsync(file,Json.Write(document));
        } else if(command=="import-db") {
            document=Json.Read<Dictionary<string,JsonElement>>(await File.ReadAllTextAsync(file));
            await using var tx=await db.Database.BeginTransactionAsync(); await db.Lock();
            foreach(var type in Tables) {
                var entities=(System.Collections.IEnumerable)JsonSerializer.Deserialize(document[type.Name],typeof(List<>).MakeGenericType(type),Json.Options)!;
                var id=type.GetProperty("Id")!;
                foreach(var row in entities) {
                    var existing=await db.FindAsync(type,id.GetValue(row));
                    if(existing!=null) {
                        // EnsureCreated seeds only the default singleton; replace its initial values.
                        if(type==typeof(Settings)) db.Entry(existing).CurrentValues.SetValues(row);
                        else if(JsonSerializer.Serialize(existing,type,Json.Options)!=JsonSerializer.Serialize(row,type,Json.Options)) throw new InvalidOperationException("Target has differing rows; use an empty dedicated database.");
                    } else db.Add(row);
                }
                await db.SaveChangesAsync();db.ChangeTracker.Clear();
            }
            await tx.CommitAsync();
        } else document=Json.Read<Dictionary<string,JsonElement>>(await File.ReadAllTextAsync(file));
        var report=new Dictionary<string,object>();
        foreach(var type in Tables) {
            var target=await Read(db,type);var expected=Canonical(document[type.Name]);var actual=Canonical(target);
            if(expected!=actual) throw new InvalidOperationException("Transfer mismatch: "+type.Name);
            report[type.Name]=new {count=target.GetArrayLength(),sha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)))};
        }
        Console.WriteLine(Json.Write(report));
    }
    private static string Canonical(JsonElement value) => string.Join("\n",value.EnumerateArray().OrderBy(e=>e.GetProperty("id").ToString(),StringComparer.Ordinal).Select(e=>e.GetRawText()));
    private static Task<JsonElement> Read(Store db,Type type) => (Task<JsonElement>)typeof(DatabaseTransfer).GetMethod(nameof(ReadTyped),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null,[db])!;
    private static async Task<JsonElement> ReadTyped<T>(Store db) where T:class => JsonSerializer.SerializeToElement(await db.Set<T>().AsNoTracking().ToListAsync(),Json.Options);
}
