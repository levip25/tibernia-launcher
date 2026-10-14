using OtsLauncher.Core;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OtsLauncher.Windows;

public sealed class MainForm : Form
{
    private enum ClientStatusState
    {
        Checking,
        NeedsUpdate,
        Ready,
        Error
    }

    private readonly DefaultPlatformPaths _paths;
    private readonly IAppLog _log;
    private readonly ManifestStore _store = new();
    private readonly ProfileResolver _resolver = new();
    private readonly FileIndexer _indexer = new();
    private readonly IPlatformInfo _platform = new WindowsPlatformInfo();
    private readonly IClientLauncher _launcher;
    private readonly ComboBox _servers = new();
    private readonly Label _info = new();
    private readonly Panel _statusPanel = new();
    private readonly Label _statusBadge = new();
    private readonly Button _update = new();
    private readonly Button _updateAll = new();
    private readonly Button _repair = new();
    private readonly Button _play = new();
    private readonly Dictionary<string, Button> _languageButtons = new();
    private readonly Label _worldPrompt = new();
    private readonly Label _worldTitle = new();
    private readonly Label _worldSubtitle = new();
    private readonly Label _connection = new();
    private readonly Panel _newsPanel = new();
    private readonly Label _newsHeading = new();
    private readonly Label _newsPreviousHeading = new();
    private readonly Label _newsFeaturedHeading = new();
    private readonly List<LinkLabel> _newsLinks = new();
    private readonly ProgressBar _progress = new();
    private readonly Dictionary<string, WorldTileButton> _worldButtons = new();
    private readonly Dictionary<string, Image> _art = new();
    private Image? _currentArt;
    private Image? _wordmark;
    private int _connectionRevision;
    private static readonly Color Gold = Color.FromArgb(224, 177, 82);
    private static readonly Color Ink = Color.FromArgb(9, 13, 16);
    private static readonly Color Parchment = Color.FromArgb(240, 223, 189);
    private NetworkManifest? _manifest;
    private CancellationTokenSource? _cts;
    private readonly string _preferencesPath;
    private readonly ClientLanguageService _clientLanguages;
    private string _languageCode = LauncherLanguage.English;
    private string? _verifiedProfileKey;

