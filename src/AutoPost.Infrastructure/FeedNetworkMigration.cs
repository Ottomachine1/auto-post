using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;

[DbContext(typeof(Store))]
[Migration("202610100002_FeedNetwork")]
public sealed class FeedNetworkMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        foreach (var name in new[] { "Category", "Topic", "Language", "Region", "Publisher", "ETag", "LastModified", "Validation" })
        {
            var value = name switch { "Category" => "world", "Language" => "en", "Region" => "US", "Validation" => "unchecked", _ => "" };
            m.Sql($"ALTER TABLE \"Sources\" ADD COLUMN \"{name}\" text NOT NULL DEFAULT '{value}'");
        }
        m.Sql("""
ALTER TABLE "Sources" ADD COLUMN "EnableAfterValidation" boolean NOT NULL DEFAULT false, ADD COLUMN "Priority" integer NOT NULL DEFAULT 50, ADD COLUMN "FreshnessDays" integer NOT NULL DEFAULT 30, ADD COLUMN "ConsecutiveFailures" integer NOT NULL DEFAULT 0, ADD COLUMN "SuspendedUntil" bigint NOT NULL DEFAULT 0, ADD COLUMN "CheckedAt" bigint, ADD COLUMN "LatestPublishedAt" bigint, ADD COLUMN "HttpStatus" integer;
ALTER TABLE "Events" ADD COLUMN "Author" text NOT NULL DEFAULT '', ADD COLUMN "Language" text NOT NULL DEFAULT 'und', ADD COLUMN "OriginalSummary" text NOT NULL DEFAULT '', ADD COLUMN "Publisher" text NOT NULL DEFAULT '', ADD COLUMN "EvidenceKey" text NOT NULL DEFAULT '', ADD COLUMN "Relation" text NOT NULL DEFAULT 'unverified_report', ADD COLUMN "PublishedEstimated" boolean NOT NULL DEFAULT false, ADD COLUMN "Embedding" text NOT NULL DEFAULT '[]', ADD COLUMN "EmbeddingModel" text NOT NULL DEFAULT '';
CREATE INDEX ON "Events" ("EvidenceKey");
CREATE INDEX ON "Sources" ("Enabled","NextRun","Priority");
""");
    }
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}

[DbContext(typeof(Store))]
[Migration("202610100003_WorkerPriority")]
public sealed class WorkerPriorityMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("ALTER TABLE \"Jobs\" ADD COLUMN \"Priority\" integer NOT NULL DEFAULT 50");
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
