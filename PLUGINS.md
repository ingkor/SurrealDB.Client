# Plugin Architecture: Extensibility Framework

> Extensible plugin system for custom behaviors, domain-specific features, and third-party integrations.

## Core Plugin Interface

```csharp
public interface ISurrealDbPlugin
{
    string Name { get; }
    string Version { get; }
    void Initialize(SurrealDbClientBuilder builder);
    void Configure(SurrealDbClientOptions options);
    Task OnConnectedAsync(ISurrealDbClient client);
    Task OnDisconnectingAsync(ISurrealDbClient client);
}

// Implementation
public class CustomAuditingPlugin : ISurrealDbPlugin
{
    public string Name => "Custom Auditing";
    public string Version => "1.0.0";

    public void Initialize(SurrealDbClientBuilder builder)
    {
        builder.AddInterceptor<AuditingInterceptor>();
        builder.AddMigration<CreateAuditTablesM>();
    }

    public void Configure(SurrealDbClientOptions options)
    {
        options.AuditingEnabled = true;
        options.AuditTableName = "audit_logs";
    }

    public async Task OnConnectedAsync(ISurrealDbClient client)
    {
        Console.WriteLine("Auditing plugin connected");
    }

    public async Task OnDisconnectingAsync(ISurrealDbClient client)
    {
        Console.WriteLine("Auditing plugin disconnecting");
    }
}
```

## Using Plugins

```csharp
var client = new SurrealDbClientBuilder()
    .WithConnectionString("surreal://localhost:8000")
    .UsePlugin(new CustomAuditingPlugin())
    .UsePlugin(new EncryptionPlugin())
    .UsePlugin(new CachingPlugin())
    .Build();
```

## Built-in Plugins

- SurrealDB.Plugins.Auditing
- SurrealDB.Plugins.Encryption
- SurrealDB.Plugins.Caching
- SurrealDB.Plugins.Validation
- SurrealDB.Plugins.Soft Deletes

See repository for full plugin examples.
