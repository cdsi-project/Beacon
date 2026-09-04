using System.Diagnostics;
using CDSI.Agent.Application.Reader;
using CDSI.Agent.Core.Reader;

namespace CDSI.Agent.WinForms;

public sealed partial class MainForm
{
    private readonly StateDatabaseWriteGate _stateDatabaseWriteGate = new();
    private readonly ReaderApplicationService _readerService;
    private readonly TabPage _readerTabPage = new("RSS订阅");
    private readonly TreeView _readerSourceTree = new();
    private readonly DataGridView _readerEntryGrid = new();
    private readonly RichTextBox _readerContentTextBox = new();
    private readonly Label _readerTitleLabel = new();
    private readonly Label _readerMetadataLabel = new();
    private readonly LinkLabel _readerOriginalLink = new();
    private readonly ToolStripTextBox _readerSearchTextBox = new();
    private readonly ToolStripLabel _readerSummaryLabel = new();
    private readonly ToolStrip _readerToolbar = new();
    private readonly ContextMenuStrip _readerSourceContextMenu = new();
    private readonly ContextMenuStrip _readerEntryContextMenu = new();
    private Font? _readerUnreadFont;
    private IReadOnlyList<ReaderFeedSummary> _readerFeeds = [];
    private IReadOnlyList<ReaderEntryListItem> _readerEntries = [];
    private bool _readerUpdating;
    private bool _readerInitialized;

    private void ConfigureReaderTab()
    {
        _readerTabPage.Padding = Padding.Empty;
        _readerTabPage.BackColor = Color.White;
        _readerTabPage.Controls.Add(CreateReaderLayout());
        Disposed += (_, _) => _readerUnreadFont?.Dispose();
        ConfigureReaderSourceContextMenu();
        ConfigureReaderEntryContextMenu();
        _mainTabControl.Selected += async (_, e) =>
        {
            if (ReferenceEquals(e.TabPage, _readerTabPage))
            {
                try
                {
                    await ShowSelectedReaderEntryAsync(markRead: true);
                }
                catch (Exception exception)
                {
                    ShowError("无法打开 RSS 条目", exception);
                }
            }
        };
    }

