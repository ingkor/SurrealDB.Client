# Scenario 2 — Aspire Sample Cleanup & Solution Integrity

Fix four concrete issues so that `dotnet build SurrealDB.Client.sln` and
`dotnet test tests/SurrealDB.Client.Tests.Unit/` both pass with **zero errors
and zero warnings**.

```xml
<project>
  <name>SurrealDB.Client — Aspire Sample Cleanup</name>
  <description>
    Fix the four known issues left after the Aspire sample was added:
    (1) a broken unit test caused by a missing namespace import,
    (2) a NuGet version-mismatch warning for Scalar.AspNetCore,
    (3) the SurrealDB.Client.ServiceDefaults project missing from the
    solution file, and (4) uncommitted working-tree changes that should
    be committed as a clean "build housekeeping" commit.
  </description>
  <language>C# / .NET 9 / .NET Aspire 9.2</language>
  <repo_root>C:\Projects\SurrealDB.Client</repo_root>
</project>

<scope>
  WHAT TO BUILD / FIX
  1. Add `using SurrealDB.Client.Query;` to
     tests/SurrealDB.Client.Tests.Unit/QueryCompilerTests.cs (line 1 area)
     so that `SurrealQueryCompiler` resolves and the test compiles.

  2. Update the Scalar.AspNetCore PackageReference in
     samples/SurrealDB.Client.Sample.Api/SurrealDB.Client.Sample.Api.csproj
     from Version="2.3.4" to the resolved version Version="2.4.1"
     to eliminate the NU1603 mismatch warning.

  3. Add SurrealDB.Client.ServiceDefaults to SurrealDB.Client.sln so the
     solution is complete. It must be nested inside the "samples" solution
     folder (GUID {5D20AA90-6969-D8BD-9DCD-8634F4692FDA}).

  4. After the fixes, verify the build and tests, then create ONE git commit
     containing all changes with an appropriate message.

  WHAT NOT TO DO
  - Do not modify any library source under src/SurrealDB.Client/
  - Do not change endpoint implementations in samples/
  - Do not alter the Aspire AppHost project structure
  - Do not bump any version other than Scalar.AspNetCore
  - Do not reformat files that are not being changed
  - Do not add new projects or NuGet packages
</scope>

<constraints>
  - .NET SDK: 10.0.200-preview (installed); target frameworks remain net9.0
  - Shell: bash (Unix paths in commands, forward slashes)
  - Working directory: C:/Projects/SurrealDB.Client
  - No interactive git operations (-i flag is forbidden)
  - Do NOT use --no-verify on git commit
  - TreatWarningsAsErrors is false in Directory.Build.props — still aim for
    zero warnings to keep CI clean
  - The solution file uses Visual Studio 12.00 format; do not rewrite it,
    only append the missing Project(...) block and NestedProjects entry
</constraints>

<architecture>
  Relevant file tree (unchanged structure — only file contents are edited):

  SurrealDB.Client.sln                        ← ADD ServiceDefaults entry here
  Directory.Build.props                       ← already correct (net9.0, STJ 9.0.4)
  samples/
    apphost.cs                                ← .NET 10 file-based AppHost (do not touch)
    apphost.run.json                          ← launch settings (do not touch)
    .aspire/settings.json                     ← points to apphost.cs (do not touch)
    SurrealDB.Client.ServiceDefaults/
      SurrealDB.Client.ServiceDefaults.csproj ← ADD to solution
      Extensions.cs
    SurrealDB.Client.Sample.Api/
      SurrealDB.Client.Sample.Api.csproj      ← FIX Scalar version
      Program.cs
      Endpoints/  (ProductEndpoints, OrderEndpoints, QueryEndpoints,
                   UserEndpoints, DemoEndpoints, EventStreamEndpoints)
      Models/     (Product.cs, Order.cs, User.cs)
  tests/
    SurrealDB.Client.AppHost/
      SurrealDB.Client.AppHost.csproj         ← already correct (Aspire.AppHost.Sdk/13.1.2)
      Properties/launchSettings.json
    SurrealDB.Client.Tests.Unit/
      QueryCompilerTests.cs                   ← FIX missing namespace
      (other test files — do not touch)
    SurrealDB.Client.Tests.Integration/
      (do not touch)
  src/
    SurrealDB.Client/                         ← do not touch
</architecture>

<data_sources>
  No external APIs or databases required. All fixes are purely file edits.

  SurrealDB.Client.ServiceDefaults.csproj current content (for reference):
    ProjectTypeGuid: {9A19103F-16F7-4668-BE54-9A1E7A4F7556}  (C# SDK-style)
    Path (relative to .sln): samples\SurrealDB.Client.ServiceDefaults\
                              SurrealDB.Client.ServiceDefaults.csproj
    Parent solution folder GUID: {5D20AA90-6969-D8BD-9DCD-8634F4692FDA}
    New project GUID (generate a valid UUID):
      Use a deterministic new value, e.g. {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
      (any valid UUID not already in the .sln is fine)

  Existing solution structure (relevant GUIDs):
    Solution folder "src"    : {1E3E3D2A-4A5B-4C6D-7E8F-9A0B1C2D3E4F}
    Solution folder "tests"  : {EC0A40FC-894C-4F78-A432-2BF27955340E}
    Solution folder "samples": {5D20AA90-6969-D8BD-9DCD-8634F4692FDA}
    SurrealDB.Client         : {FCDF5CEA-5C67-4D7F-81C5-54929D4DCD6D}
    SurrealDB.Client.Sample.Api : {13DDFF30-AF2C-4207-B4C7-7372EA383468}
    SurrealDB.Client.AppHost : {052209BC-ECCF-4DFC-AECC-66180F90A96F}
</data_sources>

<models>
  No data models to define. All models already exist.
</models>

<algorithm>
  Fix 1 — QueryCompilerTests.cs namespace
  ----------------------------------------
  Read tests/SurrealDB.Client.Tests.Unit/QueryCompilerTests.cs.
  The file begins:
    namespace SurrealDB.Client.Tests.Unit;
    using System;
    ...
  Insert `using SurrealDB.Client.Query;` after the existing using directives
  (before the class declaration). The SurrealQueryCompiler type lives in
  src/SurrealDB.Client/Query/SurrealQueryCompiler.cs with
  `namespace SurrealDB.Client.Query;`.

  Fix 2 — Scalar.AspNetCore version
  -----------------------------------
  In samples/SurrealDB.Client.Sample.Api/SurrealDB.Client.Sample.Api.csproj:
  Change:
    <PackageReference Include="Scalar.AspNetCore" Version="2.3.4" />
  To:
    <PackageReference Include="Scalar.AspNetCore" Version="2.4.1" />

  Fix 3 — Add ServiceDefaults to solution
  -----------------------------------------
  In SurrealDB.Client.sln:

  Step A — Insert a new Project(...) block immediately before the line
  `Project("{FAE04EC0...}") = "SurrealDB.Client.AppHost"`:

    Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "SurrealDB.Client.ServiceDefaults", "samples\SurrealDB.Client.ServiceDefaults\SurrealDB.Client.ServiceDefaults.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
    EndProject

  Step B — Inside GlobalSection(ProjectConfigurationPlatforms), add six
  entries for the new GUID (same pattern as the other C# projects):

    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.Build.0 = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|x64.ActiveCfg = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|x64.Build.0 = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|x86.ActiveCfg = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|x86.Build.0 = Debug|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.ActiveCfg = Release|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.Build.0 = Release|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|x64.ActiveCfg = Release|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|x64.Build.0 = Release|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|x86.ActiveCfg = Release|Any CPU
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|x86.Build.0 = Release|Any CPU

  Step C — Inside GlobalSection(NestedProjects), add:
    {A1B2C3D4-E5F6-7890-ABCD-EF1234567890} = {5D20AA90-6969-D8BD-9DCD-8634F4692FDA}

  Fix 4 — Commit all changes
  ---------------------------
  Stage only the four modified/new files:
    - SurrealDB.Client.sln
    - samples/SurrealDB.Client.Sample.Api/SurrealDB.Client.Sample.Api.csproj
    - tests/SurrealDB.Client.Tests.Unit/QueryCompilerTests.cs
    - tests/SurrealDB.Client.AppHost/Properties/launchSettings.json
    - Directory.Build.props   (already staged from previous session)
    - samples/apphost.cs      (already staged from previous session)
    - tests/SurrealDB.Client.AppHost/SurrealDB.Client.AppHost.csproj  (already staged)
    - .claude/settings.local.json  (skip — local tooling config, do not commit)

  git commit message:
    "Fix solution integrity: add ServiceDefaults to sln, fix Scalar version pin, fix QueryCompilerTests namespace"
</algorithm>

<store>
  No database. No schema changes.
</store>

<cli>
  Verification commands (run these after all edits, before committing):

  1. Build the full solution (must produce 0 errors, 0 warnings):
       dotnet build SurrealDB.Client.sln

  2. Run unit tests (must produce 0 failures):
       dotnet test tests/SurrealDB.Client.Tests.Unit/

  3. Confirm the solution loads ServiceDefaults:
       grep -c "ServiceDefaults" SurrealDB.Client.sln
     Expected output: >= 3  (Project line, NestedProjects entry, path)

  If any step fails, diagnose and fix before committing.
</cli>

<edge_cases>
  1. GUID collision — If the chosen GUID {A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
     already exists in the .sln (unlikely but possible), generate a new UUID
     with `python -c "import uuid; print(str(uuid.uuid4()).upper())"` and use
     that instead. Update all three places in the .sln consistently.

  2. NU1603 persists after Scalar update — If the warning still appears after
     bumping to 2.4.1, run `dotnet restore` to clear the package cache, then
     rebuild. If 2.4.1 is no longer the latest resolved version, use whatever
     version NuGet resolves to (check via `dotnet list package --outdated`).

  3. QueryCompilerTests still fails after adding the using — Verify the
     namespace in SurrealQueryCompiler.cs is exactly `SurrealDB.Client.Query`
     (read the file first). If it differs, use the actual namespace.

  4. Solution file line endings — The .sln uses CRLF on Windows. Use the Edit
     tool (not sed/awk) for all edits to preserve line endings and avoid
     corrupting the file. Verify with `git diff SurrealDB.Client.sln` that
     the diff looks clean.

  5. Build order warning (MSB4011) — If building the sln produces a build-order
     warning for ServiceDefaults because it's not referenced by AppHost,
     add a ProjectReference from AppHost.csproj to ServiceDefaults.csproj.
     Verify this doesn't break the AppHost build by running
     `dotnet build tests/SurrealDB.Client.AppHost/`.

  6. .claude/settings.local.json in git diff — This file contains local
     tooling config. Do NOT stage or commit it. Use `git reset HEAD
     .claude/settings.local.json` if it was accidentally staged.
</edge_cases>

<testing>
  All verification is via CLI commands in the `<cli>` section.

  test_unit_suite_passes
    Command: dotnet test tests/SurrealDB.Client.Tests.Unit/ --verbosity normal
    Expected: "Test Run Successful. Total tests: N, Passed: N, Failed: 0"
    The QueryCompilerTests class must contribute 5 passing tests:
      - Compile_SimpleQuery_GeneratesBasicSelect
      - CompileDetailed_WithTableName_IncludesFromClause
      - CompileDetailed_ReturnsCompiledQuery
      - Compile_NullExpression_ThrowsArgumentNullException
      - CompileDetailed_NullExpression_ThrowsArgumentNullException

  test_solution_builds_clean
    Command: dotnet build SurrealDB.Client.sln
    Expected: "Build succeeded." with 0 Warning(s) and 0 Error(s)
    (NETSDK1057 preview-SDK informational messages are not counted as warnings)

  test_servicedefaults_in_solution
    Command: grep "ServiceDefaults" SurrealDB.Client.sln | wc -l
    Expected: >= 3

  test_scalar_version_correct
    Command: grep "Scalar.AspNetCore" samples/SurrealDB.Client.Sample.Api/SurrealDB.Client.Sample.Api.csproj
    Expected: Version="2.4.1"
</testing>

<implementation_order>
  Execute in this exact order. Each step must succeed before proceeding.

  Step 1 — Read QueryCompilerTests.cs and add the missing namespace import.
    Verify: dotnet build tests/SurrealDB.Client.Tests.Unit/ → 0 errors

  Step 2 — Update Scalar.AspNetCore version in Sample.Api.csproj.
    Verify: dotnet restore samples/SurrealDB.Client.Sample.Api/ → no NU1603

  Step 3 — Read SurrealDB.Client.sln. Add ServiceDefaults Project block,
    build config entries, and NestedProjects entry.
    Verify: dotnet build SurrealDB.Client.sln → 0 errors, 0 warnings

  Step 4 — Run full unit test suite.
    Verify: dotnet test tests/SurrealDB.Client.Tests.Unit/ → all pass

  Step 5 — Stage and commit all changed files (excluding .claude/settings.local.json).
    Verify: git status shows clean working tree for tracked files.
</implementation_order>

<quality>
  - Use the Edit tool for all file modifications (not sed, awk, or echo)
  - Use the Read tool before any Edit to confirm current file contents
  - No reformatting of unchanged lines
  - Preserve all existing comments and XML attributes
  - Commit message must be concise (under 72 chars for the subject line)
  - No TODOs or placeholder code
</quality>

<bootstrap>
  No setup required. The repo is already cloned and all SDKs are installed.
  Confirm working directory:
    pwd  → should end in /SurrealDB.Client

  Confirm .NET SDK:
    dotnet --version  → 10.0.200-preview (or higher) is fine

  Confirm current build state (expected: 1 error in unit tests, warnings in sln):
    dotnet build SurrealDB.Client.sln 2>&1 | grep -E "error|warning|succeeded"
</bootstrap>
```

> **Usage:** Paste the XML block into Claude Code. It is fully self-contained —
> four targeted fixes, verified by CLI commands, ending in a clean git commit.
> No questions need to be asked; all decisions are made upfront.
