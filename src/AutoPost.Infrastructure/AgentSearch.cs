using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AutoPost.Core;

namespace AutoPost.Infrastructure;
public static partial class AgentSearch
{
    private static readonly HashSet<string> Stop = new("what is are the latest news about please analyze analyse tell me of on and or a an generate draft for write today update updates current now show help with explain".Split(' '));
    private static readonly string[] Phrases=["帮我","请","分析","解读","最新","今天","消息","新闻","一下","有哪些","什么","最近","动态","追踪","生成","草稿","关于","看看","现在","当前","写一条","帮忙","汇总","总结","全球","时事","的"];
    private static readonly string[][] Aliases=[
        ["比特币","bitcoin","btc"],["以太坊","ethereum","eth"],["索拉纳","solana","sol"],
        ["币安链","bnb","bsc"],["稳定币","stablecoin"],["美联储","federal reserve","fomc"],
        ["通胀","inflation","cpi","pce"],["黄金","gold","xauusd"],["石油","oil","opec"],
        ["人工智能","ai","artificial intelligence"],["英伟达","nvidia"],["半导体","semiconductor"],
        ["地缘政治","geopolitical","geopolitics"],["监管","regulation","regulatory"],
        ["黑客攻击","hack","exploit"],["空投","airdrop"],["关税","tariff"],
    ];
    public static string[][] Terms(string question)
    {
        var groups=new List<string[]>();var residual=question.ToLowerInvariant();
        // Match complete Latin tokens; do not interpret news text as commands or tools.
        foreach(var aliases in Aliases) foreach(var phrase in aliases.Where(a=>a.Contains(' ')))
            if(residual.Contains(phrase)) {groups.Add(aliases);residual=residual.Replace(phrase,"");}
        foreach(Match word in Latin().Matches(residual)) {
            var term=word.Value.ToLowerInvariant();if(Stop.Contains(term)) continue;
            var aliases=Aliases.FirstOrDefault(a=>a.Contains(term));groups.Add(aliases??[term]);
        }
        residual=Latin().Replace(residual,"");
        foreach(var aliases in Aliases) if(residual.Contains(aliases[0])) {groups.Add(aliases);residual=residual.Replace(aliases[0],"");}
        foreach(var phrase in Phrases) residual=residual.Replace(phrase,"");
        foreach(Match word in Han().Matches(residual)) if(word.Length>1) groups.Add([word.Value]);
        return groups.DistinctBy(g=>string.Join('|',g)).Take(8).ToArray();
    }
    public static IQueryable<Event> Apply(IQueryable<Event> query,string question)
    {
        var parameter=Expression.Parameter(typeof(Event),"e");
        foreach(var group in Terms(question)) {
            Expression match=Expression.Constant(false);
            foreach(var term in group) foreach(var field in new[]{nameof(Event.Title),nameof(Event.Body)}) {
                var lower=Expression.Call(Expression.Property(parameter,field),nameof(string.ToLower),Type.EmptyTypes);
                match=Expression.OrElse(match,Expression.Call(lower,nameof(string.Contains),Type.EmptyTypes,Expression.Constant(term)));
            }
            query=query.Where(Expression.Lambda<Func<Event,bool>>(match,parameter));
        }
        return query;
    }
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9.+-]*")]
    private static partial Regex Latin();
    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}]+")]
    private static partial Regex Han();
}
