using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoPost.Core;

namespace AutoPost.Infrastructure;
public static class ModelCatalog
{
    public static string Resolve(string? selected) => string.IsNullOrWhiteSpace(selected) ? (Connectors.Env("OPENAI_MODEL") is { Length: > 0 } m ? m : "zai-org/GLM-5.3-Flash") : selected;
    public static async Task<string[]> List(IHttpClientFactory factory, CancellationToken ct)
    {
        if (Connectors.Env("OPENAI_API_KEY")=="") return [];
        var url=Connectors.Env("OPENAI_BASE_URL"); if(url=="") url="https://api.siliconflow.cn/v1";
        using var request=new HttpRequestMessage(HttpMethod.Get,url.TrimEnd('/')+"/models?sub_type=chat");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Connectors.Env("OPENAI_API_KEY"));
        using var response=await factory.CreateClient("platform").SendAsync(request,ct);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException("模型目录暂时不可用，请检查授权或稍后刷新");
        var root=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return root.GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("id").GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Length<=200).Distinct().Order().ToArray();
    }
    public static async Task Validate(string? model,IHttpClientFactory factory,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(model)) return;
        if(model.Length>200 || !(await List(factory,ct)).Contains(model)) throw new ArgumentException("请选择当前账号模型目录中的对话模型");
    }
}
