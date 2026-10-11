using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610100006_Agent")]
public sealed class AgentMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "AgentSessions" ("Id" text PRIMARY KEY,"Title" text NOT NULL,"Demo" boolean NOT NULL,"CreatedAt" bigint NOT NULL,"UpdatedAt" bigint NOT NULL);
        CREATE INDEX "IX_AgentSessions_Demo_UpdatedAt" ON "AgentSessions" ("Demo","UpdatedAt");
        CREATE TABLE "AgentMessages" ("Id" text PRIMARY KEY,"SessionId" text NOT NULL,"RequestId" text NOT NULL,"Prompt" text NOT NULL,"Action" text NOT NULL,"EventId" text NULL,"Status" text NOT NULL,"Progress" text NOT NULL,"Result" text NOT NULL,"Model" text NOT NULL,"Usage" text NOT NULL,"CancelRequested" boolean NOT NULL,"CreatedAt" bigint NOT NULL,"UpdatedAt" bigint NOT NULL);
        CREATE INDEX "IX_AgentMessages_SessionId_CreatedAt" ON "AgentMessages" ("SessionId","CreatedAt");
        CREATE UNIQUE INDEX "IX_AgentMessages_SessionId_RequestId" ON "AgentMessages" ("SessionId","RequestId");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
