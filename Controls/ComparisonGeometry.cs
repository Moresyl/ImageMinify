using System.Windows;

namespace ImageMinify.Controls;

/// <summary>
/// Where the picture goes, how big it is drawn, and where the seam between the two versions falls.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ComparisonSurface"/> so the arithmetic can be checked without a
/// window. Nothing here touches a visual: every method is a pure function of the four things that
/// decide the answer — how big the picture is, how big the hole it is being shown in is, how far it
/// is magnified, and how far it has been dragged.
/// </remarks>
public static class ComparisonGeometry
{
    /// <summary>
    /// The most a picture can be magnified. Eight puts a single JPEG block across sixty-four pixels,
    /// which is as close as anyone needs to get to a ringing artefact.
    /// </summary>
    public const double MaxZoom = 8d;

    /// <summary>
    /// The least — except where the whole picture does not fit even at that, because a photograph
    /// too large to fit at 5% would otherwise have a "fit to window" its own zoom floor forbade.
    /// <see cref="ClampZoom"/> is where that exception lives.
    /// </summary>
    public const double MinZoom = 0.05d;

    /// <summary>
    /// Round numbers to step through. A fixed multiplier per press is simpler and lands on 63% and
    /// 132%, which are figures nobody asked for and cannot navigate back to.
    /// </summary>
    private static readonly double[] Rungs =
        [0.05, 0.1, 0.15, 0.25, 0.33, 0.5, 0.67, 1, 1.5, 2, 3, 4, 6, 8];

    /// <summary>
    /// A step compares against a rung with a little room, so a zoom sitting exactly on one moves
    /// past it rather than returning it and appearing to do nothing.
    /// </summary>
    private const double Tolerance = 1e-6;

    /// <summary>The magnification at which the whole picture is on screen.</summary>
    /// <remarks>
    /// Capped at 1. "Fit" means all of it is visible, not that a 48-pixel icon is blown up to fill an
    /// 800-pixel stage — magnifying is a thing the user asks for, not a thing a default does.
    /// </remarks>
    public static double FitZoom(Size content, Size viewport)
    {
        if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return 1d;
        }

        return Math.Min(1d, Math.Min(viewport.Width / content.Width, viewport.Height / content.Height));
    }

    /// <summary>Holds a magnification inside the usable range, with fit always inside it.</summary>
    public static double ClampZoom(double zoom, double fit) =>
        double.IsNaN(zoom) ? fit : Math.Clamp(zoom, Math.Min(MinZoom, fit), MaxZoom);

    /// <summary>The next round magnification above or below the current one.</summary>
    public static double StepZoom(double zoom, int direction)
    {
        if (direction < 0)
        {
            for (var i = Rungs.Length - 1; i >= 0; i--)
            {
                if (Rungs[i] < zoom - Tolerance)
                {
                    return Rungs[i];
                }
            }

            // Below the lowest rung already, which is where a picture too large to fit at 5% starts.
            // Standing still is right here: the alternative is a "zoom out" button that zooms in.
            return zoom;
        }

        foreach (var rung in Rungs)
        {
            if (rung > zoom + Tolerance)
            {
                return rung;
            }
        }

        return MaxZoom;
    }

    /// <summary>The rectangle the picture occupies inside the stage.</summary>
    /// <remarks>
    /// Centred, then displaced by the pan. Writing it this way rather than as a top-left corner is
    /// what makes a pan of zero mean "centred" at every magnification, so a picture that shrinks
    /// back to fitting lands in the middle without anything having to reset the offset.
    /// </remarks>
    public static Rect ContentRect(Size content, Size viewport, double zoom, Vector pan)
    {
        var width = Math.Max(0d, content.Width * zoom);
        var height = Math.Max(0d, content.Height * zoom);

        return new Rect(
            ((viewport.Width - width) / 2) + pan.X,
            ((viewport.Height - height) / 2) + pan.Y,
            width,
            height);
    }

    /// <summary>Stops a drag from pushing the picture off the stage.</summary>
    /// <remarks>
    /// Slack is how far a picture larger than the stage can travel before the edge it is being
    /// dragged away from comes inside. No slack forces the offset to zero, which is what keeps a
    /// picture that already fits centred instead of letting it be shoved into a corner.
    /// </remarks>
    public static Vector ClampPan(Size content, Size viewport, double zoom, Vector pan)
    {
        var slackX = Math.Max(0d, ((content.Width * zoom) - viewport.Width) / 2);
        var slackY = Math.Max(0d, ((content.Height * zoom) - viewport.Height) / 2);

        return new Vector(
            double.IsNaN(pan.X) ? 0d : Math.Clamp(pan.X, -slackX, slackX),
            double.IsNaN(pan.Y) ? 0d : Math.Clamp(pan.Y, -slackY, slackY));
    }

    /// <summary>
    /// The pan that keeps whatever is under <paramref name="anchor"/> under it after the
    /// magnification changes.
    /// </summary>
    /// <remarks>
    /// This is the difference between a wheel that zooms and a wheel that zooms usefully. Without
    /// it, magnifying always moves towards the middle of the picture, so inspecting a corner means
    /// zoom, drag back, zoom, drag back.
    /// </remarks>
    public static Vector PanForZoomAbout(Size content, Size viewport, double from, double to, Vector pan, Point anchor)
    {
        if (from <= 0 || to <= 0)
        {
            return ClampPan(content, viewport, to, pan);
        }

        // Which point of the picture, in its own pixels, the anchor is currently over.
        var rect = ContentRect(content, viewport, from, pan);
        var pictureX = (anchor.X - rect.X) / from;
        var pictureY = (anchor.Y - rect.Y) / from;

        // That point has to land back on the anchor. The rest is solving ContentRect's own
        // expression — centre plus pan, plus the point scaled up — for the pan.
        var moved = new Vector(
            anchor.X - (pictureX * to) - ((viewport.Width - (content.Width * to)) / 2),
            anchor.Y - (pictureY * to) - ((viewport.Height - (content.Height * to)) / 2));

        return ClampPan(content, viewport, to, moved);
    }

    /// <summary>
    /// Where a pointer at <paramref name="x"/> puts the seam, as a fraction of the stage.
    /// </summary>
    /// <remarks>
    /// A fraction of the stage rather than of the picture. Once a picture can be larger than what it
    /// is shown in, a seam pinned to the picture slides off the side the moment you magnify a
    /// corner; a seam pinned to the stage is always the line down the middle of what is in front of
    /// you. With the picture centred, half the stage is still half the picture.
    /// </remarks>
    public static double SplitAt(double x, double width) =>
        width <= 0 || double.IsNaN(x) ? 0.5d : Math.Clamp(x / width, 0d, 1d);
}
