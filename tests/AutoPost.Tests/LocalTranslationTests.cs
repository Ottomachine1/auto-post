using AutoPost.Core;
using AutoPost.Infrastructure;
using Xunit;
namespace AutoPost.Tests;
public sealed class LocalTranslationTests
{
    [Theory]
    [InlineData("比特币突破关键价格，市场关注政策变化", true)]
    [InlineData("Bitcoin investors watch Federal Reserve policy", false)]
    [InlineData("", false)]
    public void DetectsChineseOriginalWithoutTreatingEnglishAsTranslated(string text, bool expected)
        => Assert.Equal(expected, LocalTranslation.NativeChinese(text));

    [Fact]
    public async Task ChineseOriginalDoesNotCreateAnyNetworkRequest()
    {
        var item = new Event { Title = "全球市场最新消息", Body = "政策变化影响市场预期。" };
        await LocalTranslation.Run(item, new NoNetwork(), CancellationToken.None);
        Assert.Equal("native", item.TranslationStatus);
        Assert.Equal(item.Title, item.ChineseTitle); Assert.Equal(item.Body, item.ChineseBody);
        Assert.Equal("original-zh", item.TranslationEngine);
    }
    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Unexpected external call");
    }
    [Fact]
    public async Task ForeignTextUsesOnlyLocalTranslationEndpointAndKeepsOriginal()
    {
        var before = Environment.GetEnvironmentVariable("TRANSLATION_URL");
        Environment.SetEnvironmentVariable("TRANSLATION_URL", "http://localhost:8092");
        try
        {
            var factory = new StubFactory(System.Net.HttpStatusCode.OK, "{\"title\":\"市场新闻\",\"body\":\"政策影响市场。\",\"engine\":\"argos-offline\"}");
            var item = new Event { Title = "Market news", Body = "Policy affects markets.", Language = "en" };
            await LocalTranslation.Run(item, factory, CancellationToken.None);
            Assert.Equal("completed", item.TranslationStatus); Assert.Equal("Market news", item.Title);
            Assert.Equal("市场新闻", item.ChineseTitle); Assert.Equal("translation", factory.Name);
            Assert.Equal("localhost", factory.Uri!.Host);
        }
        finally { Environment.SetEnvironmentVariable("TRANSLATION_URL", before); }
    }
    [Fact]
    public async Task MissingLanguageCannotProduceSuccessfulTranslation()
    {
        var before = Environment.GetEnvironmentVariable("TRANSLATION_URL");
        Environment.SetEnvironmentVariable("TRANSLATION_URL", "http://localhost:8092");
        try
        {
            var item = new Event { Title = "Market news", Body = "Policy affects markets." };
            await Assert.ThrowsAsync<HttpRequestException>(() => LocalTranslation.Run(item, new StubFactory(System.Net.HttpStatusCode.UnprocessableEntity, "{}"), CancellationToken.None));
            Assert.Equal("pending", item.TranslationStatus); Assert.Empty(item.ChineseTitle);
        }
        finally { Environment.SetEnvironmentVariable("TRANSLATION_URL", before); }
    }
    private sealed class StubFactory(System.Net.HttpStatusCode status, string content) : HttpMessageHandler, IHttpClientFactory
    {
        public string? Name; public Uri? Uri;
        public HttpClient CreateClient(string name) { Name = name; return new HttpClient(this); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
