# Documentation Structure

This document describes how documentation is organized for SurrealDB.Client.

## Directory Layout

```
docs/
├── DOCUMENTATION_STRUCTURE.md    ← This file
├── consumer/                      ← Consumer-facing documentation
│   ├── README.md                  ← Main entry point
│   ├── GETTING_STARTED.md         ← Installation and first steps
│   ├── API_REFERENCE.md           ← Complete API documentation
│   ├── EXAMPLES.md                ← Real-world code examples
│   ├── SECURITY.md                ← Security best practices
│   └── CHANGELOG.md               ← Version history and upgrade guide
│
├── internal/                      ← Internal documentation (planned)
│   └── [Architecture deep-dives, design decisions, etc.]
│
└── roles/                         ← Role-based guidance
    ├── README.md                  ← Role overview
    ├── architect/                 ← Architect guidance
    ├── developer/                 ← Developer guidance
    ├── 10x-developer/             ← Performance specialist guidance
    ├── tester/                    ← QA/Testing guidance
    ├── db-owner/                  ← Database owner guidance
    └── product-owner/             ← Product owner guidance
```

## Documentation by Audience

### For Package Consumers

Start with these documents (all in `/docs/consumer/`):

1. **README.md** - Understand what SurrealDB.Client is
2. **GETTING_STARTED.md** - Install and connect
3. **EXAMPLES.md** - See real-world code
4. **API_REFERENCE.md** - Learn the full API
5. **SECURITY.md** - Implement security best practices
6. **CHANGELOG.md** - Review version history

**Navigation**: The root repository `README.md` links to these consumer docs first.

### For Maintainers & Contributors

See internal documentation (in repository root and `/docs/roles/`):

- **ARCHITECTURE.md** - System design
- **DESIGN_DECISIONS.md** - Design rationale
- **BACKLOG.md** - Task list and implementation
- **DEVELOPER_ASSIGNMENT.md** - Work assignments
- **REVIEW_WORKFLOW.md** - Code review process
- **docs/roles/** - Role-specific guidance

## Documentation Standards

### Consumer Documentation

- **Audience**: .NET developers using the NuGet package
- **Assumptions**: Basic .NET knowledge, no SurrealDB knowledge
- **Style**: Clear, practical, example-driven
- **No emojis**: Professional tone throughout
- **Actionable**: Always include "how to" and "why"

### Internal Documentation

- **Audience**: Team members, contributors, architects
- **Assumptions**: Deep SurrealDB and .NET knowledge
- **Style**: Detailed, technical, comprehensive
- **Structure**: Clear sections, cross-references, decision rationale

### Code Examples

- Professional, runnable code
- Comments explaining key points
- Error handling included
- Best practices demonstrated
- No emojis in code examples

## File Organization

### Consumer Docs

Each document has a specific purpose:

| File | Purpose | Audience |
|------|---------|----------|
| README.md | Entry point, overview | New users |
| GETTING_STARTED.md | Installation, first steps | New developers |
| API_REFERENCE.md | Complete API docs | All developers |
| EXAMPLES.md | Code samples, patterns | Developers |
| SECURITY.md | Best practices, hardening | All developers |
| CHANGELOG.md | Version history, upgrades | All developers |

### Internal Docs

Placed in repository root for visibility to maintainers:

| File | Purpose | Audience |
|------|---------|----------|
| ARCHITECTURE.md | System design, roadmap | Architects, team |
| DESIGN_DECISIONS.md | Design rationale | Team, reviewers |
| BACKLOG.md | Task list, details | Developers |
| DEVELOPER_ASSIGNMENT.md | Work instructions | Developers |
| REVIEW_WORKFLOW.md | Review process | Reviewers |
| docs/roles/ | Role-specific guidance | Team members |

## Cross-Referencing

### From Consumer Docs

- Link to other consumer docs using relative paths: `[API_REFERENCE.md](API_REFERENCE.md)`
- Link to external SurrealDB docs: `[surrealdb.com/docs](https://surrealdb.com/docs)`
- Don't link to internal docs from consumer docs

### From Internal Docs

- Can reference consumer docs: `[docs/consumer/API_REFERENCE.md](docs/consumer/API_REFERENCE.md)`
- Can reference internal docs using relative paths: `[ARCHITECTURE.md](ARCHITECTURE.md)`

## Documentation Updates

### When Adding a Feature

1. **Update** docs/consumer/API_REFERENCE.md with new API
2. **Add examples** to docs/consumer/EXAMPLES.md
3. **Note in** docs/consumer/CHANGELOG.md (Features section)
4. **Update** internal docs (ARCHITECTURE.md, DESIGN_DECISIONS.md)

### When Fixing a Bug

1. **Note in** docs/consumer/CHANGELOG.md (Bug Fixes section)
2. **Mention in** docs/consumer/EXAMPLES.md if affects patterns
3. **Update** docs/consumer/SECURITY.md if security-related

### When Breaking Something

1. **Document in** docs/consumer/CHANGELOG.md (Breaking Changes)
2. **Add upgrade guide** in CHANGELOG.md
3. **Update affected docs** (API_REFERENCE, EXAMPLES, etc.)
4. **Note version requirement** in SECURITY.md if needed

## Structure Rationale

### Consumer Docs in `/docs/consumer/`

- Clear separation from internal documentation
- Easy to find from root README
- Can be extracted for distribution
- Focused on user experience

### Internal Docs in Root

- Visible to contributors
- Easy navigation during development
- Version-controlled with code
- Historical record of decisions

### Role-Based Docs in `/docs/roles/`

- Team members find their specific guidance
- Prevents information overload
- Focused on responsibilities
- Easy onboarding reference

## Navigation Flow

### New User Discovery

```
README.md (root)
    ↓
Point to /docs/consumer/
    ↓
README.md (consumer)
    ↓
Choose path:
├─ Getting started → GETTING_STARTED.md
├─ Code examples → EXAMPLES.md
├─ Full API → API_REFERENCE.md
├─ Security → SECURITY.md
└─ Releases → CHANGELOG.md
```

### Team Member Onboarding

```
README.md (root)
    ↓
Internal Documentation section
    ↓
Choose role:
├─ Developer → docs/roles/developer/
├─ Architect → docs/roles/architect/
├─ Tester → docs/roles/tester/
└─ [etc]
    ↓
Start with README.md in role directory
```

## Maintenance Guidelines

### Keep Consumer Docs Fresh

- Review with each release
- Update examples with new features
- Fix outdated information quickly
- Ensure links work (especially external)

### Keep Internal Docs Accurate

- Update DESIGN_DECISIONS.md when making changes
- Keep BACKLOG.md in sync with actual work
- Review ARCHITECTURE.md quarterly
- Update role docs as responsibilities change

### Review Process

1. Check cross-references work
2. Verify code examples are correct
3. Ensure consistency across docs
4. Validate claims and assertions
5. Test any provided code

## Related Information

- **NuGet Package**: https://www.nuget.org/packages/SurrealDB.Client
- **GitHub Repository**: https://github.com/your-org/SurrealDB.Client
- **SurrealDB Docs**: https://surrealdb.com/docs
- **Issue Tracker**: https://github.com/your-org/SurrealDB.Client/issues
