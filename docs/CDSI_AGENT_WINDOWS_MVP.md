# CDSI Beacon — Historical Windows MVP Technical Design

> Project: CDSI Beacon (repository: `cdsi-agent`)
> Platform: Windows First
> UI: WinForms
> Language: C# / .NET
> Local Database: SQLite
> Status: Historical draft v0.1; not the current product specification

> **Current released baseline (v0.200):** Beacon has progressed beyond the milestone checklists in this document. The WinForms implementation now includes multi-root scanning, project management, verified backups to Aliyun OSS / Tencent COS / Qiniu Kodo, selected-replica cloud restore and deletion, OpenWeb publishing, database snapshots, pagination, filtering, statistics, logs, and single-instance behavior. CDSI Server integration, AI classification, embeddings, background body-text extraction, and telemetry are still not implemented. Use the root [README](../README.md) and [AGENTS.md](../AGENTS.md) as the current source of truth; use the [project/cloud model](CDSI_BEACON_PROJECT_CLOUD_MODEL.md) for the next storage architecture.

---

# 1. Project Goal

`cdsi-agent` is the local execution runtime of CDSI.

Its purpose is to help creators:

- discover digital assets scattered across local computers
- index assets without forcing users to reorganize folders
- identify duplicate files
- extract metadata and content features
- classify and group assets
- identify projects and relationships
- manage local and remote asset locations
- upload large files directly to object storage
- verify backup integrity
- synchronize local assets with CDSI Server

The initial product should prioritize Windows.

The first version does not need a cross-platform desktop framework.

Use:

```text
C#
.NET
WinForms
SQLite
```

---

# 2. Core Architectural Principle

Do NOT build the entire Agent inside WinForms.

WinForms should only be the presentation layer.

Recommended architecture:

```text
cdsi-agent
│
├── CDSI.Agent.Core
│
├── CDSI.Agent.Infrastructure
│
├── CDSI.Agent.WinForms
│
└── CDSI.Agent.Service        # later milestone
```

Responsibilities:

```text
CDSI.Agent.Core
    ↓
domain models
asset logic
scanner abstractions
job abstractions
classification logic
storage abstractions

CDSI.Agent.Infrastructure
    ↓
SQLite
filesystem
Aliyun OSS
HTTP API
Windows platform integrations

CDSI.Agent.WinForms
    ↓
UI only

CDSI.Agent.Service
    ↓
background runtime / future Windows Service
```

---

# 3. Initial Architecture

For MVP v0.1:

```text
WinForms UI
    │
    ▼
Application Services
    │
    ▼
Core
    │
    ├── Scanner
    ├── Fingerprint
    ├── Asset Registry
    ├── Duplicate Detector
    └── Job Manager
    │
    ▼
SQLite
```

Do NOT implement Windows Service immediately.

First get the complete local workflow running inside one application process.

---

# 4. Future Architecture

After the core workflow becomes stable:

```text
Windows
│
├── CDSI.Agent.Service.exe
│       │
│       ├── scan
│       ├── watch
│       ├── hash
│       ├── analyze
│       ├── upload
│       ├── verify
│       └── sync
│
└── CDSI.Agent.WinForms.exe
        │
        └── IPC
```

Possible IPC:

```text
Named Pipes
localhost HTTP
gRPC
```

Preferred first option:

```text
Named Pipes
```

because the UI and service are expected to run on the same Windows machine.

Do not introduce IPC until needed.

---

# 5. Why WinForms

WinForms is appropriate for the Windows-first MVP because the product is primarily a local desktop utility.

The UI will mainly need:

```text
TreeView
DataGridView
ListView
ProgressBar
FolderBrowserDialog
ContextMenuStrip
NotifyIcon
ToolStrip
TabControl
```

The initial value is not in UI sophistication.

The product value is in:

```text
asset discovery
asset indexing
relationship inference
classification
backup
storage synchronization
```

