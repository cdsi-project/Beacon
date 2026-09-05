# CDSI Beacon — Historical Project Initialization Guide

> Project: CDSI Beacon (repository: `cdsi-agent`)
> Target Platform: Windows
> Language: C#
> Runtime: .NET 10
> UI Framework: WinForms
> IDE: Visual Studio 2026
> Intended Consumer: Codex / Engineering
> Status: Historical initialization record; not a current implementation checklist

> **Current released baseline (v0.200):** The solution has already been initialized as `CDSI.Agent.slnx`, with the four root projects `CDSI.Agent.Core`, `CDSI.Agent.Application`, `CDSI.Agent.Infrastructure`, and `CDSI.Agent.WinForms`, plus four test projects under `tests`. Object-storage backup, OpenWeb publishing, project management, database snapshots, and the current WinForms UI have been implemented. Statements below such as “Do not implement OSS yet,” the original milestone numbering, suggested folder layout, and three-test-project list describe the repository's starting plan only. Use the root [README](../README.md), [AGENTS.md](../AGENTS.md), and [project/cloud model](CDSI_BEACON_PROJECT_CLOUD_MODEL.md) for current work.

---

# 1. Objective

Initialize the `cdsi-agent` repository as a clean, layered .NET solution suitable for the Windows MVP.

The repository must NOT start as one large WinForms application.

The initial solution should separate:

- domain logic
- application orchestration
- infrastructure integrations
- WinForms presentation

The architecture must allow future addition of:

- Windows background service
- CLI
- object storage integrations
- local AI / embedding modules
- CDSI Server API integration
- macOS/Linux clients with different UI/runtime layers

without rewriting core asset logic.

---

# 2. Visual Studio Project Template

Do NOT create:

```text
Windows Forms App (.NET Framework)
```

Use modern .NET templates only.

Create the solution first, then add individual projects.

---

# 3. Solution

Create a blank solution:

```text
Template:
Blank Solution

Solution Name:
CDSI.Agent
```

Expected solution file:

```text
CDSI.Agent.sln
```

Repository root:

```text
cdsi-agent/
├── AGENTS.md
├── CDSI.Agent.sln
├── docs/
├── src/
└── tests/
```

---

# 4. Projects

Create the following four projects.

## 4.1 CDSI.Agent.Core

Template:

```text
Class Library
```

Language:

```text
C#
```

Framework:

```text
.NET 10
```

Project name:

```text
CDSI.Agent.Core
```

Purpose:

- domain entities
- domain interfaces
- domain rules
- asset identity
- asset locations
- projects
- relationships
- scan abstractions
- storage abstractions
- job abstractions

This project must not depend on:

- WinForms
- SQLite
- Aliyun OSS SDK
- HTTP client implementation
- Windows-specific APIs
- external AI providers

## 4.2 CDSI.Agent.Application

Template:

```text
Class Library
```

Language:

```text
C#
```

Framework:

```text
.NET 10
```

Project name:

```text
CDSI.Agent.Application
```

Purpose:

- application use cases
- orchestration
- scan workflows
- asset registration
- duplicate detection workflows
- inbox workflows
- upload workflows
- verification workflows

Examples:

```text
StartScan
RegisterAsset
CalculateFingerprint
DetectDuplicates
AssignProject
CreateUploadIntent
UploadAsset
VerifyAsset
```

This project coordinates domain abstractions.

## 4.3 CDSI.Agent.Infrastructure

Template:

```text
Class Library
```

Language:

```text
C#
```

Framework:

```text
.NET 10
```

Project name:

```text
CDSI.Agent.Infrastructure
```

Purpose:

- SQLite persistence
- local filesystem access
- hashing implementation
- metadata extraction
- CDSI Server API client
- Aliyun OSS integration
- S3-compatible storage
- Windows platform integrations
- logging implementations

Initial MVP should only implement the infrastructure required for:

```text
filesystem scan
SQLite
SHA-256
basic metadata
```

Do not implement OSS in the first milestone unless explicitly requested.

## 4.4 CDSI.Agent.WinForms

Template:

```text
Windows Forms App
```

Important:

Use:

```text
Windows Forms App
```

Do NOT use:

```text
Windows Forms App (.NET Framework)
```

Language:

```text
C#
```

Framework:

```text
.NET 10
```

Project name:

```text
CDSI.Agent.WinForms
```

Purpose:

- desktop UI
- directory selection
- scan controls
- asset list
- duplicate list
- progress display
- inbox UI
- settings UI

WinForms must contain presentation logic only.

Do not place:

```text
filesystem scanning
hashing
SQLite queries
asset classification
OSS SDK calls
LLM calls
```

directly inside Form event handlers.

---

# 5. Required Project References

Configure the following references.

```text
CDSI.Agent.Application
    ↓
CDSI.Agent.Core
```

