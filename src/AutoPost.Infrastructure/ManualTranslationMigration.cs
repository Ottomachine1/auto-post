using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610110009_ManualTranslation")]
public sealed class ManualTranslationMigration : Migration
{
    public static async Task ApplyMySql(Store db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `Translations` (
              `Id` varchar(128) NOT NULL PRIMARY KEY, `ContentHash` varchar(64) NOT NULL,
              `Title` longtext NOT NULL, `Body` longtext NOT NULL, `SourceLanguage` longtext NOT NULL,
              `TargetLanguage` varchar(16) NOT NULL, `TranslatedTitle` longtext NOT NULL,
              `TranslatedBody` longtext NOT NULL, `Status` longtext NOT NULL, `Engine` longtext NOT NULL,
              `Error` longtext NULL, `Demo` tinyint(1) NOT NULL, `CreatedAt` bigint NOT NULL, `UpdatedAt` bigint NOT NULL,
              UNIQUE KEY `IX_Translations_ContentHash_TargetLanguage_Demo` (`ContentHash`,`TargetLanguage`,`Demo`)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
            CREATE TABLE IF NOT EXISTS `AutoPostSchemaVersions` (`Version` int NOT NULL PRIMARY KEY);
            UPDATE `Jobs` SET `Status`='cancelled', `Error`='V2: 自动翻译已停用，请手动请求'
              WHERE `Kind` IN ('translate','translate_manual') AND `Status` IN ('pending','running')
              AND NOT EXISTS (SELECT 1 FROM `AutoPostSchemaVersions` WHERE `Version`=9);
            INSERT IGNORE INTO `AutoPostSchemaVersions` (`Version`) VALUES (9);
            """, ct);
    }
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "Translations" (
          "Id" text PRIMARY KEY, "ContentHash" text NOT NULL, "Title" text NOT NULL, "Body" text NOT NULL,
          "SourceLanguage" text NOT NULL, "TargetLanguage" text NOT NULL,
          "TranslatedTitle" text NOT NULL, "TranslatedBody" text NOT NULL,
          "Status" text NOT NULL, "Engine" text NOT NULL, "Error" text NULL,
          "Demo" boolean NOT NULL, "CreatedAt" bigint NOT NULL, "UpdatedAt" bigint NOT NULL);
        CREATE UNIQUE INDEX "IX_Translations_ContentHash_TargetLanguage_Demo" ON "Translations" ("ContentHash", "TargetLanguage", "Demo");
        UPDATE "Jobs" SET "Status"='cancelled', "Error"='V2: 自动翻译已停用，请手动请求'
          WHERE "Kind" IN ('translate','translate_manual') AND "Status" IN ('pending','running');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
