# Developer Checklist - SurrealDB.Client

## Before Starting Any Work

- [ ] `git pull origin main` — always start from latest
- [ ] `dotnet build` — confirm baseline builds cleanly
- [ ] `dotnet test tests/SurrealDB.Client.Tests.Unit/` — confirm baseline tests pass
- [ ] Read the relevant interface in `ISurrealDbClient.cs` or `IConnectionPool.cs` before implementing

---

## Implementing a New Feature

- [ ] Write at least one failing unit test before writing implementation code
- [ ] Add `CancellationToken cancellationToken = default` to every new async method signature
- [ ] Call `ThrowIfDisposed()` as the first line of every new public method
- [ ] Validate inputs with a `ValidateXxx()` helper or inline guard before doing any I/O
- [ ] Use `ConfigureAwait(false)` on every `await` in library code
- [ ] Wrap non-SurrealDb exceptions: `catch (Exception ex) when (!(ex is SurrealDbException))`
- [ ] Add XML doc comment (`<summary>`, `<param>`, `<returns>`, `<exception>`) to all new public members
- [ ] Run `dotnet test` — all tests must pass
- [ ] Run `dotnet build -f net8.0` and `dotnet build -f net9.0` — no new warnings

---

## Fixing a Bug in ConnectionPool.cs

- [ ] Reproduce the bug with a failing test first
- [ ] Check whether the fix touches semaphore logic — if yes, add a concurrent stress test
- [ ] Verify `_disposeSemaphore` and `_acquireSemaphore` are not acquired in nested calls
- [ ] Confirm every code path that calls `AcquireAsync` has a corresponding `ReleaseAsync` in a `finally` block
- [ ] Confirm `_allConnections` is always accessed under `lock (_allConnections)`
- [ ] Run the `ConnectionPool_*` tests after fixing
- [ ] Add a regression test that would have caught the original bug

---

## Implementing a CRUD Operation (e.g., `GetAsync`)

- [ ] Determine which SurrealQL statement maps to the operation: `SELECT`, `CREATE`, `UPDATE`, `DELETE`, `RELATE`
- [ ] Use `_currentConnection.SendAsync("POST", "sql", surrealQlBody, cancellationToken)` as the transport call
- [ ] Serialize request body through `_serializer`
- [ ] Deserialize the response through `_serializer`
- [ ] Handle the `QueryResult.IsSuccess == false` case and throw `QueryException`
- [ ] Write a unit test with a mocked `IProtocolAdapter`
- [ ] Write an integration test that calls real SurrealDB (goes in `Tests.Integration`)
- [ ] Verify the method handles `null` return values gracefully (especially `GetAsync<T?>`)

---

## Before Submitting a Pull Request

- [ ] `dotnet test` — all tests pass
- [ ] `dotnet build` — zero warnings on new code
- [ ] Diff reviewed for accidental debug code (`Console.WriteLine`, hardcoded strings, etc.)
- [ ] No new `// TODO` comments left unless tracked in a GitHub issue
- [ ] No `.Result` or `.Wait()` anywhere in the diff
- [ ] All new `IAsyncDisposable` types have `DisposeAsync` implemented
- [ ] PR description explains _what_ changed and _why_
- [ ] PR links to the relevant issue or backlog item

---

## WebSocket Response Reading Checklist

When working on `WebSocketProtocolAdapter.SendAsync`:

- [ ] Receive loop accumulates bytes until `result.EndOfMessage == true`
- [ ] Buffer reuse strategy doesn't allocate on every iteration (prefer `ArrayPool<byte>`)
- [ ] `WebSocketMessageType.Close` is handled gracefully
- [ ] Total message size limit is enforced to prevent OOM on malicious/corrupt responses
- [ ] Tests cover responses larger than 4 KB and responses spanning multiple frames

---

## After Merging

- [ ] Delete the feature branch locally: `git branch -d feature/my-feature`
- [ ] Pull latest main: `git pull origin main`
- [ ] Verify CI passes on main
