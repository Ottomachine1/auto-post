using AutoPost.Core;
using AutoPost.Infrastructure;
public static class AnalyticsEndpoints
{
    public static void MapAnalytics(this RouteGroupBuilder api)=>api.MapGet("/analytics",async(string? range,long? from,long? to,Store db,CancellationToken ct)=>Results.Ok(await Analytics.Read(db,Analytics.Resolve(range,from,to,Clock.Now),ct)));
}
