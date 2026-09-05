# Feature: Beacon State Backup & Restore

> Product: CDSI Beacon
> Feature type: Core data protection
> Priority: P0
> Target repository: `cdsi-project/Beacon`
> Audience: Codex / maintainers / contributors

---

# 0. Current Implementation Status (Beacon v0.2.21)

Beacon v0.2.21 implements the first local recovery milestone of this design.
It is deliberately smaller than the complete provider-backed architecture described
later in this document.

The current implementation provides:

- a fixed, portable `.cdsibak` ZIP bundle containing both Beacon SQLite databases
- a versioned `manifest.json` with one descriptor per required database
- SQLite Online Backup API snapshots of `cdsi.db` and `reader.db`
- full SQLite integrity and foreign-key validation
- payload size and SHA-256 validation
- bounded, defensive archive parsing
- manual local state backup, validation, export, and restore from the Data Protection UI
- a portable pre-restore safety bundle when the current databases are healthy
- repository-independent emergency restore with raw SQLite file-family isolation when
  the current databases are missing or damaged
- restart-bound restore before normal repositories are initialized
- a persisted restore transaction with rollback if the two-database replacement fails

The current state bundle is local and unencrypted. It is created only in response to an
explicit user action, is not uploaded by Beacon, is not generated on a schedule, and is
not subject to automatic bundle retention. A confirmed healthy-state restore creates a
mandatory pre-restore bundle; this is part of that restore transaction, not a background
backup schedule.
Cloud upload, client-side encryption, recovery-key handling, automatic state bundles,
and remote backup history remain future milestones.

Beacon's existing automatic database snapshots remain enabled independently. Those
`.db` snapshots and sidecar JSON manifests continue to live under
`System/DatabaseBackups`; they are not `.cdsibak` State Bundles and are not removed or
replaced by this feature.

In this document, **State Bundle v1** means the local v0.2.21 format. Sections that
describe encryption, cloud providers, remote retention, or automatic bundle creation
are future architecture unless they explicitly say otherwise.

---

# 1. Background

Beacon is no longer only a local file index.

As Beacon evolves, its local database stores increasingly valuable and partly non-recoverable user state, including:

- asset index
- stable asset identity
- projects
- collections
- tags
- locations
- duplicate relationships
- RSS subscriptions
- RSS feed items
- read / unread state
- favorites
- notes
- OpenWeb mappings
- Git mappings
- backup history
- local preferences
- audit information

Some of this data can be rebuilt from files.

Some of it cannot.

For example:

```text
RSS subscription list         partially recoverable
old RSS items                 may no longer exist upstream
read / unread state           not recoverable
favorites                     not recoverable
notes                         not recoverable
project relationships         not recoverable
asset tags                    not recoverable
publish mappings              not recoverable
```

Therefore Beacon local state must be treated as a user-owned digital asset.

---

# 2. Goal

Implement a reliable, local-first, provider-neutral:

> **Beacon State Backup & Restore**

The feature must allow the user to protect and restore Beacon application state without converting Beacon into a cloud-first SaaS.

The local Beacon databases remain the primary source of truth.

Cloud storage is used as backup, not as the authoritative database.

---

# 3. Core Principle

The following is the long-term provider-backed flow. Beacon v0.2.21 stops after the
local versioned bundle and begins restore from a local file.

```text
Local Beacon State
        ↓
Consistent Snapshot
        ↓
Validate
        ↓
Package
        ↓
Encrypt
        ↓
Upload
        ↓
Versioned Backup
```

Restore:

```text
Select Backup
        ↓
Download
        ↓
Decrypt
        ↓
Verify
        ↓
Validate Database
        ↓
Schema Migration
        ↓
Restore
```

---

# 4. Scope

## In Scope for State Bundle v1 / Beacon v0.2.21

- consistent snapshots of both Beacon SQLite databases
- fixed State Bundle v1 format and compression
- manifest, size, SHA-256, SQLite integrity, foreign-key, role, and schema validation
- manual local backup, validation, export, and restore
- local bundle listing and user-visible validation status
- pre-restore safety bundle
- restart-bound replacement and rollback of the two-database state set
- restore diagnostics that do not expose secrets

