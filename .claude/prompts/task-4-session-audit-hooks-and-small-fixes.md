# Task 4 — Session Audit Hooks + Small Bug Fixes

Three targeted fixes in one batch:
1. Enforce `[CreatedAt]`, `[UpdatedAt]`, `[CreatedBy]`, `[UpdatedBy]` attributes
   in `SurrealDbSession.ExecuteChangesInTransaction` — attributes are defined but ignored.
2. Fix `LogoutAsync` — currently has `// TODO: Send logout command to server`.
3. Fix `IsConnectedAsync` — currently has `// TODO: Implement health check`, returns stale flag.

```xml
<project>
  <name>SurrealDB.Client — Session Audit Hooks + Small Fixes</name>
  <description>
    Three targeted fixes: (1) make CreatedAt/UpdatedAt/CreatedBy/UpdatedBy attributes
    functional in the session's SaveChangesAsync pipeline, (2) send the INVALIDATE command
    on LogoutAsync so the server-side session is actually cleared, (3) wire up
    IsConnectedAsync to call the real health check instead of returning a stale flag.
  </description>
  <language>C# 13 / .NET 9</language>
  <repo_root>C:\Projects\SurrealDB.Client</repo_root>
</project>

<scope>
  WHAT TO FIX

  Fix A — Audit attribute enforcement in SurrealDbSession
    File: src/SurrealDB.Client/Session/SurrealDbSession.cs
    Method: ExecuteChangesInTransaction (private)

    Before INSERT (Added entities):
      - Find all properties with [CreatedAt] → set to DateTime.UtcNow
      - Find all properties with [UpdatedAt] → set to DateTime.UtcNow
      - Find all properties with [CreatedBy] → set to _currentUserId (if set; else skip)
      - Find all properties with [UpdatedBy] → set to _currentUserId (if set; else skip)

    Before UPDATE (Modified entities):
      - Find all properties with [UpdatedAt] → set to DateTime.UtcNow
      - Find all properties with [UpdatedBy] → set to _currentUserId (if set; else skip)
      - Do NOT touch [CreatedAt] or [CreatedBy] on updates

    Add a new method to ISurrealDbSession and SurrealDbSession:
      void SetCurrentUser(string? userId)
    This stores the userId used to populate CreatedBy/UpdatedBy.
    Store in a private field: private string? _currentUserId;

  Fix B — LogoutAsync sends INVALIDATE to server
    File: src/SurrealDB.Client/SurrealDbClient.cs
    Method: LogoutAsync
    Replace: // TODO: Send logout command to server\n await Task.CompletedTask;
    With:
      if (_currentConnection != null)
      {
          try
          {
              await _currentConnection.SendAsync(ProtocolMethods.Query, "INVALIDATE;", null, cancellationToken);
          }
          catch
          {
              // Suppress — best-effort server invalidation; local session is already cleared
          }
      }

  Fix C — IsConnectedAsync does a real health check
    File: src/SurrealDB.Client/SurrealDbClient.cs
    Method: IsConnectedAsync
    Replace the TODO with:
      if (!_isConnected || _currentConnection == null)
          return false;
      try
      {
          return await _currentConnection.HealthCheckAsync(cancellationToken);
      }
      catch
      {
          _isConnected = false;
          return false;
      }
    Note: IProtocolAdapter.HealthCheckAsync already exists on both HttpProtocolAdapter
    and WebSocketProtocolAdapter. The method signature:
      Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)

  WHAT NOT TO DO
  - Do not change the attribute definitions in AuditAttributes.cs
  - Do not add NuGet packages
  - Do not change the SessionTransaction implementation
  - Do not add a [TableAttribute] system — table name inference stays as-is (lowercase type name)
</scope>

<constraints>
  - Reflection must be cached per type to avoid per-call overhead.
    Use a static ConcurrentDictionary<Type, PropertyInfo[]> for each attribute type.
  - SetCurrentUser(null) must clear the user ID (support anonymous operations)
  - If a [CreatedAt] or [UpdatedAt] property is not of type DateTime or DateTimeOffset,
    skip silently (log nothing — reflection check: prop.PropertyType == typeof(DateTime)
    or typeof(DateTime?) or typeof(DateTimeOffset) or typeof(DateTimeOffset?))
  - If a [CreatedBy] or [UpdatedBy] property is not of type string or string?, skip silently
  - Audit attribute enforcement must not throw — any reflection failure must be caught
    and silently suppressed (audit is best-effort, not a hard dependency)
  - LogoutAsync: INVALIDATE failure is suppressed — the method always succeeds locally
  - IsConnectedAsync: if HealthCheckAsync throws any exception, set _isConnected = false
    and return false. Do not rethrow.
</constraints>

<architecture>
  Files modified (no new files):

  src/SurrealDB.Client/Session/ISurrealDbSession.cs
    → Add: void SetCurrentUser(string? userId);

  src/SurrealDB.Client/Session/SurrealDbSession.cs
    → Add field: private string? _currentUserId;
    → Add method: void SetCurrentUser(string? userId) { _currentUserId = userId; }
    → Add static cache fields for reflected PropertyInfo arrays
    → Add private method: ApplyAuditAttributes(object entity, bool isInsert)
    → Call ApplyAuditAttributes in ExecuteChangesInTransaction before CreateAsync/UpdateAsync

  src/SurrealDB.Client/SurrealDbClient.cs
    → Fix LogoutAsync (lines ~411-412)
    → Fix IsConnectedAsync (lines ~243-244)

  Tests:
  tests/SurrealDB.Client.Tests.Unit/AuditAttributeTests.cs  ← new
  (LogoutAsync and IsConnectedAsync fixes get tested in existing SurrealDbClientTests.cs)
</architecture>

<data_sources>
  IProtocolAdapter.HealthCheckAsync signature (already implemented):
    // HttpProtocolAdapter: sends GET /health, returns true if 200
    // WebSocketProtocolAdapter: sends {"method":"ping"}, returns true if response arrives
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

  AuditAttributes (already defined in src/SurrealDB.Client/Security/AuditAttributes.cs):
    [CreatedAt]  — DateTime/DateTimeOffset property → set to UtcNow on INSERT
    [UpdatedAt]  — DateTime/DateTimeOffset property → set to UtcNow on INSERT and UPDATE
    [CreatedBy]  — string property → set to currentUserId on INSERT (if not null)
    [UpdatedBy]  — string property → set to currentUserId on INSERT and UPDATE (if not null)
</data_sources>

<models>
  No new models. New ISurrealDbSession member:
    void SetCurrentUser(string? userId);
</models>

<algorithm>
  Reflection cache in SurrealDbSession (static fields):
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _createdAtProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _updatedAtProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _createdByProps = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _updatedByProps = new();

  private static PropertyInfo[] GetPropertiesWithAttribute<TAttr>(Type type)
      where TAttr : Attribute
    → return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                 .Where(p => p.GetCustomAttribute<TAttr>() != null && p.CanWrite)
                 .ToArray()

  ApplyAuditAttributes(object entity, bool isInsert):
    var type = entity.GetType()
    var now = DateTime.UtcNow

    if isInsert:
      foreach prop in _createdAtProps.GetOrAdd(type, t => GetPropertiesWithAttribute<CreatedAtAttribute>(t)):
        if prop.PropertyType == typeof(DateTime) or typeof(DateTime?):
          prop.SetValue(entity, now)
        else if prop.PropertyType == typeof(DateTimeOffset) or typeof(DateTimeOffset?):
          prop.SetValue(entity, new DateTimeOffset(now))

      foreach prop in _createdByProps.GetOrAdd(type, t => GetPropertiesWithAttribute<CreatedByAttribute>(t)):
        if _currentUserId != null and prop.PropertyType == typeof(string):
          prop.SetValue(entity, _currentUserId)

    foreach prop in _updatedAtProps.GetOrAdd(type, t => GetPropertiesWithAttribute<UpdatedAtAttribute>(t)):
      if prop.PropertyType == typeof(DateTime) or typeof(DateTime?):
        prop.SetValue(entity, now)
      else if prop.PropertyType == typeof(DateTimeOffset) or typeof(DateTimeOffset?):
        prop.SetValue(entity, new DateTimeOffset(now))

    foreach prop in _updatedByProps.GetOrAdd(type, t => GetPropertiesWithAttribute<UpdatedByAttribute>(t)):
      if _currentUserId != null and prop.PropertyType == typeof(string):
        prop.SetValue(entity, _currentUserId)

  Call site in ExecuteChangesInTransaction:
    // Before INSERT (addedEntities loop):
    try { ApplyAuditAttributes(entity, isInsert: true); } catch { /* suppress */ }
    await _client.CreateAsync(tableName, entity, cancellationToken)

    // Before UPDATE (modifiedEntities loop, after GetModifiedProperties check):
    try { ApplyAuditAttributes(entity, isInsert: false); } catch { /* suppress */ }
    await _client.UpdateAsync(recordId, entity, cancellationToken)
</algorithm>

<edge_cases>
  1. ENTITY WITH NO AUDIT ATTRIBUTES — cache returns empty array; foreach does nothing.
     Zero overhead after first call (cached).

  2. PROPERTY NOT SETTABLE — CanWrite check in GetPropertiesWithAttribute filters these out.
     No exception.

  3. WRONG PROPERTY TYPE — Type check in ApplyAuditAttributes skips silently.
     E.g. [CreatedAt] on a string property → skipped.

  4. NULL userId WITH [CreatedBy] — _currentUserId is null → skip. Field retains its
     existing value (empty string, null, whatever was set). Never overwrite with null.

  5. SetCurrentUser CALLED AFTER Add — the user ID is read at SaveChangesAsync time,
     so calling SetCurrentUser at any point before SaveChangesAsync is valid.

  6. LOGOUT WHILE NOT CONNECTED — _currentConnection is null → skip the INVALIDATE send,
     still clear local _authSession. No exception.

  7. IsConnectedAsync CALLED BEFORE ConnectAsync — _isConnected is false → return false
     immediately without calling HealthCheckAsync.

  8. HealthCheckAsync THROWS — catch all exceptions, set _isConnected = false, return false.
     Do not surface the exception. The caller receives false and can decide to reconnect.
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/AuditAttributeTests.cs

  test_CreatedAt_SetOnInsert
    → Entity with [CreatedAt] DateTime property
    → session.Add(entity); await session.SaveChangesAsync()
    → Assert entity.CreatedAt != default (was set to UtcNow)

  test_UpdatedAt_SetOnInsert
    → Entity with [UpdatedAt] DateTime property
    → session.Add(entity); await session.SaveChangesAsync()
    → Assert entity.UpdatedAt != default

  test_UpdatedAt_SetOnUpdate_NotCreatedAt
    → Entity with both [CreatedAt] and [UpdatedAt]
    → Load entity, mutate, call SaveChangesAsync (modified path)
    → Assert UpdatedAt was changed; CreatedAt was NOT changed

  test_CreatedBy_SetWhenUserIdProvided
    → session.SetCurrentUser("users:alice")
    → session.Add(entity); await session.SaveChangesAsync()
    → Assert entity.CreatedBy == "users:alice"

  test_CreatedBy_NotSetWhenUserIdNull
    → session.SetCurrentUser(null)
    → entity.CreatedBy = "original"
    → session.Add(entity); await session.SaveChangesAsync()
    → Assert entity.CreatedBy == "original" (unchanged)

  test_WrongPropertyType_DoesNotThrow
    → Entity with [CreatedAt] on a string property
    → session.Add(entity); await session.SaveChangesAsync()
    → Assert no exception thrown

  test_NoAuditAttributes_NoSideEffects
    → Plain entity with no audit attributes
    → Full session cycle → Assert no exception, entity unchanged

  test_ReflectionCache_SecondCallDoesNotInvokeGetProperties
    → Call SaveChangesAsync twice with same entity type
    → Use mock to verify cache hit (or just assert no exception and timing < 1ms delta)

  File: tests/SurrealDB.Client.Tests.Unit/SurrealDbClientTests.cs (add to existing)

  test_LogoutAsync_SendsInvalidate_DoesNotThrow
    → MockProtocolAdapter captures SendAsync calls
    → await client.LogoutAsync()
    → Assert "INVALIDATE" appeared in at least one SendAsync call

  test_IsConnectedAsync_WhenConnected_ReturnsHealthCheckResult
    → MockProtocolAdapter.HealthCheckAsync returns true
    → Assert await client.IsConnectedAsync() == true

  test_IsConnectedAsync_WhenHealthCheckFails_ReturnsFalse
    → MockProtocolAdapter.HealthCheckAsync throws
    → Assert await client.IsConnectedAsync() == false
    → Assert client.IsConnected == false (flag updated)
</testing>

<implementation_order>
  Step 1 — Add SetCurrentUser to ISurrealDbSession.cs
    Verify: dotnet build → 0 errors

  Step 2 — Add _currentUserId field + SetCurrentUser method to SurrealDbSession.cs
    Verify: dotnet build → 0 errors

  Step 3 — Add static reflection cache fields to SurrealDbSession
    Verify: dotnet build → 0 errors

  Step 4 — Add ApplyAuditAttributes private method + call sites in ExecuteChangesInTransaction
    Verify: dotnet build → 0 errors

  Step 5 — Fix LogoutAsync in SurrealDbClient.cs
    Verify: dotnet build → 0 errors

  Step 6 — Fix IsConnectedAsync in SurrealDbClient.cs
    Verify: dotnet build → 0 errors

  Step 7 — Write AuditAttributeTests.cs
    Verify: dotnet test tests/SurrealDB.Client.Tests.Unit/ → all pass

  Step 8 — Add LogoutAsync + IsConnectedAsync tests to SurrealDbClientTests.cs
    Verify: dotnet test → all pass
</implementation_order>

<quality>
  - Reflection cache uses ConcurrentDictionary (thread-safe, lock-free reads after first call)
  - ApplyAuditAttributes is wrapped in try/catch at the call site — it must never fail the save
  - SetCurrentUser added to ISurrealDbSession interface so consumers can inject it via DI
  - All new code has XML doc comments
  - Existing tests must continue to pass (run full suite at end)
</quality>

<bootstrap>
  No setup required. Run to confirm baseline:
    dotnet build src/SurrealDB.Client/ → 0 errors
    dotnet test tests/SurrealDB.Client.Tests.Unit/ → (QueryCompilerTests may fail — fix that first)
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code. Three self-contained fixes that
> can be applied in order. Each step compiles independently before proceeding to the next.
