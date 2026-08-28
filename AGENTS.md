# AGENTS.md

## Project

**Repository:** `Beacon`

CDSI Beacon is the local execution runtime of CDSI.

It runs on the creator's own device and is responsible for discovering, indexing, understanding, organizing, verifying, and synchronizing local digital assets.

The agent must be designed as a **local-first, privacy-conscious, non-destructive system**.

In the target architecture, CDSI Server is the optional control plane, CDSI Beacon is the local execution plane, and cloud storage such as Aliyun OSS, AWS S3, Cloudflare R2, Tencent COS, MinIO, NAS, or local filesystem is the data/storage plane. The current v0.2.11 application operates without CDSI Server.

---

## 1. Core Product Definition

CDSI Beacon is NOT:

- a generic file manager
- a cloud drive client
- a desktop uploader
- an autonomous LLM agent
- a replacement for Windows Explorer / Finder
- a tool that silently reorganizes user files
- a tool that requires all files to be uploaded to CDSI Server

CDSI Beacon SHOULD become:

> A local digital asset discovery, understanding, organization, integrity, and synchronization agent for creators.

The agent should help answer questions such as:

- What digital assets do I have?
- Where are they located?
- Which files are duplicates?
- Which files belong to the same project?
- Which file is the latest version?
- Which assets have not been backed up?
- Which assets exist only on one device?
- Which assets have already been uploaded to object storage?
- Which files are likely source assets, drafts, finals, covers, subtitles, references, or derivatives?
- Which assets are related even if they are stored in different folders?

### 1.1 Current Repository Baseline (v0.2.11)

The current baseline is v0.2.11, a working Windows desktop application built with .NET 10, WinForms, and SQLite. Before planning or implementing a change, distinguish these implemented capabilities from future requirements:

- local workspace and multiple read-only scan roots
- stable local Asset IDs and local Project IDs
- paged asset search, tags, duplicate detection, media metadata, and statistics
- project creation, local name/type editing, membership, and project-bound cloud backup
- explicit single-project Git sync to a selected configured GitHub or Gitee repository, with a stable ProjectId manifest and non-overwrite safeguards
- local Git project management backed by latest-successful sync records; listing and search do not contact remote repositories
- Aliyun OSS, Tencent COS, and Qiniu Kodo storage adapters
- verified multipart upload, restore, and explicit remote deletion
- cloud-backup management grouped by project-name object-key prefix
- OpenWeb publishing to multiple WordPress sites
- local SQLite consistent snapshots, audit records, task progress, and run logs
- a stable per-installation UUID stored outside SQLite and shown in the About panel
- single-instance Windows desktop behavior
- asynchronous update discovery from the public Gitee repository VERSION file, with a manual Help-menu command and no automatic download or execution

The current application does **not** connect to CDSI Server, use temporary server-issued credentials, run AI/embedding pipelines, extract document bodies in the background, or send telemetry. Startup update discovery performs a read-only request for Gitee's public `VERSION` file and sends no asset metadata, paths, configuration, credentials, or client ID. Saving a Git profile does not perform network activity; clone, commit, and push occur only after the user explicitly selects a project and repository and confirms synchronization.

The cloud project model is transitional in v0.200: local projects have stable IDs, but remote objects still use `<project name>/<original filename>` and the cloud UI groups records by that prefix. Stable remote ProjectId manifests and cross-device project reconstruction are next-stage work, not existing behavior.

---

## 2. Architectural Principles

### 2.1 Local First

Local asset discovery and deterministic analysis should happen locally whenever practical.

Preferred local operations include:

- filesystem scanning
- file fingerprinting
- SHA-256 calculation
- MIME detection
- metadata extraction
- duplicate detection
- path analysis
- filename analysis
- local indexing
- deterministic classification
- local database operations
- storage uploads
- integrity verification

Do not require uploading original files to CDSI Server for basic analysis.

### 2.2 Control Plane / Data Plane Separation

Maintain a clear separation:

