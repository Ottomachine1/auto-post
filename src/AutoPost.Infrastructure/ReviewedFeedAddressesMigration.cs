using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AutoPost.Infrastructure;
[DbContext(typeof(Store))]
[Migration("202610100004_ReviewedFeedAddresses")]
public sealed class ReviewedFeedAddressesMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
UPDATE "Sources" SET "Address"=replace("Address",'&hl=en&','&hl=en-US&'), "Validation"='unchecked', "Enabled"=false, "ETag"='', "LastModified"='' WHERE "Topic"<>'' AND "Address" LIKE 'https://news.google.com/rss/search?%' AND "Address" LIKE '%&hl=en&%';
UPDATE "Sources" SET "Address"=CASE "Address"
 WHEN 'https://www.coindesk.com/arc/outboundfeeds/rss/' THEN 'https://www.coindesk.com/arc/outboundfeeds/rss'
 WHEN 'https://blog.google/technology/ai/rss/' THEN 'https://blog.google/innovation-and-ai/technology/ai/rss/'
 WHEN 'https://u.today/rss' THEN 'https://u.today/rss.php'
 WHEN 'https://thedefiant.io/feed' THEN 'https://thedefiant.io/api/feed' END,
 "Validation"='unchecked', "Enabled"=false, "ETag"='', "LastModified"=''
WHERE "Address" IN ('https://www.coindesk.com/arc/outboundfeeds/rss/','https://blog.google/technology/ai/rss/','https://u.today/rss','https://thedefiant.io/feed');
""");
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
