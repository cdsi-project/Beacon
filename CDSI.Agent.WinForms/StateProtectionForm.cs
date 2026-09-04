using System.Diagnostics;
using CDSI.Agent.Infrastructure.Persistence;

namespace CDSI.Agent.WinForms;

internal sealed class StateProtectionForm : Form
{
    internal const string StateBackupFileFilter =
        "CDSI Beacon 状态备份 (*.cdsibak)|*.cdsibak|所有文件 (*.*)|*.*";

    private readonly LocalStateProtectionService _stateProtectionService;
    private readonly string _workspacePath;
    private readonly string _clientId;
    private readonly RuntimeLogService _runtimeLog;
    private readonly DataGridView _backupGrid = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _backupDirectoryLabel = new();
    private readonly Label _operationStatusLabel = new();
    private readonly ProgressBar _operationProgressBar = new();
    private readonly Button _createBackupButton = new();
    private readonly Button _restoreFileButton = new();
    private readonly Button _validateButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _openDirectoryButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _restoreSelectedButton = new();
    private readonly Button _closeButton = new();
    private bool _busy;

    public StateProtectionForm(
        LocalStateProtectionService stateProtectionService,
        string workspacePath,
        string clientId,
        RuntimeLogService runtimeLog)
    {
        _stateProtectionService = stateProtectionService ??
            throw new ArgumentNullException(nameof(stateProtectionService));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _workspacePath = Path.GetFullPath(workspacePath);
        _clientId = clientId.Trim();
        _runtimeLog = runtimeLog ?? throw new ArgumentNullException(nameof(runtimeLog));

        Text = "数据保护";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(760, 480);
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;

        ConfigureStateBackupGrid(_backupGrid);
        _backupGrid.SelectionChanged += (_, _) => UpdateActionState();
        _backupGrid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                await RestoreSelectedBackupAsync();
            }
        };

        Controls.Add(CreateStateProtectionLayout(
            _summaryLabel,
            _backupDirectoryLabel,
            _backupGrid,
            CreateToolbar(),
            CreateOperationStatus(),
            CreateFooter()));

        Shown += async (_, _) => await ReloadBackupsAsync();
        UpdateActionState();
    }

    public bool RestartRequested { get; private set; }

    internal static void ConfigureStateBackupGrid(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Dock = DockStyle.Fill;
        grid.AccessibleName = "状态备份列表";
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToOrderColumns = true;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CreatedAt",
            HeaderText = "创建时间",
            ValueType = typeof(DateTime),
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "yyyy-MM-dd HH:mm:ss",
                NullValue = "-"
            },
            Width = 150
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Kind",
            HeaderText = "类型",
            ValueType = typeof(string),
            Width = 120
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "BeaconVersion",
            HeaderText = "Beacon 版本",
            ValueType = typeof(string),
            Width = 110
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Contents",
            HeaderText = "内容",
            ValueType = typeof(string),
            Width = 120
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Size",
            HeaderText = "大小",
            ValueType = typeof(long),
            Width = 100
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "验证状态",
            ValueType = typeof(string),
            Width = 100
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Path",
            HeaderText = "位置",
            ValueType = typeof(string),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220
        });
        grid.CellFormatting += StateBackupGrid_CellFormatting;
    }

    internal static TableLayoutPanel CreateStateProtectionLayout(
        Label summaryLabel,
        Label backupDirectoryLabel,
        DataGridView backupGrid,
        Control toolbar,
        Control operationStatus,
        Control footer)
    {
        ArgumentNullException.ThrowIfNull(summaryLabel);
        ArgumentNullException.ThrowIfNull(backupDirectoryLabel);
        ArgumentNullException.ThrowIfNull(backupGrid);
        ArgumentNullException.ThrowIfNull(toolbar);
        ArgumentNullException.ThrowIfNull(operationStatus);
        ArgumentNullException.ThrowIfNull(footer);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.Text = "正在读取状态备份";
        summaryLabel.Font = new Font("Segoe UI Semibold", 13F);
        summaryLabel.ForeColor = Color.FromArgb(31, 37, 43);
        summaryLabel.AccessibleName = "数据保护状态";
        header.Controls.Add(summaryLabel, 0, 0);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "保护内容：资产、项目、标签、发布记录，以及 RSS订阅和阅读状态。",
            ForeColor = Color.FromArgb(72, 81, 89),
            AccessibleName = "状态备份范围"
        }, 0, 1);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "边界：不包含素材文件、运行日志或 client-identity.json；" +
                CreateCredentialBoundaryNotice(),
            ForeColor = Color.FromArgb(72, 81, 89),
            AccessibleName = "状态备份排除范围"
        }, 0, 2);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = CreateSensitiveMetadataNotice(),
            ForeColor = Color.FromArgb(72, 81, 89),
            AccessibleName = "状态备份敏感信息说明"
        }, 0, 3);
        backupDirectoryLabel.Dock = DockStyle.Fill;
        backupDirectoryLabel.ForeColor = Color.FromArgb(112, 121, 129);
        backupDirectoryLabel.AutoEllipsis = true;
        backupDirectoryLabel.AccessibleName = "状态备份目录";
        header.Controls.Add(backupDirectoryLabel, 0, 4);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "安全提示：状态包当前未加密，请妥善保管；同盘备份不能防止整块磁盘损坏。",
            ForeColor = Color.FromArgb(150, 85, 25),
            AccessibleName = "状态备份安全提示"
        }, 0, 5);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(backupGrid, 0, 2);
        layout.Controls.Add(operationStatus, 0, 3);
        layout.Controls.Add(footer, 0, 4);
        return layout;
    }

    internal static bool CanRestoreStateBackup(
        LocalStateBackupInfo? backup,
        bool busy)
    {
        return !busy && backup?.Status == LocalStateBackupStatus.Restorable;
    }

    internal static string FormatStateBackupKind(LocalStateBackupKind? kind)
    {
        return kind switch
        {
            LocalStateBackupKind.Manual => "手动备份",
            LocalStateBackupKind.PreRestore => "恢复前安全备份",
            _ => "未知"
        };
    }

    internal static string FormatStateBackupStatus(LocalStateBackupStatus status)
    {
        return status switch
        {
            LocalStateBackupStatus.Restorable => "可恢复",
            LocalStateBackupStatus.NewerVersion => "版本过新",
            _ => "已损坏"
        };
    }

    internal static string CreateStateBackupFilename(DateTimeOffset createdAtUtc)
    {
        return $"beacon-state-{createdAtUtc.UtcDateTime:yyyyMMdd-HHmmss'Z'}.cdsibak";
    }

    internal static string CreateCredentialBoundaryNotice() =>
        "Beacon 不读取 Windows 凭据管理器中由 Beacon 管理的密码/令牌，也不读取 SSH 私钥。";

    internal static string CreateSensitiveMetadataNotice() =>
        "敏感元数据：包内的绝对路径、来源客户端 ID、RSS URL/内容、账号与连接元数据可能敏感，" +
        "请作为私密数据保存；恢复不会替换目标客户端身份。";

    internal static string CreateStateRestoreConfirmation(LocalStateBackupInfo backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var createdAt = backup.CreatedAtUtc is { } value
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "未知时间";
        return
            $"将恢复 {createdAt} 创建的 Beacon 状态备份。\n\n" +
            "将恢复：\n" +
            "资产索引、项目、标签、发布及备份记录；\n" +
            "RSS订阅、条目、已读和收藏状态。\n\n" +
            "不会恢复或修改：\n" +
            "本地素材文件和云端对象；\n" +
            "client-identity.json 和当前 Beacon 客户端 ID。\n\n" +
            $"{CreateCredentialBoundaryNotice()}\n" +
            $"{CreateSensitiveMetadataNotice()}\n\n" +
            "当前状态会先创建“恢复前安全备份”。Beacon 将关闭，并在重新启动时完成恢复。确定继续吗？";
    }

    internal static bool ShouldCancelClose(bool busy, bool restartRequested) =>
        busy && !restartRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (ShouldCancelClose(_busy, RestartRequested))
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private Control CreateToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 5)
        };
        ConfigureButton(_createBackupButton, "立即创建状态备份", 142);
        _createBackupButton.AccessibleName = "立即创建状态备份";
        _createBackupButton.Click += async (_, _) => await CreateBackupAsync();
        ConfigureButton(_restoreFileButton, "从文件恢复...", 112);
        _restoreFileButton.Click += async (_, _) => await RestoreFromFileAsync();
        ConfigureButton(_validateButton, "验证所选", 88);
        _validateButton.Click += async (_, _) => await ValidateSelectedBackupAsync();
        ConfigureButton(_exportButton, "导出副本...", 104);
        _exportButton.Click += async (_, _) => await ExportSelectedBackupAsync();
        ConfigureButton(_openDirectoryButton, "打开备份目录", 112);
        _openDirectoryButton.Click += (_, _) => OpenBackupDirectory();
        ConfigureButton(_refreshButton, "刷新", 76);
        _refreshButton.Click += async (_, _) => await ReloadBackupsAsync();
        toolbar.Controls.AddRange(
        [
            _createBackupButton,
            _restoreFileButton,
            _validateButton,
            _exportButton,
            _openDirectoryButton,
            _refreshButton
        ]);
        return toolbar;
    }

    private Control CreateOperationStatus()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 7, 0, 3)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        _operationStatusLabel.Dock = DockStyle.Fill;
        _operationStatusLabel.ForeColor = Color.FromArgb(88, 98, 106);
        _operationStatusLabel.AutoEllipsis = true;
        _operationStatusLabel.AccessibleName = "数据保护操作状态";
        _operationProgressBar.Dock = DockStyle.Fill;
        _operationProgressBar.Style = ProgressBarStyle.Marquee;
        _operationProgressBar.MarqueeAnimationSpeed = 24;
        _operationProgressBar.Visible = false;
        _operationProgressBar.AccessibleName = "数据保护操作进度";
        layout.Controls.Add(_operationStatusLabel, 0, 0);
        layout.Controls.Add(_operationProgressBar, 1, 0);
        return layout;
    }

    private Control CreateFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        ConfigureButton(_closeButton, "关闭", 88);
        _closeButton.DialogResult = DialogResult.Cancel;
        ConfigureButton(_restoreSelectedButton, "恢复所选备份...", 136);
        _restoreSelectedButton.BackColor = Color.FromArgb(236, 239, 242);
        _restoreSelectedButton.Click += async (_, _) => await RestoreSelectedBackupAsync();
        footer.Controls.Add(_closeButton);
        footer.Controls.Add(_restoreSelectedButton);
        CancelButton = _closeButton;
        return footer;
    }

    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Size = new Size(width, 30);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(205, 211, 216);
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(31, 37, 43);
    }

    private async Task ReloadBackupsAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "正在验证本地状态备份");
        try
        {
            var backups = await _stateProtectionService.ListBackupsAsync(
                _workspacePath);
            PopulateBackups(backups);
            _operationStatusLabel.Text = "状态备份列表已刷新";
        }
        catch (Exception exception)
        {
            ShowOperationError("无法读取状态备份", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CreateBackupAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "正在创建状态备份");
        try
        {
            var backup = await _stateProtectionService.CreateBackupAsync(
                _workspacePath,
                LocalStateBackupKind.Manual,
                _clientId);
            var backups = await _stateProtectionService.ListBackupsAsync(
                _workspacePath);
            PopulateBackups(backups, backup.Path);
            _operationStatusLabel.Text =
                $"状态备份已创建：{Path.GetFileName(backup.Path)}";
            _runtimeLog.WriteInformation($"已创建 Beacon 状态备份：{backup.Path}");
        }
        catch (Exception exception)
        {
            ShowOperationError("无法创建状态备份", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RestoreFromFileAsync()
    {
        if (_busy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择 Beacon 状态备份",
            Filter = StateBackupFileFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await InspectAndPrepareRestoreAsync(dialog.FileName);
    }

    private async Task RestoreSelectedBackupAsync()
    {
        if (!CanRestoreStateBackup(GetSelectedBackup(), _busy))
        {
            MessageBox.Show(
                this,
                "请先选择一个状态为“可恢复”的备份。",
                "数据保护",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        await InspectAndPrepareRestoreAsync(GetSelectedBackup()!.Path);
    }

    private async Task ValidateSelectedBackupAsync()
    {
        var selected = GetSelectedBackup();
        if (selected is null || _busy)
        {
            MessageBox.Show(
                this,
                "请先选择一个状态备份。",
                "数据保护",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "正在验证所选状态备份");
        try
        {
            var inspected = await _stateProtectionService.InspectAsync(selected.Path);
            var backups = _backupGrid.Rows
                .Cast<DataGridViewRow>()
                .Select(row => (LocalStateBackupInfo)row.Tag!)
                .Select(backup => string.Equals(
                        backup.Path,
                        inspected.Path,
                        StringComparison.OrdinalIgnoreCase)
                    ? inspected
                    : backup)
                .ToArray();
            PopulateBackups(backups, inspected.Path);
            _operationStatusLabel.Text = inspected.Status == LocalStateBackupStatus.Restorable
                ? "所选状态备份验证通过"
                : inspected.Error ?? "所选状态备份验证失败";
        }
        catch (Exception exception)
        {
            ShowOperationError("无法验证状态备份", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InspectAndPrepareRestoreAsync(string path)
    {
        SetBusy(true, "正在验证所选状态备份");
        LocalStateBackupInfo backup;
        try
        {
            backup = await _stateProtectionService.InspectAsync(path);
        }
        catch (Exception exception)
        {
            ShowOperationError("无法验证状态备份", exception);
            SetBusy(false);
            return;
        }

        SetBusy(false);
        if (backup.Status != LocalStateBackupStatus.Restorable)
        {
            var message = backup.Status == LocalStateBackupStatus.NewerVersion
                ? "此备份由更高版本的 Beacon 创建。请先升级 Beacon，再执行恢复。"
                : backup.Error ?? "备份校验失败，文件可能已损坏或被修改。当前数据未更改。";
            MessageBox.Show(
                this,
                message,
                "无法恢复状态备份",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
                this,
                CreateStateRestoreConfirmation(backup),
                "恢复 Beacon 状态",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, "正在创建恢复前安全备份并安排恢复");
        try
        {
            var preparation = await _stateProtectionService.PrepareRestoreAsync(
                backup.Path,
                _workspacePath,
                _clientId,
                backup);
            _runtimeLog.WriteInformation(
                $"已安排 Beacon 状态恢复；RestoreId={preparation.RestoreId:D}；" +
                $"状态备份={backup.Path}；安全备份={preparation.SafetyBackupPath}");
            RestartRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            ShowOperationError("无法安排状态恢复", exception);
            SetBusy(false);
        }
    }

    private async Task ExportSelectedBackupAsync()
    {
        var selected = GetSelectedBackup();
        if (!CanRestoreStateBackup(selected, _busy))
        {
            MessageBox.Show(
                this,
                "请先选择一个状态为“可恢复”的备份。",
                "数据保护",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出 Beacon 状态备份副本",
            Filter = StateBackupFileFilter,
            FileName = CreateStateBackupFilename(
                selected!.CreatedAtUtc ?? DateTimeOffset.UtcNow),
            AddExtension = true,
            DefaultExt = "cdsibak",
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true, "正在验证并导出状态备份副本");
        try
        {
            await _stateProtectionService.ExportAsync(
                selected.Path,
                dialog.FileName,
                selected,
                overwrite: true);
            _operationStatusLabel.Text = $"状态备份副本已导出：{dialog.FileName}";
            _runtimeLog.WriteInformation($"已导出 Beacon 状态备份副本：{dialog.FileName}");
        }
        catch (Exception exception)
        {
            ShowOperationError("无法导出状态备份副本", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenBackupDirectory()
    {
        try
        {
            var path = _stateProtectionService.GetBackupDirectory(_workspacePath);
            Directory.CreateDirectory(path);
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowOperationError("无法打开状态备份目录", exception);
        }
    }

    private void PopulateBackups(
        IReadOnlyList<LocalStateBackupInfo> backups,
        string? pathToSelect = null)
    {
        var selectedPath = pathToSelect ?? GetSelectedBackup()?.Path;
        _backupGrid.Rows.Clear();
        foreach (var backup in backups
                     .OrderByDescending(item => item.CreatedAtUtc)
                     .ThenByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            object createdAtValue = backup.CreatedAtUtc is { } createdAt
                ? createdAt.ToLocalTime().DateTime
                : DBNull.Value;
            var rowIndex = _backupGrid.Rows.Add(
                createdAtValue,
                FormatStateBackupKind(backup.Kind),
                backup.BeaconVersion ?? "-",
                "资产 + RSS",
                backup.FileSize,
                FormatStateBackupStatus(backup.Status),
                backup.Path);
            var row = _backupGrid.Rows[rowIndex];
            row.Tag = backup;
            row.Cells["Path"].ToolTipText = backup.Error ?? backup.Path;
            if (!string.IsNullOrWhiteSpace(selectedPath) &&
                string.Equals(
                    backup.Path,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _backupGrid.CurrentCell = row.Cells[0];
            }
        }

        if (_backupGrid.Rows.Count > 0 && _backupGrid.CurrentRow is null)
        {
            _backupGrid.Rows[0].Selected = true;
            _backupGrid.CurrentCell = _backupGrid.Rows[0].Cells[0];
        }

        var restorable = backups.Count(item =>
            item.Status == LocalStateBackupStatus.Restorable);
        var latest = backups
            .Where(item => item.Status == LocalStateBackupStatus.Restorable)
            .MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc;
        _summaryLabel.Text = latest is null
            ? "尚无可恢复的状态备份"
            : $"可恢复备份 {restorable:N0} 份 · 最近备份 {latest.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        _backupDirectoryLabel.Text =
            $"备份位置：{_stateProtectionService.GetBackupDirectory(_workspacePath)}";
        UpdateActionState();
    }

    private LocalStateBackupInfo? GetSelectedBackup()
    {
        return _backupGrid.CurrentRow?.Tag as LocalStateBackupInfo;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            _operationStatusLabel.Text = status;
        }

        _backupGrid.Enabled = !busy;
        _createBackupButton.Enabled = !busy;
        _restoreFileButton.Enabled = !busy;
        _validateButton.Enabled = !busy && GetSelectedBackup() is not null;
        _openDirectoryButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        _operationProgressBar.Visible = busy;
        UseWaitCursor = busy;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var canUseSelected = CanRestoreStateBackup(GetSelectedBackup(), _busy);
        _restoreSelectedButton.Enabled = canUseSelected;
        _exportButton.Enabled = canUseSelected;
        _validateButton.Enabled = !_busy && GetSelectedBackup() is not null;
    }

    private void ShowOperationError(string title, Exception exception)
    {
        _operationStatusLabel.Text = title;
        _runtimeLog.WriteError(title, exception);
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void StateBackupGrid_CellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0)
        {
            return;
        }

        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (string.Equals(columnName, "Size", StringComparison.Ordinal) &&
            e.Value is long size)
        {
            e.Value = MainForm.FormatFileSize(size);
            e.FormattingApplied = true;
            return;
        }

        if (!string.Equals(columnName, "Status", StringComparison.Ordinal) ||
            grid.Rows[e.RowIndex].Tag is not LocalStateBackupInfo backup)
        {
            return;
        }

        var color = backup.Status == LocalStateBackupStatus.Restorable
            ? Color.FromArgb(24, 121, 78)
            : Color.FromArgb(176, 62, 55);
        e.CellStyle.ForeColor = color;
        e.CellStyle.SelectionForeColor = color;
    }
}
