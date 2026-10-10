using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoPost.Core;
using Microsoft.EntityFrameworkCore;

namespace AutoPost.Infrastructure;

public sealed partial class Connectors
{
    private async Task<string[]> UploadX(Attachment[] attachments, string token, CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var attachment in attachments)
        {
            var asset = await db.Media.SingleAsync(m => m.Id == attachment.Id && m.Demo == Registration.Demo, ct);
            using var req = new HttpRequestMessage(HttpMethod.Post,"https://api.x.com/2/media/upload");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new { media = asset.Data, media_category = "tweet_image" });
            using var response = await factory.CreateClient("platform").SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = root.GetProperty("data");
            if (data.TryGetProperty("processing_info", out var info) && info.GetProperty("state").GetString() != "succeeded")
                throw new InvalidOperationException("图片仍在处理，请稍后重新预览并发布");
            var id = data.GetProperty("id").ToString();
            if (attachment.Alt != "")
            {
                using var metadata = new HttpRequestMessage(HttpMethod.Post,"https://api.x.com/2/media/metadata");
                metadata.Headers.Authorization = req.Headers.Authorization;
                metadata.Content = JsonContent.Create(new { id, metadata = new { alt_text = new { text = attachment.Alt } } });
                using var updated = await factory.CreateClient("platform").SendAsync(metadata,ct);
                updated.EnsureSuccessStatusCode();
            }
            ids.Add(id);
        }
        return ids.ToArray();
    }

    private async Task<JsonElement> BinanceCall(string endpoint, object body, CancellationToken ct, bool posting = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,"https://www.binance.com/bapi/composite/" + (posting ? "v1" : "v2") + "/public/pgc/openApi" + endpoint);
        request.Headers.Add("X-Square-OpenAPI-Key",Env("BINANCE_SQUARE_OPENAPI_KEY"));
        request.Headers.Add("clienttype","binanceSkill");
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await factory.CreateClient("platform").SendAsync(request,ct);
            if (posting && (int)response.StatusCode >= 500) throw new Uncertain();
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (root.GetProperty("code").GetString() != "000000") throw new InvalidOperationException("币安明确拒绝发布，请检查权限、内容及限额");
            return root.GetProperty("data").Clone();
        }
        catch (Exception ex) when (posting && (ex is OperationCanceledException or JsonException or KeyNotFoundException || ex is HttpRequestException { StatusCode: null })) { throw new Uncertain(); }
    }
    private async Task<(string Status, string? Remote)> DeliverBinance(string content, Attachment[] attachments, CancellationToken ct)
    {
        if (!Configured("binance")) throw new InvalidOperationException("币安发布凭据未配置");
        var images = new List<string>();
        foreach (var attachment in attachments)
        {
            var asset = await db.Media.SingleAsync(m => m.Id == attachment.Id && m.Demo == Registration.Demo, ct);
            var extension = asset.ContentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
            var ticket = await BinanceCall("/image/presignedUrl",new { imageName = asset.Id + extension },ct);
            var uri = new Uri(ticket.GetProperty("presignedUrl").GetString()!);
            Network.Validate(uri.ToString());
            using var upload = new HttpRequestMessage(HttpMethod.Put,uri);
            upload.Content = new ByteArrayContent(Convert.FromBase64String(asset.Data));
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue(asset.ContentType);
            // The presigned URL never receives the publishing key; DNS/socket checks also apply.
            using var uploaded = await factory.CreateClient("rss").SendAsync(upload,ct);
            uploaded.EnsureSuccessStatusCode();
            string? url = null;
            for (var i = 0; i < 10; i++)
            {
                var state = await BinanceCall("/image/imageStatus", new { fileTicket = ticket.GetProperty("fileTicket").GetString() }, ct);
                var status = state.GetProperty("status").GetInt32();
                if (status == 1) { url = state.GetProperty("imageUrl").GetString(); break; }
                if (status == 2) throw new InvalidOperationException("币安图片处理失败");
                await Task.Delay(3000,ct);
            }
            if (url == null) throw new InvalidOperationException("币安图片处理超时，尚未发送正文");
            images.Add(url);
        }
        var body = new Dictionary<string,object> { ["contentType"] = 1, ["bodyTextOnly"] = content };
        if (images.Count > 0) body["imageList"] = images;
        var result = await BinanceCall("/content/add",body,ct,posting:true);
        if (!result.TryGetProperty("id",out var id) || string.IsNullOrWhiteSpace(id.ToString())) throw new Uncertain();
        return ("published",id.ToString());
    }
}
