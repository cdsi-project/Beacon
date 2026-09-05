# CDSI Beacon — Workspace and Scan Roots Specification

> Project: CDSI Beacon (repository: `cdsi-agent`)
>
> Document Type: Architecture / Implementation Specification
>
> Target: Codex / Engineering
>
> Status: Implemented v0.200 baseline with historical design sections

> **Implementation note:** Beacon now creates and persists a managed workspace, supports multiple read-only scan roots, per-root file-type / extension policies, enable and soft-remove actions, overlap checks, volume-identity remapping, explicit copy / move operations, and scan-root exclusion without deleting source files. The milestone checkboxes near the end are retained as historical planning records and must not be used to infer current implementation status. `FileSystemWatcher`, periodic background reconciliation, NAS reconnect automation, and automatic upload remain future work. Cloud backup is now project-scoped; see the [project/cloud model](CDSI_BEACON_PROJECT_CLOUD_MODEL.md).

---

# 1. Purpose

This document defines how `cdsi-agent` should manage local filesystem scope.

The core design principle is:

> CDSI must not take control of the user's entire computer.

Instead, CDSI should distinguish between:

1. **Managed Workspace**
2. **User-defined Scan Roots**

The Managed Workspace is controlled by CDSI.

User-defined Scan Roots are read-only by default.

This separation is mandatory.

---

# 2. Core Concept

The local filesystem is divided into two major zones:

```text
External File System
    ↓
User-defined Scan Roots
    ↓
Read-only discovery / indexing / analysis

CDSI Managed Workspace
    ↓
Managed assets
    ↓
Organization / backup / sync / storage
```

Conceptually:

```text
┌─────────────────────────────────────────────┐
│ User Computer                               │
│                                             │
│ D:\素材                                     │
│ E:\Projects                                 │
│ C:\Users\User\Documents                     │
│ \\NAS\Creator                               │
│                                             │
│        ↓ user adds Scan Roots               │
│                                             │
│ CDSI Beacon                                 │
│        ↓                                    │
│ Read-only scan / index / analyze            │
│                                             │
│        ↓ optional user action               │
│                                             │
│ D:\CDSI\                                    │
│ └── Managed Workspace                       │
└─────────────────────────────────────────────┘
```

---

# 3. Managed Workspace

CDSI should have one dedicated managed directory.

Example:

```text
D:\CDSI\
```

The path should be configurable during initial setup.

Recommended structure:

```text
D:\CDSI\
├── Inbox\
├── Assets\
├── Exports\
├── Cache\
├── Temp\
└── System\
    ├── DatabaseBackups\
    ├── StateBackups\
    └── Logs\
```

Optional future directory:

```text
Projects\
```

However, Projects should preferably remain logical entities rather than duplicate physical files.

---

# 4. Managed Workspace Responsibilities

Inside the Managed Workspace, CDSI may perform controlled operations such as:

```text
create directories
create files
move CDSI-managed files
rename CDSI-managed files
generate thumbnails
generate extracted text
generate metadata
generate cache
sync to OSS / S3
verify backups
manage versions
```

These capabilities apply only to assets explicitly managed by CDSI.

The Agent must not assume that arbitrary user directories are managed.

---

# 5. User-defined Scan Roots

Users must be able to add arbitrary directories for scanning.

Examples:

```text
D:\素材
E:\项目
C:\Users\User\Documents
C:\Users\User\Desktop
\\NAS\Creator
```

Users should also be able to disable or remove Scan Roots.

Adding a Scan Root means:

> CDSI is allowed to inspect and index files under this path.

It does NOT mean:

> CDSI is allowed to reorganize or modify files under this path.

---

# 6. Default Scan Root Permission

Every user-added Scan Root must default to:

```text
readonly
```

In read-only mode, CDSI may:

```text
scan files
read metadata
calculate hashes
extract text
analyze content
generate local index
detect duplicates
detect versions
suggest projects
suggest asset roles
```

CDSI must NOT:

```text
delete files
move files
rename files
overwrite files
modify file content
deduplicate by deletion
reorganize folders
upload files automatically without explicit policy
```

---

# 7. Directory Modes

The architecture should support a directory mode field.

Initial recommended values:

```text
readonly
managed
```

Future optional mode:

```text
watch
```

Definitions:

## readonly

```text
Scan and analyze only.
No mutation.
```

## managed

```text
CDSI-controlled workspace.
Controlled mutation is allowed.
```

## watch

Future mode:

```text
Read-only scan root with filesystem monitoring enabled.
```

Do not require `watch` in the first milestone.

---

# 8. Safety Rule

The system must enforce:

```text
if location is outside Managed Workspace:
    mutation = denied by default
```

This must not be only a UI convention.

The restriction should exist at the application/service layer.

Potential example:

```text
IFileMutationPolicy
```

