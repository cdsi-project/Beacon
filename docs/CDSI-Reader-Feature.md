# Feature: CDSI Reader / Sources
## Beacon RSS / Atom / JSON Feed 订阅模块

> 目标：在 Beacon 中增加一个“Sources / Reader”模块，让用户可以订阅开放 Web 信息源，并将订阅关系、阅读状态、收藏和归档内容保存在自己的本地数据中。
>
> 本 Feature 第一阶段不引入 AI，不做推荐算法，不做复杂社交功能。先完成一个可靠、开放、可导入、可导出、本地优先的 Reader。

## 当前实现状态（Beacon v0.2.16）

本轮已完成 Reader Core：直接订阅 RSS 2.0、Atom 和 JSON Feed URL，三栏阅读界面，单文件夹归类，已读/未读、收藏、搜索、手动刷新、ETag、Last-Modified、304、抓取日志、稳定去重、OPML 导入导出，以及包含条目和阅读状态的完整 JSON 备份恢复。

Reader 使用独立的 `%LOCALAPPDATA%\CDSI\reader.db`，并在工作目录的 `System\DatabaseBackups\Reader` 中生成一致性快照。Feed 内容只以纯文本呈现；本机和局域网来源默认拒绝，需在添加订阅时显式允许。

尚未实现：从普通网站自动发现 Feed、稍后读、归档、定时刷新、并发刷新、网页全文抓取、保存到 Beacon 资产库、标注和 AI 能力。下方未勾选项仍属于后续路线图，不代表当前版本能力。

---

# 1. Feature 定位

## 1.1 一句话定义

> CDSI Reader 是 Beacon 中负责“我的信息输入”的模块。

Beacon 当前主要处理：

```text
My Assets
├── Documents
├── Images
├── Videos
├── Audio
└── Published Content
```

新增：

```text
My Sources
├── RSS
├── Atom
├── JSON Feed
└── Reader
```

长期目标：

```text
CDSI
├── My Assets
│   └── 我拥有的数字资产
│
└── My Sources
    └── 我主动选择的信息源
```

---

# 2. 产品原则

本模块必须遵循：

1. Local-first
2. 用户拥有自己的订阅关系
3. 用户拥有自己的阅读状态
4. 支持 OPML 导入与导出
5. 不制造数据锁定
6. 不强依赖 CDSI 云端
7. 第一版不需要账号
8. 第一版不需要 AI
9. 第一版不做算法推荐
10. 所有抓取和阅读数据默认保存在本机

核心原则：

> 进得来，也出得去。

---

# 3. MVP 范围

第一版必须支持：

- [x] 添加 Feed URL
- [ ] 输入网站 URL 自动发现 Feed
- [x] RSS 2.0
- [x] Atom
- [x] JSON Feed
- [x] Feed 列表
- [x] Folder / 分类
- [x] Entry 列表
- [x] 未读 / 已读
- [x] 收藏
- [ ] 稍后阅读
- [ ] Archive
- [x] OPML Import
- [x] OPML Export
- [x] SQLite 本地存储
- [x] ETag
- [x] Last-Modified
- [x] 304 Not Modified
- [ ] 定时刷新
- [x] Fetch Log
- [x] 手动刷新 Feed
- [ ] Save to Beacon Library

---

# 4. 第一版明确不做

MVP 不做：

- AI 摘要
- AI 翻译
- AI 推荐
- 语义搜索
- Newsletter 邮箱接收
- Web Page Monitor
- YouTube API
- 社交关系
- 评论
- 点赞
- 云同步
- CDSI Account 强绑定
- 服务端统一 Feed 抓取
- 复杂全文抓取
- 复杂反爬
- 浏览器扩展
- 移动端

---

# 5. 模块名称

代码目录建议：

```text
Reader/
```

产品 UI 可以显示：

```text
Sources
```

或者：

```text
Reader
```

推荐最终 UI：

```text
Sources
```

原因：

> 长期不仅支持 RSS，未来可以扩展到 Podcast、Newsletter、GitHub Releases、Web Monitor 等其他 Source 类型。

MVP 内部可以先叫：

```text
Reader
```

---

# 6. Beacon UI 信息架构

建议：

