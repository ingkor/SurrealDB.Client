# SurrealDB.Client v1.0.0 - Publication Action Checklist

**Start Date**: _______________
**Target Completion**: _______________
**Status**: 🔴 NOT READY

---

## PHASE 1: FIX CRITICAL BLOCKERS (HOURS 1-6)

### 1.1 Fix 14 Failing Unit Tests (2-4 hours)
- [ ] Run test suite with verbose output
  ```bash
  cd /c/Projects/SurrealDB.Client
  dotnet test --verbosity detailed 2>&1 | tee test-failures.log
  ```

- [ ] Review test failure report: `test-failures.log`
  - Number of failures: _____
  - Primary test classes affected: _______________

- [ ] Analyze each failing test
  - [ ] ResourceManagementTests.F2_ConnectAsync_WhenConnectAsyncThrows_ReleasesConnection
  - [ ] ResourceManagementTests.F2_ConnectAsync_WhenSendAsyncThrows_ReleasesConnection
  - [ ] (list other failures below)
    - [ ] _______________
    - [ ] _______________
    - [ ] _______________

- [ ] Fix root causes in implementation
  - Changes made: _______________________________________________

- [ ] Verify fixes with multiple test runs
  ```bash
  dotnet test --repeat-each 3
  ```

- [ ] **Acceptance**: All 190 tests passing
  ```bash
  dotnet test | tail -5
  # Should show: "Failed: 0, Passed: 190"
  ```
  Status: ✅ COMPLETE

---

### 1.2 Resolve System.Text.Json Vulnerabilities (30 minutes)
- [ ] Check current vulnerability status
  ```bash
  dotnet list package --vulnerable
  ```
  Current vulnerabilities found:
  ```

  ```

- [ ] Choose fix strategy
  - [ ] **Strategy A (Recommended)**: Remove explicit System.Text.Json reference
    - System.Text.Json is included in .NET SDK
    - No reason to explicitly reference it in Directory.Build.props

  - [ ] **Strategy B**: Update to latest System.Text.Json version
    - Use if there's a specific need for explicit reference

- [ ] **If Strategy A** - Remove package reference:
  - [ ] Open: `/c/Projects/SurrealDB.Client/Directory.Build.props`
  - [ ] Delete lines 43-45 (System.Text.Json ItemGroup):
    ```xml
    <!-- DELETE THESE LINES -->
    <ItemGroup>
      <PackageReference Include="System.Text.Json" Version="8.0.0" />
    </ItemGroup>
    ```
  - [ ] Save file

- [ ] **If Strategy B** - Update version:
  - [ ] Edit line 44 to use latest stable:
    ```xml
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
    ```

- [ ] Rebuild and verify
  ```bash
  dotnet clean
  dotnet restore
  dotnet build --configuration Release
  dotnet list package --vulnerable
  ```

- [ ] **Acceptance**: "No vulnerable packages detected"
  Status: ✅ COMPLETE

---

### 1.3 Add Missing XML Documentation (2-3 hours)
- [ ] Identify all missing documentation
  ```bash
  dotnet build 2>&1 | grep "CS1591" > missing-docs.log
  cat missing-docs.log | wc -l
  ```
  Total missing docs: _____

- [ ] Review missing documentation list
  - Files affected: _________________________________________________

- [ ] Add XML comments to each public member
  - [ ] All exception classes and constructors
    - [ ] AuthenticationException (all constructors)
    - [ ] ConnectionException (all constructors)
    - [ ] QueryException (all constructors)
    - [ ] SerializationException (all constructors)
    - [ ] SurrealDbException (all constructors)
    - [ ] TimeoutException (all constructors)
    - [ ] ValidationException (all constructors)

  - [ ] Authentication providers
    - [ ] BasicAuthenticationProvider.AuthenticateAsync
    - [ ] TokenAuthenticationProvider.AuthenticateAsync
    - [ ] IAuthenticationProvider interface members

  - [ ] Other public members (list from missing-docs.log)
    - [ ] _______________
    - [ ] _______________
    - [ ] _______________