or equivalent domain/application guard.

Do not rely solely on disabled buttons.

---

# 9. Asset States

Assets discovered outside the Managed Workspace should initially be treated as external assets.

Example:

```text
Asset
Status: External
Location:
D:\素材\video.mp4
```

Managed assets should be explicitly distinguishable.

Example:

```text
Asset
Status: Managed
Location:
D:\CDSI\Assets\01K...\original
```

Suggested lifecycle:

```text
External
    ↓
Register
    ↓
Indexed External Asset

Optional user action:
    ↓
Copy or Move into CDSI
    ↓
Managed Asset
```

---

# 10. Register / Copy / Move

When users choose to bring an external asset into CDSI, support three conceptual actions.

## Register

```text
Keep file where it is.
Create asset record only.
```

Result:

```text
External Asset
```

No filesystem mutation.

## Copy

```text
Copy the file into the CDSI Managed Workspace.
Keep original file unchanged.
```

Recommended default when users want CDSI-managed ownership.

## Move

```text
Move the file into the CDSI Managed Workspace.
```

This is destructive relative to the original location and must require explicit user action.

Do NOT use Move as the default.

---

# 11. Recommended Default

Use:

```text
Register
```

for passive indexing.

Use:

```text
Copy
```

as the recommended managed-ingestion action.

Use:

```text
Move
```

only after explicit user selection.

---

# 12. Inbox

The Managed Workspace should include:

```text
D:\CDSI\Inbox\
```

The Inbox is special because files placed there are intentionally handed to CDSI.

The Agent may:

```text
discover
hash
analyze
classify
register
move into managed asset storage
```

inside the Managed Workspace according to explicit CDSI rules.

Suggested workflow:

```text
User drops file into Inbox
    ↓
Agent discovers file
    ↓
Fingerprint
    ↓
Metadata extraction
    ↓
Duplicate detection
    ↓
Classification
    ↓
Create Asset
    ↓
Move to Assets storage
    ↓
Create Inbox/Review item if needed
```

Because Inbox belongs to the Managed Workspace, internal reorganization is permitted.

---

# 13. Asset Storage Layout

Recommended physical managed storage:

```text
D:\CDSI\Assets\
```

Do not use user filenames as stable physical identity.

Recommended structure:

```text
D:\CDSI\Assets\
└── 01KXXXXXXXXXXXX\
    ├── original
    └── manifest.yaml
```

or:

```text
D:\CDSI\Assets\
└── 01\
    └── KX\
        └── 01KXXXXXXXXXXXX\
            └── original
```

The exact layout can evolve.

Important:

```text
Asset ID != Filename
Asset ID != Path
```

The original filename belongs in metadata.

---

# 14. Project Organization

Projects should be logical, not duplicate physical asset containers.

Preferred model:

```text
Project
├── asset-001
├── asset-002
├── asset-003
└── asset-004
```

Avoid:

```text
Assets\
    video.mp4

Projects\
    ProjectA\
        video.mp4
```

which creates physical duplication.

The UI may present Projects as folders or collections without physically copying assets.

---

# 15. Scan Root Database Model

Create or extend a `scan_roots` table/entity.

Suggested fields:

```text
Id
Path
Mode
Enabled
Recursive
CreatedAt
UpdatedAt
LastScannedAt
LastScanStatus
DisplayName
```

Potential future fields:

```text
WatchEnabled
IncludePatterns
ExcludePatterns
FollowSymlinks
ScanPriority
```

Minimal MVP:

```text
Id
Path
Mode
Enabled
CreatedAt
LastScannedAt
```

---

# 16. Scan Root Validation

When a user adds a Scan Root:

Validate:

```text
path exists
path is a directory
path is accessible
path is not already registered
path is not a child duplicate of another equivalent root where unnecessary
```

Do not reject nested roots categorically.

Nested roots may be intentional.

Instead, detect and warn about overlap.

Example:

```text
D:\素材
D:\素材\Video
```

Potential UI warning:

```text
This directory is already covered by another scan root.
```

---

# 17. Managed Workspace Validation

The Managed Workspace must not accidentally overlap with an external read-only root in a way that creates conflicting semantics.

Recommended rule:

```text
Managed Workspace has precedence.
```

Example:

```text
Scan Root:
D:\

Managed Workspace:
D:\CDSI\
```

Files under:

```text
D:\CDSI\
```

must still be treated as managed.

The scanner should recognize workspace boundaries.

---

# 18. UI — Scan Roots

Recommended Settings / Scan page:

```text
Scan Directories

✓ D:\素材                    Read-only     [Disable] [Remove]
✓ E:\项目                    Read-only     [Disable] [Remove]
✓ \\NAS\Creator              Read-only     [Disable] [Remove]

[ + Add Directory ]
```

Optional columns:

```text
Path
Mode
Enabled
Last Scan
Files
Status
```

Do not expose `managed` as a casual mode toggle for arbitrary directories in the first version.

Managed Workspace should be configured separately.

---

# 19. UI — Managed Workspace

Show Managed Workspace separately.

Example:

```text
Managed Workspace

Path:
D:\CDSI\

Status:
Healthy

Assets:
3,842

Inbox:
27

[Open Folder]
[Change Location]
```

Changing the Managed Workspace path should be considered a migration operation.

Do not simply switch the path without handling existing assets.

---

# 20. Scan Behavior

The scanner should accept one or more enabled Scan Roots.

Pseudo flow:

```text
for each enabled scan root:
    validate path
    enumerate files
    skip ignored entries
    resolve whether file is:
        managed
        external
    register location
    enqueue downstream jobs
```

The scanner should not mutate any external file.

---

# 21. Overlapping Scan Roots

The system must prevent duplicate processing where roots overlap.

Example:

```text
D:\素材
D:\素材\Videos
```

A file under:

```text
D:\素材\Videos\a.mp4
```

should not create duplicate AssetLocation records because two roots discovered it.

Canonical local location identity should be based on normalized path + device semantics, not scan-root membership.

---

# 22. Path Normalization

Normalize paths before persistence/comparison.

Consider:

```text
case-insensitive Windows paths
trailing separators
relative paths
UNC paths
long paths
junctions
symlinks
drive letters
```

Do not assume raw path strings are canonical.

---

# 23. Read-only Is a CDSI Policy

`readonly` means:

> CDSI will not intentionally mutate files under that root.

It does not imply OS-level read-only filesystem permissions.

CDSI must enforce the rule internally.

---

# 24. File Mutation Service

Any future file mutation should pass through one controlled service.

Conceptual interface:

```text
IManagedFileOperations
```

Potential operations:

```text
CopyIntoWorkspace
MoveIntoWorkspace
RenameManagedAsset
DeleteManagedAsset
```

This service must verify that the target/source operation is allowed by policy.

Avoid scattered direct calls to:

```text
File.Move
File.Delete
File.Copy
```

throughout the codebase.

---

# 25. Project-Scoped Cloud Backup Policy

External Scan Root assets must not automatically upload to OSS merely because they were discovered.

Default:

```text
External Asset
→ no automatic cloud upload
```

In v0.200, an asset becomes eligible for cloud operations only after the user adds it to a Project. Being inside the Managed Workspace alone is not sufficient.

Project members may become eligible for:

```text
backup
sync
OSS upload
S3 upload
verification
```

according to user configuration.

This is an important privacy boundary.

---

# 26. Storage Eligibility

Current rule:

```text
Asset in an explicit Project
    ↓
Eligible for that Project's configured backup profiles

Asset outside every Project
    ↓
Not eligible for cloud backup
```

An external read-only asset may still be added to a Project without being copied or moved. The user must then explicitly start project synchronization. Discovery, indexing, project creation, or saving a backup configuration never starts an upload automatically.

---

# 27. Example User Flow

## Step 1

User installs CDSI Beacon.

Sets:

```text
Managed Workspace:
D:\CDSI\
```

## Step 2

User adds Scan Roots:

```text
D:\素材
E:\项目
C:\Users\User\Documents
```

## Step 3

CDSI scans read-only.

Result:

```text
42,381 files discovered
34,220 assets indexed
3,122 duplicate locations
1,482 possible versions
```

No file is modified.

## Step 4

User finds:

```text
D:\素材\CSI\BP-final.pptx
```

and chooses:

```text
Add to CDSI
```

Options:

```text
Register
Copy
Move
```

## Step 5

User selects:

```text
Copy
```

Result:

```text
Original:
D:\素材\CSI\BP-final.pptx

Managed:
D:\CDSI\Assets\<asset-id>\original
```

The managed location may later sync to OSS.

---

# 28. Example Asset Model

External asset:

```yaml
asset:
  id: asset-01KAAA
  status: external
  original_filename: BP-final.pptx

locations:
  - type: local
    mode: external
    path: D:\素材\CSI\BP-final.pptx
```

After Copy:

```yaml
asset:
  id: asset-01KAAA
  status: managed
  original_filename: BP-final.pptx

locations:
  - type: local
    mode: external
    path: D:\素材\CSI\BP-final.pptx

  - type: local
    mode: managed
    path: D:\CDSI\Assets\01KAAA\original
```

The logical Asset remains the same.

---

# 29. Missing External File

If an external file disappears:

```text
D:\素材\CSI\BP-final.pptx
```

do NOT delete the Asset.

Instead mark:

```text
AssetLocation.Status = Missing
```

The Asset may still have:

```text
Managed Workspace copy
OSS copy
NAS copy
other device copy
```

