# Task 5 — LINQ SELECT Projection: Fix `VisitSelectLambda` + Table Name Extraction

Two concrete bugs in `SurrealQueryCompiler` that make the LINQ query API incomplete:
1. `VisitSelectLambda` always returns `"SELECT *"` regardless of the projection expression.
2. `VisitConstant` sets `_tableName = "unknown_table"` instead of extracting the real table name
   from `SurrealDbQuery<T>`.

```xml
<project>
  <name>SurrealDB.Client — LINQ SELECT Projection Fix</name>
  <description>
    Fix two bugs in SurrealQLExpressionVisitor so that LINQ projections generate correct
    SurrealQL SELECT column lists, and the query provider correctly passes the table name
    through to the compiler instead of leaving it as "unknown_table".
  </description>
  <language>C# 13 / .NET 9</language>
  <repo_root>C:\Projects\SurrealDB.Client</repo_root>
</project>

<scope>
  WHAT TO FIX

  Fix A — VisitSelectLambda generates real column list
    File: src/SurrealDB.Client/Query/SurrealQueryCompiler.cs
    Class: SurrealQLExpressionVisitor
    Method: VisitSelectLambda (currently returns "SELECT *" for all inputs)

    Support these projection patterns:
    1. Single-property: .Select(u => u.Name)
       → "SELECT name"

    2. Anonymous object (new { }): .Select(u => new { u.Name, u.Email })
       → "SELECT name, email"

    3. Named members in new {}: .Select(u => new { FullName = u.Name, u.Email })
       → "SELECT name AS full_name, email"
       (alias is the key name converted to snake_case)

    4. Constant / no-property: .Select(u => 1) — treat as "SELECT 1"

    Fall back to "SELECT *" ONLY if the lambda body is not recognizable (defensive).

  Fix B — Table name passes through VisitConstant correctly
    File: src/SurrealDB.Client/Query/SurrealQueryCompiler.cs
    Class: SurrealQLExpressionVisitor
    Method: VisitConstant

    Current broken code:
      if (node.Value?.GetType() is { IsGenericType: true } t &&
          t.GetGenericTypeDefinition() == typeof(SurrealDbQuery<>))
      {
          _tableName = "unknown_table";    // ← WRONG
      }

    Fix: SurrealDbQuery<T> must expose a TableName property (add it if missing).
    Then:
      if (node.Value is ISurrealDbQueryMetadata q)
          _tableName = q.TableName ?? _tableName;

    Add internal interface ISurrealDbQueryMetadata to Query/SurrealDbQuery.cs:
      internal interface ISurrealDbQueryMetadata { string? TableName { get; } }

    Make SurrealDbQuery<T> implement ISurrealDbQueryMetadata.
    SurrealDbQueryProvider already passes tableName to SurrealDbQuery constructor —
    verify and wire up.

  WHAT NOT TO DO
  - Do not change IQueryCompiler or the public Compile/CompileDetailed signatures
  - Do not support .Select() with method calls (e.g. u.Name.ToUpper()) — skip/SELECT *
  - Do not support nested object projections (e.g. new { u.Address.City }) — SELECT *
  - Do not change SurrealDbQueryProvider's cache key logic
  - Do not add new NuGet packages
</scope>

<constraints>
  - Property names are always converted to snake_case (existing ToSnakeCase method)
  - Alias names (from named members) are also snake_case
  - If a projection references a member that is NOT a direct property of the parameter
    (e.g. method call, nested property), fall back to "SELECT *" for safety
  - The SELECT clause is prefixed with "SELECT " (with space); the column list has no trailing comma
  - Multiple columns: "SELECT col1, col2, col3"
  - Single column: "SELECT col1" (no wrapping parens)
</constraints>

<architecture>
  Files modified:

  src/SurrealDB.Client/Query/SurrealDbQuery.cs
    → Add internal interface ISurrealDbQueryMetadata { string? TableName { get; } }
    → Make SurrealDbQuery<T> implement it with a TableName property
    → Verify constructor accepts tableName and stores it

  src/SurrealDB.Client/Query/SurrealQueryCompiler.cs
    → Fix VisitSelectLambda (the primary change)
    → Fix VisitConstant (use ISurrealDbQueryMetadata)

  src/SurrealDB.Client/Query/SurrealDbQueryProvider.cs
    → Verify tableName is passed to SurrealDbQuery<T> constructor (read and confirm)
    → No changes needed if already correct

  Tests:
  tests/SurrealDB.Client.Tests.Unit/QueryCompilerTests.cs
    → Add projection test cases (file already exists — append to it)
</architecture>

<data_sources>
  Existing SurrealDbQuery<T> (read src/SurrealDB.Client/Query/SurrealDbQuery.cs first):
    Current constructor signature (verify before editing):
      public SurrealDbQuery(IQueryProvider provider)
    OR
      public SurrealDbQuery(IQueryProvider provider, Expression expression)

    SurrealDbQueryProvider creates SurrealDbQuery via:
      new SurrealDbQuery<T>(this)
    It holds _tableName as a field. Verify how it builds the IQueryable.

  SurrealQLExpressionVisitor key state fields (do not rename):
    private string? _selectClause = "SELECT *";   ← this gets replaced by VisitSelectLambda
    private string? _tableName;                   ← this gets set by VisitConstant
</data_sources>

<models>
  New internal interface (in SurrealDbQuery.cs):
    internal interface ISurrealDbQueryMetadata
    {
        string? TableName { get; }
    }
</models>

<algorithm>
  Read SurrealDbQuery.cs first to verify current constructor. Then:

  SurrealDbQuery<T> changes:
    If constructor is: public SurrealDbQuery(IQueryProvider provider)
    → Change to: public SurrealDbQuery(IQueryProvider provider, string? tableName = null)
    → Store: private readonly string? _tableName;
    → Implement ISurrealDbQueryMetadata.TableName → return _tableName;

    If SurrealDbQueryProvider creates "new SurrealDbQuery<T>(this)" → update to
    "new SurrealDbQuery<T>(this, _tableName)"

  VisitConstant fix:
    protected override Expression? VisitConstant(ConstantExpression node)
    {
        if (node.Value is ISurrealDbQueryMetadata meta && meta.TableName != null)
            _tableName = meta.TableName;
        return base.VisitConstant(node);
    }

  VisitSelectLambda fix:
    private string VisitSelectLambda(LambdaExpression lambda)
    {
        var param = lambda.Parameters[0];
        var body = lambda.Body;

        // Pattern 1: single member access — .Select(u => u.Name)
        if (body is MemberExpression memberExpr &&
            memberExpr.Expression is ParameterExpression pe &&
            pe.Name == param.Name)
        {
            return "SELECT " + ToSnakeCase(memberExpr.Member.Name);
        }

        // Pattern 2 & 3: new { } anonymous type
        if (body is NewExpression newExpr)
        {
            var columns = new List<string>();
            for (int i = 0; i < newExpr.Arguments.Count; i++)
            {
                var arg = newExpr.Arguments[i];
                var memberName = newExpr.Members?[i]?.Name;  // alias name (may differ)

                if (arg is MemberExpression argMember &&
                    argMember.Expression is ParameterExpression ap &&
                    ap.Name == param.Name)
                {
                    var sourceCol = ToSnakeCase(argMember.Member.Name);
                    var aliasCol  = memberName != null ? ToSnakeCase(memberName) : null;

                    // Only emit AS alias if alias differs from source column
                    if (aliasCol != null && aliasCol != sourceCol)
                        columns.Add($"{sourceCol} AS {aliasCol}");
                    else
                        columns.Add(sourceCol);
                }
                else
                {
                    // Unrecognized argument — fall back to SELECT *
                    return "SELECT *";
                }
            }
            return "SELECT " + string.Join(", ", columns);
        }

        // Pattern 4: constant value (e.g. .Select(u => 1))
        if (body is ConstantExpression constBody)
        {
            return $"SELECT {constBody.Value}";
        }

        // Fallback
        return "SELECT *";
    }
</algorithm>

<edge_cases>
  1. SELECT with zero members in new { } — returns "SELECT " (empty columns). This should
     not happen with valid C# but if it does, fall back to "SELECT *":
     if (columns.Count == 0) return "SELECT *";

  2. MEMBER FROM DIFFERENT PARAMETER — e.g. closure capture. If arg.Expression is not
     the lambda parameter, fall back to "SELECT *" for the whole projection.

  3. NESTED PROPERTY — u.Address.City: MemberExpression.Expression is another
     MemberExpression, not a ParameterExpression. Fall back to "SELECT *".

  4. METHOD CALL in projection — u.Name.ToUpper(): body is MethodCallExpression,
     not handled → fall back to "SELECT *".

  5. MemberInitExpression (new User { Name = u.Name }) — not an anonymous type.
     Fall back to "SELECT *". Do not attempt to parse MemberInitExpression.

  6. TABLE NAME STILL NULL after VisitConstant — tableName passed via CompileDetailed
     takes precedence. If both are null, GetSQL() generates "SELECT ... FROM " (no table).
     This is the existing behavior for the Compile() overload and should not regress.

  7. ToSnakeCase("Id") → "id" — verify existing ToSnakeCase handles single-char correctly.
     "Id" → "_d"? No — ToSnakeCase only inserts underscore before uppercase that follows
     a non-uppercase char. "Id": i=0 lowercase → 'i', i=1 uppercase after i=0 → "_d".
     Actually "Id" → "i_d" which is wrong. Fix: don't insert underscore if previous char
     is also uppercase (acronym handling). But this is pre-existing behavior — do NOT change
     ToSnakeCase logic. Document as known limitation.

  8. PROJECTION CHANGES ENTITY TYPE — .Select(u => new { u.Name }) returns anonymous type,
     but SurrealDbQuery<T> is still typed as <User>. The query executor deserializes as T.
     This is a known limitation — projections that change the result shape will cause
     deserialization failures at runtime. Document in XML comment:
     "Projections that change the result type require QueryAsync<T> with a matching T."
</edge_cases>

<testing>
  Add to tests/SurrealDB.Client.Tests.Unit/QueryCompilerTests.cs

  test_Compile_SinglePropertyProjection_GeneratesSelectColumn
    → query.Select(u => u.Name)
    → Assert result contains "SELECT name" and NOT "SELECT *"

  test_Compile_AnonymousTypeProjection_GeneratesColumnList
    → query.Select(u => new { u.Name, u.Email })
    → Assert result contains "SELECT name, email"

  test_Compile_AnonymousTypeWithAlias_GeneratesAsClauses
    → query.Select(u => new { FullName = u.Name, u.Email })
    → Assert result contains "SELECT name AS full_name, email"

  test_Compile_AliasMatchesSource_NoAsClauses
    → query.Select(u => new { u.Name })
    → Assert result contains "SELECT name" and NOT "AS"

  test_Compile_UnrecognizedProjection_FallsBackToSelectStar
    → query.Select(u => u.Name.Length)  // method call
    → Assert result contains "SELECT *"

  test_Compile_ProjectionCombinedWithWhere
    → query.Where(u => u.Age > 18).Select(u => new { u.Name, u.Email })
    → Assert result contains "SELECT name, email"
    → Assert result contains "WHERE age > @p0"

  test_CompileDetailed_TableNameFromQueryMetadata
    → SurrealDbQuery<TestUser> with tableName "users"
    → CompileDetailed(expression) (no explicit tableName arg)
    → Assert result.TableName == "users"
    → Assert result.SurrealQL contains "FROM users"

  test_CompileDetailed_ExplicitTableNameOverridesMetadata
    → SurrealDbQuery<TestUser> with tableName "users"
    → CompileDetailed(expression, tableName: "accounts")
    → Assert result.TableName == "accounts"
    → Assert result.SurrealQL contains "FROM accounts"

  test_NoTableName_NoFromClause
    → expression from List<TestUser>.AsQueryable() (no SurrealDbQuery metadata)
    → CompileDetailed(expression, tableName: null)
    → Assert result.SurrealQL does NOT contain "FROM"
</testing>

<implementation_order>
  Step 1 — Read SurrealDbQuery.cs and SurrealDbQueryProvider.cs to understand
    current constructor and table name passing. Do NOT edit yet.

  Step 2 — Add ISurrealDbQueryMetadata interface to SurrealDbQuery.cs,
    update SurrealDbQuery<T> constructor to accept tableName, implement interface.
    Verify: dotnet build → 0 errors

  Step 3 — Update SurrealDbQueryProvider to pass tableName to SurrealDbQuery<T> constructor.
    Verify: dotnet build → 0 errors

  Step 4 — Fix VisitConstant in SurrealQueryCompiler.cs.
    Verify: dotnet build → 0 errors

  Step 5 — Fix VisitSelectLambda in SurrealQueryCompiler.cs.
    Verify: dotnet build → 0 errors

  Step 6 — Add projection tests to QueryCompilerTests.cs.
    Verify: dotnet test tests/SurrealDB.Client.Tests.Unit/ → all pass including new tests
</implementation_order>

<quality>
  - VisitSelectLambda must never throw — all unhandled patterns fall back to "SELECT *"
  - ISurrealDbQueryMetadata is internal — do not make it part of the public API
  - New test cases follow the existing naming pattern in QueryCompilerTests.cs
  - Keep VisitSelectLambda under 50 lines — current stub is 3 lines; expanded version
    should be clean and readable with comments on each pattern
</quality>

<bootstrap>
  Read these two files before making any edits:
    src/SurrealDB.Client/Query/SurrealDbQuery.cs
    src/SurrealDB.Client/Query/SurrealDbQueryProvider.cs

  Confirm the constructor signatures match the algorithm assumptions above.
  If they differ, adjust the algorithm accordingly before proceeding.
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code. Read the two bootstrap files first
> (explicit instruction in the prompt). The algorithm is conditional on what the current
> constructor looks like — this self-correction step prevents build failures.
