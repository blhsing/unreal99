using System.Drawing;

namespace Unreal99.Setup;

internal sealed class InstallerForm : Form
{
    private readonly TextBox _directory = new();
    private readonly CheckBox _startMenu = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Button _install = new();
    private readonly Button _uninstall = new();
    private readonly Button _browse = new();
    private readonly Button _close = new();
    private CancellationTokenSource _operation;

    public InstallerForm()
    {
        Text = "虛幻競技場 99 — 1.0.1 安裝程式";
        ClientSize = new Size(720, 555);
        MinimumSize = new Size(680, 535);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 10f);
        BackColor = Color.FromArgb(15, 19, 29);
        ForeColor = Color.FromArgb(230, 237, 248);
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new Panel { Dock = DockStyle.Top, Height = 176, BackColor = Color.FromArgb(20, 34, 56) };
        Controls.Add(header);
        var logo = new PictureBox
        {
            Bounds = new Rectangle(32, 18, 140, 140),
            SizeMode = PictureBoxSizeMode.Zoom,
            AccessibleName = "虛幻競技場標誌",
        };
        string logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Unreal99Logo.png");
        if (File.Exists(logoPath)) logo.Image = Image.FromFile(logoPath);
        header.Controls.Add(logo);
        header.Controls.Add(new Label
        {
            Text = "虛幻競技場 99",
            Font = new Font(Font.FontFamily, 25f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 174, 54),
            AutoSize = true,
            Location = new Point(190, 40),
        });
        header.Controls.Add(new Label
        {
            Text = "1.0.1 重製版安裝程式  ·  不需要系統管理員權限",
            ForeColor = Color.FromArgb(173, 202, 236),
            AutoSize = true,
            Location = new Point(194, 96),
        });

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(38, 24, 38, 26) };
        Controls.Add(content);
        content.BringToFront();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            BackColor = BackColor,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        content.Controls.Add(layout);

        var directoryLabel = new Label { Text = "安裝位置", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(directoryLabel, 0, 0);
        layout.SetColumnSpan(directoryLabel, 2);
        _directory.Text = InstallService.DefaultInstallDirectory;
        _directory.Dock = DockStyle.Fill;
        _directory.AccessibleName = "安裝位置";
        _directory.TextChanged += (_, _) => RefreshInstalledState();
        layout.Controls.Add(_directory, 0, 1);
        _browse.Text = "瀏覽…";
        _browse.Dock = DockStyle.Fill;
        _browse.Click += Browse;
        layout.Controls.Add(_browse, 1, 1);

        _startMenu.Text = "新增開始選單捷徑";
        _startMenu.Checked = true;
        _startMenu.Dock = DockStyle.Fill;
        layout.Controls.Add(_startMenu, 0, 2);
        layout.SetColumnSpan(_startMenu, 2);

        var note = new Label
        {
            Text = "現有安裝可直接更新；移除時只會刪除由本安裝程式加入的檔案。",
            ForeColor = Color.FromArgb(159, 176, 201),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);
        _status.Text = "準備安裝";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(195, 215, 241);
        layout.Controls.Add(_status, 0, 4);
        layout.SetColumnSpan(_status, 2);
        _progress.Dock = DockStyle.Fill;
        layout.Controls.Add(_progress, 0, 5);
        layout.SetColumnSpan(_progress, 2);

        var cli = new Label
        {
            Text = "命令列安裝：Unreal99Installer.exe install --install-dir <路徑>\r\n" +
                   "完整選項：Unreal99Installer.exe --help",
            ForeColor = Color.FromArgb(124, 153, 188),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
        };
        layout.Controls.Add(cli, 0, 6);
        layout.SetColumnSpan(cli, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 2);
        ConfigureButton(_install, "安裝", Color.FromArgb(220, 115, 24));
        ConfigureButton(_uninstall, "移除", Color.FromArgb(108, 57, 65));
        ConfigureButton(_close, "關閉", Color.FromArgb(55, 67, 86));
        _install.Click += async (_, _) => await InstallAsync();
        _uninstall.Click += async (_, _) => await UninstallAsync();
        _close.Click += CloseOrCancel;
        buttons.Controls.Add(_install);
        buttons.Controls.Add(_uninstall);
        buttons.Controls.Add(_close);
        AcceptButton = _install;
        CancelButton = _close;
        RefreshInstalledState();
    }

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Width = 104;
        button.Height = 34;
        button.Margin = new Padding(8, 3, 0, 3);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
    }

    private void Browse(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "選擇虛幻競技場 99 的安裝位置",
            SelectedPath = _directory.Text,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _directory.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        try
        {
            SetBusy(true);
            string source = InstallService.FindPayload();
            await InstallService.InstallAsync(new InstallOptions(_directory.Text, source, _startMenu.Checked),
                new Progress<InstallProgress>(ShowProgress), _operation.Token);
            _status.Text = "安裝完成。現在可從開始選單啟動遊戲。";
            MessageBox.Show(this, _status.Text, "安裝完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { _status.Text = "操作已取消"; }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RefreshInstalledState(); }
    }

    private async Task UninstallAsync()
    {
        if (MessageBox.Show(this, "要從這台電腦移除虛幻競技場 99 嗎？", "確認移除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            SetBusy(true);
            await InstallService.UninstallAsync(_directory.Text,
                new Progress<InstallProgress>(ShowProgress), _operation.Token);
            _status.Text = "遊戲已移除";
        }
        catch (OperationCanceledException) { _status.Text = "操作已取消"; }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RefreshInstalledState(); }
    }

    private void ShowProgress(InstallProgress progress)
    {
        _progress.Value = Math.Clamp(progress.Percent, 0, 100);
        _status.Text = progress.Message;
    }

    private void ShowError(Exception ex)
    {
        _status.Text = "無法完成操作";
        MessageBox.Show(this, ex.Message, "安裝程式", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetBusy(bool busy)
    {
        if (busy) _operation = new CancellationTokenSource();
        else { _operation?.Dispose(); _operation = null; }
        _install.Enabled = !busy;
        _uninstall.Enabled = !busy && InstallService.IsInstalled(_directory.Text);
        _browse.Enabled = !busy;
        _directory.Enabled = !busy;
        _startMenu.Enabled = !busy;
        _close.Text = busy ? "取消" : "關閉";
    }

    private void CloseOrCancel(object sender, EventArgs e)
    {
        if (_operation != null) _operation.Cancel();
        else Close();
    }

    private void RefreshInstalledState()
    {
        try { _uninstall.Enabled = _operation == null && InstallService.IsInstalled(_directory.Text); }
        catch (Exception) { _uninstall.Enabled = false; }
    }
}
