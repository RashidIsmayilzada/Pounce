using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Pounce.Core;
using Pounce.Launcher.Platform;
using Pounce.Launcher.Themes;
using Pounce.Launcher.Views;

namespace Pounce.Launcher;

/// <summary>
/// The search panel, like Spotlight: a search field, then matching applications, then files with their passage.
/// </summary>
/// <remarks>
/// <para>Speed: the window, its HWND and its visual tree are built once at startup and shown once off-screen with two
/// sample rows (so fonts, row templates, the icon pipeline and the render target exist before the first real show and
/// the first keystroke, see <see cref="WarmUp"/>); afterwards it is only shown and hidden.
/// Showing reads two registry values and moves the window; nothing else. Typing writes straight to
/// <see cref="SearchModel.Query"/>, and result rows are pooled and updated in place (see <see cref="SyncRows"/>).</para>
/// <para>Steadiness: each keystroke brings Early Results (often only a few) and then Final Results (often 8). While a
/// search runs the window may grow but never shrinks (<see cref="PanelPlacement.WindowHeightDip"/>), so it does not
/// collapse and regrow on every letter; rows the Early Results do not fill are hidden, leaving an empty band below
/// them until the Final Results arrive. Once the search is final the window fits its content.</para>
/// <para>Look: a framed window whose frame the content covers (WindowChrome), so DWM gives it a shadow, rounded corners
/// and, on Windows 11 22H2 and later, the acrylic backdrop under a translucent tint. Anywhere the backdrop cannot be
/// applied or might come out black (Windows 10, Windows 11 21H2, a DWM failure, software rendering, Remote Desktop),
/// the content paints an opaque themed background instead,
/// so the panel is never black or see-through.</para>
/// <para>Focus: the panel hides when it loses the focus to another program, after opening an item, and on Esc.</para>
/// <para>Hiding clears the search first and hides one frame later. A hidden window keeps its last frame, and Windows
/// can show that frame for a moment when the window is shown again, before WPF has drawn a new one; rendering the
/// empty search bar before hiding means that moment shows the right thing instead of the previous search.
/// Opening an item is the exception: it hides at once (see <see cref="OpenSelected"/>), so the next show may show the
/// old search for one frame. If that is visible on real machines, cloaking the window (DWMWA_CLOAK) from Show until
/// its first new frame is the fix.</para>
/// </remarks>
public sealed partial class LauncherWindow : Window, ILauncherPanel
{
    /// <summary>A search must run this long before the progress indicator appears, so quick searches never flicker it.</summary>
    private static readonly TimeSpan SpinnerDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>The installed applications are listed again on a show at most this often.</summary>
    private static readonly TimeSpan AppsReloadInterval = TimeSpan.FromSeconds(30);

    /// <summary>Where the window waits during the warm-up show: outside any real monitor layout.</summary>
    private const int OffScreen = -20000;

    private readonly SearchModel model;
    private readonly IShellActions shell;
    private readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly ObservableCollection<RowModel> rows = [];
    private readonly RepeatGuard toggleGuard = new(TimeSpan.FromMilliseconds(350));
    private readonly RepeatGuard appsReloadGuard = new(AppsReloadInterval);
    private readonly DispatcherTimer spinnerDelay;
    private readonly LightTheme lightTheme = new();
    private readonly DarkTheme darkTheme = new();

    private IntPtr hwnd;
    private bool shown;
    private bool warmingUp;
    private bool warmUpRows;
    private long shownAt;
    private SystemTheme? theme;
    private WindowNative.MonitorArea? monitor;
    private RowModel? pressedRow;
    private bool reloadingApps;
    private bool focusCheckPending;
    private bool themeCheckPending;
    private bool spinning;
    private bool fitPending;
    private int visibleRows;
    private bool closed;
    private bool hidePending;
    private Action? afterHidden;
    private bool backdrop;
    private DispatcherTimer? unfocusedWatch;
    private IntPtr foregroundAtShow;