## Explicitly Deferred Beyond v0.2.21

- State Bundle encryption and recovery-key handling
- upload to existing storage providers
- cloud restore and remote backup catalogs
- automatic or change-triggered State Bundle creation
- automatic State Bundle retention or deletion
- portable credential recovery
- packaging original asset files

The existing per-database snapshot scheduler and retention policy are retained, but
they remain a separate internal redundancy mechanism rather than State Bundle v1.
RSS OPML and Reader JSON import/export also remain separate portability tools.

## Out of Scope for the State Backup Feature

- full multi-device real-time synchronization
- distributed database replication
- conflict resolution between multiple active Beacon devices
- zero-downtime multi-device state merge
- backing up arbitrary user files
- password recovery through CDSI server
- permanent cloud credential backup
- server-side plaintext database processing
- proprietary cloud-only restore path

---

# 5. Data Classification

Beacon should explicitly distinguish three categories.

## 5.1 Assets

Large user-owned files:

- documents
- images
- videos
- audio
- source code
- project files

These are handled by existing asset backup / storage workflows.

## 5.2 State

Beacon-generated or Beacon-maintained structured information:

- database records
- asset identity
- asset locations
- tags
- projects
- collections
- RSS data
- read states
- favorites
- publish records
- mappings
- backup metadata
- local configuration where appropriate

This feature protects **State**.

## 5.3 Secrets

Examples:

- OSS AccessKeySecret
- COS SecretKey
- GitHub token
- Gitee token
- WordPress Application Password
- private keys
- session tokens

Secrets must not be exported into the ordinary State Bundle in plaintext.

For v1:

> Restore state first, re-authorize secrets separately.

State Bundle v1 includes neither Windows Credential Manager values nor SSH private
keys. It also excludes runtime logs, existing database snapshots, original asset
files, and `%LOCALAPPDATA%\CDSI\client-identity.json`. The source installation ID may
does appear in the manifest as provenance, but restore never replaces the target
installation ID. Database-local device records remain part of `cdsi.db` and are
restored with that database.

The bundle is nevertheless sensitive. Its databases contain absolute asset paths,
project and connection metadata, RSS URLs and fetched content. User-entered URLs can
also contain private query parameters or embedded tokens. The v1 guarantee is that
Beacon does not read managed secrets from Windows Credential Manager or copy SSH
private keys into the bundle; it cannot guarantee that arbitrary database text is
secret-free.

---

# 6. Local Database Semantics

Beacon local SQLite remains:

> **Authoritative Local State**

Do not redesign Beacon around a remote database.

The feature must not require:

- CDSI account
- CDSI server
- CDSI cloud database

for local backup to user-owned storage providers.

---

# 7. SQLite Snapshot Strategy

Do not copy the live SQLite database file directly while Beacon is actively using it.

Forbidden approach:

```text
copy beacon.db backup.db
```

because the database may be using:

- WAL
- active transactions
- pending writes
- checkpoints

Use a consistent SQLite snapshot mechanism.

Preferred options:

1. SQLite Online Backup API
2. equivalent safe snapshot API supported by current .NET SQLite library
3. controlled checkpoint + backup only if proven consistent

The implementation must ensure:

- transactionally consistent snapshot
- no broken WAL dependency
- database can be opened independently
- snapshot passes integrity validation

---

# 8. Snapshot Pipeline

```text
cdsi.db + reader.db
        ↓
Create two consistent SQLite snapshots
        ↓
PRAGMA integrity_check + PRAGMA foreign_key_check
        ↓
Read each independent schema version
        ↓
Generate manifest with size and SHA-256 per payload
        ↓
Write compressed temporary archive
        ↓
Atomically publish local .cdsibak
```

Both databases are required. A failed snapshot or validation aborts the operation,
and no final bundle is published. The databases have no cross-database foreign keys;
the bundle records them as one recovery set and restores them together.

---

# 9. Beacon State Bundle

Define a portable bundle format.

Suggested extension:

```text
.cdsibak
```

Example:

```text
beacon-state-2026-09-02T09-00-00Z.cdsibak
```

State Bundle v1 is a standard ZIP archive with exactly these logical contents:

```text
manifest.json
databases/
├── cdsi.db
└── reader.db
```

No original asset payload, credential, private key, runtime log, prior snapshot,
Reader JSON export, or OPML file is included. The two database paths are fixed and
must not be renamed. Unknown or additional archive entries are rejected by format v1.

---

# 10. Manifest Format

Suggested initial schema:

```json
{
  "format": "cdsi-beacon-state",
  "format_version": 1,
  "backup_id": "d930f2b8-425d-4770-b45f-24bf36ea54d8",
  "created_at_utc": "2026-09-02T09:00:00Z",
  "beacon_version": "0.2.21",
  "source_client_id": "c90d475f-e6ba-42c7-af7a-f3e8c0f7f728",
  "platform": "Microsoft Windows ...",
  "architecture": "X64",
  "encrypted": false,
  "backup_kind": "Manual",
  "databases": [
    {
      "role": "asset",
      "path": "databases/cdsi.db",
      "required": true,
      "schema_version": 28,
      "size": 123456,
      "sha256": "64 uppercase hexadecimal characters"
    },
    {
      "role": "reader",
      "path": "databases/reader.db",
      "required": true,
      "schema_version": 1,
      "size": 123456,
      "sha256": "64 uppercase hexadecimal characters"
    }
  ]
}
```

Requirements:

- versioned
- forward-extensible
- human-readable
- no Beacon-managed credential payloads
- no permanent cloud credentials read from the credential store
- stable backup ID
- exactly one required `asset` database and one required `reader` database
- independent schema versions for the two databases
- `source_client_id` is provenance only and is never restored
- platform and architecture are diagnostic metadata, not SQLite compatibility gates

---

# 11. Checksums

Each database payload has its size and integrity hash in `manifest.json`; State Bundle
v1 does not use a second `checksums.json` file.

Use SHA-256 for bundle payload integrity.

Restore must reject corrupted payloads. SHA-256 provides accidental corruption
detection, not authenticity against a malicious party who can replace both a payload
and its manifest. A future authenticated-encryption envelope will provide tamper
authentication for encrypted backups.

---

# 12. Compression

State Bundle v1 uses ZIP compression and is readable by ordinary ZIP tooling.
Archive creation streams database files rather than loading the complete bundle into
memory. A later encrypted format must compress before encryption.

```text
Bundle
  ↓
Compress
  ↓
Encrypt
```

Correctness and portability are more important than optimization.

---

# 13. Encryption (Future, Not in v0.2.21)

State Bundle v1 local files are not encrypted. The Data Protection UI and README must
state that a bundle contains private asset paths, configuration metadata, RSS content,
and reading state. Users should store or export it with appropriate filesystem access
controls.

Before any future cloud upload is implemented, backup contents must be encrypted
locally before upload.

Requirements:

- encryption occurs on the user device
- storage provider receives ciphertext
- encryption key must not be embedded inside the backup archive
- no plaintext database uploaded to cloud storage
- no secret key written to logs

Preferred direction:

- modern authenticated encryption
- AEAD
- random nonce / IV
- explicit format version
- future key rotation support

Possible algorithms:

- AES-256-GCM
- XChaCha20-Poly1305

Final choice should follow the .NET crypto stack and existing Beacon security architecture.

Do not invent custom cryptography.

---

# 14. Backup Key Handling (Future)

Backup-key handling is not implemented by State Bundle v1. A future encrypted format
should reuse or extend Beacon's existing secure local credential mechanism where
possible.

Requirements:

- backup encryption key stored securely
- never stored in plaintext config
- never stored in logs
- never exported unencrypted
- architecture must leave room for recovery

Future options may include:

- user-held recovery key
- recovery phrase
- encrypted key export
- trusted-device recovery
- optional managed recovery

These do not all need to ship in the first encrypted milestone.

---

# 15. Storage Provider Integration (Future)

Beacon v0.2.21 does not upload or download State Bundles. When provider integration is
added, State Backup must reuse Beacon's provider abstraction.

Initial compatible providers should include current Beacon storage providers where practical:

- Aliyun OSS
- Tencent COS
- Qiniu Kodo

Future:

- AWS S3
- S3-compatible storage
- NAS
- local folder
- CDSI Managed Backup

