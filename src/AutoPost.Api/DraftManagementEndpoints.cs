using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;

public static class DraftManagementEndpoints
{
    public static void MapDraftManagement(this RouteGroupBuilder api)
    {
        api.MapPost("/drafts/bulk",async (BulkInput input,Store db,CancellationToken ct)=>{
            await DraftLifecycle.Apply(db,input.Items,input.Action,ct);
            return Results.Ok(new {changed=input.Items.Length});
        });
        api.MapGet("/drafts/selection",async (Store db,string? view,string? q,string? status,string? channel,CancellationToken ct)=>{
            var items=await Filter(db,view,q,status,channel).OrderBy(d=>d.Id).Select(d=>new {d.Id,d.Revision}).ToListAsync(ct);
            return Results.Ok(new {items});
        });
        api.MapGet("/drafts/page",async (Store db,string? view,string? q,string? status,string? channel,string? sort,int? page,CancellationToken ct)=>{
            var query=Filter(db,view,q,status,channel);
            var number=Math.Clamp(page??1,1,100000);var total=await query.CountAsync(ct);
            if(sort is not (null or "newest" or "oldest"))throw new ArgumentException("排序无效");
            var ordered=sort=="oldest"?query.OrderBy(d=>d.CreatedAt).ThenBy(d=>d.Id):query.OrderByDescending(d=>d.CreatedAt).ThenByDescending(d=>d.Id);
            var items=await ordered.Skip((number-1)*50).Take(50).ToListAsync(ct);
            var ids=items.Select(d=>d.Id).ToArray();var deliveries=await db.Deliveries.Where(d=>ids.Contains(d.DraftId)).ToListAsync(ct);
            return Results.Ok(new {total,page=number,pageSize=50,items=items.Select(d=>new {d.Id,d.Content,d.Status,d.Revision,d.ApprovedRevision,d.DeletedAt,d.ArchivedAt,d.Demo,d.Quarantined,channels=Json.Read<string[]>(d.Channels),media=Json.Read<Attachment[]>(d.MediaJson),event_id=d.EventId,created_at=d.CreatedAt,deliveries=deliveries.Where(x=>x.DraftId==d.Id)})});
        });
    }
    private static IQueryable<Draft> Filter(Store db,string? view,string? q,string? status,string? channel)
    {
            if(view is not (null or "active" or "trash" or "archived"))throw new ArgumentException("列表类型无效");
            var query=db.Drafts.AsNoTracking().Where(d=>d.Demo==Registration.Demo);
            query=view=="trash"?query.Where(d=>d.DeletedAt!=null):view=="archived"?query.Where(d=>d.DeletedAt==null&&d.ArchivedAt!=null):query.Where(d=>d.DeletedAt==null&&d.ArchivedAt==null);
            if(!string.IsNullOrWhiteSpace(q)){if(q.Length>200)throw new ArgumentException("搜索词过长");query=query.Where(d=>d.Content.Contains(q));}
            if(!string.IsNullOrWhiteSpace(status))query=query.Where(d=>d.Status==status);
            if(!string.IsNullOrWhiteSpace(channel)){
                if(!Connectors.Channels.Contains(channel))throw new ArgumentException("渠道无效");
                var token="\""+channel+"\"";query=query.Where(d=>d.Channels.Contains(token));
            }
        return query;
    }
    public sealed record BulkInput(string Action,DraftLifecycle.Selection[] Items);
}
