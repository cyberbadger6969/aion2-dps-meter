using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AionMeter.App.Services;
using AionMeter.App.ViewModels;
using AionMeter.App.Windows;
using AionMeter.Core.Capture;

namespace AionMeter.App;

public static class AppHost
{
    public static App Current => (App)Application.Current;

    /// <summary>Clipboard writes fail while another process holds the clipboard; retry briefly.</summary>
    public static void CopyText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
    }
}

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MeterService _meter = null!;
    private OverlayWindow _overlay = null!;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private DispatcherTimer? _timer;
    private HistoryWindow? _history;
    private SettingsWindow? _settingsWindow;
    private BossTimersWindow? _timersWindow;
    private readonly Dictionary<object, BreakdownWindow> _breakdowns = new();
    private int _ticks;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ArgValue(e.Args, "--render-sample") is { } samplePath)
        {
            SampleRenderer.Render(samplePath, ArgValue(e.Args, "--lang") ?? "en", ArgValue(e.Args, "--window") ?? "overlay",
                int.TryParse(ArgValue(e.Args, "--width"), out var width) ? width : null);
            Shutdown();
            return;
        }
        _singleInstance = new Mutex(true, "AionMeter.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show($"{AppInfo.Name} is already running — look for the gold \"A\" in the notification area.", AppInfo.Name);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Fatal exception", ex.ExceptionObject as Exception);

        var settings = AppSettings.Load();
        settings.Save(); // persist any migration done while loading
        UiText.Use(settings.Language);
        Func<Core.Game.GameData, IEventSource> factory = data => new LiveCapture(data, new LiveCaptureOptions
        {
            DeviceName = settings.CaptureDevice,
            RecordDirectory = settings.RecordPackets ? Path.Combine(AppSettings.AppDataDir, "captures") : null,
        });

        _meter = new MeterService(settings, factory);
        Log.Info($"Started. Data: {_meter.Data.Skills.Count} skills, {_meter.Data.Npcs.Count} npcs.");
        _meter.StartCapture();

        _overlay = new OverlayWindow(_meter);
        if (settings.OverlayVisible || e.Args.Contains("--show")) _overlay.Show(); // --show: this run only, not saved
        else new WindowInteropHelper(_overlay).EnsureHandle();

        _hotkeys = new HotkeyManager(new WindowInteropHelper(_overlay).Handle);
        RegisterHotkeys();

        _tray = new TrayIcon(new TrayActions(
            ToggleOverlay: () => SetOverlayVisible(!_overlay.IsVisible),
            ToggleClickThrough: ToggleClickThrough,
            Reset: () => _meter.Tracker.Reset(),
            History: ShowHistory,
            BossTimers: ShowBossTimers,
            Settings: ShowSettings,
            Demo: () => _meter.StartDemo(),
            Replay: PickReplay,
            ToggleLanguage: ToggleLanguage,
            Update: () => ShowUpdate(),
            CheckUpdates: () => _ = CheckUpdatesFromTrayAsync(),
            Exit: Shutdown));

        // A new player's first start: without Npcap nothing can be measured — say so and offer the download.
        if (!LiveCapture.IsNpcapInstalled())
            Dispatcher.BeginInvoke(ShowNpcapHelp, DispatcherPriority.ApplicationIdle);

        // New versions on GitHub: tray item, the overlay's update button and one notification per version.
        _meter.Updates.Changed += OnUpdatesChanged;
        _meter.Updates.Start();
        Updater.CleanDownloads();
        if (e.Args.Contains("--updated")) // started by the installer after an in-app update
            Dispatcher.BeginInvoke(() => _tray?.ShowBalloon(AppInfo.Name,
                string.Format(UiText.Current.UpdatedBalloon, _meter.Updates.Current.ToString(3))), DispatcherPriority.ApplicationIdle);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        // Developer switches: --demo, --replay <file.pcap> (instant), --open-last (breakdown of the newest fight).
        if (e.Args.Contains("--demo")) _meter.StartDemo();
        if (ArgValue(e.Args, "--replay") is { } replay) _meter.Replay(replay);
        if (e.Args.Contains("--open-last"))
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            once.Tick += (_, _) =>
            {
                once.Stop();
                var seg = _meter.Tracker.Segments().OrderByDescending(s => s.TotalDamage).FirstOrDefault();
                if (seg is null || _meter.Tracker.Snapshot(seg.Id, 0) is not { } snap || snap.Combatants.Count == 0) return;
                var who = snap.Combatants.FirstOrDefault(c => c.IsSelf) ?? snap.Combatants[0];
                ShowBreakdown(seg.Id, who.ActorId);
                if (ArgValue(e.Args, "--tab") is { } tab && _breakdowns.TryGetValue(seg.Id, out var w)) w.ShowTab(tab);
            };
            once.Start();
        }
    }

    private static void ShowNpcapHelp()
    {
        var answer = MessageBox.Show(UiText.Current.NpcapNeeded, AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Could not open the Npcap page", ex);
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void OnTick()
    {
        _meter.Tick();
        UpdateAutoVisibility();
        if (_overlay.IsVisible) _overlay.Refresh();
        if (++_ticks % 5 == 0)
        {
            _tray?.Update(_overlay.IsVisible, _meter.Settings.ClickThrough, _meter.DemoRunning ? "demo" : _meter.CaptureStatus.Message);
            CheckBossAlerts();
        }
    }

    // ------------------------------------------------------------ automatic show / hide

    private bool _autoShown;           // the overlay is up because a fight brought it up, not the user
    private Guid? _suppressedFight;    // hidden by hand during this fight: do not pop it back until the next one
    private long _lastEngagedTick;

    /// <summary>
    /// A boss being fought nearby, or any fight you take part in, brings the overlay up. One that came up by itself
    /// goes away again after <see cref="AppSettings.AutoHideSeconds"/> out of combat (never while the mouse is on it).
    /// </summary>
    private void UpdateAutoVisibility()
    {
        var s = _meter.Settings;
        var fight = _meter.Tracker.LiveFight();
        var engaged = fight is { } f && (f.Boss || f.Self);
        var now = Environment.TickCount64;
        if (engaged) _lastEngagedTick = now;
        if (!s.AutoShow) return;

        if (engaged && !_overlay.IsVisible && fight!.Value.Id != _suppressedFight)
        {
            _overlay.Show();
            _autoShown = true;
        }
        else if (!engaged && _autoShown && _overlay.IsVisible && s.AutoHideSeconds > 0 &&
                 now - _lastEngagedTick > s.AutoHideSeconds * 1000L && !_overlay.IsMouseOver)
        {
            _overlay.Hide();
            _autoShown = false;
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        _hotkeys.UnregisterAll();
        var s = _meter.Settings;
        _hotkeys.Register(s.HotkeyToggleOverlay, () => SetOverlayVisible(!_overlay.IsVisible));
        _hotkeys.Register(s.HotkeyReset, () => _meter.Tracker.Reset());
        _hotkeys.Register(s.HotkeyClickThrough, ToggleClickThrough);
        _hotkeys.Register(s.HotkeyTimers, ToggleBossTimers);
    }

    // ------------------------------------------------------------ window management (used by windows via AppHost)

    public void SetOverlayVisible(bool visible)
    {
        _autoShown = false; // the user decided: automatic hiding no longer applies
        if (visible)
        {
            _overlay.Show();
            _overlay.Refresh();
        }
        else
        {
            _overlay.Hide();
            _suppressedFight = _meter.Tracker.LiveFight()?.Id;
        }
        _meter.Settings.OverlayVisible = visible;
        _meter.Settings.Save();
    }

    private void ToggleClickThrough()
    {
        var on = !_meter.Settings.ClickThrough;
        _overlay.SetClickThrough(on);
        _tray?.ShowBalloon(AppInfo.Name, on
            ? string.Format(UiText.Current.ClickThroughOn, _meter.Settings.HotkeyClickThrough)
            : UiText.Current.ClickThroughOff);
    }

    public void ShowBreakdown(Guid segmentId, uint actorId) =>
        ShowBreakdown(segmentId, new LiveFightView(_meter.Tracker, segmentId), actorId);

    public void ShowBreakdown(IFightView view, uint actorId) => ShowBreakdown(view, view, actorId);

    private void ShowBreakdown(object key, IFightView view, uint actorId)
    {
        if (_breakdowns.TryGetValue(key, out var existing))
        {
            existing.Select(actorId);
            Activate(existing);
            return;
        }
        var w = new BreakdownWindow(view, actorId, _meter.Data, _meter.Icons, _meter.Portraits);
        _breakdowns[key] = w;
        w.Closed += (_, _) => _breakdowns.Remove(key);
        w.Show();
    }

    /// <summary>English ⇄ Russian, live: names reload, the overlay re-binds, other windows reopen in the new language.</summary>
    public void SetLanguage(string language)
    {
        _meter.SetLanguage(language);
        foreach (var w in _breakdowns.Values.ToList()) w.Close();
        _history?.Close();
        _timersWindow?.Close();
        _updateWindow?.Close();
        _tray?.ApplyTexts();
        _overlay.OnLanguageChanged();
    }

    public void ToggleLanguage() => SetLanguage(UiText.Current.Code == "ru" ? "en" : "ru");

    public void ShowSavedInOverlay(Core.Storage.HistoryEntry entry)
    {
        if (!_overlay.IsVisible) SetOverlayVisible(true);
        _overlay.ShowSaved(entry);
    }

    public void ShowHistory()
    {
        if (_history is { IsLoaded: true })
        {
            _history.Reload();
            Activate(_history);
            return;
        }
        _history = new HistoryWindow(_meter);
        _history.Closed += (_, _) => _history = null;
        _history.Show();
    }

    /// <summary>Hotkey: open the boss timers, or close them when they are already up.</summary>
    private void ToggleBossTimers()
    {
        if (_timersWindow is { IsLoaded: true }) _timersWindow.Close();
        else ShowBossTimers();
    }

    public void ShowBossTimers()
    {
        if (_timersWindow is { IsLoaded: true })
        {
            Activate(_timersWindow);
            return;
        }
        _timersWindow = new BossTimersWindow(_meter);
        _timersWindow.Closed += (_, _) => _timersWindow = null;
        _timersWindow.Show();
    }

    /// <summary>Tray notification (and a chime) shortly before a tracked boss respawns.</summary>
    private void CheckBossAlerts()
    {
        var t = UiText.Current;
        foreach (var timer in _meter.Timers.TakeDueAlerts(DateTimeOffset.Now, _meter.Settings.BossAlertMinutes))
        {
            var next = timer.NextSpawn!.Value;
            var minutes = (int)Math.Ceiling((next - DateTimeOffset.Now).TotalMinutes);
            var name = _meter.Data.NpcName(timer.NpcCode);
            var text = minutes > 0
                ? string.Format(t.AlertSoon, name, minutes, next.ToString("HH:mm"))
                : string.Format(t.AlertNow, name, next.ToString("HH:mm"));
            _tray?.ShowBalloon(t.TimersTitle, text);
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            Activate(_settingsWindow);
            return;
        }
        _settingsWindow = new SettingsWindow(_meter);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // ------------------------------------------------------------ updates

    private UpdateWindow? _updateWindow;
    private Version? _announcedUpdate;
    private bool _checkingByHand;

    /// <summary>A check finished or a version was skipped: the tray item follows; a new version is announced once.</summary>
    private void OnUpdatesChanged()
    {
        var available = _meter.Updates.Available;
        _tray?.SetUpdate(available?.Version.ToString(3));
        if (available is null || available.Version == _announcedUpdate) return;
        _announcedUpdate = available.Version;
        if (_checkingByHand) return; // the one who asked gets the update window instead
        var t = UiText.Current;
        _tray?.ShowBalloon(t.UpdateTitle, string.Format(t.UpdateBalloon, available.Version.ToString(3)), () => ShowUpdate());
    }

    /// <summary>The update window for <paramref name="release"/>, by default the available update.</summary>
    public void ShowUpdate(Core.Updates.ReleaseInfo? release = null)
    {
        release ??= _meter.Updates.Available;
        if (release is null) return;
        if (_updateWindow is { IsLoaded: true } open && open.Release.Version == release.Version)
        {
            Activate(open);
            return;
        }
        _updateWindow?.Close();
        var w = new UpdateWindow(_meter.Updates, release, Updater.IsInstalled);
        w.Closed += (_, _) =>
        {
            if (_updateWindow == w) _updateWindow = null;
        };
        _updateWindow = w;
        w.Show();
    }

    /// <summary>"Check for updates" by hand (tray or settings): asks GitHub now; a newer version opens the update window.</summary>
    public async Task<UpdateCheckResult> CheckUpdatesNowAsync()
    {
        UpdateCheckResult result;
        _checkingByHand = true;
        try
        {
            result = await _meter.Updates.CheckAsync();
        }
        finally
        {
            _checkingByHand = false;
        }
        if (result == UpdateCheckResult.Available) ShowUpdate(_meter.Updates.Latest);
        return result;
    }

    private async Task CheckUpdatesFromTrayAsync()
    {
        var t = UiText.Current;
        switch (await CheckUpdatesNowAsync())
        {
            case UpdateCheckResult.UpToDate:
                _tray?.ShowBalloon(AppInfo.Name, t.UpToDate);
                break;
            case UpdateCheckResult.Failed:
                _tray?.ShowBalloon(AppInfo.Name, string.Format(t.UpdateCheckFailed, _meter.Updates.LastError));
                break;
        }
    }

    public void OnSettingsChanged()
    {
        RegisterHotkeys();
        _overlay.ApplyAppearance();
        _overlay.Refresh();
    }

    private void PickReplay()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Replay an AION 2 capture",
            Filter = "Packet captures (*.pcap;*.pcapng)|*.pcap;*.pcapng|All files|*.*",
            InitialDirectory = Path.Combine(AppSettings.AppDataDir, "captures"),
        };
        if (dialog.ShowDialog() == true) _meter.Replay(dialog.FileName);
    }

    private static void Activate(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _timer?.Stop();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_meter is not null)
        {
            _meter.Settings.Save();
            _meter.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
