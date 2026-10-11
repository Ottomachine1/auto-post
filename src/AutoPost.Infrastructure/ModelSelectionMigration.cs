using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610100007_ModelSelection")]
public sealed class ModelSelectionMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Settings" ADD COLUMN "AnalysisModel" text NOT NULL DEFAULT '';
        ALTER TABLE "Settings" ADD COLUMN "AgentModel" text NOT NULL DEFAULT '';
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
