using System.Diagnostics;
using Zyron.Print.Configuration;
using Zyron.Print.Infrastructure;
using Zyron.Print.Models;
using Zyron.Print.Printing;
using Zyron.Print.Services;

namespace Zyron.Print;

public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly RawPrinterService _printer;
    private readonly SupabaseDeviceClient _api;
    private readonly PrintQueueWorker _worker;
    private readonly StartupManager _startup;
    private readonly FileLogger _logger;
    private readonly bool _startMinimized;

    private readonly ComboBox _printers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ComboBox _paperWidth = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _cut = new() { Text = "Cortar o papel quando disponível", AutoSize = true };
    private readonly CheckBox _startWindows = new() { Text = "Iniciar minimizado com o Windows", AutoSize = true };
    private readonly TextBox _supabaseUrl = new() { Width = 440, PlaceholderText = "https://seu-projeto.supabase.co" };
    private readonly TextBox _anonKey = new() { Width = 440, UseSystemPasswordChar = true, PlaceholderText = "Chave pública anon/publishable" };
    private readonly TextBox _pairingCode = new() { Width = 180, CharacterCasing = CharacterCasing.Upper, MaxLength = 12 };
    private readonly Label _pairingStatus = new() { AutoSize = true };
    private readonly Label _stateLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Panel _stateDot = new() { Width = 14, Height = 14, Margin = new Padding(0, 6, 8, 0) };
    private readonly Button _pairButton = new() { Text = "Parear computador", AutoSize = true };
    private readonly Button _unpairButton = new() { Text = "Desvincular", AutoSize = true };
    private readonly Button _testButton = new() { Text = "Imprimir teste", AutoSize = true };
    private readonly NotifyIcon _tray = new() { Text = "ZYRON Print", Visible = true };
    private bool _allowClose;

    public MainForm(
        SettingsStore settingsStore,
        CredentialStore credentialStore,
        RawPrinterService printer,
        SupabaseDeviceClient api,
        PrintQueueWorker worker,
        StartupManager startup,
        FileLogger logger,
        bool startMinimized)
    {
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _printer = printer;
        _api = api;
        _worker = worker;
        _startup = startup;
        _logger = logger;
        _startMinimized = startMinimized;

        Text = "ZYRON Print";
        Width = 720;
        Height = 560;
        MinimumSize = new Size(620, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _tray.Icon = Icon;
        BuildLayout();
        BuildTrayMenu();
        LoadSettings();
        UpdatePairingView();

        _worker.StatusChanged += WorkerOnStatusChanged;
        Shown += OnShown;
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) HideToTray();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 16),
            ColumnCount = 1,
            RowCount = 5
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "ZYRON Print",
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 42, 120),
            Margin = new Padding(0, 0, 0, 4)
        };
        var subtitle = new Label
        {
            Text = "Impressão automática de comandas do ZYRON Delivery",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 10)
        };
        var state = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        state.Controls.Add(_stateDot);
        state.Controls.Add(_stateLabel);

        _paperWidth.Items.AddRange(["58 mm", "80 mm"]);
        var printerContent = PageContent([
            Row("Impressora instalada", _printers, Button("Atualizar lista", (_, _) => RefreshPrinters())),
            Row("Largura do papel", _paperWidth),
            _cut,
            _startWindows,
            new FlowLayoutPanel { AutoSize = true, Controls = { _testButton } }
        ]);
        _testButton.Click += TestPrint;

        var pairingActions = new FlowLayoutPanel { AutoSize = true };
        pairingActions.Controls.Add(_pairingCode);
        pairingActions.Controls.Add(_pairButton);
        pairingActions.Controls.Add(_unpairButton);
        var pairingContent = PageContent([
            new Label { Text = "Digite o código temporário gerado no painel da loja.", AutoSize = true },
            pairingActions,
            _pairingStatus
        ]);
        _pairButton.Click += PairDevice;
        _unpairButton.Click += (_, _) =>
        {
            if (MessageBox.Show("Desvincular este computador da loja?", "ZYRON Print",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _credentialStore.Clear();
            UpdatePairingView();
        };

        var connectionContent = PageContent([
            Row("URL do Supabase", _supabaseUrl),
            Row("Chave pública", _anonKey),
            new Label
            {
                Text = "A chave pública não é uma chave administrativa. Nunca use service_role neste aplicativo.",
                AutoSize = true,
                ForeColor = Color.DimGray
            }
        ]);

        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        actions.Controls.Add(Button("Salvar configurações", (_, _) => SaveSettings(true)));
        actions.Controls.Add(Button("Abrir pasta de logs", (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", _logger.DirectoryPath) { UseShellExecute = true })));
        actions.Controls.Add(Button("Minimizar para a bandeja", (_, _) => HideToTray()));

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(Tab("Impressora", printerContent));
        tabs.TabPages.Add(Tab("Pareamento", pairingContent));
        tabs.TabPages.Add(Tab("Conexão", connectionContent));

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(state);
        root.Controls.Add(tabs);
        root.Controls.Add(actions);
        Controls.Add(root);
        SetStatus(new WorkerStatus(ConnectionState.Disconnected, "Aguardando configuração"));
    }

    private void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir ZYRON Print", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Imprimir teste", null, TestPrint);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, async (_, _) =>
        {
            _allowClose = true;
            await _worker.StopAsync();
            Close();
        });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Current;
        RefreshPrinters();
        if (_printers.Items.Contains(settings.PrinterName)) _printers.SelectedItem = settings.PrinterName;
        _paperWidth.SelectedIndex = settings.PaperWidth == 80 ? 1 : 0;
        _cut.Checked = settings.CutPaper;
        _startWindows.Checked = settings.StartWithWindows;
        _supabaseUrl.Text = settings.SupabaseUrl;
        _anonKey.Text = settings.SupabaseAnonKey;
    }

    private void SaveSettings(bool notify)
    {
        var settings = new AppSettings
        {
            PrinterName = _printers.SelectedItem?.ToString() ?? "",
            PaperWidth = _paperWidth.SelectedIndex == 1 ? 80 : 58,
            CutPaper = _cut.Checked,
            StartWithWindows = _startWindows.Checked,
            SupabaseUrl = _supabaseUrl.Text.Trim(),
            SupabaseAnonKey = _anonKey.Text.Trim(),
            PollIntervalSeconds = _settingsStore.Current.PollIntervalSeconds
        };
        _settingsStore.Save(settings);
        _startup.SetEnabled(settings.StartWithWindows);
        if (notify)
            MessageBox.Show("Configurações salvas.", "ZYRON Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void PairDevice(object? sender, EventArgs eventArgs)
    {
        try
        {
            SaveSettings(false);
            if (_pairingCode.Text.Trim().Length < 6)
                throw new InvalidOperationException("Digite um código de pareamento válido.");
            _pairButton.Enabled = false;
            _pairingStatus.Text = "Pareando...";
            var result = await _api.PairAsync(_pairingCode.Text, Environment.MachineName, CancellationToken.None);
            _credentialStore.Save(new DeviceCredential
            {
                DeviceId = result.DeviceId,
                RestaurantId = result.RestaurantId,
                RestaurantName = result.RestaurantName,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, result.ExpiresIn)),
                PairedAt = DateTimeOffset.UtcNow
            });
            _pairingCode.Clear();
            UpdatePairingView();
            MessageBox.Show($"Computador vinculado à loja {result.RestaurantName}.", "ZYRON Print",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _logger.Error("Falha no pareamento.", exception);
            _pairingStatus.Text = exception.Message;
            MessageBox.Show(exception.Message, "Não foi possível parear", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _pairButton.Enabled = true;
        }
    }

    private void TestPrint(object? sender, EventArgs eventArgs)
    {
        try
        {
            SaveSettings(false);
            var settings = _settingsStore.Current;
            var store = _credentialStore.Load()?.RestaurantName ?? "ZYRON Delivery";
            _printer.Print(settings.PrinterName,
                EscPosReceiptBuilder.BuildTest(store, settings.PaperWidth, settings.CutPaper),
                "ZYRON - Comanda de teste");
            MessageBox.Show("Comanda de teste enviada para a impressora.", "ZYRON Print",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _logger.Error("Falha na impressão de teste.", exception);
            MessageBox.Show(exception.Message, "Falha na impressão", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshPrinters()
    {
        var selected = _printers.SelectedItem?.ToString() ?? _settingsStore.Current.PrinterName;
        _printers.Items.Clear();
        _printers.Items.AddRange(_printer.GetInstalledPrinters().Cast<object>().ToArray());
        if (_printers.Items.Contains(selected)) _printers.SelectedItem = selected;
        else if (_printers.Items.Count > 0) _printers.SelectedIndex = 0;
    }

    private void UpdatePairingView()
    {
        var credential = _credentialStore.Load();
        _pairingStatus.Text = credential is null
            ? "Este computador ainda não está vinculado."
            : $"Vinculado a: {credential.RestaurantName}";
        _pairButton.Enabled = credential is null;
        _unpairButton.Enabled = credential is not null;
        _pairingCode.Enabled = credential is null;
    }

    private void WorkerOnStatusChanged(object? sender, WorkerStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(status));
            return;
        }
        SetStatus(status);
    }

    private void SetStatus(WorkerStatus status)
    {
        _stateLabel.Text = status.Message;
        _stateDot.BackColor = status.State switch
        {
            ConnectionState.Connected => Color.SeaGreen,
            ConnectionState.Printing => Color.RoyalBlue,
            ConnectionState.Error => Color.Firebrick,
            _ => Color.Gray
        };
        _tray.Text = $"ZYRON Print - {status.Message}"[..Math.Min(63, $"ZYRON Print - {status.Message}".Length)];
    }

    private void OnShown(object? sender, EventArgs eventArgs)
    {
        _worker.Start();
        if (_startMinimized) HideToTray();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose) return;
        eventArgs.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _tray.ShowBalloonTip(1500, "ZYRON Print", "O aplicativo continua ativo perto do relógio.", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _worker.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += handler;
        return button;
    }

    private static Control Row(string label, params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
        panel.Controls.Add(new Label { Text = label, Width = 130, Padding = new Padding(0, 6, 0, 0) });
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static FlowLayoutPanel PageContent(Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 16, 18, 12),
            AutoScroll = true
        };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private static TabPage Tab(string title, Control content)
    {
        var page = new TabPage(title)
        {
            Padding = new Padding(4),
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = true
        };
        page.Controls.Add(content);
        return page;
    }
}