    /// <summary>Creates the panel, hidden, with its window handle and visual tree ready.</summary>
    public LauncherWindow(SearchModel model, IShellActions shell, IIconProvider icons)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(icons);
        this.model = model;
        this.shell = shell;
        IconLoader.Provider = icons;

        InitializeComponent();
        DataContext = model;
        ResultsList.ItemsSource = rows;
        spinnerDelay = new DispatcherTimer(DispatcherPriority.Normal) { Interval = SpinnerDelay };
        spinnerDelay.Tick += SpinnerDelay_Tick;
        model.PropertyChanged += Model_PropertyChanged;
        ApplyTheme(SystemTheme.Read());

        // Creates the HWND now (OnSourceInitialized applies the styles and the backdrop), without showing it.
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Once startup is done, show it once off-screen to build everything the first real show would otherwise.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WarmUp));
    }

    /// <inheritdoc/>
    public bool IsShown => shown;

    /// <inheritdoc/>
    public void ShowPanel()
    {
        if (closed)
        {
            return;
        }
        if (shown)
        {
            // Already up (perhaps without the focus): just take the focus.
            BringToFront();
            return;
        }
        if (hidePending)
        {
            // Shown again within a frame of hiding: finish that first (it may have an item to open).
            FinishHide();
        }
        if (warmUpRows)
        {
            // Asked for during the warm-up, while its sample rows are on the off-screen window: clear them and let
            // that render first (Background runs after the render pass), so the sample rows are never on screen.
            ClearWarmUpRows();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowPanel));
            return;
        }
        warmingUp = false;
        // Cheap (two GetWindowLong calls) and only writes when WPF has put a style back.
        _ = WindowNative.ApplyPanelStyles(hwnd);
        RefreshTheme();
        Place();
        foregroundAtShow = WindowNative.ForegroundWindow();
        shown = true;
        shownAt = Environment.TickCount64;
        ShowActivated = true;
        Show();
        BringToFront();
        // The move may have crossed to a monitor with another DPI, which WPF applies when it is shown: re-centre.
        CorrectPosition();
        ReloadApps();
    }

    /// <inheritdoc/>
    public void HidePanel() => HideThen(null);

    /// <summary>
    /// Clears the search (the next open starts with an empty field and a panel no taller than the search bar), lets
    /// WPF render that, then hides the window and runs <paramref name="then"/>. From the start the panel counts as
    /// hidden: <see cref="IsShown"/> is false and the focus loss that hiding causes is ignored.
    /// </summary>
    private void HideThen(Action? then)
    {
        if (closed)
        {
            return;
        }
        shown = false;
        warmingUp = false;
        ClearWarmUpRows();
        StopSpinner();
        model.Query = "";
        ResultsScroller.ScrollToVerticalOffset(0);
        afterHidden += then;
        if (!IsVisible)
        {
            FinishHide();
            return;
        }
        if (!hidePending)
        {
            hidePending = true;
            // Background runs after the layout and render passes (Loaded and Render priorities) the clear queued.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FinishHide));
        }
    }

    private void FinishHide()
    {
        hidePending = false;
        Action? then = afterHidden;
        afterHidden = null;
        if (!closed && !shown && IsVisible)
        {
            Hide();
        }
        if (!shown)
        {
            // Anything typed in the frame before hiding.
            model.Query = "";
        }
        then?.Invoke();
    }

    /// <inheritdoc/>
    public void Toggle()
    {
        // A held shortcut repeats; only the first press counts (as in the macOS toggle).
        if (!toggleGuard.Accept(TimeSpan.FromMilliseconds(Environment.TickCount64)))
        {
            return;
        }
        // The foreground window, not IsActive: after a refused activation, focusing the search field can make WPF
        // report the window active while another program is in front. Then the shortcut must bring it forward.
        if (shown && WindowNative.IsForeground(hwnd))
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    /// <summary>
    /// Re-reads the installed applications in the background: at most one listing at a time, and at most one every
    /// <see cref="AppsReloadInterval"/> (the first call always lists). The listing runs on the shell thread ahead of
    /// icon loads, so listing on every show would delay the icons of the first results. A failure is logged and the
    /// previous list stays.
    /// </summary>
    public void ReloadApps()
    {
        if (reloadingApps || !appsReloadGuard.Accept(TimeSpan.FromMilliseconds(Environment.TickCount64)))
        {
            return;
        }
        reloadingApps = true;
        AwaitReload(model.ReloadAppsAsync());
    }

    private async void AwaitReload(Task reload)
    {
        try
        {
            await reload;
        }
        catch (Exception e)
        {
            Log.Warn("Listing the installed applications failed; the previous list stays", e);
        }
        finally
        {
            reloadingApps = false;
        }
    }

    // ---- Window setup ----

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        if (!WindowNative.ApplyPanelStyles(hwnd))
        {
            Log.Warn("Could not make the panel a tool window; it may show in Alt+Tab");
        }
        // Without this, Windows animates a framed window in and out: the panel must appear at once.
        CheckHResult(WindowNative.DisableTransitions(hwnd), "DWMWA_TRANSITIONS_FORCEDISABLED");
        // Square corners on Windows 10, where this fails: nothing to fall back to.
        _ = WindowNative.RoundCorners(hwnd);
        // Before the backdrop, so the material starts in the right theme.
        _ = WindowNative.SetDarkFrame(hwnd, theme is { Light: false });

        // The acrylic path needs WPF to present with alpha, which hardware rendering does. Under software rendering
        // (render tier 0: no usable GPU, or rendering forced to software) and over Remote Desktop, the transparent
        // background may come out black instead, so those take the opaque path without asking DWM.
        string detail;
        if (RenderCapability.Tier >> 16 == 0)
        {
            backdrop = false;
            detail = "software rendering (render tier 0)";
        }
        else if (SystemParameters.IsRemoteSession)
        {
            backdrop = false;
            detail = "Remote Desktop session";
        }
        else
        {
            backdrop = WindowNative.TryApplyAcrylic(hwnd, out detail);
        }
        if (backdrop && source?.CompositionTarget is { } target)
        {
            // WPF must leave the pixels it does not paint transparent, or the backdrop would be covered in black.
            target.BackgroundColor = Colors.Transparent;
        }
        else if (backdrop)
        {
            backdrop = false;
            detail = "no composition target";
        }
        // Fallback: an opaque background in the theme's colour over the whole window, and the same colour for
        // pixels WPF has not painted yet (the strip a growing window gains before the next frame), which would
        // otherwise show the extended frame: black on Windows 10.
        Host.SetResourceReference(Panel.BackgroundProperty, backdrop ? "PanelTintBrush" : "PanelOpaqueBrush");
        SyncCompositionBackground();
        Log.Info(backdrop ? "Panel: acrylic backdrop" : $"Panel: opaque background ({detail})");
    }

    /// <summary>
    /// Shows the window once off-screen, with one sample app row and one sample file row (a section title each, a
    /// snippet with a highlight, an icon request), so the first keystroke does not pay for the row template, the
    /// snippet view and the icon pipeline. The rows are put in the list directly: the model is not touched, so there
    /// is no Query and no search. Three steps, each after the previous one rendered: show with the sample rows; clear
    /// them; hide. The window is hidden only once it has rendered without them, so the frame a later show may
    /// briefly reuse (see the remarks above) is the empty search bar.
    /// </summary>
    private void WarmUp()
    {
        if (closed || shown || IsVisible)
        {
            return;
        }
        warmingUp = true;
        if (visibleRows == 0)
        {
            warmUpRows = true;
            SyncRows(WarmUpItems());
            ResultsList.SelectedIndex = 0;
            SyncResultsVisibility();
        }
        _ = WindowNative.MoveTopmost(hwnd, OffScreen, OffScreen);
        ShowActivated = false;
        Show();
        // ApplicationIdle runs after the render pass that the Show queued.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FinishWarmUp));
    }

    private void FinishWarmUp()
    {
        if (!warmingUp || closed || shown)
        {
            return; // ShowPanel took over in the meantime.
        }
        if (warmUpRows)
        {
            ClearWarmUpRows();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FinishWarmUp));
            return;
        }
        warmingUp = false;
        Hide();
        ShowActivated = true;
    }

    /// <summary>Puts the list back to what the model says (nothing, at startup) once the warm-up rows have done their job.</summary>
    private void ClearWarmUpRows()
    {
        if (!warmUpRows)
        {
            return;
        }
        warmUpRows = false;
        SyncRows(model.Items);
        SyncSelection();
        SyncResultsVisibility();
    }

    private static SearchItem[] WarmUpItems()
    {
        string program = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Pounce.exe");
        // Not a real file: its icon is the generic one of its type, the same any .txt Result gets.
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Pounce warm-up.txt");
        return
        [
            new AppItem(new AppEntry("Pounce", program)),
            new FileItem(new ResultItem(file, "Pounce warm-up.txt", new Snippet("A warm-up passage", [new Highlight(2, 9)]))),
        ];
    }

    private IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        // The app theme ("ImmersiveColorSet") and the accent colour change through these. lParam is not read: a
        // re-read of two registry values is cheaper than trusting a pointer from any program's broadcast.
        if ((message == WM_SETTINGCHANGE || message == WM_DWMCOLORIZATIONCOLORCHANGED) && !themeCheckPending)
        {
            themeCheckPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                themeCheckPending = false;
                RefreshTheme();
            }));
        }
        return IntPtr.Zero;
    }

    private static void CheckHResult(int hr, string what)
    {
        if (hr < 0)
        {
            Log.Warn($"{what} failed: 0x{hr:X8}");
        }
    }

    // ---- Placement ----

    /// <summary>
    /// Puts the hidden window on the monitor with the mouse pointer: centred, top edge a quarter down the work area.
    /// Works in that monitor's physical pixels and moves the window with SetWindowPos before it is shown, so the
    /// first frame is already in place.
    /// </summary>
    private void Place()
    {
        monitor = WindowNative.MonitorWithPointer();
        if (monitor is not { } target)
        {
            // No monitor information: the primary work area in WPF's own units.
            Log.Warn("No monitor information; placing the panel on the primary monitor");
            Rect work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + work.Height / 4;
            ResultsScroller.MaxHeight = Math.Max(PanelPlacement.MinResultsHeightDip,
                work.Bottom - Top - PanelPlacement.BarHeightDip - 1 - PanelPlacement.BottomMarginDip);
            return;
        }
        int width = PanelPlacement.ToPixels(PanelPlacement.WidthDip, target.Dpi);
        (int x, int y) = PanelPlacement.Origin(target.WorkArea, width);
        ResultsScroller.MaxHeight = PanelPlacement.MaxResultsHeightDip(target.WorkArea, y, target.Dpi);
        if (!WindowNative.MoveTopmost(hwnd, x, y))
        {
            Log.Warn("SetWindowPos failed; the panel stays where it was");
        }
        CorrectPosition();
    }

    /// <summary>
    /// Re-centres the window using its actual width. Moving to a monitor with another DPI makes Windows send
    /// WM_DPICHANGED, and WPF then resizes the window (680 DIPs at the new DPI) and may move it to the rectangle Windows
    /// suggests; this puts it back where it belongs. Does nothing when it already is.
    /// </summary>
    private void CorrectPosition()
    {
        if (monitor is not { } target || WindowNative.WindowBounds(hwnd) is not { } bounds)
        {
            return;
        }
        (int x, int y) = PanelPlacement.Origin(target.WorkArea, bounds.Width);
        if (Math.Abs(bounds.Left - x) > 1 || Math.Abs(bounds.Top - y) > 1)
        {
            _ = WindowNative.MoveTopmost(hwnd, x, y);
        }
    }

    /// <inheritdoc/>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (shown)
        {
            // After WPF has applied the new size.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CorrectPosition));
        }
    }

    /// <summary>
    /// The window's height follows the content's natural height, except that it does not shrink while a search runs
    /// (<see cref="PanelPlacement.WindowHeightDip"/>); the top edge stays where it is.
    /// </summary>
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
        {
            FitHeight(e.NewSize.Height);
        }
    }

    private void FitHeight(double content)
    {
        double height = PanelPlacement.WindowHeightDip(Height, content, model.IsSearching);
        if (height != Height)
        {
            Height = height;
        }
    }

    /// <summary>
    /// Once no search runs any more, fits the window to its content after the layout pass that the last change
    /// queued. Needed besides <see cref="Root_SizeChanged"/>: the content may not change size at all when the search
    /// ends (the Final Results as tall as the Early ones), while the window is still taller from an earlier search.
    /// </summary>
    private void FitHeightWhenSettled()
    {
        if (fitPending)
        {
            return;
        }
        fitPending = true;
        // Loaded runs after the layout pass (Render priority) of the changes already made.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            fitPending = false;
            if (!model.IsSearching)
            {
                FitHeight(Root.ActualHeight);
            }
        }));
    }

    // ---- Activation and focus ----

    /// <summary>
    /// Brings the shown panel to the foreground with the caret in the search field.
    /// </summary>
    /// <remarks>
    /// Windows lets a process take the foreground only in some situations. From the hotkey it may: the process that
    /// receives a registered hotkey has received the last input event. From the tray menu it usually may (the click
    /// was input to this process). From a second launch it depends on whether that launch passed its right on
    /// (AllowSetForegroundWindow). So: WPF's Activate (SetForegroundWindow) first; if Windows refused, the documented
    /// AttachThreadInput route (see <see cref="WindowNative.ForceForeground"/>). If even that fails, the panel stays
    /// on screen, topmost, and takes the focus when clicked; the failure is logged.
    /// </remarks>
    private void BringToFront()
    {
        _ = Activate();
        if (!WindowNative.IsForeground(hwnd) && !WindowNative.ForceForeground(hwnd))
        {
            Log.Warn("Windows did not let the panel take the foreground; it gets the focus when clicked");
            WatchWhileUnfocused();
        }
        FocusSearchField();
    }

    /// <summary>
    /// A panel that never got the focus never loses it either, so nothing would hide it when the user clicks
    /// elsewhere. While it is shown without the focus, check four times a second who is in front: once that is a
    /// window of another program other than the one in front when the panel was shown, the user has moved on, so hide.
    /// Clicking the panel brings it to the foreground and ends the watch (from then on, losing the focus hides it as
    /// usual). "In the foreground" is asked of Windows, not read from IsActive, which WPF can report as true for a
    /// window that never got the foreground (FocusSearchField after a refused activation); the watch would stop at
    /// once and the panel would never hide.
    /// </summary>
    private void WatchWhileUnfocused()
    {
        if (unfocusedWatch is null)
        {
            unfocusedWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            unfocusedWatch.Tick += (_, _) =>
            {
                if (!shown || WindowNative.IsForeground(hwnd))
                {
                    unfocusedWatch.Stop();
                    return;
                }
                IntPtr foreground = WindowNative.ForegroundWindow();
                if (foreground != foregroundAtShow && foreground != hwnd
                    && WindowNative.CurrentForegroundOwner() == ForegroundOwner.OtherProcess)
                {
                    unfocusedWatch.Stop();
                    HidePanel();
                }
            };
        }
        unfocusedWatch.Start();
    }

    private void FocusSearchField()
    {
        _ = SearchBox.Focus();
        _ = Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    /// <inheritdoc/>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (shown)
        {
            FocusSearchField();
        }
    }

    /// <summary>
    /// Losing the focus hides the panel, decided a moment later (once Windows has finished switching), so that a
    /// focus loss that comes right after showing (to the tray icon's own window as its menu closes) does not hide it.
    /// </summary>
    /// <inheritdoc/>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!shown || focusCheckPending)
        {
            return;
        }
        focusCheckPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CheckFocusLoss));
    }

    private void CheckFocusLoss()
    {
        focusCheckPending = false;
        TimeSpan sinceShown = TimeSpan.FromMilliseconds(Environment.TickCount64 - shownAt);
        switch (PanelFocusPolicy.Decide(shown, WindowNative.IsForeground(hwnd), sinceShown, WindowNative.CurrentForegroundOwner()))
        {
            case FocusLossAction.Hide:
                HidePanel();
                break;
            case FocusLossAction.Reactivate:
                BringToFront();
                break;
            case FocusLossAction.Ignore:
            default:
                break;
        }
    }

    /// <summary>The panel is never closed, only hidden: a WM_CLOSE from elsewhere hides it. Application.Shutdown
    /// closes it regardless (WPF ignores Cancel then). Hiding is deferred: WPF refuses Hide while a close is under way.</summary>
    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(HidePanel));
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        shown = false;
        spinnerDelay.Stop();
        model.PropertyChanged -= Model_PropertyChanged;
        base.OnClosed(e);
    }

    // ---- Keyboard and mouse ----

    /// <summary>Handles the panel's keys before the search field sees them; every other key edits the text.</summary>
    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool plainOrShift = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0;
        switch (e.Key)
        {
            case Key.Down when plainOrShift:
                model.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up when plainOrShift:
                model.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                OpenSelected(reveal: (modifiers & ModifierKeys.Control) != 0);
                e.Handled = true;
                break;
            case Key.Escape:
                HidePanel();
                e.Handled = true;
                break;
            case Key.Tab:
                // The search field is the only stop: Tab must not move the focus into the list.
                e.Handled = true;
                break;
            case Key.System when e.SystemKey == Key.F4:
                // Alt+F4 hides, like Esc (OnClosing would too; this keeps it out of the close path).
                HidePanel();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Opens (or reveals) the selected row. Everything happens now, inside the handling of the key press or click:
    /// that input is what gives Pounce the right to pass the foreground on. So: allow any process to take the
    /// foreground (while Pounce still has it), hide the panel at once (not a frame later as on Esc: by then the
    /// key-up may have gone to the app Windows activated in its place, and the right would be gone), then ask the
    /// shell to open the item, which starts it asynchronously and lets it come to the front.
    /// </summary>
    private void OpenSelected(bool reveal)
    {
        if (model.Selected is not { } item)
        {
            return;
        }
        if (!WindowNative.AllowAnyProcessToTakeForeground())
        {
            Log.Info("AllowSetForegroundWindow was refused; the opened item may not come to the front");
        }
        HideThen(null);
        FinishHide();
        Launch(item, reveal);
    }

    private void Launch(SearchItem item, bool reveal)
    {
        try
        {
            if (reveal)
            {
                shell.Reveal(item);
            }
            else
            {
                shell.Open(item);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"{(reveal ? "Reveal" : "Open")} failed for {item.Path}", e);
        }
    }

    private void RowMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Handled here so the list neither selects on press nor takes the keyboard focus from the search field.
        pressedRow = (sender as FrameworkElement)?.DataContext as RowModel;
        e.Handled = true;
    }

    private void RowMouseUp(object sender, MouseButtonEventArgs e)
    {
        RowModel? row = (sender as FrameworkElement)?.DataContext as RowModel;
        bool clicked = row is { IsVisible: true } && ReferenceEquals(row, pressedRow);
        pressedRow = null;
        e.Handled = true;
        int index = clicked ? rows.IndexOf(row!) : -1;
        if (index >= 0 && index < visibleRows)
        {
            model.Selection = index;
            OpenSelected(reveal: false);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---- Model to view ----

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchModel.Items):
                if (warmUpRows)
                {
                    break; // The warm-up's sample rows stay until it is done; ClearWarmUpRows then catches up.
                }
                // The model keeps the selection inside the rows (and on the same item across an apps reload).
                SyncRows(model.Items);
                SyncSelection();
                SyncResultsVisibility();
                break;
            case nameof(SearchModel.Selection):
                SyncSelection();
                break;
            case nameof(SearchModel.Message):
                MessageText.Text = model.Message ?? "";
                SyncResultsVisibility();
                break;
            case nameof(SearchModel.Query):
                SyncSpinner();
                break;
            case nameof(SearchModel.IsSearching):
                SyncSpinner();
                if (!model.IsSearching)
                {
                    FitHeightWhenSettled();
                }
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Shows <paramref name="items"/> in the pooled rows: row i shows item i, rows past the items are hidden, and
    /// a row is only added when there are more items than ever before (at most 3 apps + 8 Results). The list never
    /// removes or re-adds a container, so replacing Early Results by Final Results only updates bound properties.
    /// Visible rows come first, so a row's index is its item's index (and the model's <see cref="SearchModel.Selection"/>).
    /// </summary>
    private void SyncRows(IReadOnlyList<SearchItem> items)
    {
        int appCount = 0;
        while (appCount < items.Count && items[appCount] is AppItem)
        {
            appCount++;
        }
        for (int i = 0; i < items.Count; i++)
        {
            if (i == rows.Count)
            {
                rows.Add(new RowModel());
            }
            rows[i].Show(items[i], PanelSections.TitleAbove(i, appCount), userProfile, items.Count);
        }
        for (int i = items.Count; i < rows.Count; i++)
        {
            rows[i].Hide();
        }
        visibleRows = items.Count;
    }

    /// <summary>The list's selection follows the model's, never the other way round; hidden rows are never selected.</summary>
    private void SyncSelection()
    {
        int index = model.Selection;
        int target = index >= 0 && index < visibleRows ? index : -1;
        if (ResultsList.SelectedIndex != target)
        {
            ResultsList.SelectedIndex = target;
        }
        if (target >= 0)
        {
            ResultsList.ScrollIntoView(rows[target]);
        }
    }

    private void SyncResultsVisibility()
    {
        bool message = model.Message is not null;
        Visibility results = visibleRows > 0 || message ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = results;
        ResultsScroller.Visibility = results;
        MessageText.Visibility = message ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The progress indicator appears only when a search is still running <see cref="SpinnerDelay"/> after it started
    /// (each keystroke starts a new one), and goes as soon as no search runs.
    /// </summary>
    private void SyncSpinner()
    {
        if (!shown || !model.IsSearching)
        {
            StopSpinner();
            return;
        }
        if (!spinning)
        {
            spinnerDelay.Stop();
            spinnerDelay.Start();
        }
    }

    private void SpinnerDelay_Tick(object? sender, EventArgs e)
    {
        spinnerDelay.Stop();
        if (shown && model.IsSearching && !spinning)
        {
            spinning = true;
            Spinner.Visibility = Visibility.Visible;
            // Runs only while visible: the animation is removed again in StopSpinner.
            SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    private void StopSpinner()
    {
        spinnerDelay.Stop();
        if (!spinning)
        {
            return;
        }
        spinning = false;
        SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        Spinner.Visibility = Visibility.Collapsed;
    }

    // ---- Theme ----

    /// <summary>Follows the Windows app theme and accent colour; cheap when nothing changed.</summary>
    private void RefreshTheme() => ApplyTheme(SystemTheme.Read());

    private void ApplyTheme(SystemTheme next)
    {
        if (theme == next)
        {
            return;
        }
        bool lightChanged = theme?.Light != next.Light;
        theme = next;
        if (lightChanged)
        {
            Resources.MergedDictionaries[0] = next.Light ? lightTheme : darkTheme;
            if (hwnd != IntPtr.Zero)
            {
                _ = WindowNative.SetDarkFrame(hwnd, !next.Light);
            }
        }
        SyncCompositionBackground();
        Resources["AccentBrush"] = Frozen(next.Accent);
        // The selected row: the accent at 22 %, as on macOS.
        Resources["SelectionBrush"] = Frozen(Color.FromArgb(0x38, next.Accent.R, next.Accent.G, next.Accent.B));
    }

    /// <summary>Without a backdrop, what WPF has not painted yet is the opaque panel colour rather than black.</summary>
    private void SyncCompositionBackground()
    {
        if (backdrop || hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd)?.CompositionTarget is not { } target)
        {
            return;
        }
        if (TryFindResource("PanelOpaqueBrush") is SolidColorBrush opaque)
        {
            target.BackgroundColor = opaque.Color;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