- [ ] Example documentation comment:
  ```csharp
  /// <summary>
  /// Thrown when authentication fails.
  /// </summary>
  /// <remarks>
  /// This exception indicates that the provided credentials were invalid
  /// or the authentication attempt failed for security reasons.
  /// </remarks>
  public class AuthenticationException : SurrealDbException { }
  ```

- [ ] Verify all docs added
  ```bash
  dotnet build 2>&1 | grep "CS1591" | wc -l
  ```
  Should be: 0

- [ ] **Acceptance**: 0 CS1591 warnings
  Status: ✅ COMPLETE

---

## PHASE 2: FIX REMAINING WARNINGS (30 MINUTES)

### 2.1 Fix Null Reference Warning (15 minutes)
- [ ] Locate issue
  - File: `src/SurrealDB.Client/SurrealDbClient.cs`
  - Line: 95
  - Warning: CS8604 - Possible null reference for `identifier`

- [ ] Review code at line 95
  - Current code:
    ```csharp

    ```

- [ ] Apply fix (choose one)
  - [ ] **Option 1**: Add null check
    ```csharp
    if (identifier == null)
        throw new ArgumentNullException(nameof(identifier));
    ```

  - [ ] **Option 2**: Update parameter type annotation
    - Review method signature
    - Add proper nullable annotation if needed

- [ ] Verify fix
  ```bash
  dotnet build 2>&1 | grep "SurrealDbClient.cs" | grep "CS8604"
  ```
  Should show: 0 matches

- [ ] **Acceptance**: 0 CS8604 warnings
  Status: ✅ COMPLETE

---

### 2.2 Fix XML Comment Reference Warnings (15 minutes)
- [ ] Locate issues
  - File: `src/SurrealDB.Client/ISurrealDbClient.cs`
  - Warning: CS1574 - XML comment cref cannot be resolved
  - Count: 3 broken references

- [ ] Find all broken references
  ```bash
  dotnet build 2>&1 | grep "CS1574"
  ```

- [ ] Fix each broken reference (example)
  - [ ] Reference 1: _______________
    - Find: `cref="ConnectionException"`
    - Replace: `cref="SurrealDB.Client.Exceptions.ConnectionException"`

  - [ ] Reference 2: _______________
  - [ ] Reference 3: _______________

- [ ] Verify fix
  ```bash
  dotnet build 2>&1 | grep "CS1574" | wc -l
  ```
  Should be: 0

- [ ] **Acceptance**: 0 CS1574 warnings
  Status: ✅ COMPLETE

---

### 2.3 Final Warning Verification (5 minutes)
- [ ] Build and check for remaining warnings
  ```bash
  dotnet clean
  dotnet build --configuration Release 2>&1 | grep "warning"
  ```

- [ ] Count warnings: _____
  - **Goal**: 0 warnings

- [ ] If warnings remain:
  - [ ] Document warning: _______________
  - [ ] Evaluate if necessary
  - [ ] Fix or suppress appropriately

- [ ] **Acceptance**: 0 compiler warnings (build clean)
  Status: ✅ COMPLETE

---

## PHASE 3: QUICK CONFIGURATION UPDATES (10 MINUTES)

### 3.1 Update Version to 1.0.0
- [ ] Open: `/c/Projects/SurrealDB.Client/Directory.Build.props`

- [ ] Find lines 15-16 (current version):
  ```xml
  <VersionPrefix>0.1.0</VersionPrefix>
  <VersionSuffix>beta-1</VersionSuffix>
  ```

- [ ] Update to:
  ```xml
  <VersionPrefix>1.0.0</VersionPrefix>
  <VersionSuffix></VersionSuffix>
  ```

- [ ] Save file

- [ ] Verify version
  ```bash
  dotnet pack --configuration Release --no-build | head -20
  # Should show: "Packing SurrealDB.Client 1.0.0"
  ```

