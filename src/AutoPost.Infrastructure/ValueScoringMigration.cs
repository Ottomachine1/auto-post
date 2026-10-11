using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110011_ValueScoring")]
public sealed class ValueScoringMigration : Migration
{
    private const string Tables = """
        CREATE TABLE "ValueScores" (
          "Id" text PRIMARY KEY, "EventId" text NOT NULL, "Demo" boolean NOT NULL,
          "InputHash" text NOT NULL, "Score" integer NOT NULL, "DetailJson" text NOT NULL, "CreatedAt" bigint NOT NULL);
        CREATE UNIQUE INDEX "IX_ValueScores_EventId_InputHash_Demo" ON "ValueScores" ("EventId","InputHash","Demo");
        CREATE TABLE "ValueFeedback" (
          "Id" text PRIMARY KEY, "EventId" text NOT NULL, "SourceId" text NOT NULL, "Demo" boolean NOT NULL,
          "Rating" integer NOT NULL, "SourceRating" integer NULL, "Note" text NOT NULL, "UpdatedAt" bigint NOT NULL);
        CREATE UNIQUE INDEX "IX_ValueFeedback_EventId_Demo" ON "ValueFeedback" ("EventId","Demo");
        CREATE INDEX "IX_ValueFeedback_SourceId_Demo" ON "ValueFeedback" ("SourceId","Demo");
        """;
    protected override void Up(MigrationBuilder m)
    {
        m.Sql(Tables);
        m.Sql("""
            ALTER TABLE "Settings" ADD COLUMN "ScoreWeightsJson" text NOT NULL DEFAULT '{"source":25,"freshness":20,"impact":25,"novelty":20,"evidence":10}',
                ADD COLUMN "ScoreWeightsVersion" integer NOT NULL DEFAULT 1;
            """);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a verified backup.");
    public static async Task ApplyMySql(Store db,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `ValueScores` (
              `Id` varchar(128) PRIMARY KEY, `EventId` varchar(128) NOT NULL, `Demo` tinyint(1) NOT NULL,
              `InputHash` varchar(64) NOT NULL, `Score` int NOT NULL, `DetailJson` longtext NOT NULL, `CreatedAt` bigint NOT NULL,
              UNIQUE KEY `IX_ValueScores_EventId_InputHash_Demo` (`EventId`,`InputHash`,`Demo`)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
            CREATE TABLE IF NOT EXISTS `ValueFeedback` (
              `Id` varchar(128) PRIMARY KEY, `EventId` varchar(128) NOT NULL, `SourceId` varchar(128) NOT NULL, `Demo` tinyint(1) NOT NULL,
              `Rating` int NOT NULL, `SourceRating` int NULL, `Note` longtext NOT NULL, `UpdatedAt` bigint NOT NULL,
              UNIQUE KEY `IX_ValueFeedback_EventId_Demo` (`EventId`,`Demo`), KEY `IX_ValueFeedback_SourceId_Demo` (`SourceId`,`Demo`)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
            """,ct);
        var columns=new[]{("ScoreWeightsJson","ALTER TABLE `Settings` ADD COLUMN `ScoreWeightsJson` longtext NULL"),("ScoreWeightsVersion","ALTER TABLE `Settings` ADD COLUMN `ScoreWeightsVersion` int NOT NULL DEFAULT 1")};
        foreach(var (column,sql) in columns)
        {
            var exists=await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='Settings' AND column_name={column}").SingleAsync(ct);
            if(exists==0)await db.Database.ExecuteSqlRawAsync(sql,ct);
        }
        var defaults=new AutoPost.Core.Settings().ScoreWeightsJson;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE `Settings` SET `ScoreWeightsJson`={defaults} WHERE `ScoreWeightsJson` IS NULL",ct);
        await db.Database.ExecuteSqlRawAsync("INSERT IGNORE INTO `AutoPostSchemaVersions` (`Version`) VALUES (11)",ct);
    }
}