```text
CDSI Server
    │
    │ Control Plane
    │
    ├── creator identity
    ├── device registration
    ├── asset registry
    ├── project/content metadata
    ├── storage configuration
    ├── temporary upload authorization
    └── synchronization state
            ▲
            │ API
            ▼
CDSI Local Agent
    │
    │ Local Execution Plane
    │
    ├── scan
    ├── fingerprint
    ├── extract
    ├── analyze
    ├── index
    ├── classify
    ├── verify
    └── synchronize
            │
            ▼
Storage Providers
    ├── local filesystem
    ├── Aliyun OSS
    ├── S3-compatible storage
    ├── NAS
    └── other configured storage
```

Large binary payloads SHOULD flow directly between the local device and the configured storage provider.

CDSI Server SHOULD remain on the control path rather than the large-file data path.

### 2.3 Asset Identity Is Independent of File Location

Never treat a local path, object-storage URL, filename, or platform URL as the identity of an asset.

Conceptually:

```text
Asset
  ├── Asset Metadata
  ├── Relationships
  └── Locations
      ├── Local Device A
      ├── Local Device B
      ├── NAS
      ├── Aliyun OSS
      └── S3
```

An asset may have multiple physical locations. The same physical file may move without changing the asset identity.

### 2.3A Project Is the Managed Operation Boundary

Assets may be discovered before they are classified. New cloud backup uploads must operate in an explicit Project context. In the current implementation, restore and remote deletion still act on explicitly selected registered replicas, and the cloud-management UI groups them by object-key prefix. Whole-project restore, manifest-backed synchronization, and stable project-level remote deletion are target behavior, not current behavior.

Project identity must follow these rules:

- `ProjectId` is stable and is the canonical identity.
- Project name is mutable display metadata, not an identity or uniqueness key.
- Local and cloud representations of one logical project must carry the same ProjectId in the target model.
- Same-name projects must never be silently merged.
- Deleting a local project, deleting its cloud backup, removing a member, and deleting a local file are independent explicit operations.
- Legacy name-prefix cloud objects must remain readable during migration.

Do not deepen the current v0.200 coupling between project identity and object-key prefix. Follow [docs/CDSI_BEACON_PROJECT_CLOUD_MODEL.md](docs/CDSI_BEACON_PROJECT_CLOUD_MODEL.md) when evolving project synchronization.

### 2.4 Non-Destructive by Default

The default behavior must be:

> Discover and register, not move and reorganize.

The agent must never silently:

- delete user files
- move user files
- rename user files
- overwrite user files
- replace user directories
- remove duplicate files
- modify source documents
- upload private files without policy or user authorization

Asset organization should initially be logical / virtual. Physical reorganization must be a separate explicit action.

### 2.5 Deterministic First, AI Second

Do not use LLMs where deterministic logic is sufficient.

Preferred order:

```text
filesystem metadata
→ file signatures
→ hashes
→ path / filename rules
→ optional, policy-approved content extraction
→ embeddings
→ clustering / graph analysis
→ LLM interpretation
→ human confirmation
```

LLMs should primarily be used for:

- semantic interpretation
- cluster naming
- tag suggestions
- project suggestions
- ambiguous relationship reasoning
- summarization

LLMs should NOT control destructive filesystem operations.

### 2.6 Human Confirmation for Ambiguous Decisions

Classification results should carry confidence, for example:

```text
high
medium
low
```

Low-confidence results should enter an Inbox or Review state. Do not silently convert uncertain inference into canonical metadata.

---

## 3. Core Domain Model

Preserve this conceptual hierarchy:

```text
Creator
   │
Device
   │
Project
   │
Content Entity
   │
Asset
   │
Asset Location
```

An Asset may initially be `unclassified` or outside every Project so discovery remains low-friction. Once the user performs managed cloud operations, the Project is the required operational boundary. An Asset may belong to more than one Project; membership is a relationship and must not change the Asset identity or physical file.

---

## 4. Asset

An `Asset` represents a logical digital asset.

Examples:

- Markdown article
- text file
- image
- audio file
- video
- PDF
- Word document
- Excel workbook
- PowerPoint presentation
- archive
- subtitle
- design source file
- arbitrary binary file

The architecture must not hard-code CDSI to only a small list of creator file formats. Unknown file formats should still be indexable as generic assets.

Recommended fields include:

