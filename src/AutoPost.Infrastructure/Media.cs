using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPost.Infrastructure;

public static class MediaFiles
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static string Detect(byte[] data)
    {
        if (data.Length is < 16 or > MaxBytes) throw new ArgumentException("图片应小于5MB");
        if (data.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return "image/png";
        if (data[0] == 255 && data[1] == 216 && data[2] == 255) return "image/jpeg";
        if (System.Text.Encoding.ASCII.GetString(data,0,4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data,8,4) == "WEBP") return "image/webp";
        throw new ArgumentException("请选择PNG、JPEG或WebP图片");
    }
    public static async Task Validate(Store db, Attachment[] media, CancellationToken ct = default)
    {
        if (media.Length > 4 || media.Select(m => m.Id).Distinct().Count() != media.Length || media.Any(m => m.Alt.Length > 1000)) throw new ArgumentException("最多4张图片，替代文字最多1000字符");
        foreach (var item in media)
            if (!await db.Media.AnyAsync(m => m.Id == item.Id && m.Demo == Registration.Demo, ct)) throw new ArgumentException("图片不存在或属于其他运行模式");
    }
    public static string? ChannelError(string channel, Attachment[] media) => media.Length > 0 && channel is not ("x" or "binance") ? "此渠道暂不支持图片投递，请单独创建文字草稿" : null;
}

[DbContext(typeof(Store))]
[Migration("202610100005_ComposerMedia")]
public sealed class ComposerMediaMigration : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
ALTER TABLE "Drafts" ADD COLUMN "MediaJson" text NOT NULL DEFAULT '[]';
ALTER TABLE "Versions" ADD COLUMN "MediaJson" text NOT NULL DEFAULT '[]';
CREATE TABLE "Media" ("Id" text PRIMARY KEY,"ContentType" text NOT NULL,"Data" text NOT NULL,"Size" bigint NOT NULL,"Demo" boolean NOT NULL,"CreatedAt" bigint NOT NULL);
""");
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup.");
}