Do not spend Phase 1 development effort building a heavily customized visual system.

---

# 6. Product UI Structure

Recommended main layout:

```text
┌────────────────────────────────────────────────────┐
│ CDSI Beacon                                        │
├───────────────┬────────────────────────────────────┤
│ Overview      │                                    │
│ Scan          │                                    │
│ Assets        │          Main Content              │
│ Inbox         │                                    │
│ Projects      │                                    │
│ Duplicates    │                                    │
│ Storage       │                                    │
│ Jobs          │                                    │
│ Settings      │                                    │
├───────────────┴────────────────────────────────────┤
│ Status / Background Tasks                          │
└────────────────────────────────────────────────────┘
```

---

# 7. Overview Page

Display summary information.

Example:

```text
Local assets        18,421
Indexed assets      18,302
Duplicate groups       813
Inbox items             127
Projects                 48
Assets backed up      9,820

Last scan:
2026-08-18 12:30

Agent status:
Idle
```

Later add:

```text
Storage Health
Backup Health
Device Health
Sync Status
```

---

# 8. Scan Page

Responsibilities:

- configure scan roots
- start manual scan
- pause / cancel scan
- show progress
- show recent scan history
- show ignored paths
- show scan errors

Example scan roots:

```text
C:\Users\User\Desktop
C:\Users\User\Documents
D:\Creator
E:\Video
\\NAS\Creator
```

Each scan root should be stored in SQLite.

---

# 9. Assets Page

Primary asset browser.

Recommended columns:

```text
Asset ID
Filename
Type
Size
Path
Modified Time
Hash Status
Classification
Project
Backup Status
```

Filters:

```text
file type
project
status
location
duplicate
backup state
date range
```

Search:

```text
filename
path
metadata
future semantic search
```

Do not load every asset into memory.

Use pagination / virtual mode.

---

# 10. Inbox Page

Inbox is for uncertain or newly discovered assets.

Examples:

```text
unclassified files
project suggestions
duplicate suggestions
version suggestions
upload suggestions
missing backup
```

User actions:

```text
Accept
Reject
Edit
Ignore
Batch Accept
Assign Project
Assign Role
```

Do not interrupt the user with modal dialogs for every classification decision.

Use batch review.

---

# 11. Projects Page

A Project groups related assets.

Example:

```text
Project: 双离合低速为什么比高速难

Assets:
├── script.docx
├── cover.psd
├── cover.jpg
├── raw-01.mp4
├── raw-02.mp4
├── subtitle.srt
└── final.mp4
```

Initial Project implementation may be manual.

Automatic Project inference should be a later milestone.

---

# 12. Duplicate Page

Exact duplicate detection should use:

```text
SHA-256
```

Example:

```text
Duplicate Group #123

D:\Downloads\cover.jpg
D:\Creator\Cover\cover.jpg
E:\Backup\cover.jpg
```

Do NOT automatically delete files.

Actions may include:

```text
Ignore
Mark Preferred Location
Open Folder
Register Backup Copy
```

Deletion should not be part of the initial MVP.

---

# 13. Storage Page

Display configured storage providers.

Initial support:

```text
Local
Aliyun OSS
```

Future:

```text
AWS S3
Cloudflare R2
Tencent COS
MinIO
NAS
```

Example:

```text
Primary Storage
Type: Aliyun OSS
Bucket: cdsi-assets
Status: Connected
```

Never display full credentials.

---

# 14. Jobs Page

Long-running tasks should be represented as jobs.

Job types:

```text
Scan
Hash
Metadata Extraction
Text Extraction
Embedding
Upload
Storage Verify
Sync
```

Recommended fields:

```text
ID
Type
Status
Progress
Created At
Started At
Finished At
Retry Count
Error
```

Statuses:

```text
Pending
Running
Paused
Completed
Failed
Cancelled
```

---

# 15. Settings Page

Initial settings:

