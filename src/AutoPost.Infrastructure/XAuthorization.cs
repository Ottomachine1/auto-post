using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoPost.Core;

namespace AutoPost.Infrastructure;

/// <summary>Private single-account OAuth state. Never retries an ambiguous token exchange.</summary>
public sealed class XAuthorization(IHttpClientFactory factory)
{
    public static bool RefreshConfigured => Connectors.Env("X_CLIENT_ID")!="" && Connectors.Env("X_OAUTH_REFRESH_TOKEN")!="";
    public sealed record State(string ClientId,string SeedHash,string AccessToken,string RefreshToken,long ExpiresAt,bool Pending);
    public async Task<string> AccessToken(CancellationToken ct)
    {
        if(!RefreshConfigured) return Connectors.Env("X_USER_ACCESS_TOKEN") is {Length:>0} legacy ? legacy : throw new InvalidOperationException("X 用户授权未配置");
        var path=Connectors.Env("X_OAUTH_STATE_PATH");
        if(!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("X 刷新授权需要私有持久化路径");
        var directory=Path.GetDirectoryName(path)!;Directory.CreateDirectory(directory);
        if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        await using var gate=await Lock(path+".lock",ct);
        var clientId=Connectors.Env("X_CLIENT_ID");var seed=Connectors.Env("X_OAUTH_REFRESH_TOKEN");var hash=Network.Hash(seed);
        State state;
        if(File.Exists(path)) {
            try {state=Json.Read<State>(await File.ReadAllTextAsync(path,ct));}
            catch {throw new InvalidOperationException("X 私有授权状态无效，需管理员恢复或重新授权");}
            if(state.ClientId!=clientId || state.SeedHash!=hash) state=new(clientId,hash,"",seed,0,false);
        } else state=new(clientId,hash,"",seed,0,false);
        if(state.Pending) throw new InvalidOperationException("X 上次授权刷新结果不明确，请重新授权后恢复，禁止自动重试");
        if(state.ExpiresAt>Clock.Now+60 && state.AccessToken!="") return state.AccessToken;
        // Persist before sending: a crash/timeout may already have rotated the remote refresh token.
        await Save(path,state with {Pending=true},ct);
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.x.com/2/oauth2/token");
        var fields=new Dictionary<string,string>{{"grant_type","refresh_token"},{"refresh_token",state.RefreshToken}};
        var secret=Connectors.Env("X_CLIENT_SECRET");
        if(secret!="") request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(clientId)+":"+Uri.EscapeDataString(secret))));
        else fields["client_id"]=clientId;
        request.Content=new FormUrlEncodedContent(fields);
        try {
            using var response=await factory.CreateClient("platform").SendAsync(request,ct);
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException("X 刷新授权被拒绝，请检查应用权限或重新授权");
            var root=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var access=root.GetProperty("access_token").GetString();
            var seconds=root.GetProperty("expires_in").GetInt32();
            var refresh=root.TryGetProperty("refresh_token",out var rotated)?rotated.GetString():state.RefreshToken;
            if(string.IsNullOrWhiteSpace(access)||string.IsNullOrWhiteSpace(refresh)||seconds<=60||seconds>31536000||!string.Equals(root.GetProperty("token_type").GetString(),"bearer",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("X 刷新响应无效，需重新授权");
            var updated=state with {AccessToken=access,RefreshToken=refresh,ExpiresAt=Clock.Now+seconds,Pending=false};
            await Save(path,updated,ct); return access;
        } catch(OperationCanceledException) when(ct.IsCancellationRequested) {throw;}
        catch {throw new InvalidOperationException("X 授权刷新未完成，请检查私有授权状态；未发送文章");}
    }
    private static async Task<FileStream> Lock(string path,CancellationToken ct)
    {
        for(var attempt=0;attempt<100;attempt++) {
            ct.ThrowIfCancellationRequested();
            try {return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) {await Task.Delay(50,ct);}
        }
        throw new InvalidOperationException("X 授权状态正在使用，请稍后操作");
    }
    private static async Task Save(string path,State state,CancellationToken ct)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            await using(var file=new FileStream(temporary,options)) {
                await JsonSerializer.SerializeAsync(file,state,Json.Options,ct);await file.FlushAsync(ct);file.Flush(true);
            }
            File.Move(temporary,path,true);
        } finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }
}
