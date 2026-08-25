using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ImageMinify.Interop;
using ImageMinify.Services;
using ImageMinify.ViewModels;
using Microsoft.Win32;

namespace ImageMinify.Views;

/// <summary>
/// The one window. Everything it does here is something a view model cannot: it owns the Win32
/// handle, the file dialogs, and the drag-and-drop surface.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService)
    {
        _viewModel = viewModel;
        _settingsService = settingsService;

        InitializeComponent();

        // The three things the view model needs a window for. Handed over as functions rather than
        // an IDialogService interface: each is one call with one return value, and an interface with
        // three members and exactly one implementation is a layer that only exists to be named.
        _viewModel.SelectFilesHandler = PickFilesAsync;
        _viewModel.SelectFolderHandler = PickFolderAsync;
        _viewModel.SaveReportHandler = PickReportPathAsync;

        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Hooks the window procedure and applies the Windows 11 frame.
    /// </summary>
    /// <remarks>
    /// This is the earliest point at which there is a handle to hook, and it is before the first
    /// frame is drawn — so the rounded corners and the themed caption are in place from the moment
    /// the window appears rather than replacing a default frame a beat later.
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(OnWindowMessage);

        WindowFrame.Apply(this, _viewModel.IsDarkTheme);

        // Note what is deliberately absent: nothing here clears the window's background. The ground
        // XAML binds to Brush.Bg.Base stays, in both themes, on every machine. Handing it over to a
        // Mica backdrop is how this window once ended up composited against black — see WindowFrame.
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        WindowFrame.HandleMaximizeExtents(hwnd, msg, lParam, ref handled);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
        {
            // The caption buttons and the resize border are drawn by DWM, not by us, so the palette
            // swap the view model has just done does not reach them.
            WindowFrame.Apply(this, _viewModel.IsDarkTheme);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settingsService.RestoreWindowPlacement(this);
        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Saves the window's placement and flushes any settings still sitting in the debounce timer.
    /// </summary>
    /// <remarks>
    /// Closing rather than Closed, and awaited: settings are written on a delay so that dragging the
    /// quality slider is one disk write instead of a hundred, which means the last change a user
    /// makes is very often still pending when they reach for the close button. Once Closed has run
    /// the dispatcher is on its way down and the flush would be racing the process exit.
    /// </remarks>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _settingsService.SaveWindowPlacement(this);
        await _viewModel.ShutdownAsync();
    }

    // ==== Caption =============================================================================

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ==== Keyboard ============================================================================

    /// <summary>
    /// Escape and Enter for the two overlays, which are panels in this window rather than real
    /// dialogs and so get none of the key handling a real one would come with.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // The keyboard path into the comparison, now that there is no slider to tab to. Not taken
        // while a text box has focus: the preview is a panel rather than a real dialog, so a field
        // behind it can still be the thing being typed into.
        if (_viewModel.IsPreviewOpen
            && !_viewModel.IsDialogOpen
            && Keyboard.FocusedElement is not TextBoxBase
            && HandlePreviewKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Escape or Key.Enter))
        {
            return;
        }

        // The dialog is on top of the preview when both are open, so it answers first.
        if (_viewModel.IsDialogOpen)
        {
            if (e.Key == Key.Enter)
            {
                _viewModel.DialogConfirmCommand.Execute(null);
            }
            else if (_viewModel.IsDialogCancellable)
            {
                _viewModel.DialogCancelCommand.Execute(null);
            }
            else
            {
                // A dialog with no cancel is reporting something the user has to acknowledge.
                // Escape dismisses it, because there is only one way out and both keys are it.
                _viewModel.DialogConfirmCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (_viewModel.IsPreviewOpen && e.Key == Key.Escape)
        {
            _viewModel.ClosePreviewCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The comparison's keys, or false if this one is not one of them.
    /// </summary>
    /// <remarks>
    /// The magnification keys are the ones every image viewer already uses, and each has both the
    /// main-row and the numeric-keypad key because Windows reports those separately and no user
    /// thinks of them as different keys. The seam moves a twentieth of the width per press, which
    /// crosses the picture in twenty — quick enough to sweep, fine enough to stop on a detail.
    /// </remarks>
    private bool HandlePreviewKey(Key key)
    {
        switch (key)
        {
            case Key.Left or Key.Right:
                var step = key == Key.Left ? -0.05 : 0.05;
                _viewModel.PreviewSplit = Math.Clamp(_viewModel.PreviewSplit + step, 0d, 1d);
                return true;

            case Key.OemPlus or Key.Add:
                Comparison.ZoomIn();
                return true;

            case Key.OemMinus or Key.Subtract:
                Comparison.ZoomOut();
                return true;

            // Ctrl is not required. Nothing else in the preview answers to a bare digit, and asking
            // for a modifier to get back to a whole picture is a key combination for a panic button.
            case Key.D0 or Key.NumPad0:
                Comparison.FitToWindow();
                return true;

            case Key.D1 or Key.NumPad1:
                Comparison.ShowActualSize();
                return true;

            default:
                return false;
        }
    }

    // ==== Before / after ======================================================================

    // The wipe and the pan live in ComparisonSurface, which owns the geometry they both move. Only
    // the header buttons are wired here, because a button is a thing the window arranges and a drag
    // is a thing the surface interprets.

    private void OnZoomOut(object sender, RoutedEventArgs e) => Comparison.ZoomOut();

    private void OnZoomToggle(object sender, RoutedEventArgs e) => Comparison.ToggleFit();

    private void OnZoomIn(object sender, RoutedEventArgs e) => Comparison.ZoomIn();

    // ==== Drag and drop =======================================================================

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        UpdateDragFeedback(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateDragFeedback(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        DropHint.Visibility = Visibility.Collapsed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        DropHint.Visibility = Visibility.Collapsed;

        if (_viewModel.IsCompressing || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // No filtering by extension here. AddPaths keeps whatever exists and the compressor decides
        // what it can read — a check in the view would have to be kept in step with the engine's
        // list of formats, and would silently reject a file the engine could have handled.
        _viewModel.AddPaths(paths);
        e.Handled = true;

        Activate();
    }

    private void UpdateDragFeedback(DragEventArgs e)
    {
        var accepted = !_viewModel.IsCompressing && e.Data.GetDataPresent(DataFormats.FileDrop);

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        DropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==== File dialogs ========================================================================

    private Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "选择图片",
            Filter = BuildFilter(),
        };

        IReadOnlyList<string> files = dialog.ShowDialog(this) == true ? dialog.FileNames : [];
        return Task.FromResult(files);
    }

    private Task<string?> PickFolderAsync(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            Title = title,
        };

        return Task.FromResult(dialog.ShowDialog(this) == true ? dialog.FolderName : null);
    }

    private Task<string?> PickReportPathAsync(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出压缩报告",
            FileName = suggestedFileName,
            DefaultExt = ".csv",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            AddExtension = true,
        };

        return Task.FromResult(dialog.ShowDialog(this) == true ? dialog.FileName : null);
    }

    /// <summary>
    /// Builds the open dialog's filter from the engine's own list of extensions, so the two cannot
    /// disagree about what this build can read.
    /// </summary>
    private string BuildFilter()
    {
        var extensions = string.Join(';', _viewModel.SupportedExtensions.Select(extension => '*' + extension));
        return $"图片文件|{extensions}|所有文件|*.*";
    }
}