```text
id
original_filename
mime_type
extension
size
sha256
created_at
modified_at
discovered_at
status
metadata
```

The exact persistent schema may evolve, but stable asset identity is required.

---

## 5. Asset Location

Asset location must be modeled separately.

Examples:

```text
local filesystem
NAS
Aliyun OSS
AWS S3
Cloudflare R2
Tencent COS
MinIO
```

Conceptual representation:

```yaml
asset_id: asset-xxx

locations:
  - type: local
    device_id: device-001
    path: D:\Creator\video.mp4

  - type: object_storage
    storage_id: primary
    key: assets/asset-xxx/original
```

Do not store permanent public URLs as the canonical asset identity. Prefer `storage_id + object_key` and generate URLs dynamically when required.

---

## 6. Project

A `Project` is a logical work unit.

Every Project must have a stable ProjectId independent of its display name. Project members, bound storage profiles, sync state, and future cloud manifests refer to this ID. Renaming a Project must not create a new logical Project or silently move/merge remote data.

In v0.200, local project identity already follows this rule, while remote keys and cloud grouping still use the project name. Treat that as legacy-compatible behavior to migrate, not as the target storage model.

Examples:

```text
one short-video episode
one research article
one course
one financing project
one presentation
one content series
one design project
```

Example:

```text
Project: Dual Clutch Low-Speed Driving
│
├── script.docx
├── cover.psd
├── cover.jpg
├── raw-01.mp4
├── raw-02.mp4
├── subtitle.srt
└── final.mp4
```

Project inference should use multiple signals rather than semantic similarity alone.

---

## 7. Asset Relationships

Support explicit relationships between assets.

Initial recommended relationship types:

```text
DUPLICATE_OF
NEAR_DUPLICATE_OF
VERSION_OF
DERIVED_FROM
BELONGS_TO_PROJECT
BELONGS_TO_CONTENT
REFERENCES
RELATED_TO
```

Do not infer a complex graph before sufficient evidence exists.

Relationships should include, where practical:

```text
source
target
type
confidence
evidence
created_by
```

---

## 8. Local Agent Pipeline

The implemented v0.200 deterministic pipeline is:

```text
Scan
  ↓
Fingerprint / Candidate Hashing
  ↓
Media Metadata Extraction
  ↓
Local Registry / Search / Duplicate Detection
```

Potential semantic analysis must remain a separate, optional, policy-controlled pipeline:

```text
Scan
  ↓
Fingerprint
  ↓
Metadata Extraction
  ↓
Opt-in Content Extraction
  ↓
Feature Generation
  ↓
Embedding
  ↓
Similarity / Candidate Relations
  ↓
Clustering / Graph Analysis
  ↓
Semantic Interpretation
  ↓
Confidence Assignment
  ↓
Inbox / Human Review
  ↓
Registry / Sync
```

Each stage must be independently testable. Avoid building a monolithic `analyze_file()` or giant Agent class. Do not reintroduce background body-text extraction into the current scan path without an explicit product decision, privacy review, migration plan, and UI policy.

---

## 9. Scanner

The scanner is responsible only for discovering filesystem entries.

Responsibilities:

- recursively scan configured paths
- respect ignore rules
- detect files
- detect directories
- collect basic filesystem metadata
- emit scan events or scan results

The scanner should NOT:

- classify project membership
- invoke LLMs
- upload files
- move files
- delete files

Recommended scan scopes:

```text
user-selected directories
desktop
documents
downloads
creator workspaces
external drives
NAS mounts
```

Avoid scanning the entire operating system by default.

---

## 10. Ignore Rules

Support configurable ignore rules.

Common default exclusions may include:

```text
.git
node_modules
vendor
cache directories
temporary files
OS system directories
browser caches
application caches
package caches
```

Do not assume every hidden file is irrelevant. Ignore behavior must be configurable.

---

## 11. Fingerprinting

Each discovered file should support deterministic fingerprinting.

Minimum:

```text
size
mtime
MIME
SHA-256
```

SHA-256 should be used for exact duplicate detection and integrity verification.

Optimization is allowed for large files:

- cache previously calculated hashes
- avoid rehashing unchanged files
- use size + mtime to identify whether recalculation is necessary

