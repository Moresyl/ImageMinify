using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageMinify.Controls;

/// <summary>
/// The before-and-after stage: two versions of one picture on one rectangle, with a wipe between
/// them, at any magnification.
/// </summary>
/// <remarks>
/// <para>
/// This began as a stack of <c>Image</c> elements whose widths were multiplied together by
/// converters. That worked until the layers had to agree on a size the layout system had no opinion
/// about, at which point every offset on the stage was a binding into a size the bindings were
/// themselves producing — and the two pictures came apart in a way nothing in the markup made
/// visible. One element that owns the arithmetic is smaller than that graph and, unlike it, can be
/// tested; the arithmetic is in <see cref="ComparisonGeometry"/>.
/// </para>
/// <para>
/// Fitting the whole picture into the stage is the wrong magnification for the job this view exists
/// to do: a 4000-pixel photograph drawn 300 pixels wide has had every artefact the encoder made
/// resampled out of it before it reaches the screen. So the stage magnifies, up to 1:1 and past it,
/// and the two files stay locked to the same rectangle at every step — which is the only way the
/// comparison means anything.
/// </para>
/// </remarks>
public sealed class ComparisonSurface : FrameworkElement
{
    /// <summary>Which of the two drags is in progress.</summary>
    private enum Grab
    {
        None,
        Seam,
        Pan,
    }

    private BitmapSource? _original;
    private BitmapSource? _compared;
    private Vector _pan;
    private Grab _grab;
    private Point _panFrom;
    private bool _reloadQueued;

    public ComparisonSurface()
    {
        Cursor = Cursors.SizeWE;
        ClipToBounds = true;
    }

    // ==== What to show ========================================================================

    public static readonly DependencyProperty OriginalPathProperty = DependencyProperty.Register(
        nameof(OriginalPath),
        typeof(string),
        typeof(ComparisonSurface),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty ComparedPathProperty = DependencyProperty.Register(
        nameof(ComparedPath),
        typeof(string),
        typeof(ComparisonSurface),
        new PropertyMetadata(null, OnSourceChanged));

    /// <summary>The file the run started from. Drawn to the left of the seam.</summary>
    public string? OriginalPath
    {
        get => (string?)GetValue(OriginalPathProperty);
        set => SetValue(OriginalPathProperty, value);
    }

    /// <summary>The file the run produced. It is the one being judged, so it sets the rectangle.</summary>
    public string? ComparedPath
    {
        get => (string?)GetValue(ComparedPathProperty);
        set => SetValue(ComparedPathProperty, value);
    }

    // ==== Where the seam is ===================================================================

    public static readonly DependencyProperty SplitProperty = DependencyProperty.Register(
        nameof(Split),
        typeof(double),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(
            0.5d,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null,
            CoerceSplit));

    /// <summary>Where the wipe sits, 0 to 1 across the stage.</summary>
    public double Split
    {
        get => (double)GetValue(SplitProperty);
        set => SetValue(SplitProperty, value);
    }

    // ==== How far in ==========================================================================

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(ComparisonSurface),
        new PropertyMetadata(0d, OnZoomChanged));

