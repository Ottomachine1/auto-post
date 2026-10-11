using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110012_AnalyticsIndexes")]
public sealed class AnalyticsIndexesMigration:Migration
{
 protected override void Up(MigrationBuilder m){m.CreateIndex(name:"IX_Events_Demo_CollectedAt",table:"Events",columns:new[]{"Demo","CollectedAt"});m.CreateIndex(name:"IX_ValueScores_Demo_EventId_CreatedAt_Id",table:"ValueScores",columns:new[]{"Demo","EventId","CreatedAt","Id"});}
 protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a verified backup.");
 public static async Task ApplyMySql(Store db,CancellationToken ct){foreach(var index in new[]{("Events","IX_Events_Demo_CollectedAt","CREATE INDEX `IX_Events_Demo_CollectedAt` ON `Events` (`Demo`,`CollectedAt`)"),("ValueScores","IX_ValueScores_Demo_EventId_CreatedAt_Id","CREATE INDEX `IX_ValueScores_Demo_EventId_CreatedAt_Id` ON `ValueScores` (`Demo`,`EventId`,`CreatedAt`,`Id`)")}){var exists=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name={index.Item1} AND index_name={index.Item2}").SingleAsync(ct);if(exists==0)await db.Database.ExecuteSqlRawAsync(index.Item3,ct);}await db.Database.ExecuteSqlRawAsync("INSERT IGNORE INTO `AutoPostSchemaVersions` (`Version`) VALUES (12)",ct);}
}