```text
Beacon

├── Library
├── Sources
│   ├── All
│   ├── Unread
│   ├── Starred
│   ├── Read Later
│   ├── Archived
│   └── Folders
│
├── Publisher
├── Backup
└── Settings
```

---

# 7. Reader 主界面

三栏布局建议：

```text
┌──────────────┬──────────────────────┬──────────────────────────┐
│ SOURCES      │ ENTRIES              │ READER                   │
│              │                      │                          │
│ All      32  │ Article title        │ Article Title            │
│ Unread   18  │ Source · 2h ago      │                          │
│ Starred   5  │                      │ Author / Date            │
│              │ Article title        │                          │
│ Tech         │ Source · 5h ago      │ Article Content          │
│   Blog A     │                      │                          │
│   Blog B     │                      │                          │
│              │                      │                          │
│ Auto         │                      │                          │
│   VW Blog    │                      │                          │
└──────────────┴──────────────────────┴──────────────────────────┘
```

---

# 8. Feed 添加流程

用户支持两种输入方式。

## 8.1 直接输入 Feed URL

例如：

```text
https://example.com/feed.xml
```

系统：

```text
Fetch
↓
Detect Feed Type
↓
Parse
↓
Preview
↓
Save
```

## 8.2 输入网站 URL

例如：

```text
https://example.com
```

系统自动发现：

```html
<link
  rel="alternate"
  type="application/rss+xml"
  href="/feed.xml">
```

同时支持：

```text
application/rss+xml
application/atom+xml
application/feed+json
```

流程：

```text
Website URL
↓
Fetch HTML
↓
Find <link rel="alternate">
↓
Resolve Relative URL
↓
Validate Feed
↓
Show Candidates
```

如果只有一个 Feed：

```text
直接预选
```

如果多个：

```text
让用户选择
```

---

# 9. Feed 类型

MVP 支持：

```text
RSS 2.0
Atom
JSON Feed
```

统一转换为内部模型：

```text
Feed
Entry
```

不要让 UI 关心底层格式。

---

# 10. Feed 数据模型

```csharp
public sealed class Feed
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string? SiteUrl { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public FeedType Type { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastEntryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}
```

---

# 11. Entry 数据模型

```csharp
public sealed class Entry
{
    public Guid Id { get; set; }
    public Guid FeedId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
```

ExternalId 优先使用：

```text
RSS guid
Atom id
JSON Feed id
```

fallback：

```text
Canonical URL
```

最后才允许：

```text
Hash(feed + title + date)
```

---

# 12. Entry State

不要把用户状态直接混进原始 Entry。

```csharp
public sealed class EntryState
{
    public Guid EntryId { get; set; }
    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public bool IsReadLater { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? StarredAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
```

理由：

> Source data 和 User state 分离。

---

# 13. Folder

```csharp
public sealed class FeedFolder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
```

支持：

```text
一个 Feed 属于一个 Folder
```

MVP 暂不做多 Folder / 多 Tag。

后续可增加 Tag。

---

# 14. SQLite Schema

建议数据库：

```text
reader.db
```

v0.2.16 已确定使用独立 Reader 数据库，不合并进资产主数据库。这样 Reader 的外部订阅内容、阅读记录和抓取错误不会扩大 `cdsi.db` 的资产数据边界，也可独立备份和演进。

表：

```text
reader_feeds
reader_entries
reader_entry_states
reader_folders
reader_feed_folders
reader_fetch_logs
```

---

# 15. Fetcher

核心服务：

```csharp
public interface IFeedFetcher
{
    Task<FeedFetchResult> FetchAsync(
        Feed feed,
        CancellationToken cancellationToken = default);
}
```

必须支持：

```text
ETag
If-None-Match

Last-Modified
If-Modified-Since
```

---

# 16. HTTP Conditional Request

第一次：

```http
GET /feed.xml
```

Response：

```http
ETag: "abc123"
Last-Modified: Tue, 01 Sep 2026 12:00:00 GMT
```

保存：

```text
ETag
LastModified
```

下一次：

```http
If-None-Match: "abc123"
If-Modified-Since: Tue, 01 Sep 2026 12:00:00 GMT
```

如果：

```http
304 Not Modified
```

处理：

```text
不 Parse
不写 Entry
只更新 LastCheckedAt
```

---

# 17. Feed Parser

接口：

