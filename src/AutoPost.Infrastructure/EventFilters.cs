using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
namespace AutoPost.Infrastructure;
public static class EventFilters
{
 public static IQueryable<ValueScore> LatestScores(Store db)=>db.ValueScores.AsNoTracking().Where(s=>s.Demo==Registration.Demo&&!db.ValueScores.Any(other=>other.Demo==s.Demo&&other.EventId==s.EventId&&(other.CreatedAt>s.CreatedAt||(other.CreatedAt==s.CreatedAt&&string.Compare(other.Id,s.Id)>0))));
 public static IQueryable<Event> Apply(Store db,IQueryable<Event> query,string? language,long? from,long? to,int? minScore,int? maxScore,string? scoreState)
 {
  language=language?.Trim().ToLowerInvariant();scoreState=string.IsNullOrEmpty(scoreState)?"all":scoreState;
  if(language is {Length:>0}&&(language.Length>16||!language.All(c=>c is >= 'a' and <= 'z'||c=='-')))throw new ArgumentException("语言代码无效");
  if(from<0||to<0||from.HasValue&&to.HasValue&&to<=from)throw new ArgumentException("采集时间范围无效");
  if(minScore is <0 or >100||maxScore is <0 or >100||minScore.HasValue&&maxScore.HasValue&&minScore>maxScore)throw new ArgumentException("评分范围必须在0至100之间且下限不大于上限");
  if(scoreState is not ("all" or "scored" or "unscored")||scoreState=="unscored"&&(minScore.HasValue||maxScore.HasValue))throw new ArgumentException("评分筛选组合无效");
  if(!string.IsNullOrEmpty(language))query=query.Where(e=>e.Language.ToLower()==language);
  if(from.HasValue)query=query.Where(e=>e.CollectedAt>=from.Value);
  if(to.HasValue)query=query.Where(e=>e.CollectedAt<to.Value);
  var scores=LatestScores(db);
  if(scoreState=="unscored")query=query.Where(e=>!scores.Any(s=>s.EventId==e.Id));
  else if(scoreState=="scored"||minScore.HasValue||maxScore.HasValue)query=query.Where(e=>scores.Any(s=>s.EventId==e.Id&&(!minScore.HasValue||s.Score>=minScore.Value)&&(!maxScore.HasValue||s.Score<=maxScore.Value)));
  return query;
 }
}