- [ ] **Acceptance**: Package version shows 1.0.0
  Status: ✅ COMPLETE

---

### 3.2 Optional: Enhance Package Metadata
- [ ] Add enhanced tags (optional but recommended)
  - [ ] Open: `Directory.Build.props` Line 12
  - [ ] Current: `surrealdb;database;client;orm;ef-core;query-composition`
  - [ ] Add additional tags: `async;connection-pooling;websocket;http;type-safe`
  - [ ] Updated line:
    ```xml
    <PackageTags>surrealdb;database;client;orm;ef-core;query-composition;async;connection-pooling;websocket;http;type-safe</PackageTags>
    ```
  - [ ] Status: ✅ (Optional)

- [ ] Add comprehensive release notes (optional but recommended)
  - [ ] Open: `Directory.Build.props` after Line 31
  - [ ] Add new property:
    ```xml
    <PackageReleaseNotes>
    ## SurrealDB.Client 1.0.0 - Production Release

    All P0 security vulnerabilities fixed. Ready for production deployment.

    ### Key Features
    - Complete CRUD API
    - Connection pooling with health checks
    - HTTP and WebSocket protocol support
    - Comprehensive exception handling
    - Parameter binding (SQL injection prevention)

    ### Critical Fixes
    - DisposeAsync deadlock (P0.1)
    - GetStatistics race condition (P0.2)
    - WebSocket response truncation (P0.3)
    - Error message exposure (P0-1)
    - Connection string credentials (P0-2)

    ### Requirements
    - .NET 8.0 or 9.0
    - SurrealDB 3.0+

    See https://github.com/surrealdb/surrealdb.net for documentation.
    </PackageReleaseNotes>
    ```
  - [ ] Status: ✅ (Optional)

---

## PHASE 4: COMPREHENSIVE TESTING & VALIDATION (1-2 HOURS)

### 4.1 Complete Build Validation
- [ ] Run comprehensive build
  ```bash
  cd /c/Projects/SurrealDB.Client
  dotnet clean
  dotnet build --configuration Release
  ```

- [ ] Build output shows:
  - [ ] 0 errors
  - [ ] 0 warnings
  - [ ] Successfully built in ~[time]

- [ ] **Acceptance**: Build succeeds with no issues
  Status: ✅ COMPLETE

---

### 4.2 Complete Test Validation
- [ ] Run all tests with stability check
  ```bash
  dotnet test --configuration Release --repeat-each 3
  ```

- [ ] Test results:
  - [ ] All tests passed on run 1: ______/190
  - [ ] All tests passed on run 2: ______/190
  - [ ] All tests passed on run 3: ______/190

- [ ] **Acceptance**: 190/190 tests passing (consistently)
  Status: ✅ COMPLETE

---

### 4.3 Vulnerability Final Check
- [ ] Verify no vulnerabilities
  ```bash
  dotnet list package --vulnerable
  ```

- [ ] Output should show: "No vulnerable packages detected"

- [ ] **Acceptance**: No vulnerabilities detected
  Status: ✅ COMPLETE

---

### 4.4 Package Creation Test
- [ ] Create NuGet package
  ```bash
  dotnet pack --configuration Release
  ```

- [ ] Verify package files created:
  - [ ] `src/SurrealDB.Client/bin/Release/SurrealDB.Client.1.0.0.nupkg` exists
  - [ ] `src/SurrealDB.Client/bin/Release/SurrealDB.Client.1.0.0.snupkg` exists

- [ ] Check file sizes (should be reasonable):
  - [ ] .nupkg size: __________ KB (typical: 50-200 KB)
  - [ ] .snupkg size: __________ KB (typical: 100-500 KB)

- [ ] **Acceptance**: Package files created successfully
  Status: ✅ COMPLETE

---

### 4.5 Local Package Installation Test
- [ ] Create test project
  ```bash
  dotnet new console -n TestSurrealDBInstall
  cd TestSurrealDBInstall
  ```