```csharp
public interface IFeedParser
{
    bool CanParse(string contentType, ReadOnlySpan<char> content);
    ParsedFeed Parse(string content);
}
```

实现：

```text
Rss20Parser
AtomParser
JsonFeedParser
```

如果使用成熟 NuGet 库：

> 可以使用，但必须通过 Adapter 封装，不允许 UI 和业务层直接依赖第三方 Feed Library。

---

# 18. Feed Discovery

接口：

```csharp
public interface IFeedDiscoveryService
{
    Task<IReadOnlyList<DiscoveredFeed>> DiscoverAsync(
        Uri website,
        CancellationToken cancellationToken = default);
}
```

处理：

- HTML link alternate
- Relative URL
- Absolute URL
- Duplicate Candidate
- Redirect
- Unsupported Content-Type
- Invalid Feed

---

# 19. Duplicate Detection

Feed 去重：

```text
Normalized Feed URL
```

Entry 去重：

优先：

```text
ExternalId + FeedId
```

其次：

```text
Canonical URL + FeedId
```

不允许因为标题相同就判断重复。

---

# 20. Scheduler

MVP 支持自动刷新。

初始策略：

```text
默认：
60 minutes
```

后续增加 Adaptive Scheduler。

---

# 21. Adaptive Fetch Scheduler

后续 P1 实现。

思路：

```text
更新频繁的 Feed
15 min

普通 Feed
1 hour

低频 Feed
6 hour

长期无更新
12 hour
```

根据：

```text
历史更新间隔
最近成功时间
连续无更新次数
Error Count
```

动态计算。

---

# 22. Fetch Log

每次 Fetch 记录：

```text
FeedId
StartedAt
FinishedAt
HttpStatus
Result
NewEntries
ResponseBytes
ETagChanged
DurationMs
Error
```

UI 可以显示：

```text
Last checked: 5 min ago
Last success: 5 min ago
New items: 3
```

---

# 23. Error Handling

需要区分：

```text
DNS Error
Timeout
TLS Error
HTTP 404
HTTP 403
HTTP 429
HTTP 5xx
Invalid XML
Invalid JSON
Unsupported Feed
Empty Feed
Redirect Loop
```

Feed 抓取失败：

```text
不能删除旧数据
不能影响其他 Feed
不能让应用崩溃
```

---

# 24. Refresh

支持：

```text
Refresh All
Refresh Folder
Refresh Feed
```

必须：

```text
支持 CancellationToken
限制并发
```

初始：

```text
MaxConcurrentFetches = 4
```

做成配置。

---

# 25. Reader State

UI 支持：

```text
All
Unread
Starred
Read Later
Archived
```

动作：

```text
Mark Read
Mark Unread
Star
Unstar
Read Later
Archive
```

---

# 26. Auto Mark Read

默认：

```text
用户打开 Entry 后标记 Read
```

设置中可配置：

```text
Open = Read
Manual Only
```

MVP 可以先只实现：

```text
Open = Read
```

---

# 27. OPML Import

必须支持标准 OPML。

流程：

```text
Choose OPML
↓
Parse
↓
Preview
↓
Detect Duplicate
↓
Import
```

保留：

```text
Feed URL
Site URL
Title
Folder
```

Import 不允许：

```text
重复创建同一个 Feed
```

---

# 28. OPML Export

Export 内容：

```text
所有启用 Feed
Folder 结构
Feed URL
Site URL
Title
```

原则：

> 用户在任何时候都可以把完整订阅关系带走。

---

# 29. Save to Beacon Library

这是 CDSI Reader 的核心差异功能。

Entry 操作：

```text
Save to Library
```

流程：

```text
Reader Entry
↓
Archive Service
↓
Generate Local Asset
↓
Beacon Library
```

---

# 30. Archive 格式

建议：

```text
Library/
└── Articles/
    └── YYYY/
        └── article-slug/
            ├── article.md
            ├── metadata.json
            └── assets/
```

metadata.json：

```json
{
  "source_type": "rss",
  "title": "...",
  "author": "...",
  "source_name": "...",
  "source_url": "...",
  "original_url": "...",
  "published_at": "...",
  "saved_at": "..."
}
```

---

# 31. Archive 原则

Save to Library 必须保留来源信息。

不得：