```text
scan roots
ignore rules
hash behavior
background concurrency
SQLite path
CDSI Server URL
storage configuration
log level
```

Future:

```text
privacy mode
AI provider
embedding provider
bandwidth limit
battery policy
automatic sync
```

---

# 16. System Tray

`cdsi-agent` should eventually support system tray mode.

Use:

```text
NotifyIcon
```

Menu:

```text
Open CDSI Beacon
Scan Now
Pause Agent
Sync Assets
Open Inbox
Settings
Exit
```

Initial version may run only as a standard WinForms application.

Tray mode can be added after background jobs are stable.

---

# 17. Core Project Structure

Recommended solution:

```text
cdsi-agent/
│
├── src/
│   │
│   ├── CDSI.Agent.Core/
│   │
│   ├── CDSI.Agent.Application/
│   │
│   ├── CDSI.Agent.Infrastructure/
│   │
│   └── CDSI.Agent.WinForms/
│   │
│   └── CDSI.Agent.Service/        # future
│
├── tests/
│   ├── CDSI.Agent.Core.Tests/
│   ├── CDSI.Agent.Infrastructure.Tests/
│   └── CDSI.Agent.IntegrationTests/
│
├── docs/
│
├── AGENTS.md
│
└── CDSI.Agent.sln
```

---

# 18. CDSI.Agent.Core

Must not depend on WinForms.

Responsibilities:

```text
Asset
AssetLocation
Project
AssetRelation
ScanRoot
ScanJob
UploadJob
Storage abstraction
Scanner abstraction
Fingerprint abstraction
Classification abstractions
```

Example namespaces:

```text
CDSI.Agent.Core.Assets
CDSI.Agent.Core.Projects
CDSI.Agent.Core.Relationships
CDSI.Agent.Core.Storage
CDSI.Agent.Core.Jobs
```

---

# 19. CDSI.Agent.Application

Application orchestration layer.

Responsibilities:

```text
StartScan
RegisterAsset
CalculateFingerprint
DetectDuplicates
CreateUploadIntent
UploadAsset
VerifyAsset
AssignProject
ProcessInbox
```

This layer coordinates Core and Infrastructure.

WinForms should call Application services instead of directly operating on SQLite or filesystem internals.

---

# 20. CDSI.Agent.Infrastructure

Implementation layer.

Modules:

```text
Filesystem
SQLite
Aliyun OSS
Server API
Windows integrations
Logging
```

Example:

```text
Infrastructure/
├── Persistence/
├── FileSystem/
├── Storage/
│   ├── Local/
│   └── AliyunOSS/
├── ServerApi/
└── Windows/
```

---

# 21. CDSI.Agent.WinForms

UI only.

Do not put business logic into event handlers.

Bad:

```csharp
private void btnScan_Click(...)
{
    foreach (...)
    {
        // scan
        // hash
        // database
        // classification
        // upload
    }
}
```

Preferred:

```text
btnScan_Click
    ↓
ScanApplicationService.Start(...)
    ↓
Job Manager
    ↓
Scanner
    ↓
Database
```

The UI receives:

```text
progress
status
notifications
results
```

---

# 22. Local Database

Use:

```text
SQLite
```

Recommended data directory:

```text
%LOCALAPPDATA%\CDSI\
```

Example:

```text
%LOCALAPPDATA%\CDSI\
├── cdsi.db
├── config.json
├── logs\                  startup/emergency fallback only
├── cache\
└── thumbnails\
```

Do not store the database in the application installation directory.

---

# 23. Initial Database Tables

Recommended:

```text
devices
scan_roots
scan_jobs
assets
asset_locations
asset_metadata
duplicate_groups
projects
project_assets
inbox_items
jobs
storage_configs
upload_sessions
settings
```

Later:

```text
asset_features
asset_embeddings
asset_relations
clusters
classification_feedback
```

Use migrations.

---

# 24. Asset Model