    internal static void ConfigureReaderEntryGrid(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeColumns = true;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 36;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.ReadOnly = true;
        grid.AccessibleName = "RSS 条目列表";
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ReadStatus",
            HeaderText = "状态",
            Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Starred",
            HeaderText = "收藏",
            Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "EntryTitle",
            HeaderText = "标题",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100,
            MinimumWidth = 180,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FeedTitle",
            HeaderText = "来源",
            Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PublishedAt",
            HeaderText = "时间",
            Width = 128,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private Control CreateReaderLayout()
    {
        ConfigureReaderEntryGrid(_readerEntryGrid);
        _readerUnreadFont = new Font(_readerEntryGrid.Font, FontStyle.Bold);
        _readerEntryGrid.SelectionChanged += ReaderEntryGrid_SelectionChanged;
        _readerEntryGrid.CellDoubleClick += (_, _) => OpenSelectedReaderEntry();

        _readerSourceTree.Dock = DockStyle.Fill;
        _readerSourceTree.BorderStyle = BorderStyle.None;
        _readerSourceTree.HideSelection = false;
        _readerSourceTree.FullRowSelect = true;
        _readerSourceTree.ItemHeight = 26;
        _readerSourceTree.AccessibleName = "RSS 订阅源";
        _readerSourceTree.AfterSelect += ReaderSourceTree_AfterSelect;
        _readerSourceTree.NodeMouseClick += ReaderSourceTree_NodeMouseClick;

        var toolbar = CreateReaderToolbar();
        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            Size = new Size(1100, 560),
            SplitterDistance = 235,
            Panel1MinSize = 170,
            Panel2MinSize = 600
        };
        outer.Panel1.Padding = new Padding(0, 0, 6, 0);
        outer.Panel2.Padding = new Padding(6, 0, 0, 0);
        outer.Panel1.Controls.Add(CreateReaderPane("信息源", _readerSourceTree));

        var inner = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None,
            Size = new Size(850, 560),
            SplitterDistance = 390,
            Panel1MinSize = 280,
            Panel2MinSize = 300
        };
        inner.Panel1.Padding = new Padding(0, 0, 6, 0);
        inner.Panel2.Padding = new Padding(6, 0, 0, 0);
        inner.Panel1.Controls.Add(CreateReaderPane("条目", _readerEntryGrid));
        inner.Panel2.Controls.Add(CreateReaderContentPane());
        outer.Panel2.Controls.Add(inner);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(outer, 0, 1);
        return layout;
    }

    private ToolStrip CreateReaderToolbar()
    {
        var toolbar = _readerToolbar;
        toolbar.Dock = DockStyle.Fill;
        toolbar.GripStyle = ToolStripGripStyle.Hidden;
        toolbar.BackColor = Color.White;
        toolbar.Padding = new Padding(6, 5, 6, 5);
        toolbar.AccessibleName = "RSS 订阅工具栏";
        var add = new ToolStripButton("添加订阅");
        add.Click += async (_, _) => await AddReaderSubscriptionAsync();
        var refresh = new ToolStripButton("刷新全部");
        refresh.Click += async (_, _) => await RefreshAllReaderFeedsAsync();
        var importOpml = new ToolStripButton("导入 OPML");
        importOpml.Click += async (_, _) => await ImportReaderOpmlAsync();
        var exportOpml = new ToolStripButton("导出 OPML");
        exportOpml.Click += async (_, _) => await ExportReaderOpmlAsync();
        var importData = new ToolStripButton("恢复数据");
        importData.Click += async (_, _) => await ImportReaderDataAsync();
        var exportData = new ToolStripButton("备份数据");
        exportData.Click += async (_, _) => await ExportReaderDataAsync();
        _readerSearchTextBox.AutoSize = false;
        _readerSearchTextBox.Width = 180;
        _readerSearchTextBox.AccessibleName = "RSS 订阅搜索";
        _readerSearchTextBox.ToolTipText = "搜索标题、作者和摘要";
        _readerSearchTextBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            await RefreshReaderEntriesAsync();
        };
        var search = new ToolStripButton("搜索");
        search.Click += async (_, _) => await RefreshReaderEntriesAsync();
        _readerSummaryLabel.Alignment = ToolStripItemAlignment.Right;
        _readerSummaryLabel.ForeColor = Color.FromArgb(101, 111, 120);
        _readerSummaryLabel.Text = "尚未加载";
        toolbar.Items.AddRange(
        [
            add,
            refresh,
            new ToolStripSeparator(),
            importOpml,
            exportOpml,
            importData,
            exportData,
            new ToolStripSeparator(),
            new ToolStripLabel("检索"),
            _readerSearchTextBox,
            search,
            _readerSummaryLabel
        ]);
        return toolbar;
    }

    private static Control CreateReaderPane(string title, Control content)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Color.FromArgb(31, 37, 43)
        }, 0, 0);
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private Control CreateReaderContentPane()
    {
        _readerTitleLabel.Dock = DockStyle.Fill;
        _readerTitleLabel.Text = "选择一篇条目";
        _readerTitleLabel.Font = new Font("Segoe UI Semibold", 13F);
        _readerTitleLabel.ForeColor = Color.FromArgb(31, 37, 43);
        _readerTitleLabel.AutoEllipsis = true;
        _readerTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _readerMetadataLabel.Dock = DockStyle.Fill;
        _readerMetadataLabel.ForeColor = Color.FromArgb(101, 111, 120);
        _readerMetadataLabel.AutoEllipsis = true;
        _readerMetadataLabel.TextAlign = ContentAlignment.MiddleLeft;
        _readerOriginalLink.Dock = DockStyle.Fill;
        _readerOriginalLink.Text = string.Empty;
        _readerOriginalLink.TextAlign = ContentAlignment.MiddleLeft;
        _readerOriginalLink.LinkClicked += (_, _) => OpenSelectedReaderEntry();
        _readerContentTextBox.Dock = DockStyle.Fill;
        _readerContentTextBox.ReadOnly = true;
        _readerContentTextBox.BorderStyle = BorderStyle.None;
        _readerContentTextBox.BackColor = Color.White;
        _readerContentTextBox.DetectUrls = true;
        _readerContentTextBox.Font = new Font("Segoe UI", 10F);
        _readerContentTextBox.AccessibleName = "RSS 条目正文";
        _readerContentTextBox.LinkClicked += (_, e) => OpenReaderUrl(e.LinkText);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12, 2, 8, 8),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_readerTitleLabel, 0, 0);
        layout.Controls.Add(_readerMetadataLabel, 0, 1);
        layout.Controls.Add(_readerOriginalLink, 0, 2);
        layout.Controls.Add(_readerContentTextBox, 0, 3);
        return layout;
    }

    private async Task InitializeReaderAsync()
    {
        try
        {
            await _readerService.InitializeAsync();
            _readerInitialized = true;
            await RefreshReaderAsync();
        }
        catch (Exception exception)
        {
            _readerInitialized = false;
            _readerTabPage.Enabled = false;
            _readerSummaryLabel.Text = "RSS订阅初始化失败";
            _runtimeLog.WriteError("Reader 初始化失败", exception);
        }
    }

    private async Task RefreshReaderAsync()
    {
        if (!_readerInitialized)
        {
            return;
        }

        var selected = GetReaderNavigation();
        _readerFeeds = await _readerService.ListFeedsAsync();
        PopulateReaderSourceTree(selected);
        await RefreshReaderEntriesAsync();
    }

    private void PopulateReaderSourceTree(ReaderNavigation? selected)
    {
        _readerUpdating = true;
        try
        {
            _readerSourceTree.BeginUpdate();
            _readerSourceTree.Nodes.Clear();
            var total = _readerFeeds.Sum(item => item.EntryCount);
            var unread = _readerFeeds.Sum(item => item.UnreadCount);
            _readerSourceTree.Nodes.Add(CreateReaderNode(
                $"全部  {total:N0}",
                new ReaderNavigation(null, false, false)));
            _readerSourceTree.Nodes.Add(CreateReaderNode(
                $"未读  {unread:N0}",
                new ReaderNavigation(null, true, false)));
            _readerSourceTree.Nodes.Add(CreateReaderNode(
                "收藏",
                new ReaderNavigation(null, false, true)));
            var sources = new TreeNode("订阅源") { Name = "Sources" };
            foreach (var folderGroup in _readerFeeds.GroupBy(
                         item => item.Feed.FolderName ?? string.Empty,
                         StringComparer.OrdinalIgnoreCase))
            {
                var parent = sources;
                if (!string.IsNullOrWhiteSpace(folderGroup.Key))
                {
                    parent = new TreeNode(folderGroup.Key);
                    sources.Nodes.Add(parent);
                }

                foreach (var summary in folderGroup)
                {
                    parent.Nodes.Add(CreateReaderNode(
                        $"{summary.Feed.Title}  {summary.UnreadCount:N0}",
                        new ReaderNavigation(summary.Feed.Id, false, false)));
                }
            }

            _readerSourceTree.Nodes.Add(sources);
            sources.Expand();
            foreach (TreeNode folder in sources.Nodes)
            {
                folder.Expand();
            }

            _readerSourceTree.SelectedNode = FindReaderNode(selected) ?? _readerSourceTree.Nodes[0];
            _readerSummaryLabel.Text = $"{_readerFeeds.Count:N0} 个订阅 · {unread:N0} 未读";
        }
        finally
        {
            _readerSourceTree.EndUpdate();
            _readerUpdating = false;
        }
    }

    private async Task RefreshReaderEntriesAsync()
    {
        if (!_readerInitialized || _readerUpdating)
        {
            return;
        }

        var navigation = GetReaderNavigation() ?? new ReaderNavigation(null, false, false);
        _readerEntries = await _readerService.ListEntriesAsync(
            new ReaderEntryQuery(
                navigation.FeedId,
                navigation.UnreadOnly,
                navigation.StarredOnly,
                _readerSearchTextBox.Text,
                500));
        _readerUpdating = true;
        try
        {
            _readerEntryGrid.Rows.Clear();
            foreach (var item in _readerEntries)
            {
                var entry = item.Entry;
                var rowIndex = _readerEntryGrid.Rows.Add(
                    entry.IsRead ? "已读" : "未读",
                    entry.IsStarred ? "是" : string.Empty,
                    entry.Title,
                    item.FeedTitle,
                    FormatReaderDate(entry.PublishedAt ?? entry.UpdatedAt ?? entry.FetchedAt));
                var row = _readerEntryGrid.Rows[rowIndex];
                row.Tag = item;
                if (!entry.IsRead)
                {
                    row.DefaultCellStyle.Font = _readerUnreadFont;
                }
            }

            if (_readerEntryGrid.Rows.Count > 0)
            {
                _readerEntryGrid.Rows[0].Selected = true;
                _readerEntryGrid.CurrentCell = _readerEntryGrid.Rows[0].Cells[0];
            }
            else
            {
                ClearReaderContent();
            }

            _readerSummaryLabel.Text = $"{_readerFeeds.Count:N0} 个订阅 · 当前 {_readerEntries.Count:N0} 条";
        }
        finally
        {
            _readerUpdating = false;
        }

        await ShowSelectedReaderEntryAsync(markRead: false);
    }

    private async void ReaderEntryGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (!_readerUpdating)
        {
            try
            {
                await ShowSelectedReaderEntryAsync(
                    markRead: ReferenceEquals(_mainTabControl.SelectedTab, _readerTabPage));
            }
            catch (Exception exception)
            {
                ShowError("无法打开 RSS 条目", exception);
            }
        }
    }

    private async Task ShowSelectedReaderEntryAsync(bool markRead)
    {
        if (_readerEntryGrid.CurrentRow?.Tag is not ReaderEntryListItem selected)
        {
            ClearReaderContent();
            return;
        }

        var item = await _readerService.GetEntryAsync(selected.Entry.Id);
        if (item is null)
        {
            ClearReaderContent();
            return;
        }

        var entry = item.Entry;
        _readerTitleLabel.Text = entry.Title;
        _readerMetadataLabel.Text = string.Join(
            " · ",
            new[]
            {
                item.FeedTitle,
                entry.Author,
                FormatReaderDate(entry.PublishedAt ?? entry.UpdatedAt)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        _readerOriginalLink.Text = string.IsNullOrWhiteSpace(entry.Url) ? string.Empty : "打开原文";
        _readerOriginalLink.Tag = entry.Url;
        _readerContentTextBox.Text = entry.Content ?? entry.Summary ?? "该条目没有提供正文或摘要。";
        if (markRead && !entry.IsRead)
        {
            if (!await _stateDatabaseWriteGate.TryRunAsync(
                    () => _readerService.SetEntryReadAsync(entry.Id, true)))
            {
                return;
            }

            selected = item with { Entry = entry with { IsRead = true, ReadAt = DateTimeOffset.UtcNow } };
            _readerEntryGrid.CurrentRow.Tag = selected;
            _readerEntryGrid.CurrentRow.Cells["ReadStatus"].Value = "已读";
            _readerEntryGrid.CurrentRow.DefaultCellStyle.Font = _readerEntryGrid.Font;
            await RefreshReaderFeedsOnlyAsync();
        }
    }

    private async Task AddReaderSubscriptionAsync()
    {
        using var dialog = new ReaderSubscriptionDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunReaderTaskAsync(
            "正在添加订阅",
            dialog.FeedUrl,
            async token =>
            {
                var feed = await _readerService.SubscribeAsync(
                    new ReaderSubscribeRequest(
                        dialog.FeedUrl,
                        dialog.PreferredTitle,
                        dialog.FolderName,
                        dialog.AllowPrivateNetwork),
                    token);
                _statusLabel.Text = $"已订阅：{feed.Title}";
            });
    }

    private async Task RefreshAllReaderFeedsAsync()
    {
        await RunReaderTaskAsync(
            "正在刷新订阅",
            "所有订阅源",
            async token =>
            {
                var progress = new Progress<ReaderRefreshProgress>(UpdateReaderProgress);
                var summary = await _readerService.RefreshAllAsync(progress, token);
                _statusLabel.Text = summary.Failed == 0
                    ? $"订阅刷新完成 · 新增 {summary.NewEntries:N0} 条"
                    : $"订阅刷新完成 · 成功 {summary.Succeeded:N0} · 失败 {summary.Failed:N0}";
                if (summary.Failed > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine, summary.Errors.Take(10)),
                        "部分订阅刷新失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            });
    }

    private async Task RefreshSelectedReaderFeedAsync()
    {
        var feedId = GetReaderNavigation()?.FeedId;
        var feed = _readerFeeds.FirstOrDefault(item => item.Feed.Id == feedId)?.Feed;
        if (feed is null)
        {
            return;
        }

        await RunReaderTaskAsync(
            "正在刷新订阅",
            feed.Title,
            async token =>
            {
                var result = await _readerService.RefreshFeedAsync(feed.Id, token);
                _statusLabel.Text = result.NotModified
                    ? $"{feed.Title} 没有更新"
                    : $"{feed.Title} 新增 {result.NewEntries:N0} 条";
            });
    }

    private async Task RemoveSelectedReaderFeedAsync()
    {
        var feedId = GetReaderNavigation()?.FeedId;
        var feed = _readerFeeds.FirstOrDefault(item => item.Feed.Id == feedId)?.Feed;
        if (feed is null || MessageBox.Show(
                this,
                $"移除订阅“{feed.Title}”？\n\n本地条目和阅读状态会一并删除，不会影响源站，也不会删除 Beacon 资产。",
                "移除订阅",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        if (!await _stateDatabaseWriteGate.TryRunAsync(
                () => _readerService.DeleteFeedAsync(feed.Id)))
        {
            return;
        }

        _statusLabel.Text = $"已移除订阅：{feed.Title}";
        await RefreshReaderAsync();
    }

    private async Task ToggleSelectedReaderEntryReadAsync()
    {
        if (_readerEntryGrid.CurrentRow?.Tag is not ReaderEntryListItem item)
        {
            return;
        }

        if (!await _stateDatabaseWriteGate.TryRunAsync(
                () => _readerService.SetEntryReadAsync(
                    item.Entry.Id,
                    !item.Entry.IsRead)))
        {
            return;
        }

        await RefreshReaderEntriesAsync();
        await RefreshReaderFeedsOnlyAsync();
    }

    private async Task ToggleSelectedReaderEntryStarredAsync()
    {
        if (_readerEntryGrid.CurrentRow?.Tag is not ReaderEntryListItem item)
        {
            return;
        }

        if (!await _stateDatabaseWriteGate.TryRunAsync(
                () => _readerService.SetEntryStarredAsync(
                    item.Entry.Id,
                    !item.Entry.IsStarred)))
        {
            return;
        }

        await RefreshReaderEntriesAsync();
    }

    private async Task ImportReaderOpmlAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 OPML",
            Filter = "OPML 文件 (*.opml;*.xml)|*.opml;*.xml|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunReaderTaskAsync(
            "正在导入 OPML",
            dialog.FileName,
            async token =>
            {
                var result = await _readerService.ImportOpmlAsync(
                    dialog.FileName,
                    new Progress<ReaderRefreshProgress>(UpdateReaderProgress),
                    token);
                _statusLabel.Text =
                    $"OPML 导入完成 · 新增 {result.Imported:N0} · 跳过 {result.Skipped:N0} · 失败 {result.Failed:N0}";
                if (result.Failed > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Join(Environment.NewLine, result.Errors.Take(10)),
                        "部分订阅导入失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            });
    }

    private async Task ExportReaderOpmlAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出 OPML",
            Filter = "OPML 文件 (*.opml)|*.opml",
            DefaultExt = "opml",
            AddExtension = true,
            FileName = $"beacon-rss-subscriptions-{DateTime.Now:yyyyMMdd}.opml"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await RunReaderTaskAsync(
                "正在导出 OPML",
                dialog.FileName,
                async token =>
                {
                    await _readerService.ExportOpmlAsync(dialog.FileName, token);
                    _statusLabel.Text = "RSS 订阅已导出";
                });
        }
    }

    private async Task ExportReaderDataAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "备份 RSS 订阅数据",
            Filter = "Beacon RSS 订阅数据 (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            FileName = $"beacon-rss-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await RunReaderTaskAsync(
                "正在备份 RSS 订阅数据",
                dialog.FileName,
                async token =>
                {
                    await _readerService.ExportDataAsync(dialog.FileName, token);
                    _statusLabel.Text = "RSS 订阅数据备份完成";
                });
        }
    }

    private async Task ImportReaderDataAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "恢复 RSS 订阅数据",
            Filter = "Beacon RSS 订阅数据 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || MessageBox.Show(
                this,
                "恢复操作会合并订阅、条目和阅读状态；现有收藏和已读状态不会被清除。继续吗？",
                "恢复 RSS 订阅数据",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunReaderTaskAsync(
            "正在恢复 RSS 订阅数据",
            dialog.FileName,
            async token =>
            {
                var result = await _readerService.ImportDataAsync(dialog.FileName, token);
                _statusLabel.Text =
                    $"RSS 订阅数据恢复完成 · {result.FeedsImported:N0} 个订阅 · {result.EntriesImported:N0} 条";
            });
    }

    private async Task RunReaderTaskAsync(
        string taskName,
        string currentItem,
        Func<CancellationToken, Task> operation)
    {
        if (_isBusy || _stateDatabaseWriteGate.IsSuspended)
        {
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        SetBusy(true);
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _progressLabel.Text = taskName;
        _currentPathLabel.Text = currentItem;
        _statusLabel.Text = taskName;
        try
        {
            await operation(_scanCancellation.Token);
            await RefreshReaderAsync();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = $"{taskName}已取消";
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"{taskName}失败";
            ShowError(taskName, exception);
        }
        finally
        {
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetBusy(false);
        }
    }

    private void UpdateReaderProgress(ReaderRefreshProgress progress)
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1000;
        _progressBar.Value = progress.Total == 0
            ? 0
            : Math.Clamp(progress.Completed * 1000 / progress.Total, 0, 1000);
        _progressLabel.Text = $"RSS订阅 · {progress.Completed:N0}/{progress.Total:N0}";
        _currentPathLabel.Text = progress.Error is null
            ? progress.FeedTitle
            : $"{progress.FeedTitle} · {progress.Error}";
    }

    private async Task RefreshReaderFeedsOnlyAsync()
    {
        var selected = GetReaderNavigation();
        _readerFeeds = await _readerService.ListFeedsAsync();
        PopulateReaderSourceTree(selected);
    }

    private void ConfigureReaderSourceContextMenu()
    {
        var refresh = new ToolStripMenuItem("刷新订阅");
        refresh.Click += async (_, _) => await RefreshSelectedReaderFeedAsync();
        var open = new ToolStripMenuItem("打开源站");
        open.Click += (_, _) => OpenSelectedReaderFeedSite();
        var remove = new ToolStripMenuItem("移除订阅");
        remove.Click += async (_, _) => await RemoveSelectedReaderFeedAsync();
        _readerSourceContextMenu.Items.AddRange([refresh, open, new ToolStripSeparator(), remove]);
        _readerSourceContextMenu.Opening += (_, e) =>
        {
            var feed = GetSelectedReaderFeed();
            e.Cancel = feed is null;
            open.Enabled = !string.IsNullOrWhiteSpace(feed?.SiteUrl);
        };
        _readerSourceTree.ContextMenuStrip = _readerSourceContextMenu;
    }

    private void ConfigureReaderEntryContextMenu()
    {
        var open = new ToolStripMenuItem("打开原文");
        open.Click += (_, _) => OpenSelectedReaderEntry();
        var read = new ToolStripMenuItem("标记为已读/未读");
        read.Click += async (_, _) => await ToggleSelectedReaderEntryReadAsync();
        var star = new ToolStripMenuItem("收藏/取消收藏");
        star.Click += async (_, _) => await ToggleSelectedReaderEntryStarredAsync();
        _readerEntryContextMenu.Items.AddRange([open, read, star]);
        _readerEntryContextMenu.Opening += (_, e) =>
        {
            var entry = GetSelectedReaderEntry();
            e.Cancel = entry is null;
            open.Enabled = !string.IsNullOrWhiteSpace(entry?.Entry.Url);
            read.Text = entry?.Entry.IsRead == true ? "标记为未读" : "标记为已读";
            star.Text = entry?.Entry.IsStarred == true ? "取消收藏" : "收藏";
        };
        _readerEntryGrid.ContextMenuStrip = _readerEntryContextMenu;
    }

    private void ReaderSourceTree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _readerSourceTree.SelectedNode = e.Node;
        }
    }

    private async void ReaderSourceTree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        try
        {
            await RefreshReaderEntriesAsync();
        }
        catch (Exception exception)
        {
            ShowError("无法加载 RSS 条目", exception);
        }
    }

    private void OpenSelectedReaderFeedSite()
    {
        OpenReaderUrl(GetSelectedReaderFeed()?.SiteUrl);
    }

    private void OpenSelectedReaderEntry()
    {
        OpenReaderUrl(GetSelectedReaderEntry()?.Entry.Url ?? _readerOriginalLink.Tag as string);
    }

    private void OpenReaderUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("无法打开 RSS 链接", exception);
        }
    }

    private ReaderFeed? GetSelectedReaderFeed()
    {
        var id = GetReaderNavigation()?.FeedId;
        return _readerFeeds.FirstOrDefault(item => item.Feed.Id == id)?.Feed;
    }

    private ReaderEntryListItem? GetSelectedReaderEntry()
    {
        return _readerEntryGrid.CurrentRow?.Tag as ReaderEntryListItem;
    }

    private ReaderNavigation? GetReaderNavigation()
    {
        return _readerSourceTree.SelectedNode?.Tag as ReaderNavigation;
    }

    private TreeNode? FindReaderNode(ReaderNavigation? navigation)
    {
        if (navigation is null)
        {
            return null;
        }

        return EnumerateNodes(_readerSourceTree.Nodes)
            .FirstOrDefault(node => Equals(node.Tag, navigation));
    }

    private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Nodes))
            {
                yield return child;
            }
        }
    }

    private static TreeNode CreateReaderNode(string text, ReaderNavigation navigation)
    {
        return new TreeNode(text) { Tag = navigation };
    }

    private void ClearReaderContent()
    {
        _readerTitleLabel.Text = "选择一篇条目";
        _readerMetadataLabel.Text = string.Empty;
        _readerOriginalLink.Text = string.Empty;
        _readerOriginalLink.Tag = null;
        _readerContentTextBox.Clear();
    }

    private static string FormatReaderDate(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
    }

    private sealed record ReaderNavigation(
        Guid? FeedId,
        bool UnreadOnly,
        bool StarredOnly);
}