    private static readonly DependencyPropertyKey EffectiveZoomKey = DependencyProperty.RegisterReadOnly(
        nameof(EffectiveZoom),
        typeof(double),
        typeof(ComparisonSurface),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty EffectiveZoomProperty = EffectiveZoomKey.DependencyProperty;

    /// <summary>
    /// The magnification asked for, where zero means "whatever fits".
    /// </summary>
    /// <remarks>
    /// Zero rather than a separate flag because the two can never then disagree, and because it
    /// makes "fit" survive the stage being resized: an explicit number is a number the user chose
    /// and the window getting wider is no reason to overrule it, whereas zero re-answers the
    /// question every time the stage changes size.
    /// </remarks>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>The magnification actually in use, with fit resolved to a number.</summary>
    public double EffectiveZoom => (double)GetValue(EffectiveZoomProperty);

    /// <summary>True while the stage is showing the whole picture rather than a chosen size.</summary>
    public bool IsFitToWindow => Zoom <= 0;

    // ==== How it is painted ===================================================================

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background),
        typeof(Brush),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(double),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CheckerboardBrushProperty = DependencyProperty.Register(
        nameof(CheckerboardBrush),
        typeof(Brush),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeamBrushProperty = DependencyProperty.Register(
        nameof(SeamBrush),
        typeof(Brush),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeamGlyphBrushProperty = DependencyProperty.Register(
        nameof(SeamGlyphBrush),
        typeof(Brush),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeamGlyphProperty = DependencyProperty.Register(
        nameof(SeamGlyph),
        typeof(Geometry),
        typeof(ComparisonSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What fills the stage where the picture is not.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// The stage's own rounding, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// The control draws its own ground and clips to it rather than sitting inside a rounded Border,
    /// because a Border rounds what it paints and not what its child paints — so a magnified picture
    /// would square off the four corners it is supposed to sit inside.
    /// </remarks>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Laid under both pictures, so a flattened alpha channel is visible at a glance.</summary>
    public Brush? CheckerboardBrush
    {
        get => (Brush?)GetValue(CheckerboardBrushProperty);
        set => SetValue(CheckerboardBrushProperty, value);
    }

    /// <summary>The seam line and the disc on it.</summary>
    public Brush? SeamBrush
    {
        get => (Brush?)GetValue(SeamBrushProperty);
        set => SetValue(SeamBrushProperty, value);
    }

    /// <summary>The mark inside the disc, and the hairline that separates it from the picture.</summary>
    public Brush? SeamGlyphBrush
    {
        get => (Brush?)GetValue(SeamGlyphBrushProperty);
        set => SetValue(SeamGlyphBrushProperty, value);
    }

    /// <summary>The mark itself, on whatever grid it was drawn on; it is fitted and centred here.</summary>
    public Geometry? SeamGlyph
    {
        get => (Geometry?)GetValue(SeamGlyphProperty);
        set => SetValue(SeamGlyphProperty, value);
    }

    // ==== Commands the window drives ==========================================================

    /// <summary>Steps to the next round magnification up, about the middle of the stage.</summary>
    public void ZoomIn() => ApplyZoom(ComparisonGeometry.StepZoom(EffectiveZoom, 1), Centre);

    /// <summary>Steps to the next round magnification down.</summary>
    public void ZoomOut() => ApplyZoom(ComparisonGeometry.StepZoom(EffectiveZoom, -1), Centre);

    /// <summary>Back to the whole picture, centred.</summary>
    public void FitToWindow()
    {
        _pan = default;
        Zoom = 0d;
        Refresh();
    }

    /// <summary>
    /// One image pixel per screen pixel: the only magnification at which an artefact is the size the
    /// encoder made it.
    /// </summary>
    public void ShowActualSize() => ApplyZoom(1d, Centre);

    /// <summary>Out to the whole picture, or in to 1:1 — what the percentage in the header does.</summary>
    public void ToggleFit()
    {
        if (IsFitToWindow)
        {
            ShowActualSize();
        }
        else
        {
            FitToWindow();
        }
    }

    // ==== Layout ==============================================================================

    /// <remarks>
    /// Takes whatever it is offered. There is nothing here whose size comes from its content — the
    /// picture is fitted to the stage, never the other way round — so the honest answer to "how big
    /// would you like to be" is "as big as the row you are in".
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsPositiveInfinity(availableSize.Width) ? 0d : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? 0d : availableSize.Height);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // Fit is a function of the stage, so resizing the window re-answers it. Nothing here can
        // feed back into layout: the only thing published is EffectiveZoom, and the one control
        // bound to it has a fixed width for exactly this reason.
        Refresh();
    }

    // ==== Painting ============================================================================

    protected override void OnRender(DrawingContext drawingContext)
    {
        var stage = new Rect(RenderSize);

        if (stage.Width <= 0 || stage.Height <= 0)
        {
            return;
        }

        var radius = Math.Max(0d, CornerRadius);
        drawingContext.PushClip(new RectangleGeometry(stage, radius, radius));

        // Transparent rather than null where no ground is set. A null brush paints nothing and, more
        // to the point, is not hit-testable — and the seam has to be draggable from the empty margin
        // around a picture that does not fill the stage.
        drawingContext.DrawRectangle(Background ?? Brushes.Transparent, null, stage);

        if (_compared is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var content = Snap(ComparisonGeometry.ContentRect(ContentSize, RenderSize, EffectiveZoom, _pan), dpi);
            var seamX = Snap(RenderSize.Width * Split, dpi.DpiScaleX);

            drawingContext.DrawRectangle(CheckerboardBrush, null, content);
            drawingContext.DrawImage(_compared, content);

            DrawOriginal(drawingContext, content, seamX);
            DrawSeam(drawingContext, content, seamX);
        }

        drawingContext.Pop();
    }

    /// <remarks>
    /// Drawn on the compressed file's rectangle, not its own, and then clipped to the left of the
    /// seam. Same rectangle, same scale, same pixel for pixel — which is the whole of what makes the
    /// two comparable when the run also resized the image.
    /// </remarks>
    private void DrawOriginal(DrawingContext drawingContext, Rect content, double seamX)
    {
        if (_original is null)
        {
            return;
        }

        var before = Rect.Intersect(content, new Rect(0d, 0d, Math.Max(0d, seamX), RenderSize.Height));

        if (before is { IsEmpty: false, Width: > 0, Height: > 0 })
        {
            drawingContext.PushClip(new RectangleGeometry(before));
            drawingContext.DrawImage(_original, content);
            drawingContext.Pop();
        }
    }

    /// <remarks>
    /// Down the picture rather than down the stage. On a letterboxed picture a full-height line runs
    /// out across empty ground at both ends and reads as a divider of the dialog instead of a
    /// divider of the thing being compared.
    /// </remarks>
    private void DrawSeam(DrawingContext drawingContext, Rect content, double seamX)
    {
        var top = Math.Max(content.Top, 0d);
        var bottom = Math.Min(content.Bottom, RenderSize.Height);

        if (bottom - top <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(SeamBrush, null, new Rect(seamX - 1d, top, 2d, bottom - top));
        DrawGrip(drawingContext, new Point(seamX, (top + bottom) / 2));
    }

    /// <remarks>
    /// The disc carries a hairline in the glyph's own colour. A filled accent circle sitting
    /// straight on a photograph can land on a patch of very nearly the same colour and vanish;
    /// something has to separate it from whatever it happens to be over.
    /// </remarks>
    private void DrawGrip(DrawingContext drawingContext, Point centre)
    {
        const double Radius = 14d;
        const double GlyphSize = 15d;

        drawingContext.DrawEllipse(
            SeamBrush,
            SeamGlyphBrush is null ? null : new Pen(SeamGlyphBrush, 1.5d),
            centre,
            Radius,
            Radius);

        if (SeamGlyph is not { } glyph)
        {
            return;
        }

        var bounds = glyph.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Fitted from whatever grid the glyph was drawn on, then centred on its own ink rather than
        // on its declared box — the same thing Stretch="Uniform" in a centred box would have done.
        var scale = GlyphSize / Math.Max(bounds.Width, bounds.Height);
        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(-(bounds.X + (bounds.Width / 2)), -(bounds.Y + (bounds.Height / 2))));
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(centre.X, centre.Y));

        drawingContext.PushTransform(transform);
        drawingContext.DrawGeometry(SeamGlyphBrush, null, glyph);
        drawingContext.Pop();
    }

    // ==== Mouse ===============================================================================

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        switch (e.ChangedButton)
        {
            // Halfway is the one position worth being able to return to without aiming.
            case MouseButton.Left when e.ClickCount == 2:
                Split = 0.5d;
                break;

            // The seam is grabbed anywhere on the stage. A two-pixel line is a target to hunt for,
            // and pulling the picture across is the gesture every before-and-after already uses.
            case MouseButton.Left:
                Begin(Grab.Seam);
                MoveSeam(e.GetPosition(this));
                break;

            // Panning is on the other buttons rather than on left-drag-once-magnified, because a
            // gesture that means one thing at one zoom and something else at the next is a gesture
            // you have to check the zoom before using.
            case MouseButton.Right or MouseButton.Middle when CanPan:
                _panFrom = e.GetPosition(this);
                Begin(Grab.Pan);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsMouseCaptured)
        {
            return;
        }

        var position = e.GetPosition(this);

        switch (_grab)
        {
            case Grab.Seam when e.LeftButton == MouseButtonState.Pressed:
                MoveSeam(position);
                break;

            case Grab.Pan when e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed:
                _pan += position - _panFrom;
                _panFrom = position;
                Refresh();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <remarks>
    /// The drag ends here rather than on the button coming up, so that losing the capture to
    /// anything else — another window taking focus mid-drag — leaves the surface in the same state
    /// as letting go of it would.
    /// </remarks>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        _grab = Grab.None;
        Cursor = Cursors.SizeWE;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (_compared is null)
        {
            return;
        }

        // A fifth per notch, about the pointer: fine enough to creep up on a detail, coarse enough
        // that one flick crosses the whole range.
        ApplyZoom(EffectiveZoom * Math.Pow(1.2d, e.Delta / 120d), e.GetPosition(this));
        e.Handled = true;
    }

    // ==== State ===============================================================================

    /// <summary>The compressed file's pixel dimensions, which are the rectangle everything uses.</summary>
    private Size ContentSize =>
        _compared is null ? default : new Size(_compared.PixelWidth, _compared.PixelHeight);

    private Point Centre => new(RenderSize.Width / 2, RenderSize.Height / 2);

    private double FitZoom => ComparisonGeometry.FitZoom(ContentSize, RenderSize);

    private bool CanPan =>
        (ContentSize.Width * EffectiveZoom) - RenderSize.Width > 0.5d ||
        (ContentSize.Height * EffectiveZoom) - RenderSize.Height > 0.5d;

    private void Begin(Grab grab)
    {
        _grab = grab;
        Cursor = grab == Grab.Pan ? Cursors.SizeAll : Cursors.SizeWE;
        CaptureMouse();
    }

    private void MoveSeam(Point position) => Split = ComparisonGeometry.SplitAt(position.X, RenderSize.Width);

    private void ApplyZoom(double zoom, Point anchor)
    {
        var from = EffectiveZoom;
        var to = ComparisonGeometry.ClampZoom(zoom, FitZoom);

        _pan = Math.Abs(to - from) < 1e-9
            ? ComparisonGeometry.ClampPan(ContentSize, RenderSize, to, _pan)
            : ComparisonGeometry.PanForZoomAbout(ContentSize, RenderSize, from, to, _pan, anchor);

        // Set even when the number has not moved, so that "1:1" on a picture that already fits at
        // 100% still leaves fit mode and the next press has somewhere to go.
        Zoom = to;
        Refresh();
    }

    private void Refresh()
    {
        var fit = FitZoom;
        var zoom = ComparisonGeometry.ClampZoom(IsFitToWindow ? fit : Zoom, fit);

        _pan = ComparisonGeometry.ClampPan(ContentSize, RenderSize, zoom, _pan);
        SetValue(EffectiveZoomKey, zoom);

        // Smooth on the way down, honest on the way up. Fant above 100% blurs away exactly the
        // ringing and blocking this view exists to show; nearest neighbour below it aliases a
        // photograph into something neither file looks like.
        RenderOptions.SetBitmapScalingMode(
            this,
            zoom > 1d ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        InvalidateVisual();
    }

    // ==== Loading =============================================================================

    /// <remarks>
    /// Both paths change when a different result is opened, and each change would otherwise decode
    /// both files. Coalescing them onto the dispatcher does it once, and doing it at background
    /// priority lets the panel appear before a large photograph is read rather than after it.
    /// </remarks>
    private void QueueReload()
    {
        if (_reloadQueued)
        {
            return;
        }

        _reloadQueued = true;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _reloadQueued = false;
                ReloadSources();
            }));
    }