```text
把外部内容伪装为用户原创内容
```

必须保留：

```text
Original URL
Source
Author
PublishedAt
SavedAt
```

后续用户可增加：

```text
Notes
Annotations
Commentary
```

---

# 32. Copyright / Content Storage

MVP 默认：

> 优先保存 Feed 中实际提供的 content / summary。

不要第一版自动抓取网站完整正文。

---

# 33. Search

MVP 支持普通本地搜索：

```text
Title
Author
Summary
```

P1：

```text
Content
```

第一版无需向量数据库。

---

# 34. Settings

Reader 设置：

```text
Refresh Interval
Max Concurrent Fetches
Auto Mark Read
Default Archive Location
Retention Policy
```

第一版 Retention 默认：

```text
Keep All
```

---

# 35. Privacy

必须遵守：

```text
不上传订阅列表
不上传阅读状态
不上传收藏
不上传浏览记录
不需要账号
```

HTTP 请求直接：

```text
Beacon
↓
Source Website
```

不经过 CDSI 中央服务器。

---

# 36. Telemetry

第一版：

```text
NO TELEMETRY
```

---

# 37. Suggested Project Structure

```text
Reader/
├── Models/
│   ├── Feed.cs
│   ├── Entry.cs
│   ├── EntryState.cs
│   └── FeedFolder.cs
│
├── Services/
│   ├── FeedFetcher.cs
│   ├── FeedDiscoveryService.cs
│   ├── FeedParser.cs
│   ├── ReaderScheduler.cs
│   ├── OpmlService.cs
│   └── ArchiveService.cs
│
├── Parsers/
│   ├── Rss20Parser.cs
│   ├── AtomParser.cs
│   └── JsonFeedParser.cs
│
├── Storage/
│   ├── ReaderRepository.cs
│   └── Migrations/
│
├── ViewModels/
└── Views/
```

---

# 38. Security

Feed 是外部不可信输入。

必须注意：

```text
HTML Sanitization
XML External Entity
JSON Size
Redirect Limit
Download Size Limit
Timeout
Invalid Encoding
```

必须禁用：

```text
XML External Entity / XXE
```

HTML Content 必须 sanitize 后再显示。

---

# 39. Network Limits

设置：

```text
Timeout: 15~30 sec
Max Feed Size
Max Redirects
Max Entries Per Fetch
```

例如：

```text
Max Feed Size = 10 MB
Max Redirects = 5
```

---

# 40. Performance

要求：

```text
后台空闲 CPU 接近 0
不高频轮询
Feed Fetch 异步
DB 写入批处理
UI 不阻塞网络
```

---

# 41. P0 TODO

## Core
- [x] Reader 模块初始化
- [x] Feed model
- [x] Entry model
- [x] EntryState model
- [ ] Folder model
- [x] Reader database migration

## Fetch
- [x] HttpClient configuration
- [x] FeedFetcher
- [x] ETag
- [x] Last-Modified
- [x] 304 support
- [x] Redirect handling
- [x] Timeout handling
- [x] Fetch log

## Parse
- [x] RSS 2.0 parser
- [x] Atom parser
- [x] JSON Feed parser
- [x] Normalize to Feed / Entry
- [x] Entry duplicate detection

## Discovery
- [ ] Website HTML fetch
- [ ] rel=alternate parser
- [ ] RSS candidate
- [ ] Atom candidate
- [ ] JSON Feed candidate
- [x] relative URL resolution

## Storage
- [x] Feed repository
- [x] Entry repository
- [x] Entry state repository
- [ ] Folder repository
- [x] Fetch log repository

## Reader UI
- [x] Sources navigation
- [x] Feed list
- [x] Unread count
- [x] Entry list
- [x] Reader view
- [x] Mark read
- [x] Mark unread
- [x] Star
- [ ] Read later
- [ ] Archive

## Refresh
- [x] Refresh Feed
- [ ] Refresh Folder
- [x] Refresh All
- [ ] Concurrency limit
- [ ] Basic scheduler

## OPML
- [x] Import
- [x] Duplicate detection
- [x] Folder restore
- [x] Export

## Library
- [ ] Save to Beacon Library
- [ ] Markdown generation
- [ ] metadata.json
- [ ] Preserve source information

---

# 42. P1 TODO