Do not treat filename equality as duplicate proof.

---

## 12. Duplicate Detection

Exact duplicate detection:

```text
SHA256(A) == SHA256(B)
```

Then record:

```text
DUPLICATE_OF
```

Do NOT automatically delete duplicates.

Near-duplicate detection may later use:

- perceptual image hash
- visual embeddings
- video keyframes
- audio fingerprints
- semantic document similarity

Exact duplicate and near duplicate are different concepts and must not be conflated.

---

## 13. Metadata Extractors

Use an extractor registry rather than hard-coded branching scattered across the codebase.

Conceptual interface:

```text
supports(asset)
extract(asset)
```

Potential extractors:

```text
TextExtractor
MarkdownExtractor
PdfExtractor
OfficeExtractor
ImageExtractor
AudioExtractor
VideoExtractor
ArchiveExtractor
GenericFileExtractor
```

If a specialized extractor fails, the generic asset record should still survive. Extractor failure must not abort the entire scan.

---

## 14. Text Extraction

Current behavior intentionally does not extract, cache, display, or search document body text. Schema migration v27 permanently drops the legacy `asset_text` table and its rows; new databases do not create it, and no compatibility path remains.

Markdown and TXT contents are read only when the user explicitly publishes a selected article to OpenWeb; Beacon converts user-oriented Markdown and Front Matter to the WordPress REST representation internally.

If content extraction is reconsidered later, it must be opt-in and normalize text-bearing assets into a common extracted-text representation where practical.

Examples:

```text
TXT
Markdown
HTML
PDF
DOCX
PPTX
XLSX
```

The analysis layer should be able to work with:

```text
title
plain_text
headings
keywords
metadata
```

without caring excessively about the original file format.

Preserve the original file unchanged.

---

## 15. Image Analysis

Image analysis may eventually include:

```text
dimensions
EXIF
perceptual hash
visual embedding
visual tags
caption
OCR text
```

Prefer local and deterministic metadata extraction first. Do not invoke cloud vision analysis for every discovered image by default.

---

## 16. Audio Analysis

Audio analysis may include:

```text
duration
codec
bitrate
channels
speech transcript
audio fingerprint
semantic embedding
```

Speech transcription may be optional and policy-controlled.

---

## 17. Video Analysis

Do not send entire video files to an LLM as the default analysis mechanism.

A video semantic representation may be generated from:

```text
filesystem metadata
video metadata
filename/path
audio transcript
keyframes
visual embeddings
subtitle files
```

Video processing must be designed for large files. Avoid loading large video files fully into memory. Prefer streaming and incremental processing.

---

## 18. Feature Model

Do not reduce every asset to only one embedding.

Potential feature groups:

```text
path features
filename features
temporal features
metadata features
semantic text features
visual features
audio features
relationship features
```

These can later participate in a weighted relation score.

Conceptually:

```text
RelationScore(A, B) =
    semantic_similarity
  + path_similarity
  + filename_similarity
  + temporal_similarity
  + metadata_similarity
  + explicit_reference_signals
```

Weights must be configurable and testable. Do not bury unexplained magic constants throughout the codebase.

---

## 19. Clustering

Clustering is useful for discovering latent project or topic structure.

Do not assume the number of clusters is known.

Potential approaches include:

```text
HDBSCAN
hierarchical clustering
graph community detection
```

KMeans may be used where appropriate but should not become a hard architectural dependency.

Noise points are valid. A file classified as noise means "insufficient evidence for grouping", not "garbage file". Noise assets should normally remain available in the Inbox.

---

## 20. AI / LLM Usage

LLM calls should happen late in the pipeline.

Preferred pattern:

```text
100,000 files
    ↓
local deterministic processing
    ↓
features / embeddings
    ↓
clusters / candidate relations
    ↓
small number of semantic tasks
    ↓
LLM interpretation
```

Avoid one LLM call per file unless there is a clear feature requirement and cost/privacy justification.

LLM inputs should be minimized. Do not upload full private documents when a compact derived representation is sufficient.

---

## 21. Classification Feedback

Capture user corrections such as:

