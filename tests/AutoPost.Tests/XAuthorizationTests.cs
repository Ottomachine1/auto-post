using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoPost.Tests;
public sealed class XAuthorizationTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"autopost-oauth-test-"+Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string,string?> previous=new();
    private readonly HttpClientFactory factory=new();
    public XAuthorizationTests() {
        foreach(var key in new[]{"X_CLIENT_ID","X_CLIENT_SECRET","X_OAUTH_REFRESH_TOKEN","X_OAUTH_STATE_PATH","X_USER_ACCESS_TOKEN"}) previous[key]=Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable("X_CLIENT_ID","test-client");Environment.SetEnvironmentVariable("X_CLIENT_SECRET","");
        Environment.SetEnvironmentVariable("X_OAUTH_REFRESH_TOKEN","test-seed");Environment.SetEnvironmentVariable("X_USER_ACCESS_TOKEN","legacy");
        Environment.SetEnvironmentVariable("X_OAUTH_STATE_PATH",Path.Combine(directory,"x.json"));
    }
    private void Response(string access,string refresh) => factory.Responses.Enqueue(Json.Write(new{access_token=access,refresh_token=refresh,token_type="bearer",expires_in=7200}));
    [Fact] public async Task RotatedTokenPersistsAcrossInstancesAndConcurrentCalls() {
        Response("first-access","rotated-refresh");
        var tokens=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>new XAuthorization(factory).AccessToken(default)));
        Assert.All(tokens,t=>Assert.Equal("first-access",t));Assert.Equal(1,factory.Calls);
        var path=Connectors.Env("X_OAUTH_STATE_PATH");var state=Json.Read<XAuthorization.State>(await File.ReadAllTextAsync(path));
        Assert.Equal("rotated-refresh",state.RefreshToken);Assert.False(state.Pending);
        await File.WriteAllTextAsync(path,Json.Write(state with {ExpiresAt=Clock.Now}));Response("second-access","next-refresh");
        Assert.Equal("second-access",await new XAuthorization(factory).AccessToken(default));
        Assert.Contains("refresh_token=rotated-refresh",factory.Bodies[1]);Assert.Contains("client_id=test-client",factory.Bodies[1]);
        Assert.All(factory.Urls,u=>Assert.Equal("https://api.x.com/2/oauth2/token",u));
    }
    [Fact] public async Task AmbiguousRefreshFailsClosedAcrossRestartWithoutPostingOrRetrying() {
        factory.BeforeReply=_=>throw new HttpRequestException("connection lost");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new XAuthorization(factory).AccessToken(default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new XAuthorization(factory).AccessToken(default));
        Assert.Equal(1,factory.Calls);Assert.True(Json.Read<XAuthorization.State>(await File.ReadAllTextAsync(Connectors.Env("X_OAUTH_STATE_PATH"))).Pending);
    }
    [Fact] public async Task MalformedRefreshNeverReturnsLegacyToken() {
        factory.Responses.Enqueue("{\"access_token\":\"unpersisted\",\"expires_in\":7200,\"token_type\":\"unsupported\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new XAuthorization(factory).AccessToken(default));
        Assert.Equal(1,factory.Calls);
    }
    [Fact] public async Task MissingPersistentPathPreventsAnyRefreshRequest() {
        Environment.SetEnvironmentVariable("X_OAUTH_STATE_PATH","");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new XAuthorization(factory).AccessToken(default));Assert.Equal(0,factory.Calls);
    }
    [Fact] public async Task LegacyTokenRemainsCompatibleWithoutRefreshConfiguration() {
        Environment.SetEnvironmentVariable("X_OAUTH_REFRESH_TOKEN","");
        Assert.Equal("legacy",await new XAuthorization(factory).AccessToken(default));Assert.Equal(0,factory.Calls);
    }
    [Fact] public async Task ConfidentialClientRefreshesBeforePostingWithPersistedAccessToken() {
        Environment.SetEnvironmentVariable("X_CLIENT_SECRET","test-secret");Response("fresh-access","fresh-refresh");
        factory.Responses.Enqueue("{\"data\":{\"id\":\"posted-id\"}}");
        await using var db=new Store(new DbContextOptions<Store>());
        var result=await new Connectors(factory,db).Deliver("x","approved text",default);
        Assert.Equal("published",result.Status);Assert.Equal("posted-id",result.Remote);
        Assert.Equal(2,factory.Calls);Assert.Equal("https://api.x.com/2/tweets",factory.Urls[1]);
        Assert.StartsWith("Basic ",factory.Authorizations[0]);Assert.Equal("Bearer fresh-access",factory.Authorizations[1]);
        Assert.DoesNotContain("client_id=",factory.Bodies[0]);Assert.Contains("approved text",factory.Bodies[1]);
        Assert.Equal("fresh-refresh",Json.Read<XAuthorization.State>(await File.ReadAllTextAsync(Connectors.Env("X_OAUTH_STATE_PATH"))).RefreshToken);
    }
    [Fact] public async Task RefreshFailureCannotSendArticle() {
        factory.Status=System.Net.HttpStatusCode.BadRequest;
        await using var db=new Store(new DbContextOptions<Store>());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new Connectors(factory,db).Deliver("x","approved text",default));
        Assert.Single(factory.Urls);Assert.EndsWith("/oauth2/token",factory.Urls[0]);
    }
    public void Dispose() {
        foreach(var pair in previous) Environment.SetEnvironmentVariable(pair.Key,pair.Value);
        if(Directory.Exists(directory)) {foreach(var file in Directory.GetFiles(directory)) File.Delete(file);Directory.Delete(directory);}
    }
}
