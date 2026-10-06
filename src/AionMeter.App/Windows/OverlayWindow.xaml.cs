using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.App.ViewModels;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace AionMeter.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly MeterService _meter;
    private readonly AppSettings _settings;
    private readonly OverlayViewModel _vm = new();
    private Guid? _segment; // in-memory segment; null = live
    private SavedFight? _saved; // a fight loaded from history (takes precedence over _segment)
    private nint _hwnd;
    private readonly DragAnywhere _drag;

    private sealed record SavedFight(HistoryEntry Entry, FightRecord Record, RecordFightView View, string Title);

    public OverlayWindow(MeterService meter)
    {
        _meter = meter;
        _settings = meter.Settings;
        var opacity = _settings.BackgroundOpacity; // the slider's coercion during InitializeComponent would overwrite it
        InitializeComponent();
        _settings.BackgroundOpacity = opacity;
        DataContext = _vm;

        Left = _settings.OverlayLeft;
        Top = _settings.OverlayTop;
        Width = Math.Max(MinWidth, _settings.OverlayWidth);
        Height = Math.Max(MinHeight, _settings.OverlayHeight);
        EnsureOnScreen();

        OpacitySlider.Value = _settings.BackgroundOpacity;
        FrameBackground.Opacity = _settings.BackgroundOpacity;
        ApplyLock();
        UpdateModeLabel();
        _meter.Updates.Changed += ShowUpdateBanner;
        ShowUpdateBanner();

        // Drag the card by any part of it (unless locked); a click without movement still opens a breakdown.
        _drag = DragAnywhere.Attach(this, canDrag: () => !_settings.Locked, dropped: SavePlacement);

        // The card stays clean: its buttons only appear while the mouse is over it.
        MouseEnter += (_, _) =>
        {
            LabelPanel.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Visible;
        };
        MouseLeave += (_, _) =>
        {
            Toolbar.Visibility = Visibility.Collapsed;
            LabelPanel.Visibility = Visibility.Visible;
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Clicking the overlay must never pull keyboard focus away from the game window.
            NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            ApplyClickThrough();
            FitToMonitor();
        };
    }

    public nint Handle => _hwnd;

    /// <summary>Called by the app's UI timer (≈5 Hz).</summary>
    public void Refresh()
    {
        // Looking at an old fight when a new pull starts: jump back to live so the fight is never missed.
        if ((_saved is not null || _segment is not null) &&
            _meter.Tracker.Segments() is [{ IsActive: true } running, ..] && running.StartedAt > _selectedAt)
        {
            _saved = null;
            _segment = null;
        }

        if (_saved is { } saved)
        {
            _vm.Apply(saved.Record.Summary with { Title = saved.Title, Zone = _meter.DisplayZone(saved.Record.Summary.Zone) },
                _settings.MaxRows, _settings.BarsRelativeToTop);
            _vm.SetSegment("saved", saved.Entry.StartedAt.ToString("dd.MM HH:mm"));
            _vm.Portrait = _meter.PortraitOf(saved.Entry);
        }
        else
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snap = _meter.Tracker.Snapshot(_segment, now);
            if (snap is null && _segment is not null)
            {
                // The selected segment fell out of the in-memory list.
                _segment = null;
                snap = _meter.Tracker.Snapshot(null, now);
            }
            _vm.Apply(snap, _settings.MaxRows, _settings.BarsRelativeToTop);
            _vm.SetSegment(_segment is null ? "live" : "session", snap?.StartedAt.ToString("HH:mm"));
            _vm.Portrait = snap is null ? null : _meter.PortraitOf(snap);
        }
        if (!_settings.ShowBossPanel) _vm.HasBoss = false;
        if (Environment.TickCount64 - _timersLabelAt >= 1_000) UpdateTimersLabel();

        var status = _meter.DemoRunning ? new CaptureStatus(CaptureState.Capturing, "Demo fight running") : _meter.CaptureStatus;
        // Names are only sent on a loading screen: players already around when the meter started show as #id.
        var unnamed = _vm.Rows.Any(r => r.Name.Contains('#')); // "Templar #10388": the server has not sent this name yet
        _vm.Status = unnamed && status.State == CaptureState.Capturing
            ? "Names appear after the next loading screen"
            : status.Message;
        // Footer reads like the card's caption; capture problems take its place so they are never missed.
        FooterText.Text = status.State is CaptureState.Capturing && _vm.HasData
            ? (unnamed ? _vm.Footer + " · " + UiText.Current.NamesLater : _vm.Footer)
            : _vm.Status;
        _vm.StatusBrush = status.State switch
        {
            CaptureState.Capturing => (Brush)FindResource("Green"),
            CaptureState.Error => (Brush)FindResource("Red"),
            CaptureState.WaitingForGame or CaptureState.Starting => (Brush)FindResource("Amber"),
            _ => (Brush)FindResource("TextMute"),
        };
    }

    public void OnLanguageChanged()
    {
        _vm.LanguageChanged();
        ShowUpdateBanner();
        Refresh();
    }

    private Version? _bannerClosedFor; // the banner's ✕: hidden until a newer version comes out or the next start

    /// <summary>The green "Version 0.2.0 is out" banner above the footer, while an update is available (not skipped or closed).</summary>
    private void ShowUpdateBanner()
    {
        var available = _meter.Updates.Available;
        var show = available is not null && available.Version != _bannerClosedFor;
        UpdateBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) UpdateBannerText.Text = string.Format(UiText.Current.UpdateBanner, available!.Version.ToString(3));
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!_drag.JustDragged) AppHost.Current.ShowUpdate();
    }

    private void UpdateBannerClose_Click(object sender, RoutedEventArgs e)
    {
        _bannerClosedFor = _meter.Updates.Available?.Version;
        ShowUpdateBanner();
    }

    private void Language_Click(object sender, RoutedEventArgs e) => AppHost.Current.ToggleLanguage();

    /// <summary>Re-reads appearance settings changed in the Settings window.</summary>
    public void ApplyAppearance()
    {
        OpacitySlider.Value = _settings.BackgroundOpacity;
        FrameBackground.Opacity = _settings.BackgroundOpacity;
        UpdateModeLabel();
    }

    public void SetClickThrough(bool on)
    {
        _settings.ClickThrough = on;
        _settings.Save();
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        if (_hwnd == 0) return;
        if (_settings.ClickThrough) NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        else NativeMethods.SetExStyle(_hwnd, 0, NativeMethods.WS_EX_TRANSPARENT);
        Frame.BorderBrush = _settings.ClickThrough ? (Brush)FindResource("Line") : (Brush)FindResource("CardFrame");
    }

    private void ApplyLock()
    {
        LockButton.IsChecked = _settings.Locked;
        LockGlyph.Text = _settings.Locked ? "" : "";
        ResizeGrip.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateModeLabel() =>
        _vm.ModeLabel = _settings.TargetMode == TargetMode.BossOnly ? "BOSS" : "ALL";

    private void EnsureOnScreen()
    {
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        if (Left + 60 > vl + vw || Left + Width - 60 < vl) Left = vl + 80;
        if (Top + 40 > vt + vh || Top < vt) Top = vt + 160;
    }

    /// <summary>
    /// Keeps the whole card inside the work area of the monitor it sits on (a taller layout must not hang off the
    /// bottom edge). Runs once the window has a handle, when the monitor and its DPI are known.
    /// </summary>
    private void FitToMonitor()
    {
        if (_hwnd == 0) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea; // physical pixels
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = area.Left / dpi.DpiScaleX;
        var top = area.Top / dpi.DpiScaleY;
        var right = area.Right / dpi.DpiScaleX;
        var bottom = area.Bottom / dpi.DpiScaleY;
        if (Height > bottom - top) Height = Math.Max(MinHeight, bottom - top);
        Left = Math.Clamp(Left, left, Math.Max(left, right - Width));
        Top = Math.Clamp(Top, top, Math.Max(top, bottom - Height));
        Log.Info($"Overlay placed at {Left:0},{Top:0} {Width:0}x{Height:0} DIP; monitor work area {left:0},{top:0}-{right:0},{bottom:0} DIP (scale {dpi.DpiScaleX})");
    }

    private void SavePlacement()
    {
        _settings.OverlayLeft = Left;
        _settings.OverlayTop = Top;
        _settings.OverlayWidth = Width;
        _settings.OverlayHeight = Height;
        _settings.Save();
    }

    // ------------------------------------------------------------ handlers

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e) => SavePlacement();

    private void OpenBreakdown(uint actorId)
    {
        if (_saved is { } saved) AppHost.Current.ShowBreakdown(saved.View, actorId);
        else if (_vm.SegmentId is { } seg) AppHost.Current.ShowBreakdown(seg, actorId);
    }

    private EncounterSnapshot? ShownSummary() =>
        _saved is { } saved
            ? saved.Record.Summary with { Title = saved.Title }
            : _vm.SegmentId is { } seg ? _meter.Tracker.Snapshot(seg, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) : null;

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (_drag.JustDragged) return; // the press moved the window, it was not a click
        if ((sender as FrameworkElement)?.DataContext is RowViewModel row) OpenBreakdown(row.ActorId);
    }

    private void Row_RightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RowViewModel row) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        var open = new MenuItem { Header = UiText.Current.OpenBreakdown };
        open.Click += (_, _) => OpenBreakdown(row.ActorId);
        var copyMe = new MenuItem { Header = string.Format(UiText.Current.CopyPlayer, row.Name) };
        copyMe.Click += (_, _) => CopyChat(row.ActorId);
        var copyAll = new MenuItem { Header = UiText.Current.CopyParty };
        copyAll.Click += (_, _) => CopyChat(null);
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyMe);
        menu.Items.Add(copyAll);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CopyChat(uint? actorId)
    {
        if (ShownSummary() is not { } snap) return;
        AppHost.CopyText(actorId is { } id ? ChatLine.Player(snap, id) : ChatLine.Party(snap));
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Rows.Count == 0) return;
        var target = _vm.Rows.FirstOrDefault(r => r.IsSelf) ?? _vm.Rows[0];
        OpenBreakdown(target.ActorId);
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _settings.TargetMode = _settings.TargetMode == TargetMode.BossOnly ? TargetMode.All : TargetMode.BossOnly;
        _meter.ApplySettings();
        UpdateModeLabel();
    }

    // ------------------------------------------------------------ fight picker

    /// <summary>One selectable fight: a segment of this session or a fight saved on disk.</summary>
    private sealed record PickItem(
        string Title, DateTimeOffset Start, long CombatMs, EncounterEndReason Reason, bool Active,
        double SelfDps, int Place, double PartyDps, Guid? Segment, HistoryEntry? Saved, ImageSource? Portrait = null);

    private const int SavedInMenu = 40;

    /// <summary>Newest first: this session's segments, then saved fights not already in the session list.</summary>
    private List<PickItem> BuildFightList(int maxSaved)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var list = new List<PickItem>();
        var sessionStarts = new HashSet<long>();
        foreach (var s in _meter.Tracker.Segments())
        {
            var snap = _meter.Tracker.Snapshot(s.Id, now);
            var selfIndex = snap?.Combatants.ToList().FindIndex(c => c.IsSelf) ?? -1;
            var self = selfIndex >= 0 ? snap!.Combatants[selfIndex] : null;
            list.Add(new PickItem(s.Title, s.StartedAt, s.CombatMs, s.Reason, s.IsActive,
                self?.Dps ?? 0, selfIndex + 1, s.TotalDamage / Math.Max(1.0, s.CombatMs / 1000.0), s.Id, null,
                snap is null ? null : _meter.PortraitOf(snap)));
            sessionStarts.Add(s.StartedAt.ToUnixTimeSeconds());
        }
        foreach (var h in _meter.History.List(maxSaved + sessionStarts.Count))
        {
            if (sessionStarts.Contains(h.StartedAt.ToUnixTimeSeconds())) continue;
            list.Add(new PickItem(_meter.DisplayTitle(h), h.StartedAt, h.CombatMs, h.Reason, false,
                h.SelfDps, h.SelfPlace, h.PartyDps, null, h, _meter.PortraitOf(h)));
            if (list.Count(i => i.Saved is not null) >= maxSaved) break;
        }
        return list;
    }

    private bool IsSelected(PickItem item) =>
        item.Saved is { } h ? _saved?.Entry.FileName == h.FileName : _saved is null && _segment == item.Segment;

    private void SelectLive()
    {
        _saved = null;
        _segment = null;
        Refresh();
    }

    private DateTimeOffset _selectedAt = DateTimeOffset.MinValue;

    private void Select(PickItem item)
    {
        _selectedAt = DateTimeOffset.Now;
        if (item.Saved is { } h)
        {
            if (_meter.History.Load(h.Path) is not { } record)
            {
                AppHost.Current.ShowHistory(); // file vanished or is unreadable: let the history window refresh
                return;
            }
            _saved = new SavedFight(h, record, new RecordFightView(record), _meter.DisplayTitle(h));
            _segment = null;
        }
        else
        {
            _saved = null;
            _segment = item.Segment;
        }
        Refresh();
    }

    private void Segments_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom, MinWidth = 380 };
        var live = new MenuItem
        {
            Header = PickHeader(UiText.Current.Live, UiText.Current.LiveHint, null, 0, EncounterEndReason.None, active: true, partyDps: 0, portrait: null),
            IsCheckable = true,
            IsChecked = _saved is null && _segment is null,
        };
        live.Click += (_, _) => SelectLive();
        menu.Items.Add(live);

        var items = BuildFightList(SavedInMenu);
        string? section = null;
        foreach (var item in items)
        {
            var group = item.Saved is null ? UiText.Current.SessionSection : DayLabel(item.Start);
            if (group != section)
            {
                section = group;
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = group, Style = (Style)FindResource("SectionLabel") },
                    IsEnabled = false,
                });
            }
            var meta = $"{item.Start:HH:mm} · {Format.Clock(item.CombatMs)}";
            var row = new MenuItem
            {
                Header = PickHeader(item.Title, meta, item.SelfDps > 0 ? item.SelfDps : null, item.Place, item.Reason, item.Active, item.PartyDps, item.Portrait),
                IsCheckable = true,
                IsChecked = IsSelected(item),
            };
            var pick = item;
            row.Click += (_, _) => Select(pick);
            menu.Items.Add(row);
        }
        if (items.Count == 0)
            menu.Items.Add(new MenuItem { Header = UiText.Current.NoFights, IsEnabled = false });

        menu.Items.Add(new Separator());
        var total = _meter.History.List().Count;
        var history = new MenuItem { Header = string.Format(UiText.Current.AllSaved, total) };
        history.Click += (_, _) => AppHost.Current.ShowHistory();
        menu.Items.Add(history);
        menu.IsOpen = true;
    }

    private static string DayLabel(DateTimeOffset t)
    {
        var day = t.LocalDateTime.Date;
        if (day == DateTime.Today) return UiText.Current.SavedToday;
        if (day == DateTime.Today.AddDays(-1)) return UiText.Current.SavedYesterday;
        return string.Format(UiText.Current.SavedOn, day.ToString("dd MMM yyyy").ToUpperInvariant());
    }

    /// <summary>Two-line menu row: boss portrait, result mark, title, time · length, and your DPS with your place badge.</summary>
    private FrameworkElement PickHeader(string title, string meta, double? selfDps, int place, EncounterEndReason reason, bool active, double partyDps,
        ImageSource? portrait)
    {
        var grid = new Grid { MinWidth = 360 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new BossPortrait { Source = portrait, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left });

        var (mark, markBrush) = active ? ("●", "Red") : reason switch
        {
            EncounterEndReason.Kill => ("✔", "Green"),
            EncounterEndReason.Wipe => ("✖", "Red"),
            _ => ("•", "TextMute"),
        };
        var markText = new TextBlock { Text = mark, Foreground = (Brush)FindResource(markBrush), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(markText, 1);
        grid.Children.Add(markText);

        var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        text.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5, MaxWidth = 230,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Text"),
        });
        text.Children.Add(new TextBlock
        {
            Text = meta, FontSize = 10.5, FontFamily = (FontFamily)FindResource("NumberFont"), Foreground = (Brush)FindResource("TextMute"),
        });
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (selfDps is { } dps)
        {
            right.Children.Add(new TextBlock
            {
                Text = Format.Compact(dps) + "/s", FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = 12.5,
                FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("GoldBright"), VerticalAlignment = VerticalAlignment.Center,
            });
            if (place > 0)
            {
                var (bg, fg, border) = RankBrushes.For(place);
                right.Children.Add(new Border
                {
                    Width = 20, Height = 20, Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(5),
                    Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = place.ToString(), FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = 11,
                        FontWeight = FontWeights.Bold, Foreground = fg,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
        }
        else if (partyDps > 0)
        {
            right.Children.Add(new TextBlock
            {
                Text = UiText.Current.Party + " " + Format.Compact(partyDps) + "/s", FontFamily = (FontFamily)FindResource("NumberFont"),
                FontSize = 11.5, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Mouse wheel over the title steps through fights: down = older, up = newer, past the newest = live.</summary>
    private void Title_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var items = BuildFightList(SavedInMenu);
        var index = items.FindIndex(IsSelected); // -1 = live
        if (_saved is null && _segment is null) index = -1;
        index += e.Delta < 0 ? 1 : -1;
        if (index < 0) SelectLive();
        else if (index < items.Count) Select(items[index]);
        e.Handled = true;
    }

    /// <summary>Called by the history window to show a saved fight in the overlay.</summary>
    public void ShowSaved(HistoryEntry entry)
    {
        Select(new PickItem(entry.Title, entry.StartedAt, entry.CombatMs, entry.Reason, false,
            entry.SelfDps, entry.SelfPlace, entry.PartyDps, null, entry));
    }

    private void Timers_Click(object sender, RoutedEventArgs e)
    {
        if (!_drag.JustDragged) AppHost.Current.ShowBossTimers();
    }

    private long _timersLabelAt;

    /// <summary>The next respawn among watched bosses (or all, when none is watched): "Gartua 12:34".</summary>
    private void UpdateTimersLabel()
    {
        _timersLabelAt = Environment.TickCount64;
        var now = DateTimeOffset.Now;
        var timers = _meter.Timers.ForServer(_meter.Timers.CurrentServer).Where(t => t.NpcCode > 0).ToList();
        var pool = timers.Any(t => t.Watch) ? timers.Where(t => t.Watch) : timers;
        var next = pool.Where(t => !t.AliveNow && t.NextSpawn > now).MinBy(t => t.NextSpawn);
        if (next?.NextSpawn is not { } at)
        {
            _vm.TimersLabel = UiText.Current.TimersButton;
            return;
        }
        var name = next.NpcCode > 0 ? _meter.Data.NpcName(next.NpcCode) : string.Format(UiText.Current.UnknownSlotBoss, -next.NpcCode % 100);
        var shortName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;
        var left = at - now;
        _vm.TimersLabel = $"{shortName} {(left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss"))}";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _segment = null;
        _saved = null;
        _meter.Tracker.Reset();
        Refresh();
    }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _settings.Locked = LockButton.IsChecked == true;
        _settings.Save();
        ApplyLock();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => AppHost.Current.ShowSettings();

    private void Hide_Click(object sender, RoutedEventArgs e) => AppHost.Current.SetOverlayVisible(false);

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrameBackground is null) return;
        FrameBackground.Opacity = e.NewValue;
        _settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePlacement();
        base.OnClosed(e);
    }
}