```text
accepted classification
rejected classification
renamed project
changed asset role
confirmed relationship
rejected relationship
```

Feedback may improve:

- filename rules
- local heuristics
- confidence scoring
- per-user classification profile

Do not require model retraining to benefit from user feedback. Simple deterministic personalization is preferred where sufficient.

---

## 22. Asset Roles

Assets may have semantic roles independent of MIME type.

Examples:

```text
source
raw
draft
final
cover
thumbnail
subtitle
transcript
attachment
reference
derivative
canonical
archive
```

Do not confuse file format with business role.

Example:

```text
PPTX = file format
pitch_deck = semantic/document type
canonical = asset role
```

---

## 23. Version Detection

Version inference may use:

```text
filename similarity
semantic similarity
modification time
file size
document structure
directory proximity
```

Common creator patterns include:

```text
final
final2
latest
latest-final
v2
v3
final-final
```

Never automatically remove older versions. Version relationships are metadata, not deletion instructions.

---

## 24. Asset Inbox

Uncertain or newly discovered assets should be able to enter an Inbox.

Example states:

```text
discovered
indexed
analyzing
needs_review
classified
synced
ignored
error
```

The Inbox should help users review:

- unclassified assets
- suggested projects
- duplicate candidates
- version candidates
- upload suggestions
- assets without backups

The agent should minimize unnecessary prompts. Batch review is preferable to interrupting the user for every file.

---

## 25. Storage Abstraction

Storage must be implemented through adapters.

Implemented object-storage adapters:

```text
Aliyun OSS
Tencent COS
Qiniu Kodo through its S3-compatible API
```

Local filesystem scanning and managed copy/move are also implemented, but they
use filesystem and managed-transfer abstractions rather than
`IObjectStorageAdapter`.

Potential later targets:

```text
AWS S3 and other S3-compatible providers
Cloudflare R2
MinIO
NAS
```

Conceptual storage operations:

```text
put
get
stream
exists
stat
delete
copy
multipart_upload
signed_upload
signed_download
```

Do not design CDSI around one vendor.

Storage adapters operate on objects. New backup uploads operate on Projects, while current restore and deletion operate on explicitly selected registered replicas. In the target model, all user-facing backup, restore, and reconciliation workflows roll up into Projects. A project may bind zero, one, or multiple storage profiles, and per-asset storage-location records remain necessary for integrity checks and partial retry.

---

## 26. Direct-to-Storage Upload

Large assets should normally upload directly from the local device to storage.

In v0.200, users explicitly configure provider credentials and Beacon reads them from Windows Credential Manager only while performing a confirmed operation. There is no CDSI Server dependency. The following server-issued authorization flow is a future target:

Preferred flow:

```text
Local Agent
   │
   │ request upload authorization
   ▼
CDSI Server
   │
   │ temporary authorization
   ▼
Local Agent
   │
   │ multipart/direct upload
   ▼
OSS / S3
   │
   │ completion
   ▼
CDSI Server
```

Never embed permanent cloud access secrets in source, binaries, logs, or SQLite. Prefer temporary credentials or pre-signed operations once CDSI Server integration exists; until then, keep user-provided secrets isolated in Windows Credential Manager and never read them during unrelated operations.

---

## 27. Upload Intent

A synchronization should be modeled as an explicit Project operation containing per-provider and per-asset upload intents.

Conceptual lifecycle:

```text
pending
→ uploading
→ verifying
→ available
```

Failure states:

```text
failed
cancelled
```

The server or storage adapter should verify:

```text
object exists
expected size
integrity/checksum where supported
asset/storage key mapping
```

before considering the remote location healthy. A Project is fully synchronized to one provider only when its manifest and every intended member are verified; partial success must remain visible and resumable.

---

## 28. Multipart Upload

Large file transfers must support an architecture compatible with multipart upload.

Requirements:

- avoid reading entire files into memory
- support retry
- support resumability where provider capabilities allow
- persist upload session state when appropriate
- tolerate intermittent network failure
- verify final object

A failed upload must not corrupt the local asset record.

---

## 29. Integrity Verification

Integrity is a first-class feature.

The agent should eventually support behavior equivalent to:

```bash
cdsi storage verify
```