An Asset is a logical digital asset.

Example fields:

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

An Asset must NOT be identified by path.

---

# 25. Asset Location Model

One Asset may have multiple locations.

Examples:

```text
Local
NAS
OSS
S3
```

Example:

```text
Asset
ID: asset-001

Locations:

1.
Device: desktop-01
Path: D:\Creator\video.mp4

2.
Storage: aliyun-primary
Key: assets/asset-001/original
```

File disappearance should mark a location missing.

Do not delete the logical asset automatically.

---

# 26. Scanner

Scanner responsibilities:

```text
recursive file discovery
filesystem metadata
ignore rules
progress reporting
error reporting
```

Scanner should NOT:

```text
run AI
upload assets
delete files
move files
classify projects
```

Use separate services.

---

# 27. File Fingerprinting

Minimum fingerprint:

```text
File Size
Modified Time
MIME
SHA-256
```

Optimization:

```text
size + mtime unchanged
    ↓
reuse cached hash
```

Do not hash unchanged multi-GB videos on every scan.

---

# 28. Duplicate Detection

Exact duplicates:

```text
SHA256(A) == SHA256(B)
```

Generate duplicate groups.

Do not automatically:

```text
delete
merge physical files
move
rename
```

---

# 29. File Watching

Later milestone:

```text
FileSystemWatcher
```

Watch:

```text
Created
Changed
Renamed
Deleted
```

Important:

`FileSystemWatcher` is NOT a perfect source of truth.

Events may be lost.

Always retain periodic reconciliation scan capability.

---

# 30. Background Job System

Do not execute heavy operations directly on the WinForms UI thread.

Use background workers / hosted services / task queues.

Initial implementation may use:

```text
System.Threading.Channels
Task
CancellationToken
SemaphoreSlim
```

Potential job pipeline:

```text
Scan
    ↓
Asset Registration
    ↓
Hash
    ↓
Metadata Extraction
    ↓
Duplicate Detection
```

Later:

```text
Text Extraction
Embedding
Classification
Upload
Verify
```

---

# 31. Resource Control

The application runs on the creator's personal machine.

Support configurable concurrency.

Example:

```text
MaxHashWorkers: 2
MaxMetadataWorkers: 4
MaxUploadWorkers: 2
```

Avoid:

```text
100% CPU
disk saturation
network saturation
UI freezing
```

---

# 32. Storage Abstraction

Create an interface such as:

```text
IStorageProvider
```

Possible operations:

```text
Exists
GetMetadata
Upload
MultipartUpload
Download
Delete
Verify
GenerateUploadAuthorization
```

Initial adapters:

```text
LocalStorageProvider
AliyunOssStorageProvider
```

Do not let OSS SDK calls leak throughout the application.

---

# 33. Direct OSS Upload

Large assets should upload directly:

```text
Local Agent
     │
     │ request upload authorization
     ▼
CDSI Server
     │
     │ temporary credentials / signed operation
     ▼
Local Agent
     │
     ▼
Aliyun OSS
```

The binary file should NOT pass through CDSI Server.

---

# 34. Upload Security

Never store permanent cloud secrets inside the desktop application.

Prefer:

```text
STS temporary credentials
pre-signed upload
limited scope
short expiration
```

Credentials should be scoped to:

```text
specific bucket
specific object prefix
specific operation
```

---

# 35. Multipart Upload

Required for large video and media files.

Support:

```text
multipart upload
retry
resume
progress
cancellation
```

Persist upload session state in SQLite where necessary.

Example:

```text
upload_sessions
├── asset_id
├── upload_id
├── provider
├── object_key
├── status
└── completed_parts
```

---

# 36. Intelligent Classification — Later Phase

Do not begin with LLM classification.

Recommended pipeline:

```text
Scan
  ↓
Metadata
  ↓
Filename / Path Features
  ↓
Text Extraction
  ↓
Embedding
  ↓
Similarity
  ↓
Clustering
  ↓
Project Candidates
  ↓
LLM Interpretation
  ↓
Inbox
```

LLM is the final interpretation layer, not the first processing layer.

---

# 37. Metadata Extraction

Initial formats:

```text
TXT
Markdown
PDF
DOCX
PPTX
XLSX
Images
Video
Audio
```

Extractors should implement a shared interface.

Example:

```text
IAssetExtractor
```

Do not fail the entire scan because one file parser throws an exception.

---

# 38. Intelligent Project Discovery

Future Project inference should combine:

```text
path similarity
filename similarity
creation / modification time
semantic similarity
content references
file roles
```

Do not rely only on embeddings.

Conceptual relation score:

```text
ProjectRelationScore =
    SemanticSimilarity
  + PathSimilarity
  + FilenameSimilarity
  + TemporalSimilarity
  + MetadataSignals
```

Weights should be configurable.

---

# 39. Clustering

Potential algorithms:

```text
HDBSCAN
hierarchical clustering
graph community detection
```

Do not hard-code KMeans as the only option.

Unknown / noise assets are valid.

They should enter:

```text
Inbox
```

rather than being forced into a cluster.

---

# 40. Asset Graph

Future relationships:

```text
DUPLICATE_OF
NEAR_DUPLICATE_OF
VERSION_OF
DERIVED_FROM
BELONGS_TO_PROJECT
BELONGS_TO_CONTENT
RELATED_TO
REFERENCES
```

Example:

```text
cover.psd
   │
   └── DERIVED_FROM
           ↓
       cover.jpg
```

---

# 41. Privacy

The architecture should support:

```text
Local Only
Hybrid
Cloud Intelligence
```

Local Only may perform:

```text
scan
hash
metadata
local extraction
local rules
local embeddings
local clustering
```

Cloud intelligence should require explicit configuration.

Do not send full private files to cloud AI providers by default.

---

# 42. Logging

Recommended:

```text
Serilog
```

Normal runtime logs:

```text
<CDSI workspace>\System\Logs\
```

Before the workspace can be resolved, or when it is unavailable, startup diagnostics
fall back to `%LOCALAPPDATA%\CDSI\Logs\`.

Log:

```text
scan jobs
errors
upload jobs
storage verification
agent lifecycle
```

Do not log:

```text
credentials
private document contents
signed URLs
access tokens
```

---

# 43. Error Handling

One broken file must not stop a scan.

Pattern:

```text
try process file
    ↓
record success

or

record error
    ↓
continue
```

Errors should be attached to:

```text
Asset
Job
Scan
```

where relevant.

---

# 44. Destructive Operations

The first MVP should NOT include:

```text
automatic file deletion
automatic moving
automatic renaming
duplicate cleanup
bulk filesystem reorganization
```

If these features are added later:

```text
explicit user action
preview affected files
confirmation
audit log
reversible behavior where possible
```

AI must never directly authorize destructive operations.

---

# 45. Windows Platform Considerations

Handle correctly:

```text
NTFS paths
long paths
junctions
symbolic links
external drives
network shares
OneDrive folders
case-insensitive paths
locked files
permission errors
```

Scanner must avoid recursive symlink/junction loops.

---

# 46. MVP v0.1

Goal:

Build a useful local asset discovery tool.

Features:

- [x] WinForms shell
- [x] SQLite database
- [x] scan root management
- [x] manual directory scan
- [x] file index
- [x] MIME detection
- [x] size / timestamps
- [x] SHA-256
- [x] exact duplicate detection
- [x] asset list
- [x] duplicate list
- [x] scan progress
- [x] background job handling
- [x] safe error handling
- [x] no filesystem modification

Success condition:

> User can select scattered directories and CDSI Beacon can build a reliable asset index without moving any files.

---

# 47. MVP v0.2

Add asset understanding.

Features:

- [ ] PDF text extraction
- [ ] DOCX extraction
- [ ] PPTX extraction
- [ ] XLSX metadata/text extraction
- [ ] Markdown / TXT extraction
- [ ] image metadata
- [ ] video metadata
- [ ] audio metadata
- [ ] filename/path features
- [ ] Inbox

---

# 48. MVP v0.3

Add intelligent organization.

Features:

- [ ] embeddings
- [ ] similarity search
- [ ] clustering
- [ ] project candidates
- [ ] asset role suggestions
- [ ] confidence score
- [ ] manual confirmation
- [ ] user feedback persistence

---

# 49. MVP v0.4

Add storage integration.

Features:

- [ ] CDSI Server authentication
- [ ] storage abstraction
- [ ] Aliyun OSS
- [ ] upload intent
- [ ] direct-to-OSS upload
- [ ] multipart upload
- [ ] resume
- [ ] verification
- [ ] backup state

---

# 50. MVP v0.5

Split background runtime.

Architecture:

```text
CDSI.Agent.Service
    +
