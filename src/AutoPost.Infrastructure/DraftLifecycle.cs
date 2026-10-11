using AutoPost.Core;
using Microsoft.EntityFrameworkCore;

namespace AutoPost.Infrastructure;

public static class DraftLifecycle
{
    public sealed record Selection(string Id, int Revision);
    public static async Task Apply(Store db, Selection[] items, string action, CancellationToken ct)
    {
        if (items is null || items.Length is < 1 or > 200 || items.Any(i=>i is null||string.IsNullOrWhiteSpace(i.Id)||i.Revision<1) || items.Select(i=>i.Id).Distinct().Count()!=items.Length)
            throw new ArgumentException("请选择1至200条不重复的草稿");
        if (action is not ("trash" or "restore" or "archive" or "unarchive")) throw new ArgumentException("不支持的草稿操作");
        var ids=items.Select(i=>i.Id).ToArray();
        var drafts=await db.Drafts.Where(d=>ids.Contains(d.Id)&&d.Demo==Registration.Demo).ToListAsync(ct);
        if (drafts.Count!=items.Length || drafts.Any(d=>items.Single(i=>i.Id==d.Id).Revision!=d.Revision))
            throw new InvalidOperationException("草稿不存在、模式不符或版本已变化，请刷新后重试");
        if (await db.Deliveries.AnyAsync(d=>ids.Contains(d.DraftId)&&(d.Status=="sending"||d.Status=="unknown"),ct))
            throw new InvalidOperationException("存在发送中或结果不明的投递，请先人工核验");
        if (drafts.Any(d=>action=="restore" ? d.DeletedAt is null : d.DeletedAt is not null))
            throw new InvalidOperationException("草稿状态已变化，请刷新");
        foreach(var draft in drafts)
        {
            if (action is "trash" or "archive")
            {
                var deliveries=await db.Deliveries.Where(d=>d.DraftId==draft.Id&&d.Status=="queued").ToListAsync(ct);
                var deliveryIds=deliveries.Select(d=>d.Id).ToArray();
                foreach(var delivery in deliveries){delivery.Status="cancelled";delivery.Error="草稿已移入回收站或归档";delivery.UpdatedAt=Clock.Now;}
                foreach(var job in await db.Jobs.Where(j=>j.Kind=="publish"&&deliveryIds.Contains(j.Target)&&(j.Status=="pending"||j.Status=="running")).ToListAsync(ct)) job.Status="cancelled";
            }
            if(action=="trash") draft.DeletedAt=Clock.Now;
            if(action=="restore") draft.DeletedAt=null;
            if(action=="archive") draft.ArchivedAt=Clock.Now;
            if(action=="unarchive") draft.ArchivedAt=null;
            if(action is "restore" or "unarchive")
            {
                draft.Revision++;
                db.Versions.Add(new DraftVersion {DraftId=draft.Id,Revision=draft.Revision,Content=draft.Content,Channels=draft.Channels,MediaJson=draft.MediaJson});
            }
            // Restoring a document never restores authorization to publish it.
            draft.ApprovedRevision=null;
            if(draft.Status!="published")draft.Status="draft";
            draft.UpdatedAt=Clock.Now;
            db.Mark("draft_"+action,draft.Id,"revision="+draft.Revision);
        }
    }
}