    private void ReloadSources()
    {
        _compared = Load(ComparedPath, 0);

        // The original is decoded straight to the size the compressed file came out at. It has to be
        // drawn on that rectangle to be comparable at all, and scaling it once in the decoder beats
        // resampling a twenty-four-megapixel photograph on every frame of a drag — and beats holding
        // it at full size for as long as the comparison is open.
        _original = Load(OriginalPath, _compared?.PixelWidth ?? 0);

        _pan = default;
        Zoom = 0d;
        Refresh();
    }

    private static BitmapSource? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();

            // OnLoad is what lets the file be replaced or deleted while the comparison is open. The
            // default streams from disk on demand and holds a lock on the very file that re-running
            // the batch would overwrite.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);

            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // An unreadable, half-written or unsupported file. The stage stays empty rather than
            // taking the window down over a preview.
            return null;
        }
    }

    // ==== Pixel grid ==========================================================================

    /// <remarks>
    /// The origin only, never the size. At 100% the picture's width is already a whole number of
    /// pixels, and it is the half-pixel left over from centring it in an odd-width stage that would
    /// otherwise spread every pixel of a 1:1 comparison across two of the screen's — which is the
    /// one thing this view exists to avoid.
    /// </remarks>
    private static Rect Snap(Rect rect, DpiScale dpi) =>
        new(Snap(rect.X, dpi.DpiScaleX), Snap(rect.Y, dpi.DpiScaleY), rect.Width, rect.Height);

    private static double Snap(double value, double scale) =>
        scale <= 0 || double.IsNaN(value) ? value : Math.Round(value * scale) / scale;

    // ==== Property plumbing ===================================================================

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ComparisonSurface)d).QueueReload();

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ComparisonSurface)d).Refresh();

    private static object CoerceSplit(DependencyObject d, object baseValue) =>
        baseValue is double split && !double.IsNaN(split) ? Math.Clamp(split, 0d, 1d) : 0.5d;
}