CDSI.Agent.WinForms
```

Features:

- [ ] Windows background service
- [ ] IPC
- [ ] system tray
- [ ] FileSystemWatcher
- [ ] scheduled reconciliation scans
- [ ] background uploads
- [ ] background verification

---

# 51. Recommended Development Order

Codex should implement approximately in this sequence:

```text
1. Create solution/projects
2. SQLite persistence
3. domain models
4. scan root management
5. filesystem scanner
6. asset registration
7. SHA-256 fingerprinting
8. duplicate detection
9. WinForms asset list
10. background job framework
11. scan progress
12. error handling
13. metadata extractors
14. Inbox
15. intelligent classification
16. storage abstraction
17. CDSI Server API
18. Aliyun OSS
19. multipart upload
20. Windows Service
```

Do not start from AI classification.

---

# 52. Suggested NuGet Categories

Use mature packages where appropriate.

Potential categories:

```text
SQLite ORM / access
structured logging
MIME detection
PDF extraction
OpenXML
media metadata
Aliyun OSS SDK
HTTP resilience
```

Avoid introducing large frameworks unless they materially reduce complexity.

---

# 53. Definition of Done — Windows MVP

The Windows MVP is successful when:

- [ ] user can install and run CDSI Beacon
- [ ] user can select multiple local directories
- [ ] scanner can process large directory trees safely
- [ ] indexed assets persist in SQLite
- [ ] rescans are idempotent
- [ ] exact duplicates can be detected
- [ ] the application remains responsive during scanning
- [ ] scan failures do not terminate the whole job
- [ ] no user file is moved, deleted, renamed, or overwritten
- [ ] architecture allows later extraction, classification, and OSS integration
- [ ] WinForms contains presentation logic only
- [ ] Core does not depend on WinForms

---

# 54. Engineering Rules

Before implementing any feature:

1. Keep file operations non-destructive by default.
2. Keep the UI separate from business logic.
3. Do not put long-running work on the UI thread.
4. Use SQLite as durable local state.
5. Asset identity must remain independent from file path.
6. Keep storage providers behind adapters.
7. Keep CDSI Server behind an API client abstraction.
8. Keep AI optional.
9. Prefer deterministic logic before AI.
10. Design for 100,000+ files and multi-GB media.
11. Avoid loading entire large media files into memory.
12. Support cancellation and progress for long operations.
13. Preserve resumability where practical.
14. Never expose credentials in logs.
15. Never silently modify creator files.

---

# 55. Core Product Principle

The Windows application should follow this rule:

> The creator should not need to organize files before CDSI Beacon can understand them.

The Agent first:

```text
Discovers
Indexes
Understands
Suggests
```

Only later, if the creator explicitly chooses:

```text
Organizes
Uploads
Synchronizes
Backs up
```

CDSI Beacon should understand the user's existing digital world before attempting to change it.