Conceptually:

```text
StateBackupService
        ↓
IStorageProvider
        ↓
OSS / COS / Kodo / Future Providers
```

Do not implement a separate upload stack only for State Backup.

---

# 16. Backup Object Layout (Future)

Recommended remote layout:

```text
beacon/
└── state/
    ├── device-<uuid>/
    │   ├── latest.json
    │   ├── backups/
    │   │   ├── <backup-id>.cdsibak
    │   │   └── ...
    │   └── index.json
```

Use stable IDs.

---

# 17. Backup Metadata Index (Future)

Maintain a lightweight backup index, but ensure it can be rebuilt by scanning backup objects.

`index.json` must not become a single point of failure.

---

# 18. Backup Triggers

State Bundle v1 is created only in response to an explicit user action: either the
direct backup command or the mandatory safety step of a confirmed healthy-state restore.

## Manual

```text
Back up Beacon state now
```

## Automatic State Bundles (Future)

Initial strategy:

- periodic backup
- backup on graceful application exit
- backup after significant state changes, debounced

Do not back up after every row update.

Suggested baseline:

```text
Every 6 hours
+
On application exit
+
After major state change
```

---

# 19. Significant State Change

Examples:

- new RSS subscription
- remove RSS subscription
- large import
- project creation
- collection change
- tag bulk update
- OpenWeb mapping change
- storage mapping change
- restore completion
- schema migration

Debounce frequent changes.

---

# 20. State Bundle Retention Policy (Future)

Beacon v0.2.21 does not automatically delete `.cdsibak` files. Its existing individual
database snapshots retain their separate recent/daily/monthly policy. A future State
Bundle retention policy must not keep only one mutable backup.

Suggested default:

```text
7 daily
4 weekly
12 monthly
```

Requirements:

- versioned
- predictable
- user-visible
- low cost
- safe against silent corruption propagation

---

# 21. Corruption Protection

Never overwrite the only known-good backup.

Mitigation:

- version every backup
- run integrity validation before packaging
- allow users to keep multiple named local bundles
- mark validation failures
- do not promote invalid snapshot to latest

---

# 22. Restore Workflow

Provide:

```text
Restore Beacon
```

The v0.2.21 source is a local `.cdsibak` file. Configured cloud providers are not
contacted by this workflow.

Flow:

```text
Select local bundle
      ↓
Copy to an application-controlled staging directory
      ↓
Verify archive paths, manifest, sizes and SHA-256
      ↓
Open both snapshots and run SQLite integrity/foreign-key checks
      ↓
Check the independent asset and Reader schema versions
      ↓
Preserve current state: create a State Bundle when healthy, otherwise isolate the raw
SQLite database, WAL, SHM, and rollback-journal file families
      ↓
Persist a pending-restore transaction and restart Beacon
      ↓
Before repository initialization, validate and migrate staged copies
      ↓
Replace cdsi.db and reader.db as one recoverable operation
      ↓
Post-restore validation; commit or roll back the complete database pair
      ↓
Show the restore result when the startup process continues to the UI
```

Restore is a replacement operation, not a record merge. Validation and migration must
operate on staged copies. After replacement begins, a restore transaction record must
survive process interruption and allow the next startup to finish or roll back. A
failure must never leave one old database paired with one restored database.

---

# 23. Restore Safety Backup

Before replacing healthy current databases, Beacon creates a local safety State Bundle.
If normal repository initialization cannot complete because either current database is
missing or damaged, the emergency path must not depend on those repositories or the
workspace row. It copies the exact `cdsi.db` and `reader.db` file families, including
the presence or absence of each `-wal`, `-shm`, and `-journal`, to the controlled
`%LOCALAPPDATA%\CDSI\StateProtection\EmergencySafety` directory before replacement.
The safety copy must remain outside the files being replaced and its path must be
included in the normal success or failure notification. A raw emergency copy is
forensic and rollback material; it is not a portable `.cdsibak` and may preserve the
original corruption. The narrow hard-termination notification gap is documented in
Failure Handling.

Example:

```text
pre-restore-<timestamp>.cdsibak
```

---

# 24. Restore Compatibility