- [ ] Add local NuGet source
  ```bash
  dotnet nuget add source /c/Projects/SurrealDB.Client/src/SurrealDB.Client/bin/Release -n local
  ```

- [ ] Install package
  ```bash
  dotnet add package SurrealDB.Client --version 1.0.0
  ```

- [ ] Verify installation
  - [ ] Package appears in .csproj
  - [ ] Package restores successfully
  - [ ] IntelliSense works in IDE (XML docs appear)

- [ ] Test basic usage
  ```csharp
  using SurrealDB.Client;

  var client = new SurrealDbClient("surreal://localhost:8000");
  // Intellisense should show all public members with full documentation
  ```

- [ ] **Acceptance**: Package installs and works locally
  Status: ✅ COMPLETE

---

## PHASE 5: PREPARE FOR PUBLICATION (1-2 HOURS)

### 5.1 Set Up NuGet.org Account (if needed)
- [ ] Check if account exists
  - Already have account: ☐ YES ☐ NO
  - Account username: _______________

- [ ] If NO account:
  - [ ] Go to: https://www.nuget.org/users/account/Register
  - [ ] Fill in registration form
  - [ ] Confirm email address
  - [ ] Account created: _______________

- [ ] **Status**: Account ready
  Status: ✅ COMPLETE

---

### 5.2 Create NuGet API Key
- [ ] Login to NuGet.org: https://www.nuget.org

- [ ] Navigate to Account Settings → API Keys

- [ ] Create new API key
  - [ ] Name: `SurrealDB.Client-1.0.0-Release`
  - [ ] Pattern: Select "All" (for initial release, can scope later)
  - [ ] Expiry: Set to 90 days (or as required)

- [ ] Copy API key (shown only once)
  - API Key: ________________________ (store securely)

- [ ] Store securely (choose one method)
  - [ ] **Option A**: Environment variable
    ```powershell
    $env:NUGET_API_KEY = "sk_live_..."  # PowerShell
    ```

  - [ ] **Option B**: Local dotnet config
    ```bash
    dotnet nuget update source nuget.org \
      -u __USERNAME__ \
      -p YOUR_API_KEY \
      --store-password-in-clear-text
    ```

  - [ ] **Option C**: Secure password manager
    - Stored in: _______________

- [ ] **CRITICAL**: Never commit API key to version control!
  - [ ] Verify .gitignore excludes secrets
  - [ ] Never paste key in chat/email/docs

- [ ] **Status**: API key ready and secured
  Status: ✅ COMPLETE

---

### 5.3 Prepare Git Release Tag
- [ ] Create annotated tag for v1.0.0
  ```bash
  cd /c/Projects/SurrealDB.Client
  git tag -a v1.0.0 -m "Release v1.0.0 - Production Ready"
  ```

- [ ] Verify tag created
  ```bash
  git tag -l v1.0.0
  git show v1.0.0
  ```

- [ ] Push tag to remote
  ```bash
  git push origin v1.0.0
  ```

- [ ] Verify on GitHub
  - [ ] Go to: https://github.com/surrealdb/surrealdb.net/releases
  - [ ] Tag `v1.0.0` appears in list

- [ ] **Status**: Git tag created and pushed
  Status: ✅ COMPLETE

---

### 5.4 Prepare GitHub Release Notes
- [ ] Create release notes file or draft
  - Source: `/c/Projects/SurrealDB.Client/docs/consumer/CHANGELOG.md`
  - Version section: v1.0.0

