using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110008_LocalTranslation")]
public sealed class TranslationMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Events" ADD COLUMN "ChineseTitle" text NOT NULL DEFAULT '';
        ALTER TABLE "Events" ADD COLUMN "ChineseBody" text NOT NULL DEFAULT '';
        ALTER TABLE "Events" ADD COLUMN "TranslationStatus" text NOT NULL DEFAULT 'pending';
        ALTER TABLE "Events" ADD COLUMN "TranslationEngine" text NOT NULL DEFAULT '';
        CREATE INDEX "IX_Events_TranslationStatus_CollectedAt" ON "Events" ("TranslationStatus", "CollectedAt");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
