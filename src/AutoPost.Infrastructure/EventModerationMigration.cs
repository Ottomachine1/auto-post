using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110013_EventModeration")]
public sealed class EventModerationMigration:Migration
{
 protected override void Up(MigrationBuilder m)=>m.Sql("""
 ALTER TABLE "Events" ADD COLUMN "IsQuarantined" boolean NOT NULL DEFAULT false, ADD COLUMN "ModerationVersion" integer NOT NULL DEFAULT 0, ADD COLUMN "ModerationReason" text NOT NULL DEFAULT '', ADD COLUMN "QuarantinedAt" bigint NULL;
 CREATE INDEX "IX_Events_Demo_IsQuarantined_PublishedAt_Id" ON "Events" ("Demo","IsQuarantined","PublishedAt","Id");
 """);
 protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a verified backup.");
 public static async Task ApplyMySql(Store db,CancellationToken ct){foreach(var (column,sql) in new[]{("IsQuarantined","ALTER TABLE `Events` ADD COLUMN `IsQuarantined` tinyint(1) NOT NULL DEFAULT 0"),("ModerationVersion","ALTER TABLE `Events` ADD COLUMN `ModerationVersion` int NOT NULL DEFAULT 0"),("ModerationReason","ALTER TABLE `Events` ADD COLUMN `ModerationReason` varchar(128) NOT NULL DEFAULT ''"),("QuarantinedAt","ALTER TABLE `Events` ADD COLUMN `QuarantinedAt` bigint NULL")}){var exists=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='Events' AND column_name={column}").SingleAsync(ct);if(exists==0)await db.Database.ExecuteSqlRawAsync(sql,ct);}var indexed=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='Events' AND index_name='IX_Events_Demo_IsQuarantined_PublishedAt_Id'").SingleAsync(ct);if(indexed==0)await db.Database.ExecuteSqlRawAsync("CREATE INDEX `IX_Events_Demo_IsQuarantined_PublishedAt_Id` ON `Events` (`Demo`,`IsQuarantined`,`PublishedAt`,`Id`)",ct);await db.Database.ExecuteSqlRawAsync("INSERT IGNORE INTO `AutoPostSchemaVersions` (`Version`) VALUES (13)",ct);}
}
