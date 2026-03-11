# ✅ DONE — Scenario 4 — Batch Operations API

```xml
<project>
  <name>SurrealDB.Client — Batch Operations</name>
  <description>
    Add true server-side batch operations to SurrealDB.Client. Today,
    ISurrealDbClient.CreateAsync(table, IEnumerable&lt;T&gt;) and SaveChangesAsync
    loop over entities one-by-one, causing N+1 network round trips.

    This task introduces three new methods on ISurrealDbClient:
      - CreateManyAsync&lt;T&gt;(string table, IEnumerable&lt;T&gt; data, ct) → IEnumerable&lt;T?&gt;
      - UpdateManyAsync&lt;T&gt;(IEnumerable&lt;(string id, T data)&gt; updates, ct) → IEnumerable&lt;T?&gt;
      - DeleteManyAsync(IEnumerable&lt;string&gt; recordIds, ct) → int

    Each method serializes all operations into a single SurrealQL query string
    (or minimal set of batched queries) and sends it in one round trip. The session
    SaveChangesAsync is updated to use the batch path when >= BatchThreshold entities
    share the same state and table.
  </description>
  <language>C# 13 / .NET 9</language>
  <package_manager>dotnet CLI</package_manager>
  <working_directory>C:\Projects\SurrealDB.Client</working_directory>
</project>

<scope>
  BUILD:
  1. Add CreateManyAsync, UpdateManyAsync, DeleteManyAsync to ISurrealDbClient.
  2. Implement the three methods in SurrealDbClient.
  3. Add int BatchThreshold property to SurrealDbClientOptions (default: 5).
     When fewer than BatchThreshold entities are being saved, the existing one-by-one
     path in SaveChangesAsync remains. When >= BatchThreshold, use the batch path.
  4. Update SurrealDbSession.ExecuteChangesInTransaction to use batch methods
     for added, modified, and deleted entities when count >= options.BatchThreshold.
  5. Add unit tests in tests/SurrealDB.Client.Tests.Unit/BatchOperationTests.cs.

  DO NOT BUILD:
  - Do not change the signatures of existing CreateAsync, UpdateAsync, DeleteAsync.
  - Do not add new packages.
  - Do not implement server-side cursors or pagination (that is Scenario 5).
  - Do not change the serializer — continue using ISerializer.
</scope>

<constraints>
  - Target: net9.0, no new packages
  - File-scoped namespaces
  - ConfigureAwait(false) on all awaited calls in library code
  - All new methods must accept CancellationToken
  - Empty input (empty IEnumerable) must return immediately with an empty result —
    do NOT send a query with zero items
  - Batch size limit: if the caller passes > 1000 items to any single batch method,
    split into chunks of 1000 and execute sequentially. Accumulate results.
    1000 is a named constant: BatchOperations.MaxChunkSize = 1000
  - All 323 existing unit tests must still pass
</constraints>

<architecture>
  src/SurrealDB.Client/
  ├── Batch/
  │   └── BatchOperations.cs         ← NEW: SurrealQL batch query builder helpers
  ├── ISurrealDbClient.cs            ← MODIFY: add 3 new method signatures
  ├── SurrealDbClient.cs             ← MODIFY: implement 3 new methods
  ├── SurrealDbClientOptions.cs      ← MODIFY: add BatchThreshold property
  └── Session/
      └── SurrealDbSession.cs        ← MODIFY: use batch path in SaveChangesAsync

  tests/SurrealDB.Client.Tests.Unit/
  └── BatchOperationTests.cs         ← NEW: 10 unit tests
</architecture>

<models>
  <!-- ISurrealDbClient additions (add to #region CRUD Operations) -->

  /// Batch-creates multiple records in a single round trip.
  Task&lt;IEnumerable&lt;T?&gt;&gt; CreateManyAsync&lt;T&gt;(
      string table,
      IEnumerable&lt;T&gt; data,
      CancellationToken cancellationToken = default) where T : class;

  /// Batch-updates multiple records by ID in a single round trip.
  /// Each tuple is (recordId, updatedData).
  Task&lt;IEnumerable&lt;T?&gt;&gt; UpdateManyAsync&lt;T&gt;(
      IEnumerable&lt;(string Id, T Data)&gt; updates,
      CancellationToken cancellationToken = default) where T : class;

  /// Batch-deletes multiple records by ID in a single round trip.
  /// Returns the count of records deleted.
  Task&lt;int&gt; DeleteManyAsync(
      IEnumerable&lt;string&gt; recordIds,
      CancellationToken cancellationToken = default);

  <!-- SurrealDbClientOptions addition -->
  /// Minimum number of entities that triggers the batch path in SaveChangesAsync.
  /// Below this threshold, entities are saved one-by-one (existing behaviour).
  public int BatchThreshold { get; set; } = 5;
</models>

<algorithm>
  ═══════════════════════════════════════════════════════════════
  BatchOperations helper (src/SurrealDB.Client/Batch/BatchOperations.cs)
  ═══════════════════════════════════════════════════════════════

  internal static class BatchOperations
  {
      public const int MaxChunkSize = 1000;

      /// Splits source into chunks of at most MaxChunkSize.
      public static IEnumerable&lt;List&lt;T&gt;&gt; Chunk&lt;T&gt;(IEnumerable&lt;T&gt; source)
      {
          var chunk = new List&lt;T&gt;(MaxChunkSize);
          foreach (var item in source)
          {
              chunk.Add(item);
              if (chunk.Count == MaxChunkSize)
              {
                  yield return chunk;
                  chunk = new List&lt;T&gt;(MaxChunkSize);
              }
          }
          if (chunk.Count > 0) yield return chunk;
      }

      /// Builds a SurrealQL batch INSERT statement for a list of JSON-serialised entities.
      /// Example output for table "user" with 2 records:
      ///   INSERT INTO user [{...}, {...}];
      public static string BuildBatchInsert(string table, IReadOnlyList&lt;string&gt; jsonItems)
          => $"INSERT INTO `{table}` [{string.Join(", ", jsonItems)}];";

      /// Builds a SurrealQL batch UPDATE. Each item is (recordId, json).
      /// Returns a multi-statement string, one UPDATE per line, terminated with newline.
      /// SurrealDB does not have a native multi-row UPDATE in one statement, so this sends
      /// multiple statements in a single QueryAsync call (SurrealDB allows multi-statement
      /// queries separated by semicolons).
      /// Example: UPDATE `user:1` CONTENT {...}; UPDATE `user:2` CONTENT {...};
      public static string BuildBatchUpdate(IReadOnlyList&lt;(string Id, string Json)&gt; items)
          => string.Join(" ", items.Select(i => $"UPDATE `{i.Id}` CONTENT {i.Json};"));

      /// Builds a SurrealQL batch DELETE.
      /// Example: DELETE `user:1`; DELETE `user:2`;
      public static string BuildBatchDelete(IReadOnlyList&lt;string&gt; recordIds)
          => string.Join(" ", recordIds.Select(id => $"DELETE `{id}`;"));
  }

  ═══════════════════════════════════════════════════════════════
  CreateManyAsync implementation in SurrealDbClient
  ═══════════════════════════════════════════════════════════════

  public async Task&lt;IEnumerable&lt;T?&gt;&gt; CreateManyAsync&lt;T&gt;(
      string table, IEnumerable&lt;T&gt; data, CancellationToken ct = default) where T : class
  {
      ThrowIfDisposed();
      if (!_isConnected) throw new ConnectionException("Not connected.");
      ArgumentNullException.ThrowIfNull(table);

      var list = data.ToList();
      if (list.Count == 0) return Enumerable.Empty&lt;T?&gt;();

      var results = new List&lt;T?&gt;();

      foreach (var chunk in BatchOperations.Chunk(list))
      {
          var jsonItems = chunk
              .Select(item => _serializer.Serialize(item))
              .ToList();

          var sql = BatchOperations.BuildBatchInsert(table, jsonItems);
          var queryResult = await QueryAsync&lt;T&gt;(sql, null, ct).ConfigureAwait(false);
          results.AddRange(queryResult);
      }

      return results;
  }

  ═══════════════════════════════════════════════════════════════
  UpdateManyAsync implementation in SurrealDbClient
  ═══════════════════════════════════════════════════════════════

  public async Task&lt;IEnumerable&lt;T?&gt;&gt; UpdateManyAsync&lt;T&gt;(
      IEnumerable&lt;(string Id, T Data)&gt; updates, CancellationToken ct = default) where T : class
  {
      ThrowIfDisposed();
      if (!_isConnected) throw new ConnectionException("Not connected.");
      ArgumentNullException.ThrowIfNull(updates);

      var list = updates.ToList();
      if (list.Count == 0) return Enumerable.Empty&lt;T?&gt;();

      var results = new List&lt;T?&gt;();

      foreach (var chunk in BatchOperations.Chunk(list))
      {
          var items = chunk
              .Select(u => (u.Id, Json: _serializer.Serialize(u.Data)))
              .ToList();

          var sql = BatchOperations.BuildBatchUpdate(items);
          var queryResult = await QueryAsync&lt;T&gt;(sql, null, ct).ConfigureAwait(false);
          results.AddRange(queryResult);
      }

      return results;
  }

  ═══════════════════════════════════════════════════════════════
  DeleteManyAsync implementation in SurrealDbClient
  ═══════════════════════════════════════════════════════════════

  public async Task&lt;int&gt; DeleteManyAsync(
      IEnumerable&lt;string&gt; recordIds, CancellationToken ct = default)
  {
      ThrowIfDisposed();
      if (!_isConnected) throw new ConnectionException("Not connected.");
      ArgumentNullException.ThrowIfNull(recordIds);

      var list = recordIds.ToList();
      if (list.Count == 0) return 0;

      var deleted = 0;

      foreach (var chunk in BatchOperations.Chunk(list))
      {
          var sql = BatchOperations.BuildBatchDelete(chunk);
          await QueryAsync(sql, null, ct).ConfigureAwait(false);
          deleted += chunk.Count;
      }

      return deleted;
  }

  ═══════════════════════════════════════════════════════════════
  SurrealDbSession.ExecuteChangesInTransaction — batch path
  ═══════════════════════════════════════════════════════════════

  In the method that processes addedEntities, modifiedEntities, deletedEntities:

  // INSERTS — batch path
  if (addedEntities.Count >= _client.Options.BatchThreshold)
  {
      // Group by table name (GetTableName(entity.GetType()))
      foreach (var group in addedEntities.GroupBy(e => GetTableName(e.GetType())))
      {
          foreach (var entity in group) ApplyAuditAttributes(entity, isInsert: true);
          var typedData = group.Cast&lt;object&gt;().ToList(); // IEnumerable&lt;object&gt;
          await _client.CreateManyAsync&lt;object&gt;(group.Key, typedData, cancellationToken)
              .ConfigureAwait(false);
          foreach (var entity in group)
              _changeTracker.Entry(entity).State = EntityState.Unchanged;
          affectedCount += group.Count();
      }
  }
  else
  {
      // existing one-by-one INSERT path (unchanged)
  }

  Apply the same pattern for UPDATES and DELETES.
  For UPDATES: use UpdateManyAsync with (recordId, entity) tuples.
  For DELETES: use DeleteManyAsync with recordId list.
</algorithm>

<edge_cases>
  1. Empty IEnumerable input → return immediately with empty result / 0.
     Do NOT call QueryAsync with an empty batch — some DB drivers reject empty queries.

  2. data.Count == 1 → still goes through batch path if >= BatchThreshold(default 5)?
     No: 1 &lt; 5, so falls through to existing one-by-one path. Correct.

  3. data.Count == 1000 → single chunk, one round trip. Correct.

  4. data.Count == 1001 → two chunks: first with 1000, second with 1. Two round trips.

  5. data.Count == 2001 → three chunks: 1000 + 1000 + 1. Three round trips.

  6. Serialiser returns invalid JSON for an entity → QueryAsync will throw QueryException.
     The caller receives the exception. Partially-sent chunks (previous iterations)
     are NOT rolled back automatically — this is the caller's responsibility to wrap in
     a transaction. Document this in XML doc comments on the methods.

  7. Table name contains special characters → BuildBatchInsert/Delete wraps in backticks.
     Backtick within table name is an error condition — the existing EscapeIdentifier
     method in SurrealDbClient should be used. Reuse it in BatchOperations.BuildBatchInsert
     by making EscapeIdentifier internal or by accepting a pre-escaped table name.
     Use the pre-escaped approach: SurrealDbClient passes EscapeIdentifier(table) to
     the batch builder, so the builder does NOT double-escape.

  8. RecordId contains special characters in UpdateManyAsync / DeleteManyAsync →
     same backtick escaping applies. Pass EscapeIdentifier(id) for each id.

  9. CancellationToken cancelled mid-loop (between chunks) → the foreach exits at the
     next await. Chunks already sent are committed. Caller must handle partial results.

  10. BatchThreshold = 0 → always use batch path, even for single entities.
      BatchThreshold = 1 → same. Both are valid; Validate() must not reject them.
      Only negative values are invalid. Add to Validate():
        if (BatchThreshold &lt; 0) throw new ValidationException("BatchThreshold cannot be negative.");
</edge_cases>

<testing>
  File: tests/SurrealDB.Client.Tests.Unit/BatchOperationTests.cs
  Trait: [Trait("Category", "Unit")]
  Framework: xUnit + Moq

  Strategy: test BatchOperations static helpers in isolation (pure string building),
  and test ISurrealDbClient method contracts with Mock&lt;ISurrealDbClient&gt;.
  The real SurrealDbClient methods cannot be tested without a live DB (integration tests).

  TEST CASES:

  1. BatchOperations_BuildBatchInsert_SingleItem
     var result = BatchOperations.BuildBatchInsert("user", new[] { "{\"id\":\"1\"}" });
     Assert.Equal("INSERT INTO `user` [{\"id\":\"1\"}];", result);

  2. BatchOperations_BuildBatchInsert_MultipleItems
     var result = BatchOperations.BuildBatchInsert("user",
         new[] { "{\"id\":\"1\"}", "{\"id\":\"2\"}" });
     Assert.Contains("INSERT INTO `user`", result);
     Assert.Contains("{\"id\":\"1\"}", result);
     Assert.Contains("{\"id\":\"2\"}", result);

  3. BatchOperations_BuildBatchUpdate_SingleItem
     var result = BatchOperations.BuildBatchUpdate(
         new[] { ("user:1", "{\"name\":\"Alice\"}") });
     Assert.Contains("UPDATE `user:1` CONTENT", result);
     Assert.Contains("{\"name\":\"Alice\"}", result);

  4. BatchOperations_BuildBatchDelete_MultipleIds
     var result = BatchOperations.BuildBatchDelete(
         new[] { "user:1", "user:2", "user:3" });
     Assert.Contains("DELETE `user:1`", result);
     Assert.Contains("DELETE `user:2`", result);
     Assert.Contains("DELETE `user:3`", result);

  5. BatchOperations_Chunk_ExactlyMaxChunkSize_ReturnsSingleChunk
     var items = Enumerable.Range(0, BatchOperations.MaxChunkSize).ToList();
     var chunks = BatchOperations.Chunk(items).ToList();
     Assert.Single(chunks);
     Assert.Equal(BatchOperations.MaxChunkSize, chunks[0].Count);

  6. BatchOperations_Chunk_OverMaxChunkSize_ReturnsTwoChunks
     var items = Enumerable.Range(0, BatchOperations.MaxChunkSize + 1).ToList();
     var chunks = BatchOperations.Chunk(items).ToList();
     Assert.Equal(2, chunks.Count);
     Assert.Equal(BatchOperations.MaxChunkSize, chunks[0].Count);
     Assert.Equal(1, chunks[1].Count);

  7. BatchOperations_Chunk_EmptyInput_ReturnsNoChunks
     var chunks = BatchOperations.Chunk(Enumerable.Empty&lt;int&gt;()).ToList();
     Assert.Empty(chunks);

  8. CreateManyAsync_EmptyInput_ReturnsEmpty
     var mock = new Mock&lt;ISurrealDbClient&gt;();
     mock.Setup(c => c.CreateManyAsync&lt;TestEntity&gt;("users", It.IsAny&lt;IEnumerable&lt;TestEntity&gt;&gt;(), default))
         .ReturnsAsync(Enumerable.Empty&lt;TestEntity?&gt;());
     var result = await mock.Object.CreateManyAsync&lt;TestEntity&gt;("users",
         Enumerable.Empty&lt;TestEntity&gt;());
     Assert.Empty(result);

  9. DeleteManyAsync_EmptyInput_ReturnsZero
     var mock = new Mock&lt;ISurrealDbClient&gt;();
     mock.Setup(c => c.DeleteManyAsync(It.IsAny&lt;IEnumerable&lt;string&gt;&gt;(), default))
         .ReturnsAsync(0);
     var result = await mock.Object.DeleteManyAsync(Enumerable.Empty&lt;string&gt;());
     Assert.Equal(0, result);

  10. SurrealDbClientOptions_BatchThreshold_DefaultIsFive
      var opts = new SurrealDbClientOptions
      {
          ConnectionString = "surreal://localhost:8000",
          Namespace = "test", Database = "test"
      };
      Assert.Equal(5, opts.BatchThreshold);

  INNER CLASS:
  private class TestEntity { public string? Id { get; set; } }
</testing>

<implementation_order>
  STEP 1 — Create BatchOperations helper
    Create src/SurrealDB.Client/Batch/BatchOperations.cs
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 2 — Add BatchThreshold to options
    Edit SurrealDbClientOptions.cs: add property + Validate() guard.
    Run: dotnet build — 0 errors.

  STEP 3 — Add new method signatures to ISurrealDbClient
    Edit ISurrealDbClient.cs: add CreateManyAsync, UpdateManyAsync, DeleteManyAsync.
    Run: dotnet build — expect errors in SurrealDbClient (not yet implemented).

  STEP 4 — Implement methods in SurrealDbClient
    Edit SurrealDbClient.cs: implement all three methods.
    Run: dotnet build src/SurrealDB.Client/SurrealDB.Client.csproj
    Verify: 0 errors.

  STEP 5 — Update SaveChangesAsync batch path
    Edit SurrealDbSession.cs: update ExecuteChangesInTransaction.
    Run: dotnet build — 0 errors.

  STEP 6 — Write tests
    Create tests/SurrealDB.Client.Tests.Unit/BatchOperationTests.cs
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Verify: all pass, total >= 333 (323 + 10 new).

  STEP 7 — Full regression
    Run: dotnet build
    Run: dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
    Assert: 0 failures.
</implementation_order>

<quality>
  - File-scoped namespaces
  - ConfigureAwait(false) on all awaited calls in library code
  - BatchOperations is internal (library implementation detail)
  - XML doc comments on the three new ISurrealDbClient methods — note the partial-commit
    caveat (no automatic rollback of earlier chunks on failure)
  - No Console.WriteLine
  - EscapeIdentifier reuse — do not duplicate escaping logic
</quality>

<bootstrap>
  1. dotnet --version          → 9.x.x
  2. dotnet build              → 0 errors
  3. dotnet test tests/SurrealDB.Client.Tests.Unit --filter Category=Unit
                               → 323 passed, 0 failed
  Stop and diagnose if any step fails.
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code from the root of
> `C:\Projects\SurrealDB.Client`. Implements batch operations end-to-end
> without follow-up questions.