    public MainForm()
    {
        Text = "Tibernia Launcher";
        ClientSize = new Size(1180, 720);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1180, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BackColor = Ink;
        ForeColor = Parchment;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        _paths = new DefaultPlatformPaths(AppContext.BaseDirectory);
        _paths.EnsureLayout(new SystemFileSystem());
        _log = new AppLog(_paths.Logs);
        _launcher = new WindowsClientLauncher(_log);
        _preferencesPath = Path.Combine(_paths.Root, "launcher.preferences.json");
        _languageCode = LauncherPreferences.Load(_preferencesPath).Language;
        _clientLanguages = new ClientLanguageService(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _clientLanguages.ApplyToAll(_languageCode);
        _log.Info("launcher start windows-x64");

        foreach (var world in new[] { "custom", "classic", "latest", "war" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "branding", world + ".png");
            if (File.Exists(path)) _art[world] = Image.FromFile(path);
        }
        var wordmarkPath = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "web", "tibernia-wordmark.png");
        if (File.Exists(wordmarkPath)) _wordmark = Image.FromFile(wordmarkPath);
        var brand = new PictureBox
        {
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = _wordmark,
            Cursor = Cursors.Hand,
            AccessibleName = "Tibernia"
        };
        brand.SetBounds(30, 10, 232, 69);
        brand.Click += (_, _) => OpenWebsite();
        brand.MouseDown += DragWindow;
        Controls.Add(brand);
        MouseDown += DragWindow;
        var languageIndex = 0;
        foreach (var language in new[]
                 {
                     (LauncherLanguage.English, "English"),
                     (LauncherLanguage.Polish, "Polski"),
                     (LauncherLanguage.BrazilianPortuguese, "Português Brasileiro")
                 })
        {
            var languageButton = new Button();
            StyleButton(languageButton, string.Empty, 934 + languageIndex++ * 42, 23, 36, 32, false);
            languageButton.Image = CreateFlagImage(language.Item1);
            languageButton.ImageAlign = ContentAlignment.MiddleCenter;
            languageButton.AccessibleName = language.Item2;
            var languageCode = language.Item1;
            languageButton.Click += async (_, _) => await ChangeLanguageAsync(languageCode);
            _languageButtons[languageCode] = languageButton;
            Controls.Add(languageButton);
        }
        var minimize = new Button();
        StyleButton(minimize, "−", 1074, 23, 36, 32, false);
        minimize.AccessibleName = T("Minimize", "Minimalizuj");
        minimize.FlatAppearance.BorderSize = 0;
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = new Button();
        StyleButton(close, "×", 1116, 23, 36, 32, false);
        close.AccessibleName = T("Close", "Zamknij");
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { minimize, close });
        AddLabel(_worldPrompt, "", 40, 89, 500, 18, 7.5f, Color.FromArgb(190, 181, 162));
        _worldPrompt.Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold);
        var index = 0;
        foreach (var item in new[]
                 {
                     ("custom", "CUSTOM", "15.25  ·  NEW"),
                     ("classic", "CLASSIC", "7.72  ·  RPG"),
                     ("latest", "LATEST", "15.25  ·  GLOBAL"),
                     ("war", "WAR", "8.00  ·  PVP")
                 })
        {
            var button = new WorldTileButton
            {
                Title = item.Item2,
                Subtitle = item.Item3,
                Accent = AccentFor(item.Item1),
                Artwork = _art.GetValueOrDefault(item.Item1),
                AccessibleName = $"{item.Item2} {item.Item3}"
            };
            button.SetBounds(40 + index++ * 274, 112, 260, 70);
            var world = item.Item1;
            button.Click += (_, _) => _servers.SelectedItem = world;
            _worldButtons[world] = button;
            Controls.Add(button);
        }
        _servers.Visible = false;
        _servers.DropDownStyle = ComboBoxStyle.DropDownList;
        _servers.SelectedIndexChanged += async (_, _) => await RefreshStatusAsync();
        AddLabel(_worldTitle, "", 48, 236, 510, 68, 40, Parchment, "Georgia");
        _worldTitle.Font = new Font("Georgia", 40, FontStyle.Bold);
        AddLabel(_worldSubtitle, "", 51, 311, 455, 78, 12, Color.FromArgb(222, 216, 202));
        _worldSubtitle.Font = new Font("Segoe UI", 12, FontStyle.Regular);
        _statusPanel.SetBounds(530, 654, 160, 44);
        _statusPanel.BackColor = Color.FromArgb(226, 18, 24, 24);
        _statusPanel.BorderStyle = BorderStyle.FixedSingle;
        _statusBadge.Dock = DockStyle.Fill;
        _statusBadge.TextAlign = ContentAlignment.MiddleCenter;
        _statusBadge.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _statusPanel.Controls.Add(_statusBadge);
        Controls.Add(_statusPanel);
        AddLabel(_info, "", 362, 627, 470, 22, 8.5f, Color.FromArgb(203, 215, 205));
        _info.TextAlign = ContentAlignment.MiddleLeft;
        _info.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        AddLabel(_connection, "", 850, 627, 280, 22, 8.5f, Color.Silver);
        _connection.TextAlign = ContentAlignment.MiddleRight;
        _newsPanel.SetBounds(590, 194, 540, 410);
        _newsPanel.BackColor = Color.Transparent;
        _newsPanel.BorderStyle = BorderStyle.None;
        _newsPanel.Paint += PaintNewsPanel;
        _newsHeading.SetBounds(24, 18, 470, 28);
        _newsHeading.Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold);
        _newsHeading.ForeColor = Gold;
        _newsPanel.Controls.Add(_newsHeading);
        _newsPreviousHeading.SetBounds(24, 196, 470, 24);
        _newsPreviousHeading.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        _newsPreviousHeading.ForeColor = Color.FromArgb(226, 205, 164);
        _newsPreviousHeading.TextAlign = ContentAlignment.MiddleLeft;
        _newsPanel.Controls.Add(_newsPreviousHeading);
        _newsFeaturedHeading.SetBounds(26, 57, 470, 24);
        _newsFeaturedHeading.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        _newsFeaturedHeading.ForeColor = Gold;
        _newsFeaturedHeading.BackColor = Color.Transparent;
        _newsPanel.Controls.Add(_newsFeaturedHeading);
        for (var i = 0; i < 4; i++)
        {
            var link = new LinkLabel
            {
                Left = 22,
                Top = i == 0 ? 82 : 224 + (i - 1) * 54,
                Width = 494,
                Height = i == 0 ? 100 : 46,
                BackColor = Color.Transparent,
                LinkColor = i == 0 ? Color.FromArgb(244, 224, 185) : Color.FromArgb(232, 226, 213),
                ActiveLinkColor = Gold,
                VisitedLinkColor = Color.FromArgb(190, 183, 164),
                Font = new Font(i == 0 ? "Georgia" : "Segoe UI", i == 0 ? 17f : 10f, i == 0 ? FontStyle.Bold : FontStyle.Regular),
                LinkBehavior = LinkBehavior.NeverUnderline,
                BorderStyle = BorderStyle.None,
                Padding = i == 0 ? new Padding(4, 6, 8, 4) : new Padding(4, 5, 8, 3),
                AutoEllipsis = false,
                Cursor = Cursors.Hand
            };
            link.LinkClicked += (_, _) => OpenNewsPage();
            _newsLinks.Add(link);
            _newsPanel.Controls.Add(link);
        }
        _progress.SetBounds(40, 638, 1100, 5);
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;
        StyleButton(_update, "", 40, 654, 150, 44, false);
        _update.Click += async (_, _) => await RunUpdateSelected();
        StyleButton(_updateAll, "", 200, 654, 150, 44, false);
        _updateAll.Click += async (_, _) => await RunUpdateAll();
        StyleButton(_repair, "", 360, 654, 150, 44, false);
        _repair.Click += async (_, _) => await RepairSelected();
        StyleButton(_play, "", 850, 651, 280, 50, true);
        _play.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _update.Anchor = _updateAll.Anchor = _repair.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _play.Click += async (_, _) => await PlayAsync();
        Controls.AddRange(new Control[] { _servers, _update, _updateAll, _repair, _play, _progress, _newsPanel });
        ApplyLanguage();
        Load += async (_, _) => await LoadManifestAsync();
    }

    private bool IsPolish => _languageCode == LauncherLanguage.Polish;
    private bool IsBrazilianPortuguese => _languageCode == LauncherLanguage.BrazilianPortuguese;
    private string T(string english, string polish, string? brazilianPortuguese = null) =>
        IsPolish ? polish : IsBrazilianPortuguese ? brazilianPortuguese ?? english : english;

    private async Task ChangeLanguageAsync(string languageCode)
    {
        _languageCode = LauncherLanguage.Normalize(languageCode);
        new LauncherPreferences { Language = _languageCode }.Save(_preferencesPath);
        _clientLanguages.ApplyToAll(_languageCode);
        _log.Info($"language {_languageCode} applied to launcher and four clients");
        ApplyLanguage();
        await RefreshStatusAsync();
    }

    private void ApplyLanguage()
    {
        _worldPrompt.Text = T("SELECT YOUR REALM", "WYBIERZ SWÓJ ŚWIAT", "ESCOLHA SEU MUNDO");
        var selectedName = Selected()?.Name ?? T("selected", "wybrany", "selecionado");
        _update.Text = T($"Download {selectedName}", $"Pobierz {selectedName}", $"Baixar {selectedName}");
        _updateAll.Text = T("Download all", "Pobierz wszystkie", "Baixar tudo");
        _repair.Text = T("Repair selected", "Napraw wybrany", "Reparar selecionado");
        _play.Text = T("ENTER WORLD  →", "WEJDZ DO SWIATA  →", "ENTRAR NO MUNDO  →");
        _newsHeading.Text = T("LATEST NEWS", "NAJNOWSZE AKTUALNOSCI", "ULTIMAS NOTICIAS");
        _newsPreviousHeading.Text = T("FIELD NOTES", "ZAPISKI ZE SWIATA", "NOTAS DO MUNDO");
        _newsFeaturedHeading.Text = T("FEATURED DISPATCH", "NAJNOWSZA WIADOMOSC", "DESTAQUE");
        foreach (var (code, button) in _languageButtons)
        {
            var selected = code == _languageCode;
            button.FlatAppearance.BorderColor = selected ? Gold : Color.FromArgb(85, 83, 70);
            button.BackColor = selected ? Color.FromArgb(55, 45, 27) : Color.FromArgb(23, 30, 31);
        }
        if (Selected() is { } profile) RenderLauncherNews(profile.Id);
    }

    private static Bitmap CreateFlagImage(string languageCode)
    {
        const int width = 26;
        const int height = 18;
        var image = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var frame = new Rectangle(1, 1, width - 3, height - 3);

        if (languageCode == LauncherLanguage.Polish)
        {
            graphics.FillRectangle(Brushes.WhiteSmoke, frame.X, frame.Y, frame.Width, frame.Height / 2);
            using var red = new SolidBrush(Color.FromArgb(220, 20, 60));
            graphics.FillRectangle(red, frame.X, frame.Y + frame.Height / 2, frame.Width, frame.Height - frame.Height / 2);
        }
        else if (languageCode == LauncherLanguage.BrazilianPortuguese)
        {
            using var green = new SolidBrush(Color.FromArgb(0, 143, 76));
            using var yellow = new SolidBrush(Color.FromArgb(255, 223, 0));
            using var blue = new SolidBrush(Color.FromArgb(0, 39, 118));
            graphics.FillRectangle(green, frame);
            graphics.FillPolygon(yellow,
            new Point[]
            {
                new Point(frame.Left + frame.Width / 2, frame.Top + 2),
                new Point(frame.Right - 2, frame.Top + frame.Height / 2),
                new Point(frame.Left + frame.Width / 2, frame.Bottom - 2),
                new Point(frame.Left + 2, frame.Top + frame.Height / 2)
            });
            graphics.FillEllipse(blue, frame.Left + 8, frame.Top + 4, 8, 8);
        }
        else
        {
            using var blue = new SolidBrush(Color.FromArgb(1, 33, 105));
            using var white = new Pen(Color.WhiteSmoke, 5);
            using var red = new Pen(Color.FromArgb(200, 16, 46), 2.5f);
            graphics.FillRectangle(blue, frame);
            graphics.SetClip(frame);
            graphics.DrawLine(white, frame.Left, frame.Top, frame.Right, frame.Bottom);
            graphics.DrawLine(white, frame.Right, frame.Top, frame.Left, frame.Bottom);
            graphics.DrawLine(red, frame.Left, frame.Top, frame.Right, frame.Bottom);
            graphics.DrawLine(red, frame.Right, frame.Top, frame.Left, frame.Bottom);
            graphics.ResetClip();
            graphics.FillRectangle(Brushes.WhiteSmoke, frame.Left, frame.Top + 5, frame.Width, 5);
            graphics.FillRectangle(Brushes.WhiteSmoke, frame.Left + 9, frame.Top, 5, frame.Height);
            using var cross = new SolidBrush(Color.FromArgb(200, 16, 46));
            graphics.FillRectangle(cross, frame.Left, frame.Top + 6, frame.Width, 3);
            graphics.FillRectangle(cross, frame.Left + 10, frame.Top, 3, frame.Height);
        }

        using var border = new Pen(Color.FromArgb(180, 226, 207, 165));
        graphics.DrawRectangle(border, frame);
        return image;
    }

    private void PaintNewsPanel(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var accent = Selected() is { } profile ? AccentFor(profile.Id) : Gold;
        using var veil = new SolidBrush(Color.FromArgb(218, 9, 12, 13));
        using var frame = new Pen(Color.FromArgb(150, accent));
        using var divider = new Pen(Color.FromArgb(68, accent));
        e.Graphics.FillRectangle(veil, panel.ClientRectangle);
        e.Graphics.DrawRectangle(frame, 0, 0, panel.Width - 1, panel.Height - 1);
        e.Graphics.DrawLine(divider, 22, 48, panel.Width - 22, 48);
        e.Graphics.DrawLine(divider, 22, 192, panel.Width - 22, 192);
        e.Graphics.DrawLine(divider, 22, 274, panel.Width - 22, 274);
        e.Graphics.DrawLine(divider, 22, 328, panel.Width - 22, 328);
        using var accentBrush = new SolidBrush(accent);
        e.Graphics.FillRectangle(accentBrush, 0, 0, 4, panel.Height);
        e.Graphics.FillPolygon(accentBrush, new[]
        {
            new Point(panel.Width - 28, 0),
            new Point(panel.Width, 0),
            new Point(panel.Width, 28)
        });
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || (sender == this && e.Y > 72)) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ClassStyle |= 0x20000; return parameters; }
    }

    private void AddLabel(Label label, string text, int x, int y, int width, int height, float size, Color color, string family = "Segoe UI")
    {
        label.Text = text;
        label.SetBounds(x, y, width, height);
        label.BackColor = Color.Transparent;
        label.ForeColor = color;
        label.Font = new Font(family, size);
        Controls.Add(label);
    }

    private static void StyleButton(Button button, string text, int x, int y, int width, int height, bool primary)
    {
        button.Text = text;
        button.SetBounds(x, y, width, height);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Gold : Color.FromArgb(78, 74, 65);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(229, 201, 148) : Color.FromArgb(42, 45, 43);
        button.BackColor = primary ? Gold : Color.FromArgb(220, 17, 20, 20);
        button.ForeColor = primary ? Color.FromArgb(20, 22, 20) : Color.FromArgb(226, 219, 203);
        button.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (_currentArt is not null)
        {
            var scale = Math.Max((float)ClientSize.Width / _currentArt.Width, (float)ClientSize.Height / _currentArt.Height);
            var width = _currentArt.Width * scale;
            var height = _currentArt.Height * scale;
            e.Graphics.DrawImage(_currentArt, (ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        }
        using var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(250, 7, 10, 12), Color.FromArgb(72, 7, 10, 12), 0f);
        e.Graphics.FillRectangle(shade, ClientRectangle);
        using var header = new SolidBrush(Color.FromArgb(244, 8, 12, 15));
        e.Graphics.FillRectangle(header, 0, 0, ClientSize.Width, 84);
        using var topShade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(225, 6, 9, 11), Color.FromArgb(16, 6, 8, 9), 90f);
        e.Graphics.FillRectangle(topShade, 0, 84, ClientSize.Width, 112);
        using var heroPlate = new SolidBrush(Color.FromArgb(80, 8, 10, 10));
        e.Graphics.FillPolygon(heroPlate, new[]
        {
            new Point(28, 188), new Point(548, 188), new Point(548, 455),
            new Point(470, 533), new Point(28, 533)
        });
        using var footer = new SolidBrush(Color.FromArgb(242, 8, 11, 12));
        e.Graphics.FillRectangle(footer, 0, ClientSize.Height - 95, ClientSize.Width, 95);
        var accent = Selected() is { } profile ? AccentFor(profile.Id) : Gold;
        using var line = new Pen(Color.FromArgb(130, accent));
        e.Graphics.DrawLine(line, 40, ClientSize.Height - 95, ClientSize.Width - 40, ClientSize.Height - 95);
        e.Graphics.DrawLine(line, 0, 83, ClientSize.Width, 83);
        e.Graphics.DrawLine(line, 40, 194, ClientSize.Width - 40, 194);
        e.Graphics.DrawRectangle(line, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private static Color AccentFor(string worldId) => worldId switch
    {
        "custom" => Color.FromArgb(75, 196, 179),
        "classic" => Color.FromArgb(217, 154, 65),
        "war" => Color.FromArgb(223, 85, 87),
        _ => Color.FromArgb(224, 177, 82)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _art.Values) image.Dispose();
            _wordmark?.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task LoadManifestAsync()
    {
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "manifests", "network.manifest.json");
            var local = Path.Combine(_paths.Root, "manifests", "network.manifest.json");
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "launcher.settings.json");
            var settings = LauncherSettings.LoadIfPresent(settingsPath);

            if (!string.IsNullOrWhiteSpace(settings.ManifestUrl))
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    _manifest = await new ManifestSource(http, _log, local).LoadAsync(settings.ManifestUrl, CancellationToken.None);
                    _log.Info("remote manifest loaded");
                }
                catch (Exception ex)
                {
                    _log.Warn($"remote manifest unavailable: {ex.Message}");
                }
            }

            if (_manifest is null)
            {
                var path = File.Exists(local) ? local : bundled;
                _manifest = _store.LoadFromFile(path);
                _log.Info($"fallback manifest loaded from {path}");
            }

            _servers.Items.Clear();
            foreach (var s in _manifest.Servers)
                _servers.Items.Add(s.Id);
            if (_servers.Items.Count > 0)
                _servers.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            SetClientStatus(ClientStatusState.Error, DescribeFailure(ex));
        }
    }

    private ResolvedProfile? Selected()
    {
        if (_manifest is null || _servers.SelectedItem is null)
            return null;
        return _resolver.Resolve(_manifest, _servers.SelectedItem.ToString()!, _platform.Current);
    }

    private async Task RefreshStatusAsync()
    {
        var revision = ++_connectionRevision;
        try
        {
            var profile = Selected();
            if (profile is null)
                return;
            _update.Text = T($"Download {profile.Name}", $"Pobierz {profile.Name}", $"Baixar {profile.Name}");
            _log.Info($"select {profile.Id} {_platform.Current.Key}");
            _currentArt = _art.GetValueOrDefault(profile.Id);
            _worldTitle.Text = profile.Id switch { "classic" => "Classic", "war" => "WAR", "custom" => "Custom", _ => "Latest" };
            _worldSubtitle.Text = profile.Id switch
            {
                "classic" => T("Return to the beginning of the adventure.\r\nA simple world, great expeditions.", "Powrot do poczatku przygody.\r\nProsty swiat, wielkie wyprawy.", "Volte ao inicio da aventura.\r\nUm mundo simples, grandes expedicoes."),
                "war" => T("Fight in the shadow of ancient strongholds.\r\nClassic PvP atmosphere.", "Stan do walki w cieniu dawnych twierdz.\r\nKlasyczny klimat PvP.", "Lute nas fortalezas antigas.\r\nAtmosfera classica de PvP."),
                "custom" => T("Discover the secrets of Tervia.\r\nA unique world, new stories.", "Odkryj tajemnice Tervii.\r\nWlasny swiat, nowe historie.", "Descubra os segredos de Tervia.\r\nUm mundo unico, novas historias."),
                _ => T("A new chapter of a great adventure.\r\nExplore, grow and play together.", "Nowy rozdzial wielkiej przygody.\r\nOdkrywaj, rozwijaj postac, graj razem.", "Um novo capitulo de uma grande aventura.\r\nExplore, evolua e jogue em grupo.")
            };
            _newsHeading.Text = $"{_worldTitle.Text.ToUpperInvariant()}  ·  {T("NEWS", "AKTUALNOSCI", "NOTICIAS")}";
            RenderLauncherNews(profile.Id);
            _ = LoadLauncherNewsAsync(profile.Id, revision);
            foreach (var (id, button) in _worldButtons)
            {
                var selected = id == profile.Id;
                button.Selected = selected;
                button.Invalidate();
            }
            var worldAccent = AccentFor(profile.Id);
            _worldPrompt.ForeColor = worldAccent;
            _newsHeading.ForeColor = worldAccent;
            _newsFeaturedHeading.ForeColor = worldAccent;
            _play.BackColor = worldAccent;
            _play.FlatAppearance.BorderColor = worldAccent;
            _play.FlatAppearance.MouseOverBackColor = ControlPaint.Light(worldAccent, .18f);
            _verifiedProfileKey = null;
            SetClientStatus(ClientStatusState.Checking);
            Invalidate(true);
            _ = CheckConnectionAsync(profile, revision);

            var ready = await Task.Run(() =>
                _indexer.IsVersionCurrent(profile, _paths.ProfileDirectory(profile.Id), includeAppFiles: true));
            if (IsDisposed || revision != _connectionRevision)
                return;
            SetClientStatus(
                ready ? ClientStatusState.Ready : ClientStatusState.NeedsUpdate,
                ready
                    ? null
                    : T($"Download {profile.Name} to play.", $"Pobierz klienta {profile.Name}, aby zagrać.", $"Baixe {profile.Name} para jogar."));
            _verifiedProfileKey = ready ? ProfileVerificationKey(profile) : null;
        }
        catch (PlatformNotSupportedException ex)
        {
            SetClientStatus(ClientStatusState.Error, ex.Message);
        }
    }

    private async Task CheckConnectionAsync(ResolvedProfile profile, int revision)
    {
        _connection.Text = T("Checking server...", "Sprawdzanie serwera...", "Verificando servidor...");
        var text = T("Server unavailable", "Serwer niedostepny", "Servidor indisponivel");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var socket = new TcpClient();
            await socket.ConnectAsync(profile.Server.Host ?? "127.0.0.1", profile.Server.LoginPort ?? 7171, timeout.Token);
            text = T("Login port available", "Port logowania dostepny", "Porta de login disponivel");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { }
        if (!IsDisposed && revision == _connectionRevision) _connection.Text = text;
    }

    private async Task RunUpdateSelected()
    {
        if (_manifest is null)
            await LoadManifestAsync();
        var profile = Selected();
        if (profile is null)
            return;

        _cts = new CancellationTokenSource();
        try
        {
            SetActionButtonsEnabled(false);
            _verifiedProfileKey = null;
            SetClientStatus(ClientStatusState.Checking);
            ShowProgress(T($"Preparing {profile.Name}...", $"Przygotowywanie klienta {profile.Name}...", $"Preparando {profile.Name}..."));
            using var http = new HttpClient();
            var update = new UpdateService(new SafeDownloader(http, _log), _log);
            var progress = await CreateProgressAsync(profile, null, _cts.Token);
            await update.UpdateIfNeededAsync(profile, _paths, includeAppFiles: true, _cts.Token, progress);
            new OtClientConfigWriter().Write(profile, _paths.ProfileDirectory(profile.Id));
            _clientLanguages.ApplyToAll(_languageCode);
            SetProgressComplete(T($"{profile.Name} is ready.", $"Klient {profile.Name} jest gotowy.", $"{profile.Name} está pronto."));
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            var message = DescribeFailure(ex);
            SetClientStatus(ClientStatusState.Error, message);
            MessageBox.Show(message, T("Update failed", "Aktualizacja nie powiodła się", "Falha na atualização"));
        }
        finally
        {
            HideProgress();
            SetActionButtonsEnabled(true);
        }
    }

    private async Task RunUpdateAll()
    {
        await LoadManifestAsync();
        if (_manifest is null)
            return;

        _cts = new CancellationTokenSource();
        try
        {
            SetActionButtonsEnabled(false);
            _verifiedProfileKey = null;
            SetClientStatus(ClientStatusState.Checking);
            ShowProgress(T("Checking update versions...", "Sprawdzanie wersji aktualizacji..."));
            using var http = new HttpClient();
            var update = new UpdateService(new SafeDownloader(http, _log), _log);
            var worlds = _manifest.Servers
                .Where(server => server.Packages.ContainsKey(_platform.Current.Key))
                .ToList();
            var failures = new List<string>();

            for (var index = 0; index < worlds.Count; index++)
            {
                var profile = _resolver.Resolve(_manifest, worlds[index].Id, _platform.Current);
                _info.Text = T($"Checking world {profile.Name} ({index + 1}/{worlds.Count})...", $"Sprawdzanie swiata {profile.Name} ({index + 1}/{worlds.Count})...");
                try
                {
                    var progress = await CreateProgressAsync(profile, $"{index + 1}/{worlds.Count}", _cts.Token);
                    await update.UpdateIfNeededAsync(profile, _paths, includeAppFiles: true, _cts.Token, progress);
                    new OtClientConfigWriter().Write(profile, _paths.ProfileDirectory(profile.Id));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error($"update {profile.Id}: {ex.Message}");
                    failures.Add($"{profile.Name}: {DescribeFailure(ex)}");
                }
            }

            if (failures.Count > 0)
            {
                var message = T(
                    "Some worlds could not be updated:\r\n",
                    "Nie udalo sie zaktualizowac czesci swiatow:\r\n",
                    "Nao foi possivel atualizar alguns mundos:\r\n") + string.Join("\r\n", failures);
                SetClientStatus(ClientStatusState.Error, message);
                MessageBox.Show(message, T("Update failed", "Aktualizacja nie powiodla sie", "Falha na atualizacao"));
                return;
            }

            _clientLanguages.ApplyToAll(_languageCode);
            _info.Text = T("All four worlds are up to date.", "Wszystkie cztery swiaty sa aktualne.");
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            var message = DescribeFailure(ex);
            SetClientStatus(ClientStatusState.Error, message);
            MessageBox.Show(message, T("Update failed", "Aktualizacja nie powiodla sie", "Falha na atualizacao"));
        }
        finally
        {
            HideProgress();
            SetActionButtonsEnabled(true);
        }
    }

    private async Task RepairSelected()
    {
        var profile = Selected();
        if (profile is null)
            return;
        _cts = new CancellationTokenSource();
        try
        {
            SetActionButtonsEnabled(false);
            _verifiedProfileKey = null;
            SetClientStatus(ClientStatusState.Checking);
            ShowProgress(T("Verifying every client file... This may take a while.", "Sprawdzanie wszystkich plikow klienta... To moze chwile potrwac."));
            var update = new UpdateService(new SafeDownloader(new HttpClient(), _log), _log);
            var progress = await CreateProgressAsync(profile, null, _cts.Token);
            await update.RepairAsync(profile, _paths, includeAppFiles: true, _cts.Token, progress);
            new OtClientConfigWriter().Write(profile, _paths.ProfileDirectory(profile.Id));
            _clientLanguages.ApplyToAll(_languageCode);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            var message = DescribeFailure(ex);
            SetClientStatus(ClientStatusState.Error, message);
            MessageBox.Show(message, T("Repair failed", "Naprawa nie powiodla sie", "Falha no reparo"));
        }
        finally
        {
            HideProgress();
            SetActionButtonsEnabled(true);
        }
    }

    private void SetActionButtonsEnabled(bool enabled) =>
        _update.Enabled = _updateAll.Enabled = _repair.Enabled = _play.Enabled = enabled;

    private async Task PlayAsync()
    {
        var profile = Selected();
        if (profile is null)
            return;
        try
        {
            var root = _paths.ProfileDirectory(profile.Id);
            SetActionButtonsEnabled(false);
            var profileKey = ProfileVerificationKey(profile);
            var ready = string.Equals(_verifiedProfileKey, profileKey, StringComparison.Ordinal);
            if (!ready)
            {
                SetClientStatus(ClientStatusState.Checking);
                await Task.Yield();
                ready = await Task.Run(() => _indexer.IsVersionCurrent(profile, root, includeAppFiles: true));
            }
            if (!ready)
            {
                _verifiedProfileKey = null;
                SetClientStatus(
                    ClientStatusState.NeedsUpdate,
                    T($"Download {profile.Name} first.", $"Najpierw pobierz klienta {profile.Name}.", $"Baixe {profile.Name} primeiro."));
                MessageBox.Show(T($"Download {profile.Name} first.", $"Najpierw wybierz „Pobierz {profile.Name}”.", $"Baixe {profile.Name} primeiro."));
                return;
            }
            _verifiedProfileKey = profileKey;
            SetClientStatus(ClientStatusState.Ready);
            _clientLanguages.ApplyToAll(_languageCode);
            await _launcher.LaunchAsync(profile, root, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            _verifiedProfileKey = null;
            var message = DescribeFailure(ex);
            SetClientStatus(ClientStatusState.Error, message);
            MessageBox.Show(message, T("Launch failed", "Uruchomienie nie powiodlo sie", "Falha ao iniciar"));
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    private void ShowProgress(string message)
    {
        _info.Text = message;
        _progress.Value = 0;
        _progress.Visible = true;
        _progress.BringToFront();
    }

    private void HideProgress() => _progress.Visible = false;

    private void SetProgressComplete(string message)
    {
        _progress.Value = _progress.Maximum;
        _info.Text = message;
    }

    private async Task<IDownloadProgress> CreateProgressAsync(ResolvedProfile profile, string? sequence, CancellationToken ct)
    {
        var status = await Task.Run(
            () => _indexer.Inspect(profile, _paths.ProfileDirectory(profile.Id), includeAppFiles: true),
            ct);
        var required = status.Files.Where(file => file.Status != FileStatus.Ok).Select(file => file.File).ToList();
        var total = required.Sum(file => Math.Max(0, file.Size));
        return new ClientDownloadProgress(this, profile.Name, sequence, total);
    }

    private string DescribeFailure(Exception exception)
    {
        if (exception is DownloadFailedException download)
        {
            var file = string.IsNullOrWhiteSpace(download.FilePath) ? "" : $" ({download.FilePath})";
            return download.Kind switch
            {
                DownloadFailureKind.Network => T(
                    $"No connection to the update server{file}. Check your internet connection and try again.",
                    $"Brak polaczenia z serwerem aktualizacji{file}. Sprawdz internet i sprobuj ponownie.",
                    $"Sem conexao com o servidor de atualizacao{file}. Verifique a internet e tente novamente."),
                DownloadFailureKind.Timeout => T(
                    $"The update server did not respond in time{file}. Try again in a moment.",
                    $"Serwer aktualizacji nie odpowiedzial na czas{file}. Sprobuj ponownie za chwile.",
                    $"O servidor de atualizacao nao respondeu a tempo{file}. Tente novamente em instantes."),
                DownloadFailureKind.Server => T(
                    $"The update server returned an error{file}. Try again later.",
                    $"Serwer aktualizacji zwrocil blad{file}. Sprobuj ponownie pozniej.",
                    $"O servidor de atualizacao retornou um erro{file}. Tente novamente mais tarde."),
                DownloadFailureKind.Integrity => T(
                    $"The downloaded file failed verification{file}. Use Repair files; if the problem repeats, report it.",
                    $"Pobrany plik nie przeszedl weryfikacji{file}. Uzyj Napraw pliki; jesli blad wroci, zglos go.",
                    $"O arquivo baixado falhou na verificacao{file}. Use Reparar arquivos; se repetir, informe o erro."),
                DownloadFailureKind.Storage => T(
                    $"The launcher could not save the update{file}. Check free disk space and folder permissions.",
                    $"Launcher nie mogl zapisac aktualizacji{file}. Sprawdz wolne miejsce i uprawnienia folderu.",
                    $"O launcher nao conseguiu salvar a atualizacao{file}. Verifique o espaco e as permissoes."),
                DownloadFailureKind.Configuration => T(
                    $"The update configuration is invalid{file}. Use Repair files or install the newest launcher.",
                    $"Konfiguracja aktualizacji jest nieprawidlowa{file}. Uzyj Napraw pliki lub zainstaluj najnowszy launcher.",
                    $"A configuracao da atualizacao e invalida{file}. Use Reparar arquivos ou instale o launcher mais recente."),
                _ => T(
                    $"The update failed{file}. Try again; details were saved in the launcher log.",
                    $"Aktualizacja nie powiodla sie{file}. Sprobuj ponownie; szczegoly zapisano w logu launchera.",
                    $"A atualizacao falhou{file}. Tente novamente; os detalhes foram salvos no log.")
            };
        }

        return exception switch
        {
            HttpRequestException => T(
                "A network request failed. Check your internet connection and try again.",
                "Blad polaczenia sieciowego. Sprawdz internet i sprobuj ponownie.",
                "Falha na conexao de rede. Verifique a internet e tente novamente."),
            TaskCanceledException => T(
                "The operation timed out. Try again in a moment.",
                "Przekroczono czas operacji. Sprobuj ponownie za chwile.",
                "A operacao excedeu o tempo limite. Tente novamente em instantes."),
            UnauthorizedAccessException => T(
                "The launcher cannot write to its folder. Check folder permissions or run it from your user directory.",
                "Launcher nie moze zapisac plikow. Sprawdz uprawnienia albo uruchom go z folderu uzytkownika.",
                "O launcher nao pode gravar na pasta. Verifique as permissoes ou use a pasta do usuario."),
            IOException => T(
                "A file operation failed. Check free disk space and close programs using the client files.",
                "Operacja na pliku nie powiodla sie. Sprawdz miejsce na dysku i zamknij programy uzywajace plikow klienta.",
                "Falha na operacao de arquivo. Verifique o espaco e feche programas que usam os arquivos do cliente."),
            _ => T(
                $"Operation failed: {exception.Message}",
                $"Operacja nie powiodla sie: {exception.Message}",
                $"A operacao falhou: {exception.Message}")
        };
    }

    private static string ProfileVerificationKey(ResolvedProfile profile) =>
        $"{profile.Id}:{profile.Package.PackageVersion}:{profile.Package.AssetsVersion}";

    private void SetClientStatus(ClientStatusState state, string? detail = null)
    {
        _statusBadge.Text = state switch
        {
            ClientStatusState.Ready => T("✓  READY", "✓  GOTOWE", "✓  PRONTO"),
            ClientStatusState.NeedsUpdate => T("↓  DOWNLOAD", "↓  POBIERZ", "↓  BAIXAR"),
            ClientStatusState.Error => T("✕  ERROR", "✕  BŁĄD", "✕  ERRO"),
            _ => T("•  CHECKING", "•  SPRAWDZANIE", "•  VERIFICANDO")
        };
        _statusBadge.ForeColor = state switch
        {
            ClientStatusState.Ready => Color.FromArgb(126, 224, 157),
            ClientStatusState.NeedsUpdate => Color.FromArgb(255, 210, 96),
            ClientStatusState.Error => Color.FromArgb(255, 132, 132),
            _ => Color.FromArgb(255, 210, 96)
        };
        _statusPanel.BackColor = state switch
        {
            ClientStatusState.Ready => Color.FromArgb(16, 43, 31),
            ClientStatusState.NeedsUpdate => Color.FromArgb(58, 46, 15),
            ClientStatusState.Error => Color.FromArgb(62, 20, 24),
            _ => Color.FromArgb(58, 46, 15)
        };
        _statusPanel.BorderStyle = BorderStyle.FixedSingle;
        _info.Text = detail ?? string.Empty;
        _info.ForeColor = state == ClientStatusState.Error
            ? Color.FromArgb(255, 177, 177)
            : Color.FromArgb(211, 211, 198);
        _statusPanel.Refresh();
        _info.Refresh();
    }

    private sealed class ClientDownloadProgress : IDownloadProgress
    {
        private readonly MainForm _form;
        private readonly string _clientName;
        private readonly string? _sequence;
        private readonly long _expectedBytes;
        private readonly Dictionary<string, long> _received = new(StringComparer.OrdinalIgnoreCase);

        internal ClientDownloadProgress(MainForm form, string clientName, string? sequence, long expectedBytes)
        {
            _form = form;
            _clientName = clientName;
            _sequence = sequence;
            _expectedBytes = expectedBytes;
        }

        public void Report(string filePath, long received, long? total)
        {
            long downloaded;
            lock (_received)
            {
                _received[filePath] = Math.Max(0, received);
                downloaded = _received.Values.Sum();
            }
            var expected = _expectedBytes > 0 ? _expectedBytes : Math.Max(downloaded, total ?? 0);
            var ratio = expected > 0 ? Math.Clamp((double)downloaded / expected, 0, 1) : 0;
            if (_form.IsDisposed || !_form.IsHandleCreated)
                return;
            _form.BeginInvoke(() =>
            {
                var percent = (int)Math.Round(ratio * 100);
                _form._progress.Value = Math.Clamp(percent, _form._progress.Minimum, _form._progress.Maximum);
                var prefix = string.IsNullOrWhiteSpace(_sequence) ? string.Empty : $"({_sequence}) ";
                _form._info.Text = _form.T(
                    $"{prefix}Downloading {_clientName}: {percent}% · {FormatBytes(downloaded)} / {FormatBytes(expected)}",
                    $"{prefix}Pobieranie {_clientName}: {percent}% · {FormatBytes(downloaded)} / {FormatBytes(expected)}",
                    $"{prefix}Baixando {_clientName}: {percent}% · {FormatBytes(downloaded)} / {FormatBytes(expected)}");
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
            if (bytes >= 1024L) return $"{bytes / 1024d:0.0} KB";
            return $"{bytes} B";
        }
    }

    private void RenderLauncherNews(string world)
    {
        var items = world switch
        {
            "classic" => new[]
            {
                ("Real map, spawns and NPC base", "Real mapa, spawny i baza NPC"),
                ("Classic 7.72 login stabilized", "Logowanie Classic 7.72 ustabilizowane"),
                ("RME workspace is ready", "Srodowisko RME jest gotowe"),
                ("Launcher updates are now instant", "Aktualizacje launchera sa teraz szybsze")
            },
            "war" => new[]
            {
                ("Persistent 30-minute WAR rounds", "Trwale 30-minutowe rundy WAR"),
                ("Anti-farm and War Tokens enabled", "Anty-farm i War Tokeny wlaczone"),
                ("Six-city rotation deployed", "Rotacja szesciu miast wdrozona"),
                ("Guest accounts and team locks", "Konta guest i blokada druzyn")
            },
            "custom" => new[]
            {
                ("Tervia custom map is online", "Autorska mapa Tervii jest online"),
                ("Client 15.25 assets verified", "Pliki klienta 15.25 sprawdzone"),
                ("Independent economy and content", "Osobna ekonomia i zawartosc"),
                ("Fast launcher version checks", "Szybkie sprawdzanie wersji")
            },
            _ => new[]
            {
                ("Latest 15.25 connection maintenance", "Prace nad polaczeniem Latest 15.25"),
                ("RL world prepared for testing", "Swiat RL przygotowany do testow"),
                ("Client assets and sound verified", "Grafiki i dzwieki klienta sprawdzone"),
                ("Fast launcher version checks", "Szybkie sprawdzanie wersji")
            }
        };
        for (var i = 0; i < _newsLinks.Count; i++)
            _newsLinks[i].Text = i == 0
                ? $"{T(items[i].Item1, items[i].Item2)}\r\n\r\n{T("Read the full article on the website  →", "Czytaj calosc na stronie  →", "Leia o artigo completo no site  →")}" 
                : $"{T(items[i].Item1, items[i].Item2)}\r\n{T("Read on the website  →", "Czytaj na stronie  →", "Leia no site  →")}";
        _newsPanel.Tag = world;
    }

    private async Task LoadLauncherNewsAsync(string world, int revision)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var json = await http.GetStringAsync("https://tibernia.com/news.json");
            var feed = JsonSerializer.Deserialize<LauncherNewsFeed>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (feed?.Items is null || IsDisposed || revision != _connectionRevision) return;
            var language = IsPolish ? LauncherLanguage.Polish : IsBrazilianPortuguese ? LauncherLanguage.BrazilianPortuguese : LauncherLanguage.English;
            var titles = feed.Items
                .Where(item => item.World == world || item.World == "network")
                .Select(item => item.Title.GetValueOrDefault(language) ?? item.Title.GetValueOrDefault(LauncherLanguage.English))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Cast<string>()
                .ToList();
            for (var i = 0; i < Math.Min(titles.Count, _newsLinks.Count); i++)
                _newsLinks[i].Text = i == 0
                    ? $"{titles[i]}\r\n\r\n{T("Read the full patch notes on the website  →", "Pelne patch notes na stronie  →", "Patch notes completos no site  →")}" 
                    : $"{titles[i]}\r\n{T("Read on the website  →", "Czytaj na stronie  →", "Leia no site  →")}";
        }
        catch (Exception ex)
        {
            _log.Warn($"shared news feed: {ex.Message}");
        }
    }

    private sealed class LauncherNewsFeed
    {
        public List<LauncherNewsItem> Items { get; set; } = [];
    }

    private sealed class LauncherNewsItem
    {
        public string World { get; set; } = string.Empty;
        public Dictionary<string, string> Title { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void OpenNewsPage()
    {
        var world = _newsPanel.Tag?.ToString() ?? "latest";
        var url = $"https://tibernia.com/?world={Uri.EscapeDataString(world)}#aktualnosci";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log.Warn($"open news page: {ex.Message}"); }
    }

    private void OpenWebsite()
    {
        try { Process.Start(new ProcessStartInfo("https://tibernia.com/") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Warn($"open website: {ex.Message}"); }
    }
}

internal sealed class WorldTileButton : Button
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public Color Accent { get; init; } = Color.Goldenrod;
    public Image? Artwork { get; init; }
    public bool Selected { get; set; }
    private bool _hovered;

    public WorldTileButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.FromArgb(13, 18, 21);
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var bounds = ClientRectangle;
        if (Artwork is not null)
        {
            var scale = Math.Max((float)bounds.Width / Artwork.Width, (float)bounds.Height / Artwork.Height);
            var width = Artwork.Width * scale;
            var height = Artwork.Height * scale;
            e.Graphics.DrawImage(Artwork, (bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        }

        using var shade = new LinearGradientBrush(bounds,
            Color.FromArgb(Selected ? 130 : _hovered ? 162 : 188, 5, 8, 10),
            Color.FromArgb(Selected ? 232 : _hovered ? 232 : 244, 5, 8, 10), 0f);
        e.Graphics.FillRectangle(shade, bounds);
        using var border = new Pen(Color.FromArgb(Selected ? 210 : _hovered ? 145 : 76,
            Selected || _hovered ? Accent : Color.FromArgb(120, 108, 86)));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var accentHeight = Selected ? 4 : _hovered ? 3 : 2;
        using var accent = new SolidBrush(Color.FromArgb(Selected ? 255 : _hovered ? 185 : 105, Accent));
        e.Graphics.FillRectangle(accent, 0, Height - accentHeight, Width, accentHeight);

        using var titleFont = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, Title, titleFont,
            new Rectangle(15, 12, Width - 30, 22), Color.FromArgb(242, 232, 214),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, Subtitle, subtitleFont,
            new Rectangle(15, 38, Width - 30, 18), Selected ? Accent : Color.FromArgb(166, 164, 156),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (Focused)
        {
            var focus = Rectangle.Inflate(bounds, -5, -5);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, Color.White, Color.Transparent);
        }
    }
}
