using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;

namespace UEManager.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // The start size is also the smallest, so there are fewer layouts to keep right.
        var smallest = Smallest(new Size(Width, Height), SystemParameters.WorkArea);
        MinWidth = Width = smallest.Width;
        MinHeight = Height = smallest.Height;
        OnWindowStateChanged();
        StateChanged += (_, _) => OnWindowStateChanged();
        // Ctrl+F and F5, the keys of these commands, while no dialog covers the window.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => FocusSearch(), CanUseKeys));
        CommandBindings.Add(new CommandBinding(NavigationCommands.Refresh, async (_, _) => await ViewModel!.ReloadAsync(), CanUseKeys));
        // Ctrl+V while no text box has the keyboard: a mod's Nexus Mods link on the clipboard is imported.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => PasteLink(), CanUseKeys));
        // The view model comes before the window shows, so it opens where it was last time.
        DataContextChanged += (_, e) =>
        {
            if (!IsLoaded && e.NewValue is MainViewModel { WindowPlacement: { } placement })
                PlaceAt(placement);
        };
        // The maximize button's tooltip is set here, so it follows a change of language too, on the window's thread.
        PropertyChangedEventManager.AddHandler(L.Current, (_, _) =>
        {
            if (Dispatcher.CheckAccess())
                OnWindowStateChanged();
            else
                Dispatcher.BeginInvoke(OnWindowStateChanged);
        }, nameof(L.Language));
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void CanUseKeys(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ViewModel is { HasOverlay: false };

    /// <summary>Any other text on the clipboard is left alone.</summary>
    private void PasteLink()
    {
        if (ClipboardText.Get() is { } text && NexusMods.ParseUrl(text) is not null)
            _ = ViewModel!.ImportNexusLinkAsync(text);
    }

    /// <summary>How near the end of the Browse page's list its next mods are read, so "Load more" needs no click.</summary>
    private const double LoadMoreDistance = 600;

    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource == sender && e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - LoadMoreDistance
            && ViewModel is { SelectedPage: "Browse", Browse: { HasMore: true } browse })
            browse.LoadMoreCommand.Execute(null);
    }

    /// <summary>A finger that swipes a list past its end would push the whole window aside; the window stays put.</summary>
    protected override void OnManipulationBoundaryFeedback(ManipulationBoundaryFeedbackEventArgs e) => e.Handled = true;

    /// <summary>The search of the UE4SS log on its page, the mod search from every other page.</summary>
    private void FocusSearch()
    {
        if (ViewModel!.SelectedPage == "Ue4ss")
        {
            Ue4ssPage.FocusSearch();
            return;
        }
        ViewModel.SelectedPage = "Mods";
        // The page shows only after the change of page is drawn.
        Dispatcher.BeginInvoke(ModsPage.FocusSearch, DispatcherPriority.Input);
    }

    /// <summary>Unsaved settings in the editor: it asks first, and the window closes after the answer.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel?.SettingsEditor is { IsModified: true, IsReadOnly: false } editor)
        {
            e.Cancel = true;
            editor.RequestClose(Close);
            return;
        }
        // A maximized or minimized window keeps the place it returns to. A window that never showed has none.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))
            ViewModel?.RememberWindow(new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized));
        base.OnClosing(e);
    }

    /// <summary>The smallest window: the start size, or what fits a screen too small for it.</summary>
    internal static Size Smallest(Size start, Rect workArea) =>
        new(Math.Min(start.Width, workArea.Width), Math.Min(start.Height, workArea.Height));

    private void PlaceAt(WindowPlacement placement)
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (Usable(placement, screens, new Size(MinWidth, MinHeight)) is not { } bounds)
            return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
        OnWindowStateChanged();
    }

    /// <summary>
    /// The saved place, at least the minimum size, when enough of it and all of its title bar are on the screens; null
    /// when not, as after a screen was unplugged, so the window opens centred.
    /// </summary>
    internal static Rect? Usable(WindowPlacement placement, Rect screens, Size minimum)
    {
        if (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)
            || !double.IsFinite(placement.Width) || !double.IsFinite(placement.Height))
            return null;
        var bounds = new Rect(placement.Left, placement.Top,
            Math.Clamp(placement.Width, minimum.Width, Math.Max(minimum.Width, screens.Width)),
            Math.Clamp(placement.Height, minimum.Height, Math.Max(minimum.Height, screens.Height)));
        var visible = Rect.Intersect(bounds, screens);
        var titleBarOnScreen = bounds.Top >= screens.Top && bounds.Top + TitleBarHeight <= screens.Bottom;
        return !visible.IsEmpty && visible.Width >= 200 && visible.Height >= 100 && titleBarOnScreen ? bounds : null;
    }

    /// <summary>The height of the title bar, as in the window's chrome.</summary>
    private const double TitleBarHeight = 34;

    /// <summary>What is dragged over the window can be installed; read as the drag enters, not at each move.</summary>
    private bool canInstallDragged;

    protected override void OnDragEnter(DragEventArgs e)
    {
        canInstallDragged = CanInstall(e.Data);
        OnDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = !canInstallDragged ? DragDropEffects.None
            : e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.Link;
        if (ViewModel is not null)
            ViewModel.IsDragOver = canInstallDragged;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.IsDragOver = false;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (ViewModel is null)
            return;
        ViewModel.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _ = ViewModel.InstallAsync(files);
        else if (DraggedLink(e.Data) is { } link)
            _ = ViewModel.ImportNexusLinkAsync(link);
        e.Handled = true;
    }

    /// <summary>Files and folders, or a mod's link from a browser.</summary>
    internal static bool CanInstall(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop) || DraggedLink(data) is not null;

    /// <summary>The Nexus Mods link of a mod in what is dragged from a browser: a link on a page, or the page's address.</summary>
    internal static string? DraggedLink(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text
                && NexusMods.ParseUrl(text) is not null ? text : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void OnWindowStateChanged()
    {
        // A maximized chromeless window reaches past the screen edge by the resize border.
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? L.T("Restore") : L.T("Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