Verification should distinguish:

```text
exists
missing
size mismatch
checksum mismatch
unverified
healthy
```

Do not claim an asset is safely backed up merely because an upload request succeeded.

---

## 30. Local Database

A local embedded database is recommended. SQLite is acceptable unless a stronger requirement emerges.

`CDSI.Agent.Infrastructure/Persistence/DatabaseMigrator.cs` is the authoritative
schema definition. Do not duplicate a full table-name inventory in this guide;
it will drift from migrations. The current persistence model covers workspaces,
volumes and scan roots, assets and locations, metadata, tags, projects and
membership, storage profiles and remote locations, file/upload/restore audits,
OpenWeb publications, Git profiles, latest Git project sync records, and application settings.

The current schema uses `asset_collections` as the persisted project model.
Database snapshots and their JSON manifests are filesystem artifacts under the
managed workspace's `System/DatabaseBackups` directory, not SQLite rows. Schema
migration v27 permanently removes the legacy `asset_text` table and its
historical rows. The current schema contains no document-body cache.

Future semantic tables such as asset features, embeddings, relations, clusters, and inbox items should be added only when their owning feature is implemented. Do not infer that a table listed in an older design document already exists.

Database migrations must be versioned. Do not place core state only in transient memory.

---

## 31. Device Identity

Each Beacon installation has a stable client identity. v0.201 generates a random UUID on first startup and stores it in `%LOCALAPPDATA%\CDSI\client-identity.json`, independently of SQLite and the managed workspace. The About panel displays this ID for support and future activation workflows.

The installation client ID and the existing SQLite `devices.id` serve different purposes. The client ID identifies the installed Beacon instance. Database device IDs preserve the origin of indexed file locations and may legitimately survive inside a restored database.

Example:

```text
device_id
device_name
platform
agent_version
registered_at
last_seen_at
```

Client identity must not depend on hostname or invasive hardware fingerprinting. It is an identifier, not a secret, password, activation token, or proof of authorization. A future activation service must enforce a unique constraint, bind the ID to an authenticated account and activation record, and reissue identity when it detects a cloned profile or collision.

---

## 32. File Watching

Initial versions may rely on manual scans.

A later watcher may observe:

```text
CREATE
MODIFY
MOVE
DELETE
```

Filesystem events should be treated as hints. Watchers can lose events. Periodic reconciliation scans should remain possible.

Do not assume event watchers are a perfect source of truth.

---

## 33. Deletion Semantics

If a local file disappears, do NOT immediately delete the logical Asset.

Instead:

```text
mark local location missing
```

The asset may still exist on another device, NAS, OSS, or S3.

Asset deletion and location deletion are different operations. This distinction is mandatory.

---

## 34. Privacy Modes

The architecture should be compatible with privacy levels such as:

```text
Local Only
Hybrid
Cloud Intelligence
```

Even if all modes are not implemented immediately, do not design the core in a way that forces all file content into the cloud.

Sensitive content should remain local unless explicitly allowed.

---

## 35. Secrets

Never commit:

```text
AccessKeySecret
AWS secret keys
OAuth secrets
API keys
private keys
user passwords
temporary credentials
```

Secrets must use appropriate local secure storage or environment/config mechanisms.

Logs must not print secrets. Temporary credentials must be short-lived and minimally scoped.

---

## 36. Logging

Logs should help diagnose:

```text
scan
extract
analysis
classification
sync
upload
verification
errors
```

Do not log full private document content by default. Do not log cloud credentials. Do not log signed URLs unless explicitly redacted.

Use structured logs where practical.

---

## 37. Error Handling

One broken asset must not stop an entire scan.

Prefer:

```text
process asset
→ record failure
→ continue
```

Errors should include enough context to debug but should not expose secrets.

Network failure should not corrupt local state.

---

## 38. Background Jobs

Long-running operations should be job-based.

Examples:

```text
scan
hash
extract
embed
cluster
upload
verify
sync
```

Jobs should expose:

```text
status
progress
started_at
finished_at
error
retry_count
```

Avoid blocking UI/CLI for large operations without progress reporting.

---

## 39. Resource Control

The agent runs on the user's personal computer. Do not monopolize resources.