Restore must check:

- bundle format version
- Beacon version
- both database schema versions and database roles
- payload sizes and SHA-256 values
- safe fixed archive paths and extraction bounds
- encryption flag (State Bundle v1 accepts only `false`)
- continuous migration history and all required current tables and columns after staged
  migration

State Bundle v1 accepts archives up to 8 GiB, each database payload up to 4 GiB, and
`manifest.json` up to 256 KiB. The parser rejects missing, extra, duplicate,
case-colliding, or path-traversing entries before current state is replaced.

For unsupported newer schema:

```text
This backup was created by a newer version of Beacon.
Upgrade Beacon before restoring it.
```

Do not attempt unsafe downgrade. Older supported schemas may be migrated on staged
copies before replacement. `beacon_version`, platform, and architecture are useful
diagnostics but are not restore gates by themselves.

---

# 25. RSS OPML Export

RSS subscription list must also support standard OPML portability.

Implement:

- Export OPML
- Import OPML

OPML is a fallback portability layer, not a replacement for State Backup.

---

# 26. Data Protection UI

Beacon v0.2.21 exposes a local Data Protection window from the Tools menu:

```text
Data Protection
├── Create local state backup
├── Validate / export selected backup
├── Restore selected local backup
└── Open state backup directory
```

The local list shows creation time, Beacon version, backup kind, file size, location,
and whether the bundle is restorable. The restore confirmation must say that both
SQLite databases will be replaced, application credentials and the installation ID
will not be restored, and original asset files will not be changed.

Do not label a same-disk plaintext backup simply as "Protected". Show this limitation:

```text
Local state backup

Last verified backup: Today 09:12
Location: D:\cdsi_workspace\System\StateBackups

This local backup is not encrypted. A backup on the same disk cannot protect
against loss or failure of that disk.
```

---

# 27. Failure Handling

Archive creation is atomic: a failed temporary archive must not be listed as a valid
backup. Restore is restart-bound and recoverable rather than user-resumable: its
pending transaction records whether replacement has started and supports complete
rollback of both databases.

The main window reserves its stateful-operation gate before entering Data Protection,
drains already-started database writes, and pauses volume reconciliation before showing
the dialog. A Data Protection operation keeps that quiescent state for its full async
lifetime. The dialog rejects title-bar and Alt+F4 closing while it is busy; a successful
restore preparation records the restart request before allowing its programmatic close.

Listing or creating in Beacon's controlled State Backup directory removes only stale
temporary archives that match the exact Beacon-owned naming contract. Beacon does not
scan or delete lookalike files in an arbitrary user-selected export directory. A hard
process termination or power loss during export can therefore leave a hidden
`.<filename>.<GUID>.tmp` file beside the chosen destination for the user to inspect.

A malformed pending record is safety-indeterminate and must stop normal startup instead
of being discarded. Once a restore transaction completes or rolls back safely, failure
to remove plaintext staging data is recorded in a controlled cleanup marker and retried
on the next startup. Recursive cleanup must reject reparse points and paths outside the
application-controlled state-protection root.

For an existing installation identity, a missing `cdsi.db` or `reader.db` is also an
explicit recovery decision rather than an implicit `ReadWriteCreate` event. Before
repositories initialize, the user must choose emergency restore, explicitly accept a
new empty database, or exit. An existing installation upgrading from a version that
never used RSS can explicitly accept creation of an empty `reader.db`.

v0.2.21 does not yet persist a separately acknowledged restore-outcome receipt. If
the process is hard-terminated after a terminal restore state has been durably
recorded and cleanup has completed, but before the result dialog is shown, the next
startup will not replay that result notification. The database pair remains
consistent and the pre-restore safety copy remains retained; a future receipt should
persist outcome metadata until the UI explicitly acknowledges it.

Example:

```text
Bundle validated         DONE
Safety backup created    DONE
Asset database replaced  DONE
Reader database replaced FAILED
Rollback                 DONE
```

---

# 28. Recovery Without CDSI

This feature must pass the CDSI sovereignty test:

> **If CDSI disappears tomorrow, can the user still recover Beacon state?**

For the v0.2.21 local workflow, recovery is possible with:

```text
Local .cdsibak file
+
Beacon-compatible restore implementation
```

Avoid a restore process that requires a live CDSI API unless the user explicitly chose a managed recovery service.

---

# 29. Suggested Application Services

Conceptual services (exact names should continue to follow repository conventions):

```text
IStateBackupService
IStateRestoreService
IStateSnapshotService
IStateBundleService
IStateEncryptionService       future
IBackupRetentionService       future
IBackupCatalogService         future cloud catalog
```

Possible operations:

```text
CreateSnapshotAsync()
ValidateSnapshotAsync()

BuildBundleAsync()
EncryptBundleAsync()          future

UploadBackupAsync()           future
ListRemoteBackupsAsync()      future
DeleteRemoteBackupAsync()     future

RestoreBackupAsync()
VerifyBackupAsync()

ApplyRetentionAsync()         future
```

Exact naming should follow existing project conventions.

---

# 30. Suggested Domain Models

Conceptual model:

```text
StateBackup
- Id
- DeviceId
- CreatedAt
- Kind (Manual / PreRestore)
- LocalPath
- Size
- BeaconVersion
- Databases (role and schema version per database)
- Status
- Checksum
```

```text
Future BackupPolicy
- Enabled
- Interval
- BackupOnExit
- ProviderId
- RetentionPolicy
```

Do not over-model v1.

---

# 31. Database Migration Safety (Future Generalization)

Before Beacon database schema migration:

```text
Create State Snapshot
      ↓
Validate
      ↓
Run Migration
      ↓
Validate Database
```

If migration fails:

- preserve pre-migration backup
- do not silently discard old state

---

# 32. Performance and Concurrency

Requirements:

- avoid long UI blocking
- stream compression and hashing where practical
- use async I/O
- show progress
- keep cancellation in the service boundary for work that has not committed a pending
  restore; the v0.2.21 Data Protection UI does not expose a cancel command
- avoid loading entire bundle into memory unnecessarily
- only one State Bundle or restore preparation pipeline per Beacon instance at a time

For future automatic bundles, if another trigger fires during backup:

```text
Backup already running
↓
mark pending
↓
run later only if state changed
```

---

# 33. Local Backup Location

The managed default location is:

```text
<CDSI workspace>\System\StateBackups
```

Users may export a selected `.cdsibak` to another local destination.

Examples:

- external drive
- NAS-mounted folder
- manually selected folder

Beacon never treats an exported copy as encrypted. An external physical drive or
independently protected NAS is preferable to another directory on the same disk.

---

# 34. Test Cases

Minimum tests:

## Snapshot

- [x] both databases back up while idle
- [x] snapshot through SQLite Online Backup API
- [x] WAL changes cannot be skipped by file timestamp heuristics
- [x] each snapshot opens independently
- [x] full integrity and foreign-key checks pass

## Bundle

- [x] fixed three-entry archive generated atomically
- [x] manifest includes both roles, schema versions, sizes, and SHA-256 values
- [x] compression round trip
- [x] missing, duplicate, unsafe-path, and checksum-corrupt entries rejected by tests
- [ ] add explicit boundary tests for extra entries and the archive/database/manifest
  size ceilings (the implementation already rejects them)
- [ ] add fault-injection coverage proving a failed bundle build cannot publish its
  temporary archive as a final `.cdsibak`

Provider and encryption tests are deferred because v0.2.21 performs no State Bundle
network transfer or encryption.

## Restore

- [x] validate and restore a valid two-database bundle
- [x] reject corrupted backup before replacement
- [x] reject newer unsupported asset or Reader schema
- [x] restore over existing state only after explicit confirmation
- [x] pre-restore safety bundle created
- [x] simulated partial replacement rolls back the complete database pair
- [x] pending restore survives a process restart
- [x] missing or damaged current databases can enter repository-independent emergency
  restore
- [x] emergency rollback restores all eight database/sidecar presence states and bytes
- [x] migration gaps, incomplete current schemas, and an actual v27-to-v28 staged
  migration are covered
- [x] invalid pending state stops startup; deferred controlled cleanup retries later

## RSS

- [ ] add a semantic fixture proving real subscriptions, entries, read state, and
  favorites survive database restore (the whole validated `reader.db` is already
  restored)
