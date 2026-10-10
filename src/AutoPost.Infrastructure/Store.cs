using AutoPost.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPost.Infrastructure;

public sealed class Store(DbContextOptions<Store> options) : DbContext(options)
{
    public bool MySql => Database.ProviderName?.Contains("MySql") == true;
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<MediaAsset> Media => Set<MediaAsset>();
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
        b.Entity<AgentSession>().HasIndex(x => new { x.Demo, x.UpdatedAt });
        b.Entity<AgentMessage>().HasIndex(x => new { x.SessionId, x.CreatedAt });
        b.Entity<AgentMessage>().HasIndex(x => new { x.SessionId, x.RequestId }).IsUnique();
        if (!MySql) b.Entity<Event>().HasIndex(x => new { x.Source, x.SourceId }).IsUnique();
        else {
            b.Entity<Event>().Property<string>("SourceIdentity").HasMaxLength(64).HasComputedColumnSql("sha2(concat(length(`Source`), ':', `Source`, `SourceId`),256)", stored: true);
            b.Entity<Event>().HasIndex("SourceIdentity").IsUnique();
        }
        b.Entity<Event>().HasIndex(x => new { x.Demo, x.PublishedAt, x.Id });
        b.Entity<Event>().HasIndex(x => x.Fingerprint);
        b.Entity<Event>().HasIndex(x => x.GroupId);
        b.Entity<Event>().HasIndex(x => new { x.TranslationStatus, x.CollectedAt });
        b.Entity<Analysis>().HasIndex(x => x.EventId).IsUnique();
        b.Entity<DraftVersion>().HasIndex(x => new { x.DraftId, x.Revision }).IsUnique();
        b.Entity<Draft>().HasIndex(x => new { x.EventId, x.RuleId }).IsUnique().HasFilter(MySql ? null : "\"RuleId\" IS NOT NULL");
        b.Entity<Delivery>().HasIndex(x => new { x.DraftId, x.Revision, x.Channel }).IsUnique();
        b.Entity<Delivery>().HasIndex(x => new { x.Channel, x.CreatedAt });
        if (!MySql) b.Entity<Job>().HasIndex(x => new { x.Kind, x.Target }).IsUnique().HasFilter("\"Status\" IN ('pending', 'running')");
        else {
            b.Entity<Job>().Property<string>("ActiveTarget").HasMaxLength(64).HasComputedColumnSql("case when `Status` in ('pending','running') then sha2(concat(length(`Kind`), ':', `Kind`, `Target`),256) else null end", stored:true);
            b.Entity<Job>().HasIndex("ActiveTarget").IsUnique();
        }
        b.Entity<Job>().HasIndex(x => new { x.Status, x.DueAt });
        if (MySql) b.Entity<Job>().HasIndex(x => new { x.Status,x.LeaseUntil });
        b.Entity<Change>().Property(x => x.Id).ValueGeneratedOnAdd();
        if (MySql) {
            foreach (var entity in b.Model.GetEntityTypes())
                foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string))) {
                    var indexed = property.IsPrimaryKey() || entity.GetIndexes().Any(i => i.Properties.Contains(property));
                    if (indexed && property.GetMaxLength() == null) property.SetMaxLength(property.Name == "Id" || property.Name.EndsWith("Id") ? 128 : 256);
                    if (!indexed && property.GetMaxLength() == null) property.SetColumnType("longtext");
                    property.SetCollation("utf8mb4_bin");
                }
        }
    }
    public void Mark(string kind, string target, string detail = "")
    {
        Changes.Add(new Change { Kind = kind, Target = target });
        Audits.Add(new Audit { Action = kind, Target = target, Detail = detail });
    }
    public async Task Lock(CancellationToken ct = default) {
        if (MySql) await Settings.FromSqlRaw("SELECT * FROM `Settings` WHERE `Id`=1 FOR UPDATE").AsNoTracking().ToListAsync(ct);
        else await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(794121)", ct);
        foreach (var entry in ChangeTracker.Entries<Settings>().Where(e=>e.State==EntityState.Unchanged).ToArray()) await entry.ReloadAsync(ct);
    }
    public Task<int> Sql(FormattableString sql, CancellationToken ct) => Database.ExecuteSqlInterpolatedAsync(Dialect(sql), ct);
    public FormattableString Dialect(FormattableString sql) => System.Runtime.CompilerServices.FormattableStringFactory.Create(MySql ? sql.Format.Replace('"','`') : sql.Format, sql.GetArguments());
    public async Task Migrate(CancellationToken ct = default) {
        if (!MySql) { await Database.MigrateAsync(ct); return; }
        // MySQL schema is created only by the explicit deployment command.
        await Database.EnsureCreatedAsync(ct);
        if (!await Settings.AnyAsync(ct)) { Settings.Add(new Settings()); await SaveChangesAsync(ct); }
    }
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
        services.AddDbContext<Store>(o => Configure(o, connection));
        services.AddHttpClient("platform", c => c.Timeout = TimeSpan.FromSeconds(65))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, MaxResponseHeadersLength = 32 });
        services.AddHttpClient("translation", c => c.Timeout = TimeSpan.FromSeconds(180));
        services.AddHttpClient("rss", c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(Network.SafeHandler);
        services.AddScoped<FeedReader>();
        services.AddScoped<Connectors>();
        services.AddScoped<Pipeline>();
        services.AddScoped<AgentService>();
        return services;
    }
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connection) {
        if (Environment.GetEnvironmentVariable("DATABASE_PROVIDER") != "mysql") return options.UseNpgsql(connection);
        var config = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connection);
        if (config.Server is not ("127.0.0.1" or "localhost" or "::1") && config.SslMode is not (MySql.Data.MySqlClient.MySqlSslMode.VerifyCA or MySql.Data.MySqlClient.MySqlSslMode.VerifyFull))
            throw new InvalidOperationException("远端MySQL必须配置CA证书并启用身份验证");
        return options.UseMySQL(connection);
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
