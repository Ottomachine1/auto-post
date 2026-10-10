using AutoPost.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Services.AddAutoPost(Registration.Connection);
builder.Services.AddHostedService<Runner>();
await builder.Build().RunAsync();

sealed class Runner(IServiceScopeFactory scopes, ILogger<Runner> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(new[] { "collect", "analyse", "publish", "agent" }.Select(kind => RunLane(kind, stoppingToken)));
    private async Task RunLane(string kind, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var worked = await scope.ServiceProvider.GetRequiredService<Pipeline>().RunOnce(stoppingToken, kind);
                if (!worked) await Task.Delay(1500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("后台执行暂不可用，将重试；详细凭据不记录"); await Task.Delay(5000, stoppingToken); }
        }
    }
}