```text
CDSI.Agent.Infrastructure
    ↓
CDSI.Agent.Core
```

```text
CDSI.Agent.WinForms
    ↓
CDSI.Agent.Application
    ↓
CDSI.Agent.Core
```

WinForms may also reference:

```text
CDSI.Agent.Infrastructure
```

at the composition root for dependency registration.

Conceptually:

```text
                  ┌────────────────────┐
                  │ CDSI.Agent.WinForms│
                  └──────────┬─────────┘
                             │
                    ┌────────▼────────┐
                    │   Application   │
                    └────────┬────────┘
                             │
                         ┌───▼───┐
                         │ Core  │
                         └───▲───┘
                             │
                  ┌──────────┴──────────┐
                  │   Infrastructure    │
                  └─────────────────────┘
```

Core must remain the lowest-level domain project.

---

# 6. Suggested Folder Layout

After project creation:

```text
cdsi-agent/
│
├── AGENTS.md
├── CDSI.Agent.sln
│
├── docs/
│   ├── CDSI_AGENT_WINDOWS_MVP.md
│   └── PROJECT_INITIALIZATION.md
│
├── src/
│   ├── CDSI.Agent.Core/
│   ├── CDSI.Agent.Application/
│   ├── CDSI.Agent.Infrastructure/
│   └── CDSI.Agent.WinForms/
│
└── tests/
    ├── CDSI.Agent.Core.Tests/
    ├── CDSI.Agent.Infrastructure.Tests/
    └── CDSI.Agent.IntegrationTests/
```

Adapt to the actual repository if necessary.

Do not reorganize working files merely to match this document.

---

# 7. Test Projects

Create three test projects.

## 7.1 CDSI.Agent.Core.Tests

Template:

```text
xUnit Test Project
```

Framework:

```text
.NET 10
```

Reference:

```text
CDSI.Agent.Core
```

Initial tests:

- Asset identity
- AssetLocation behavior
- duplicate relationship rules
- scan domain rules

## 7.2 CDSI.Agent.Infrastructure.Tests

Template:

```text
xUnit Test Project
```

Framework:

```text
.NET 10
```

References:

```text
CDSI.Agent.Core
CDSI.Agent.Infrastructure
```

Initial tests:

- SHA-256 hashing
- file metadata
- SQLite persistence
- filesystem scanner
- ignore rules

## 7.3 CDSI.Agent.IntegrationTests

Template:

```text
xUnit Test Project
```

Framework:

```text
.NET 10
```

Purpose:

End-to-end local tests using temporary directories.

Never use the developer's real Desktop/Documents directories in automated tests.

---

# 8. Initial NuGet Packages

Keep dependencies minimal.

Potential initial packages:

```text
Microsoft.Data.Sqlite
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Logging
```

Optional:

```text
Serilog
Serilog.Extensions.Logging
```

Do NOT add:

- AI SDKs
- Aliyun OSS SDK
- S3 SDK
- PDF parsing libraries
- OpenXML libraries
- clustering libraries

during project initialization unless required by the current milestone.

Add dependencies only when the related module begins implementation.

---

# 9. SQLite Location

Use a per-user application directory.

Recommended:

```text
%LOCALAPPDATA%\CDSI\
```

Initial structure:

```text
%LOCALAPPDATA%\CDSI\
├── cdsi.db
├── config.json
├── logs\                  startup/emergency fallback only
├── cache\
└── thumbnails\
```

Do not write application state into:

```text
Program Files
repository directory
application binary directory
```

After the managed workspace is configured, normal runtime logs are written to
`<CDSI workspace>\System\Logs\`. `%LOCALAPPDATA%\CDSI\Logs\` remains available only
for startup diagnostics when the workspace cannot yet be resolved or accessed.

---

# 10. Initial Domain Models

Create minimal models first.

## Asset

Suggested initial fields:

```text
Id
OriginalFilename
MimeType
Extension
Size
Sha256
CreatedAt
ModifiedAt
DiscoveredAt
Status
```

Important:

```text
Asset.Id != File Path
```

An Asset is a logical identity.

## AssetLocation

Suggested fields:

```text
Id
AssetId
LocationType
DeviceId
Path
StorageId
ObjectKey
Status
LastVerifiedAt
```

Initial MVP only requires:

```text
LocationType = Local
Path
```

Do not make path part of Asset identity.

## ScanRoot

Suggested fields:

```text
Id
Path
Enabled
CreatedAt
LastScannedAt
```

## ScanJob

Suggested fields:

```text
Id
Status
StartedAt
FinishedAt
FilesDiscovered
FilesProcessed
Errors
```

---

# 11. First WinForms Screen

Do not build the full UI immediately.

Initial form only needs:

```text
Scan Roots
Scan Button
Progress
Asset Grid
Status Bar
```

Do not optimize visual styling at this stage.

---

# 12. UI Thread Rule

Never run filesystem scanning directly on the WinForms UI thread.

Preferred flow:

```text
WinForms
    ↓
