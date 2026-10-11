using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
namespace AutoPost.Infrastructure;
public static class EventModeration
{
 public static readonly string[] Reasons=["spam","marketing","low_value","irrelevant","duplicate","untrusted_source","incorrect_filter","reviewed"];
 // Caller owns the API transaction and global write lock.
 public static async Task<Event> Change(Store db,string id,bool quarantine,int expectedVersion,string reason,string note,CancellationToken ct)
 {
  if(!Reasons.Contains(reason)||string.IsNullOrWhiteSpace(note)||note.Length>2000)throw new ArgumentException("请选择原因并填写1至2000字说明");
  var item=await db.Events.SingleOrDefaultAsync(e=>e.Id==id&&e.Demo==Registration.Demo,ct)??throw new ArgumentException("情报不存在");
  if(item.ModerationVersion!=expectedVersion||item.IsQuarantined==quarantine)throw new InvalidOperationException("隔离状态或版本已变化，请刷新");
  var drafts=await db.Drafts.Where(d=>d.EventId==id&&d.Demo==item.Demo).ToListAsync(ct);var ids=drafts.Select(d=>d.Id).ToArray();
  if(quarantine&&await db.Deliveries.AnyAsync(d=>ids.Contains(d.DraftId)&&(d.Status=="sending"||d.Status=="unknown"),ct))throw new InvalidOperationException("关联投递正在发送或结果不明，请先核验");
  var previous=new {item.IsQuarantined,item.ModerationVersion,item.ModerationReason,item.QuarantinedAt};
  item.IsQuarantined=quarantine;item.ModerationVersion++;item.ModerationReason=reason;item.QuarantinedAt=quarantine?Clock.Now:null;
  if(quarantine){
   var pending=await db.Deliveries.Where(d=>ids.Contains(d.DraftId)&&d.Status=="queued").ToListAsync(ct);
   foreach(var delivery in pending){delivery.Status="blocked";delivery.Error="来源情报已隔离";delivery.UpdatedAt=Clock.Now;}
   foreach(var draft in drafts.Where(d=>d.Status!="published")){draft.ApprovedRevision=null;if(draft.DeletedAt==null&&draft.ArchivedAt==null)draft.Status="draft";}
   var deliveries=pending.Select(d=>d.Id).ToArray();
   foreach(var job in await db.Jobs.Where(j=>(j.Status=="pending"||j.Status=="running")&&((j.Kind=="analyse"&&j.Target==id)||(j.Kind=="publish"&&deliveries.Contains(j.Target)))).ToListAsync(ct)){job.Status="cancelled";job.Error="来源情报已隔离";}
  }
  db.Mark("event_moderated",id,Json.Write(new {previous,quarantine,version=item.ModerationVersion,reason,note,stage="human",policyVersion="manual-v1",drafts=ids}));
  return item;
 }
}