- [x] existing OPML and JSON portability remain separate code paths and are not bundle
  payloads

## Security

- [x] Windows Credential Manager values and SSH private keys are not exported
- [x] secrets are absent from logs
- [x] installation identity and original asset files remain unchanged
- [x] validation and staging temporary files are cleaned up safely
- [x] UI discloses that local State Bundle v1 is unencrypted
- [x] UI identifies source client ID, paths, RSS data, and connection metadata as sensitive

---

# 35. v0.2.21 Local Recovery Milestone

- [x] consistent snapshots of `cdsi.db` and `reader.db`
- [x] `PRAGMA integrity_check` and `PRAGMA foreign_key_check`
- [x] fixed State Bundle v1 archive and manifest
- [x] payload size and SHA-256 validation
- [x] streaming ZIP compression
- [x] manual local backup, listing, validation, and export
- [x] explicit local restore through a restart boundary
- [x] pre-restore safety bundle
- [x] emergency raw-file isolation when current repositories cannot initialize
- [x] recoverable two-database replacement and rollback
- [x] Data Protection UI and observable results

This milestone does not modify the object-storage upload stack.

---

# 36. P1: Encrypted and Automated Local Bundles

- [ ] authenticated local encryption
- [ ] user-controlled recovery material
- [ ] automatic periodic State Bundles
- [ ] backup after significant changes, with debounce
- [ ] State Bundle retention policy
- [ ] recovery-key verification workflow
- [ ] optional external-folder destination policy
- [ ] pre-schema-migration State Bundle

---

# 37. P2: Cloud State Backup

- [ ] upload encrypted State Bundles through existing providers
- [ ] cloud restore and rebuildable remote catalog
- [ ] versioned remote retention
- [ ] upload interruption and retry
- [ ] key rotation
- [ ] automated restore verification
- [ ] managed CDSI Backup provider
- [ ] optional encrypted credential recovery
- [ ] future multi-device backup catalog

---

# 38. Explicit Non-Goals

Do not turn this feature into:

- Dropbox clone
- cloud database
- real-time sync engine
- multi-master database
- generic enterprise backup suite

The feature protects:

> **Beacon's user-owned state.**

---

# 39. v0.2.21 Acceptance Criteria

The local recovery milestone is complete only when this scenario works:

```text
1. User has Beacon with:
   - assets
   - tags
   - projects
   - RSS subscriptions
   - RSS history
   - OpenWeb mappings

2. The user explicitly creates a local `.cdsibak` and exports it if protection
   from disk loss is required.

3. The current databases are lost, damaged, or need to be rolled back.

4. User opens Data Protection and selects:
   Restore Beacon

5. User selects a local State Bundle and confirms replacement of current state.

6. Beacon validates both payloads and creates a pre-restore safety bundle.

7. Beacon restarts, applies any supported migration to staged copies, and replaces
   both databases before normal repository initialization.

8. The user regains:
   - asset metadata
   - projects
   - tags
   - RSS subscriptions
   - RSS item history
   - read/favorite state
   - publish mappings

9. Original asset files and the target installation ID remain unchanged. Secrets
   remain outside the bundle and may require re-authorization on another Windows
   account or computer.

10. No cloud provider or CDSI service is contacted.
```

If replacement fails after it begins, Beacon must restore the previous complete
database pair from the transaction rollback area. A validation or migration failure
must leave current state untouched.

---

# 40. Final Architecture

```text
              CDSI Beacon v0.2.21

             ┌───────────┴───────────┐
             │                       │
          cdsi.db                reader.db
             │                       │
             └──── SQLite snapshots ┘
                         │
               Full integrity checks
                         │
             Local State Bundle v1
               (manual, unencrypted)
                         │
              User-managed local copy
                         │
             Validate and stage restore
                         │
                  Restart boundary
                         │
          Replace both DBs or roll back both
```

Core rule:

> **Local-first does not mean single-copy.**

Beacon remains local-first. v0.2.21 supplies the portable local recovery unit; users
must export it to another physical destination to protect against loss of the disk
that contains both the database and managed workspace. Encrypted provider-backed
copies are a later milestone.
