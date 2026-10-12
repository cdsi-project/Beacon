namespace CDSI.Agent.WinForms;

public sealed partial class SettingsForm
{
    private readonly ExplorerContextMenuService _explorerContextMenuService = new();
    private readonly CheckBox _explorerContextMenuCheckBox = new();
    private readonly Label _explorerContextMenuStatus = new();

    private TabPage CreateExplorerIntegrationPage()
    {
        var page = new TabPage("系统集成") { BackColor = Color.White, Padding = new Padding(24) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            RowCount = 4, Padding = new Padding(0)
        };
        _explorerContextMenuCheckBox.Text = "启用文件右键菜单：加入 Beacon 资源管理";
        _explorerContextMenuCheckBox.AutoSize = true;
        _explorerContextMenuCheckBox.AccessibleName = "启用资源管理器右键菜单";
        layout.Controls.Add(_explorerContextMenuCheckBox);
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 16, 0, 16),
            Text = "开启后，可在资源管理器中右键一个或多个文件并加入 Beacon。\n" +
                "只登记选中的文件，不扫描父目录，不移动文件，也不自动上传。\n" +
                "Windows 11 可在“显示更多选项”中找到此菜单。\n" +
                "设置仅对当前 Windows 用户生效，无需管理员权限。程序移动或升级到新路径后，请重新应用此设置。"
        });
        var applyButton = CreateButton("应用", Color.FromArgb(24, 121, 78), Color.White);
        applyButton.Click += (_, _) => ApplyExplorerIntegration();
        layout.Controls.Add(applyButton);
        _explorerContextMenuStatus.AutoSize = true;
        _explorerContextMenuStatus.MaximumSize = new Size(680, 0);
        _explorerContextMenuStatus.Margin = new Padding(0, 16, 0, 0);
        layout.Controls.Add(_explorerContextMenuStatus);
        page.Controls.Add(layout);
        return page;
    }

    private void RefreshExplorerIntegration()
    {
        var executable = _explorerContextMenuService.GetRegisteredExecutable();
        _explorerContextMenuCheckBox.Checked = executable is not null;
        _explorerContextMenuStatus.Text = executable is null
            ? "未启用" : $"已启用；关联程序：{executable}";
    }

    private void ApplyExplorerIntegration()
    {
        try
        {
            _explorerContextMenuService.Apply(_explorerContextMenuCheckBox.Checked,
                Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath);
            RefreshExplorerIntegration();
        }
        catch (Exception exception)
        {
            ShowError("无法更新文件右键菜单", exception);
        }
    }
}
