using AutoPost.Infrastructure;
using Xunit;
namespace AutoPost.Tests;
public sealed class AgentSearchTests
{
    [Theory]
    [InlineData("帮我分析最新 BTC 消息")]
    [InlineData("please analyze the latest bitcoin news")]
    [InlineData("请分析比特币")]
    public void BitcoinAliasesHaveOneTopic(string prompt) {
        var group=Assert.Single(AgentSearch.Terms(prompt));Assert.Contains("bitcoin",group);Assert.Contains("btc",group);
    }
    [Theory]
    [InlineData("请汇总最新消息")]
    [InlineData("what is the latest news today")]
    public void LatestQuestionDoesNotInventSearchTerms(string prompt) => Assert.Empty(AgentSearch.Terms(prompt));
    [Fact] public void SeparateTopicsMustAllMatch() {
        var groups=AgentSearch.Terms("请分析 bitcoin ETF");Assert.Equal(2,groups.Length);Assert.Contains(groups,g=>g.Contains("etf"));
    }
    [Fact] public void MultiwordAliasAndChineseAliasAgree() => Assert.Equal(AgentSearch.Terms("美联储"),AgentSearch.Terms("Federal Reserve"));
}
