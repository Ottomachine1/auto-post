using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPost.Infrastructure;

public sealed class Store(DbContextOptions<Store> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Analysis> Analyses => Set<Analysis>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<DraftVersion> Versions => Set<DraftVersion>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Audit> Audits => Set<Audit>();
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Event>().HasIndex(x => new { x.Source, x.SourceId }).IsUnique();
        b.Entity<Event>().HasIndex(x => new { x.Demo, x.PublishedAt, x.Id });
        b.Entity<Event>().HasIndex(x => x.Fingerprint);
        b.Entity<Event>().HasIndex(x => x.GroupId);
        b.Entity<Analysis>().HasIndex(x => x.EventId).IsUnique();
        b.Entity<DraftVersion>().HasIndex(x => new { x.DraftId, x.Revision }).IsUnique();
        b.Entity<Draft>().HasIndex(x => new { x.EventId, x.RuleId }).IsUnique().HasFilter("\"RuleId\" IS NOT NULL");
        b.Entity<Delivery>().HasIndex(x => new { x.DraftId, x.Revision, x.Channel }).IsUnique();
        b.Entity<Delivery>().HasIndex(x => new { x.Channel, x.CreatedAt });
        b.Entity<Job>().HasIndex(x => new { x.Kind, x.Target }).IsUnique().HasFilter("\"Status\" IN ('pending', 'running')");
        b.Entity<Job>().HasIndex(x => new { x.Status, x.DueAt });
        b.Entity<Change>().Property(x => x.Id).ValueGeneratedOnAdd();
    }
    public void Mark(string kind, string target, string detail = "")
    {
        Changes.Add(new Change { Kind = kind, Target = target });
        Audits.Add(new Audit { Action = kind, Target = target, Detail = detail });
    }
    public async Task Lock(CancellationToken ct = default) => await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(794121)", ct);
    public async Task<Job> Enqueue(string kind, string target, CancellationToken ct = default)
    {
        var old = await Jobs.FirstOrDefaultAsync(j => j.Kind == kind && j.Target == target && (j.Status == "pending" || j.Status == "running"), ct);
        if (old != null) return old;
        var job = new Job { Kind = kind, Target = target };
        Jobs.Add(job); return job;
    }
}
public static class Registration
{
    public static IServiceCollection AddAutoPost(this IServiceCollection services, string connection)
    {
        services.AddDbContext<Store>(o => o.UseNpgsql(connection));
        services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(65))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, MaxResponseHeadersLength = 32 });
        services.AddHttpClient("rss", c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(Network.SafeHandler);
        services.AddScoped<FeedReader>();
        services.AddScoped<Connectors>();
        services.AddScoped<Pipeline>();
        return services;
    }
    public static string Connection => Environment.GetEnvironmentVariable("DATABASE_URL") ?? "Host=127.0.0.1;Port=55432;Database=autopost;Username=postgres;Password=autopost-local-only";
    public static bool Demo => Environment.GetEnvironmentVariable("APP_MODE") != "live";
}

[DbContext(typeof(Store))]
[Migration("202610100001_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql(Schema.Sql);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore a verified backup instead of dropping production tables.");
}
