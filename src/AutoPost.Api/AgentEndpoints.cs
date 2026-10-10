using AutoPost.Core;
using AutoPost.Infrastructure;
using Microsoft.EntityFrameworkCore;

public static class AgentEndpoints
{
    public static void MapAgent(this RouteGroupBuilder api)
    {
        api.MapGet("/agent/status", async (Store db) => new {
            configured = Connectors.Env("OPENAI_API_KEY") != "", model = Connectors.Env("OPENAI_MODEL"),
            dailyLimit = AgentService.DailyLimit, used = (await db.Budgets.FindAsync("agent:" + Clock.Day))?.Used ?? 0,
            cloudDatabase = db.MySql ? "connected" : "pending_certificate_verification",
            capabilities = new[] { "latest", "search", "refresh", "analyse", "draft" }
        });
        api.MapGet("/agent/sessions", async (Store db) => await db.AgentSessions.AsNoTracking().Where(s => s.Demo == Registration.Demo).OrderByDescending(s => s.UpdatedAt).ThenBy(s=>s.Id).Take(100).ToListAsync());
        api.MapPost("/agent/sessions", (Store db) => {
            var row = new AgentSession { Demo = Registration.Demo }; db.AgentSessions.Add(row); db.Mark("agent_session", row.Id); return Results.Ok(row);
        });
        api.MapGet("/agent/sessions/{id}/messages", async (string id, string? before, Store db) => {
            await Session(db,id);
            var query = db.AgentMessages.AsNoTracking().Where(m=>m.SessionId==id);
            if (before != null) {
                var pivot=await query.SingleOrDefaultAsync(m=>m.Id==before) ?? throw new ArgumentException("历史游标无效");
                query=query.Where(m=>m.CreatedAt<pivot.CreatedAt || (m.CreatedAt==pivot.CreatedAt && m.Id.CompareTo(pivot.Id)<0));
            }
            var rows=await query.OrderByDescending(m=>m.CreatedAt).ThenByDescending(m=>m.Id).Take(101).ToListAsync();
            return Results.Ok(new { items=rows.Take(100).Reverse(), nextCursor=rows.Count>100?rows[99].Id:null });
        });
        api.MapPost("/agent/sessions/{id}/messages", async (string id, AgentInput input, Store db) => {
            var session=await Session(db,id);
            if (input.Action is not ("latest" or "search" or "refresh" or "analyse" or "draft") || string.IsNullOrWhiteSpace(input.RequestId) || input.RequestId.Length>100 || input.Prompt==null || input.Prompt.Length>4000) throw new ArgumentException("Agent 请求无效");
            var duplicate=await db.AgentMessages.SingleOrDefaultAsync(m=>m.SessionId==id && m.RequestId==input.RequestId);
            if (duplicate!=null) return Results.Ok(duplicate);
            if (await db.AgentMessages.AnyAsync(m=>m.SessionId==id && (m.Status=="queued" || m.Status=="running" || m.Status=="waiting_quota"))) throw new InvalidOperationException("请先停止或等待当前任务");
            if (input.EventId!=null && !await db.Events.AnyAsync(e=>e.Id==input.EventId && e.Demo==Registration.Demo)) throw new ArgumentException("事件不可用");
            if (input.Action=="analyse" && input.EventId==null) throw new ArgumentException("请先选择事件");
            var row=new AgentMessage { SessionId=id,RequestId=input.RequestId,Prompt=input.Prompt.Trim(),Action=input.Action,EventId=input.EventId };
            session.Title=string.IsNullOrWhiteSpace(row.Prompt)?"最新情报":row.Prompt[..Math.Min(40,row.Prompt.Length)]; session.UpdatedAt=Clock.Now;
            db.AgentMessages.Add(row); await db.Enqueue("agent",row.Id); db.Mark("agent",row.Id,"queued"); return Results.Accepted(value:row);
        });
        api.MapGet("/agent/tasks/{id}", async (string id, Store db) => { var row=await Message(db,id); return Results.Ok(row); });
        api.MapPost("/agent/tasks/{id}/cancel", async (string id, Store db) => {
            var row=await Message(db,id); row.CancelRequested=true;
            if (row.Status is "queued" or "waiting_quota") { row.Status="cancelled"; row.Progress="已停止"; }
            row.UpdatedAt=Clock.Now; db.Mark("agent",id,"cancel_requested"); return Results.Ok(row);
        });
    }
    private static async Task<AgentSession> Session(Store db,string id) => await db.AgentSessions.SingleOrDefaultAsync(s=>s.Id==id && s.Demo==Registration.Demo) ?? throw new ArgumentException("会话不可用");
    private static async Task<AgentMessage> Message(Store db,string id) { var row=await db.AgentMessages.SingleOrDefaultAsync(m=>m.Id==id) ?? throw new ArgumentException("任务不可用"); await Session(db,row.SessionId); return row; }
    public sealed record AgentInput(string Action,string Prompt,string RequestId,string? EventId);
}