Application Service
    ↓
Background Job
    ↓
Scanner
    ↓
SQLite
    ↓
Progress Event
    ↓
WinForms
```

Use:

```text
Task
CancellationToken
IProgress<T>
System.Threading.Channels
```

where appropriate.

---

# 13. Test Asset Directory

Before development testing, create a dedicated directory.

Example:

```text
D:\CDSI-TestAssets\
```

Recommended content:

```text
D:\CDSI-TestAssets\
├── Articles\
├── Documents\
├── Images\
├── Videos\
├── Audio\
├── Duplicates\
├── Versions\
└── Nested\
```

Include sample:

```text
.md
.txt
.pdf
.docx
.pptx
.xlsx
.jpg
.png
.mp3
.mp4
.zip
```

Also include:

- exact duplicate files
- same filename with different content
- very large file
- nested directories
- Unicode filenames
- Chinese filenames
- locked/unreadable file if practical
- empty file
- files with no extension

Do not test against the entire user profile initially.

---

# 14. Milestone 0.1

Codex should implement only the following:

- [x] create solution structure
- [x] create four source projects
- [x] create three test projects
- [x] configure references
- [x] add SQLite dependency
- [x] create Asset model
- [x] create AssetLocation model
- [x] create ScanRoot model
- [x] create ScanJob model
- [x] initialize SQLite database
- [x] allow WinForms user to select a directory
- [x] recursively scan that directory
- [x] persist files as assets/locations
- [x] display indexed files
- [x] report progress
- [x] handle errors without terminating the scan
- [x] do not modify user files

---

# 15. Milestone 0.2

After Milestone 0.1 is stable:

- [x] calculate SHA-256
- [x] cache hashes
- [x] avoid rehashing unchanged files
- [x] detect exact duplicates
- [x] create duplicate groups
- [x] display duplicate groups in UI
- [x] support rescans
- [x] ensure rescans are idempotent

---

# 15.1 Milestone 0.3

After exact duplicate detection is stable:

- [x] add an extractor registry
- [x] extract common image, audio, and video metadata locally
- [x] persist versioned metadata independently from assets
- [x] invalidate metadata when the source file changes
- [x] isolate malformed or unsupported files
- [x] display media summaries in the desktop UI
- [x] keep source files read-only

---

# 16. Do Not Implement Yet

Do NOT implement during initial project setup:

```text
Embedding
LLM
AI classification
HDBSCAN
KMeans
Project inference
Asset Graph
PDF semantic extraction
Office semantic extraction
Aliyun OSS
S3
CDSI Server API
FileSystemWatcher
Windows Service
system tray
automatic file movement
automatic deletion
automatic deduplication
```

These belong to later milestones.

---

# 17. Non-Destructive Requirement

The initial application must never:

```text
Move
Delete
Rename
Overwrite
Modify
```

user files.

The first CDSI Beacon milestone is read-only with respect to creator assets.

Writing is allowed only to CDSI-owned local state:

```text
SQLite database
logs
cache
configuration
```

---

# 18. Engineering Constraints

1. Core must not reference WinForms.
2. Core must not reference SQLite.
3. Core must not reference cloud SDKs.
4. WinForms must not contain filesystem/domain logic.
5. Infrastructure should implement Core abstractions.
6. Application should coordinate use cases.
7. All long-running operations must support cancellation.
8. Scan failures should be isolated per file.
9. Asset identity must remain independent from path.
10. Repeated scans must not create duplicate records for unchanged files.
11. No destructive file operation is allowed in the initial milestones.
12. Tests must operate only on temporary/test directories.

---

# 19. First Codex Task

After reading:

```text
AGENTS.md
docs/CDSI_AGENT_WINDOWS_MVP.md
docs/PROJECT_INITIALIZATION.md
```

Codex should:

1. Inspect the repository.
2. Preserve any existing valid structure.
3. Create missing solution/projects.
4. Configure project references.
5. Add minimal dependencies.
6. Implement the Milestone 0.1 skeleton.
7. Add initial tests.
8. Build the full solution.
9. Run tests.
10. Report:
   - files created
   - architecture decisions
   - build result
   - test result
   - remaining Milestone 0.1 tasks

Do not proceed into intelligent classification or cloud storage until explicitly requested.

---

# 20. Core Principle

The first goal is not:

> Build an intelligent AI agent.

The first goal is:

> Build a reliable local asset registry that knows what files exist without changing the user's filesystem.

Once this layer is stable, future intelligence can be added on top:

```text
Asset Registry
    ↓
Extractors
    ↓
Features
    ↓
Embeddings
    ↓
Similarity
    ↓
Clusters / Graph
    ↓
LLM Interpretation
```

The asset registry is the foundation.