Asset deletion and location disappearance are different concepts.

---

# 30. Removing a Scan Root

If user removes:

```text
D:\素材
```

do NOT automatically delete indexed Assets.

Instead:

- stop future scanning of that root
- preserve asset history
- preserve AssetLocation records
- optionally mark locations as no longer monitored

Future cleanup may be explicit.

---

# 31. Scan Root States

Suggested root states:

```text
active
disabled
unavailable
error
```

A disconnected external drive or NAS should not cause the Scan Root to be deleted.

Example:

```text
E:\Creator
Status: unavailable
```

When the drive returns, scanning can resume.

---

# 32. Network / NAS Roots

Support architecture compatible with:

```text
UNC paths
mapped network drives
NAS mounts
```

Do not assume all scan roots are always online.

Handle:

```text
timeouts
authentication failure
temporary disconnection
slow enumeration
```

without blocking other roots.

---

# 33. First Implementation Milestone

Codex should implement the following before intelligent classification.

## Milestone — Workspace and Scan Roots

- [ ] Managed Workspace configuration
- [ ] default Managed Workspace directory creation
- [ ] `scan_roots` persistence
- [ ] Add Scan Root UI
- [ ] Disable Scan Root
- [ ] Remove Scan Root
- [ ] scan enabled roots
- [ ] read-only policy
- [ ] detect managed vs external path
- [ ] prevent duplicate indexing from overlapping roots
- [ ] preserve Asset identity independent of root
- [ ] preserve locations when root is removed
- [ ] no external filesystem mutation

---

# 34. Second Implementation Milestone

After indexing is stable:

- [ ] Register external asset
- [ ] Copy asset into Managed Workspace
- [ ] Move asset into Managed Workspace with explicit confirmation
- [ ] Managed Asset status
- [ ] Managed AssetLocation
- [ ] Inbox ingestion
- [ ] physical managed asset layout
- [ ] audit log for mutations

Do not implement automatic deletion.

---

# 35. Third Implementation Milestone

Later:

- [ ] FileSystemWatcher for selected roots
- [ ] periodic reconciliation
- [ ] scan scheduling
- [ ] NAS reconnect handling
- [ ] storage policy
- [ ] OSS direct upload
- [ ] backup status
- [ ] storage verification

---

# 36. Codex Engineering Rules

When implementing this specification:

1. Read `AGENTS.md` first.
2. Preserve the non-destructive default.
3. Treat arbitrary user-added Scan Roots as read-only.
4. Keep Managed Workspace configuration separate.
5. Do not allow UI code to bypass filesystem policy.
6. Centralize mutation operations.
7. Never automatically delete duplicate files.
8. Never automatically upload external assets.
9. Do not identify Assets by paths.
10. Do not identify Assets by filenames.
11. Handle overlapping Scan Roots safely.
12. Normalize Windows paths.
13. Treat unavailable drives as temporary state.
14. Keep Asset and AssetLocation separate.
15. Add tests for all permission boundaries.
16. Add tests proving external files are never mutated during scans.
17. Add tests for nested/overlapping roots.
18. Add tests for Managed Workspace precedence.
19. Use temporary directories in automated tests.
20. Do not scan the developer's real home directory in tests.

---

# 37. Required Tests

At minimum:

```text
add scan root
remove scan root
disable scan root
scan read-only root
scan nested root
overlapping roots
same file found from two roots
managed workspace inside scan root
missing scan root
disconnected drive
UNC-like path handling where practical
external file remains unchanged
copy into managed workspace
move into managed workspace requires explicit operation
removing scan root does not delete Asset
missing location does not delete Asset
```

---

# 38. Core Security Boundary

The core safety boundary is:

```text
Outside CDSI Managed Workspace
    ↓
Read-only by default

Inside CDSI Managed Workspace
    ↓
Controlled CDSI management
```

This rule must remain obvious in code, UI, tests, and documentation.

---

# 39. Product Principle

The user should be able to say:

> These folders may be scanned by CDSI.

without implicitly saying:

> CDSI may reorganize these folders.

Scanning permission and management permission are different.

---

# 40. Final Architecture

```text
                    User Computer
                         │
        ┌────────────────┴────────────────┐
        │                                 │
 External Scan Roots               CDSI Managed Workspace
        │                                 │
        │ readonly                        │ controlled management
        ▼                                 ▼
 Discover / Index / Analyze           Inbox / Assets
        │                                 │
        └──────────────┬──────────────────┘
                       ▼
                  Asset Registry
                       │
                Asset Intelligence
                       │
                 Storage Policy
                       │
             ┌─────────┴──────────┐
             ▼                    ▼
          Local/NAS             OSS / S3
```

The most important rule is:

> Scan Roots are used to understand the creator's existing digital world. The Managed Workspace is used to manage assets explicitly entrusted to CDSI.