Design for configurable:

```text
CPU concurrency
IO concurrency
network bandwidth
background priority
battery-aware behavior
scan schedules
```

Large scans should be resumable.

---

## 40. Cross-Platform Design

Primary target platforms may include:

```text
Windows
macOS
Linux
```

Avoid embedding platform-specific filesystem assumptions into domain logic.

Use platform adapters where required.

Be careful with:

```text
path separators
case sensitivity
illegal filename characters
symlinks
junctions
mount points
network shares
filesystem permissions
```

Windows support should be treated as first-class if it is an initial target.

---

## 41. Symlinks and Recursive Loops

Filesystem scanning must safely handle:

```text
symlinks
junctions
mount loops
```

Do not recursively follow links without cycle protection. Provide explicit configuration for following symbolic links.

---

## 42. Large Collections

Assume users may eventually have:

```text
100,000+ files
TB-scale media collections
multi-GB individual files
```

Do not build core logic that requires:

- loading all assets into memory
- loading all pairwise similarities into memory
- hashing everything on every run
- calling remote services for every file

Prefer incremental processing.

---

## 43. Idempotency

Repeated scans should not create duplicate logical records for unchanged locations.

Repeated synchronization should not duplicate remote objects.

Repeated completion callbacks should be safe where practical.

Idempotency is especially important for:

```text
scan
register
upload completion
sync
verification
```

---

## 44. Suggested Repository Structure

Adapt to the actual codebase rather than forcing this exact layout.

Conceptual structure:

```text
Beacon/
├── AGENTS.md
├── README.md
├── docs/
│
├── src/
│   ├── agent/
│   ├── scanner/
│   ├── fingerprint/
│   ├── extractors/
│   ├── features/
│   ├── embeddings/
│   ├── clustering/
│   ├── relationships/
│   ├── classifier/
│   ├── inbox/
│   ├── storage/
│   ├── sync/
│   ├── uploads/
│   ├── server_api/
│   ├── database/
│   └── platform/
│
└── tests/
    ├── unit/
    ├── integration/
    └── fixtures/
```

Preserve clean module boundaries.

---

## 45. Recommended Initial Implementation Order

This section is historical planning context. The released v0.200 implementation has completed and extended the deterministic Windows MVP. For current behavior, use the README and Section 1.1. Remaining work should now prioritize stable project identity across local and cloud state:

```text
1. Stable ProjectId in remote project manifests
2. Conflict detection for same-ID and same-name projects
3. Project-level sync state and resumable reconciliation
4. Whole-project restore and cross-device reconstruction
5. Legacy name-prefix migration without destructive rewrites
6. Provider connection and least-privilege checks
7. Background scheduling and periodic integrity verification
8. Optional CDSI Server / temporary authorization integration
9. Optional Inbox and semantic analysis only after explicit product approval
```

Do not start with the LLM layer.

---

## 46. MVP Definition

The original deterministic MVP is complete and retained here as an acceptance baseline. The released v0.200 application can:

- configure one or more local scan roots
- scan directories safely
- index files without moving them
- create stable local asset records
- calculate SHA-256
- detect exact duplicates
- extract basic metadata
- identify common file formats
- display and search unclassified assets
- register local asset locations
- organize assets into projects
- upload a project directly to configured object storage
- verify the remote copy
- restore or explicitly delete registered cloud backups
- preserve local files unchanged

CDSI Server connectivity, temporary server-issued authorization, and intelligent project classification are not implemented and are not required for the current local-first product.

---

## 47. Intelligent Classification Milestone

A later milestone should add:

- extracted text from text/PDF/Office documents
- path and filename feature extraction
- semantic embeddings
- similarity scoring
- clustering
- project candidates
- role candidates
- confidence scoring
- user confirmation
- feedback persistence

Success means:

> The system can suggest useful organization without requiring users to first reorganize their folders.

---

## 48. Destructive Operations

Any operation that can modify or destroy user data must receive extra scrutiny.

Examples:

```text
move
rename
delete
overwrite
deduplicate by deletion
replace
bulk modification
```

Rules:

1. Never perform these during scanning.
2. Never perform them solely because an AI model suggested them.
3. Require explicit command or UI action.
4. Show the exact affected files.
5. Prefer reversible behavior where possible.
6. Add tests before merging destructive functionality.
7. Preserve auditability.

---

## 49. Testing Requirements

At minimum, cover:

```text
directory traversal
ignore rules
symlink handling
hashing
duplicate detection
metadata extraction
database migrations
idempotent rescans
missing files
renamed files
large files
upload retry
multipart resume
storage verification
API authentication
temporary credential handling
```

Use fixtures. Tests must never operate destructively on a developer's real home directory. Create temporary test directories.

---

## 50. Security Requirements

Treat all discovered filenames, metadata, and file contents as untrusted input.

Consider:

```text
path traversal
malformed documents
archive bombs
unexpected MIME types
oversized metadata
parser vulnerabilities
symlink attacks
credential leakage
signed URL leakage
```

Use defensive parsing. Do not execute discovered files. Do not automatically run scripts, macros, binaries, or embedded document code.

---

## 51. Codex Working Rules

When modifying this repository:

1. Inspect the existing repository before changing architecture.
2. Read this `AGENTS.md` before implementation.
3. Preserve working code unless change is necessary.
4. Prefer small, testable modules.
5. Do not introduce framework complexity without clear benefit.
6. Do not introduce cloud dependencies into local scanning code.
7. Keep storage providers behind adapters.
8. Keep CDSI Server API behind a client abstraction.
9. Keep AI providers behind an abstraction.
10. Do not make an LLM provider a hard dependency.
11. Do not make Aliyun OSS a hard dependency.
12. Prefer deterministic behavior for filesystem operations.
13. Preserve user data.
14. Never silently delete or move files.
15. Never expose secrets in source, logs, tests, or fixtures.
16. Add tests for bug fixes.
17. Add tests before destructive capabilities.
18. Update documentation when behavior or configuration changes.
19. Keep migrations backward-compatible where reasonable.
20. Avoid premature optimization, but design for large asset collections.
21. Avoid one giant Agent class.
22. Do not make scan, analysis, storage, and sync inseparable.
23. Use explicit interfaces between modules.
24. Make failures observable.
25. Keep operations resumable where practical.
26. Treat the repository-root `VERSION` file as the only application version source.
27. For a code-version or release commit, run `powershell -NoProfile -File scripts/Increment-Version.ps1` and use the repository's `x.y.zz` sequence. The final component is always two digits from `10` through `99`; increment `x.y.99` to `x.(y+1).10`. `v0.2.10` is the explicit successor to the legacy `v0.206`; preserve the update checker's explicit cutover handling instead of replacing it with ordinary `System.Version` ordering. Run the full Release test suite, create the self-contained single-file `win-x64` publish output, and smoke-check it when preparing a binary release; a documentation-only change does not require a version bump or binary publish.
28. Use `dotnet test .\CDSI.Agent.slnx -c Release --no-restore` for the standard full suite after dependencies are restored. Do not run build, test, and publish concurrently against the same output directories because MSBuild file locks can make results nondeterministic.
29. Keep new cloud uploads project-scoped. When adding whole-project restore, reconciliation, or deletion, use stable ProjectId/manifest identity; do not allow project names or object-key prefixes to become canonical identity. Preserve the current selected-replica restore/delete workflows until their replacement is complete.
30. Preserve compatibility with legacy name-prefix cloud records used by released v0.200 and earlier versions. Require explicit confirmation before any migration, merge, overwrite, or remote deletion.

---

## 52. Decision Rule

Before implementing a feature, ask:

> Does this help the creator discover, understand, organize, protect, locate, verify, or synchronize assets while preserving ownership and control?

If not, it is probably outside the core responsibility of CDSI Beacon.

---

## 53. Core Engineering Principles

The most important principles of this repository are:

> Do not require creators to reorganize their files before CDSI can understand them.

> CDSI manages asset identity and relationships; physical storage locations remain replaceable.

> CDSI may recommend organization, but user files remain under user control.

> Large asset payloads should flow directly between the creator's device and the configured storage provider whenever possible.

> AI assists interpretation. It does not receive authority over destructive filesystem operations.