- [ ] GitHub release template:
  ```markdown
  # SurrealDB.Client 1.0.0 - Production Release

  All P0 security vulnerabilities fixed. Ready for production deployment.

  ## New Features
  - Complete CRUD API
  - Connection pooling with health checks
  - HTTP and WebSocket protocol support
  - Comprehensive exception handling
  - Parameter binding (SQL injection prevention)
  - Async/await-first API design

  ## Critical Security Fixes
  - DisposeAsync deadlock (P0.1)
  - GetStatistics race condition (P0.2)
  - WebSocket response truncation (P0.3)
  - Error message exposure (P0-1)
  - Connection string credentials (P0-2)
  - Additional security hardening (P1.1-P1.6)

  ## Requirements
  - .NET 8.0 or 9.0
  - SurrealDB 3.0+

  ## Upgrade from Beta
  See [UPGRADE_GUIDE.md](https://github.com/surrealdb/surrealdb.net/blob/main/docs/consumer/UPGRADE_GUIDE.md) for migration details.

  ## Installation
  ```bash
  dotnet add package SurrealDB.Client
  ```

  ---

  See [CHANGELOG.md](https://github.com/surrealdb/surrealdb.net/blob/main/docs/consumer/CHANGELOG.md) for detailed release notes.
  ```

- [ ] Release notes prepared and saved

- [ ] **Status**: GitHub release notes ready
  Status: ✅ COMPLETE

---

## PHASE 6: PUBLICATION (15 MINUTES)

### 6.1 Publish to NuGet.org
- [ ] Pre-publication checklist
  - [ ] All tests passing: ✅
  - [ ] No warnings: ✅
  - [ ] No vulnerabilities: ✅
  - [ ] Package created: ✅
  - [ ] API key secured: ✅

- [ ] Verify package files exist
  ```bash
  ls -lh src/SurrealDB.Client/bin/Release/SurrealDB.Client.1.0.0.*
  ```
  Output:
  ```

  ```

- [ ] Push .nupkg to NuGet.org
  ```bash
  dotnet nuget push \
    src/SurrealDB.Client/bin/Release/SurrealDB.Client.1.0.0.nupkg \
    --api-key $env:NUGET_API_KEY \
    --source https://api.nuget.org/v3/index.json
  ```

- [ ] Verify push successful
  - [ ] Output shows: "Your package was pushed."
  - [ ] No errors reported

- [ ] Push .snupkg symbols (optional but recommended)
  ```bash
  dotnet nuget push \
    src/SurrealDB.Client/bin/Release/SurrealDB.Client.1.0.0.snupkg \
    --api-key $env:NUGET_API_KEY \
    --source https://api.nuget.org/v3/index.json
  ```

- [ ] **Status**: Package published to NuGet.org
  Status: ✅ COMPLETE

---

### 6.2 Create GitHub Release
- [ ] Go to: https://github.com/surrealdb/surrealdb.net/releases/new

- [ ] Fill in release form:
  - [ ] Choose tag: v1.0.0
  - [ ] Release title: "SurrealDB.Client 1.0.0 - Production Release"
  - [ ] Description: (paste prepared notes from 5.4)
  - [ ] Attach binaries: (optional - not needed since on NuGet)
    - [ ] SurrealDB.Client.1.0.0.nupkg
    - [ ] SurrealDB.Client.1.0.0.snupkg
  - [ ] Mark as latest release: ✅

- [ ] Publish release

- [ ] Verify release published
  - [ ] Goes to: https://github.com/surrealdb/surrealdb.net/releases/tag/v1.0.0
  - [ ] Release notes visible
  - [ ] Date correct: _______________

- [ ] **Status**: GitHub release created and published
  Status: ✅ COMPLETE

---

## PHASE 7: VERIFICATION & ANNOUNCEMENT (30 MINUTES)

### 7.1 Verify Package on NuGet.org
- [ ] Wait 5-10 minutes for NuGet indexing
  - [ ] Time waited: _____ minutes

- [ ] Search on NuGet.org
  - [ ] Go to: https://www.nuget.org/packages/SurrealDB.Client
  - [ ] Version 1.0.0 appears: ☐ YES ☐ NO

