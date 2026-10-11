using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110010_DraftLifecycle")]
public sealed class DraftLifecycleMigration : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE "Drafts" ADD COLUMN "DeletedAt" bigint NULL, ADD COLUMN "ArchivedAt" bigint NULL;
        CREATE INDEX "IX_Drafts_Demo_DeletedAt_UpdatedAt" ON "Drafts" ("Demo","DeletedAt","UpdatedAt");
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a verified backup.");
    public static async Task ApplyMySql(Store db,CancellationToken ct)
    {
        foreach(var (column,sql) in new[]{("DeletedAt","ALTER TABLE `Drafts` ADD COLUMN `DeletedAt` bigint NULL"),("ArchivedAt","ALTER TABLE `Drafts` ADD COLUMN `ArchivedAt` bigint NULL")})
        {
            var exists=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='Drafts' AND column_name={column}").SingleAsync(ct);
            if(exists==0)await db.Database.ExecuteSqlRawAsync(sql,ct);
        }
        var index=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='Drafts' AND index_name='IX_Drafts_Demo_DeletedAt_UpdatedAt'").SingleAsync(ct);
        if(index==0)await db.Database.ExecuteSqlRawAsync("CREATE INDEX `IX_Drafts_Demo_DeletedAt_UpdatedAt` ON `Drafts` (`Demo`,`DeletedAt`,`UpdatedAt`)",ct);
        await db.Database.ExecuteSqlRawAsync("INSERT IGNORE INTO `AutoPostSchemaVersions` (`Version`) VALUES (10)",ct);
    }
}
