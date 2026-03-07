using Scalar.AspNetCore;
using SurrealDB.Client;
using SurrealDB.Client.EventSourcing;
using SurrealDB.Client.Sample.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ── SurrealDB connection ──────────────────────────────────────────────────────
// Aspire injects: services__surrealdb__http__0 = http://host:port
// Fallback: localhost:8000 for standalone runs
var aspireEndpoint = builder.Configuration["services__surrealdb__http__0"];
string connectionString;

if (!string.IsNullOrEmpty(aspireEndpoint))
{
    var uri = new Uri(aspireEndpoint);
    connectionString = $"surreal://{uri.Host}:{uri.Port}";
}
else
{
    connectionString = builder.Configuration["SurrealDB:Url"] ?? "surreal://localhost:8000";
}

var surrealOptions = new SurrealDbClientOptions
{
    ConnectionString = connectionString,
    Namespace = builder.Configuration["SurrealDB:Namespace"] ?? "demo",
    Database = builder.Configuration["SurrealDB:Database"] ?? "shop",
    UseHttps = false,
    VerifyServerCertificate = false,
    AcknowledgeCertificateValidationRisk = true,   // local dev only
    ConnectionTimeout = TimeSpan.FromSeconds(10),
    CommandTimeout = TimeSpan.FromSeconds(30)
};

// Register SurrealDB client as singleton (shared connection pool)
builder.Services.AddSingleton<ISurrealDbClient>(_ => new SurrealDbClient(surrealOptions));

// ── Event sourcing ────────────────────────────────────────────────────────────
// Register InMemoryEventStore as singleton so the SSE stream and publish
// endpoints share the same store instance
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();

// ── OpenAPI / Scalar ──────────────────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SurrealDB.Client Demo";
        document.Info.Version = "v0.1.0-beta";
        document.Info.Description =
            "Interactive showcase of the SurrealDB.Client .NET library. " +
            "Each endpoint demonstrates a specific library feature — " +
            "CRUD, Session/ChangeTracker, IQueryable, Authentication, " +
            "Transactions, Interceptors, Plugins, Caching, Concurrency, " +
            "Event Sourcing, and Server-Sent Events.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.MapOpenApi();

app.MapScalarApiReference(options =>
{
    options.Title = "SurrealDB.Client Demo";
    options.Theme = ScalarTheme.Purple;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.ShowSidebar = true;
});

// ── Connect to SurrealDB ──────────────────────────────────────────────────────
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var client = app.Services.GetRequiredService<ISurrealDbClient>();

try
{
    await client.ConnectAsync();
    await client.AuthenticateAsync("admin", "password");
    logger.LogInformation("Connected to SurrealDB at {ConnectionString}", connectionString);
}
catch (Exception ex)
{
    logger.LogWarning(ex,
        "Could not connect to SurrealDB at startup ({ConnectionString}). " +
        "Endpoints that require a live connection will return 500. " +
        "Demo endpoints (LINQ preview, interceptors, plugins, concurrency) work without a connection.",
        connectionString);
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapProductEndpoints();       // CRUD: CreateAsync, GetAsync, SelectAsync, UpdateAsync, DeleteAsync, UpsertAsync
app.MapOrderEndpoints();          // Session: ISurrealDbSession, ChangeTracker, SaveChangesAsync, IQueryable
app.MapUserEndpoints();           // Auth: AuthenticateAsync, LogoutAsync
app.MapQueryEndpoints();          // Query: raw QueryAsync, typed QueryAsync<T>, BeginTransactionAsync, SurrealQueryCompiler
app.MapDemoEndpoints();           // Enterprise: Interceptors, Plugins, Cache, ConcurrencyToken, EventSourcing
app.MapEventStreamEndpoints();    // SSE: InMemoryEventStore stream + replay

// Root redirect to Scalar UI
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.Run();