- [ ] Verify package metadata
  - [ ] Title: SurrealDB Client ✅
  - [ ] Description: Complete and accurate ✅
  - [ ] Version: 1.0.0 ✅
  - [ ] Author: SurrealDB Contributors ✅
  - [ ] License: MIT ✅
  - [ ] Repository: https://github.com/surrealdb/surrealdb.net ✅
  - [ ] Tags visible: ✅
  - [ ] README visible: ✅
  - [ ] Dependencies: None (System.Text.Json removed) ✅

- [ ] Download count refreshing
  - [ ] Initially: 0 (or increasing) ✅

- [ ] Install test
  ```bash
  dotnet new console -n FinalTest
  cd FinalTest
  dotnet add package SurrealDB.Client --version 1.0.0
  ```
  - [ ] Package installs successfully ✅
  - [ ] IntelliSense works ✅
  - [ ] All public APIs accessible ✅

- [ ] **Status**: Package verified on NuGet.org
  Status: ✅ COMPLETE

---

### 7.2 Announce Release
- [ ] Post GitHub Discussions
  - [ ] Category: Announcements (if exists)
  - [ ] Title: "SurrealDB.Client 1.0.0 Released"
  - [ ] Content: Link to release, key highlights, upgrade info
  - [ ] Posted: ☐ YES ☐ NO

- [ ] Update project documentation
  - [ ] Update README.md if needed for visibility
  - [ ] Verify links point to correct version
  - [ ] Check consumer docs are accessible

- [ ] Social/Community (optional)
  - [ ] Post in SurrealDB Discord: ☐ YES ☐ NO
  - [ ] Post on social media: ☐ YES ☐ NO
  - [ ] Community forum: ☐ YES ☐ NO

- [ ] **Status**: Release announced
  Status: ✅ COMPLETE

---

## FINAL SIGN-OFF

### Quality Assurance Sign-Off
- [ ] QA Lead: ______________________ Date: __________
  - All tests passing ✅
  - No known defects
  - Ready for production

### Security Review Sign-Off
- [ ] Security Lead: ______________________ Date: __________
  - All P0 vulnerabilities fixed ✅
  - No new vulnerabilities introduced
  - Security best practices followed

### Product Owner Sign-Off
- [ ] Product Owner: ______________________ Date: __________
  - Features complete ✅
  - Documentation complete ✅
  - Ready to publish

### Release Manager Sign-Off
- [ ] Release Manager: ______________________ Date: __________
  - All processes followed ✅
  - Package on NuGet.org ✅
  - GitHub release created ✅
  - Release successful ✅

---

## PUBLICATION SUMMARY

**Release Version**: 1.0.0
**Release Date**: _______________
**Package URL**: https://www.nuget.org/packages/SurrealDB.Client/1.0.0
**GitHub Release**: https://github.com/surrealdb/surrealdb.net/releases/tag/v1.0.0
**Documentation**: https://github.com/surrealdb/surrealdb.net/docs/consumer/

### Metrics
- **Tests**: 190/190 passing
- **Compiler Warnings**: 0
- **Vulnerabilities**: 0
- **Target Frameworks**: .NET 8.0, 9.0
- **Download Count**: (initial) _______

### Known Limitations (for next version)
- [ ] (List any deferred features or future improvements)
- [ ]
- [ ]

---

## POST-RELEASE MONITORING (Week 1)

- [ ] Monitor NuGet.org download count
  - Day 1: _______ downloads
  - Day 3: _______ downloads
  - Day 7: _______ downloads

- [ ] Check GitHub Issues for bug reports
  - Issues opened: _______
  - Severity: _______________

- [ ] Monitor user feedback
  - Comments/questions: _______________
  - Action items: _______________

- [ ] Prepare hotfix branch if needed
  - [ ] Branch: release/1.0.1-hotfix
  - [ ] Status: Ready/Not needed

- [ ] Schedule next release planning
  - [ ] 1.0.1 hotfix (if issues found)
  - [ ] 1.1.0 features (planned: ____________)

---

**Document Status**: Action Checklist in Progress
**Last Updated**: [current date]
**Next Review**: After publication complete