- [ ] Adaptive Fetch Scheduler
- [x] Search
- [ ] Entry tags
- [ ] Multiple folders / tags
- [ ] Feed health status
- [ ] Retention rules
- [ ] Full Text Extraction
- [ ] Annotation
- [ ] Notes
- [ ] Keyboard shortcuts
- [ ] Reader appearance options

---

# 43. P2 TODO

新增 Source Types：

```text
Podcast
GitHub Releases
YouTube RSS
Newsletter
Web Page Monitor
Sitemap
```

抽象：

```text
ISource
ISourceFetcher
ISourceEntry
```

使 RSS 只是 Source 的一种。

---

# 44. P3 / AI

AI 必须在 Reader 核心功能稳定以后再加入。

可选：

```text
Summarize
Translate
Topic Classification
Semantic Search
Duplicate / Similar Story Detection
Personal Recommendation
Daily Digest
```

原则：

> 用户可以自行选择 LLM Provider。

---

# 45. Codex 开发约束

Codex 必须遵守：

1. 不把 Reader 逻辑写进页面 Code Behind。
2. Fetch / Parse / Storage / UI 分层。
3. RSS / Atom / JSON Feed 统一内部模型。
4. UI 不直接解析 XML。
5. UI 不直接执行 HTTP。
6. 所有 HTTP 操作支持 CancellationToken。
7. 所有 Feed 数据视为不可信输入。
8. XML 禁止 XXE。
9. HTML 必须 sanitize。
10. 不上传用户订阅数据。
11. 不增加 Telemetry。
12. 第一版不增加 AI。
13. 第一版不做复杂全文抓取。
14. OPML Import / Export 是 MVP。
15. Save to Library 必须保存原始来源。
16. 单个 Feed 错误不能影响整个 Reader。
17. Fetch Scheduler 不能无限并发。
18. 数据库操作异步。
19. Parser 需要 Unit Test。
20. URL / Redirect / Duplicate 场景需要测试。

---

# 46. Codex 实现顺序

```text
STEP 1
Models + SQLite

↓

STEP 2
RSS 2.0 Parser

↓

STEP 3
FeedFetcher

↓

STEP 4
Add Feed URL

↓

STEP 5
Feed List + Entry List

↓

STEP 6
Read / Star State

↓

STEP 7
Atom

↓

STEP 8
JSON Feed

↓

STEP 9
ETag / Last-Modified

↓

STEP 10
Feed Discovery

↓

STEP 11
OPML Import / Export

↓

STEP 12
Scheduler

↓

STEP 13
Archive / Save to Library
```

不要一开始开发：

```text
AI
复杂搜索
推荐
Web Monitor
```

---

# 47. MVP 验收标准

必须完成：

```text
用户输入网站 URL
↓
自动发现 Feed
↓
Subscribe
↓
Reader 抓取
↓
显示 Entry
↓
阅读
↓
Mark Read
↓
Star
↓
关闭 Beacon
↓
重新打开
↓
状态仍存在
↓
Export OPML
↓
Save to Beacon Library
```

另外验证：

```text
304 Not Modified 正常
Fetch Error 不丢旧数据
重复 Fetch 不产生重复 Entry
OPML Export 可以重新导入
```

---

# 48. Feature 成功标准

成功不是：

> Beacon 能显示 RSS。

真正成功标准：

> 用户可以把“我关注谁、我读过什么、我收藏什么、我保存了什么”作为自己的本地数字数据长期持有，并随时迁移。

---

# 49. 长期方向

短期：

```text
RSS Reader
```

中期：

```text
CDSI Reader
```

长期：

```text
CDSI Sources
```

最终模型：

```text
CDSI

INPUT
└── Sources
    ├── RSS
    ├── Atom
    ├── JSON Feed
    ├── Podcast
    ├── Newsletter
    └── Web Monitor

OUTPUT / ASSETS
└── Library
    ├── Articles
    ├── Videos
    ├── Images
    └── Documents
```

用户形成闭环：

```text
Sources
↓
Read
↓
Save
↓
Library
↓
Write / Create
↓
OpenWeb
↓
Publish
```

---

# 最终定义

> **CDSI Reader 不是“帮用户看 RSS”，而是帮助用户拥有自己的信息输入关系。**
