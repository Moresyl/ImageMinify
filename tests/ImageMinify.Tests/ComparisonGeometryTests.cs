using System.Windows;
using ImageMinify.Controls;
using Xunit;

namespace ImageMinify.Tests;

/// <summary>
/// The arithmetic behind the before/after stage.
/// </summary>
/// <remarks>
/// This is the reason the geometry is a separate class. The stage cannot be rendered under test, so
/// everything that can be decided without a window is decided here — and the bug this replaced was
/// exactly the kind that never announces itself: two pictures that agreed on a size until they
/// didn't, with nothing in the markup to say so.
/// </remarks>
public class ComparisonGeometryTests
{
    private const double Precision = 6;

    // ==== Fitting =============================================================================

    [Fact]
    public void FitZoom_PicksTheTighterAxis()
    {
        // Wide picture in a square hole: the width runs out first.
        var zoom = ComparisonGeometry.FitZoom(new Size(4000, 1000), new Size(800, 800));

        Assert.Equal(0.2d, zoom, Precision);
    }

    [Fact]
    public void FitZoom_NeverUpscales()
    {
        // A 48px icon in an 800px stage is shown at 48px. Magnifying is something the user asks
        // for; a default that blows an icon up to fill the dialog is lying about what it has.
        var zoom = ComparisonGeometry.FitZoom(new Size(48, 48), new Size(800, 600));

        Assert.Equal(1d, zoom, Precision);
    }

    [Theory]
    [InlineData(0, 100, 800, 600)]
    [InlineData(100, 0, 800, 600)]
    [InlineData(100, 100, 0, 600)]
    [InlineData(100, 100, 800, 0)]
    public void FitZoom_FallsBackToOneWhenSomethingHasNoSize(double cw, double ch, double vw, double vh)
    {
        // The first layout pass, and the moment before an image has loaded.
        Assert.Equal(1d, ComparisonGeometry.FitZoom(new Size(cw, ch), new Size(vw, vh)), Precision);
    }

    [Fact]
    public void ClampZoom_KeepsFitReachableBelowTheFloor()
    {
        // A 40000px panorama fits at 2%, under the 5% floor. The floor has to give way, or the
        // stage would have a "fit to window" its own limit forbade.
        var fit = ComparisonGeometry.FitZoom(new Size(40000, 20000), new Size(800, 600));

        Assert.True(fit < ComparisonGeometry.MinZoom);
        Assert.Equal(fit, ComparisonGeometry.ClampZoom(fit, fit), Precision);
    }

    [Fact]
    public void ClampZoom_HoldsTheCeiling() =>
        Assert.Equal(ComparisonGeometry.MaxZoom, ComparisonGeometry.ClampZoom(500d, 0.2d), Precision);

    [Fact]
    public void ClampZoom_TreatsNaNAsFit() =>
        Assert.Equal(0.25d, ComparisonGeometry.ClampZoom(double.NaN, 0.25d), Precision);

    // ==== The ladder ==========================================================================

    [Fact]
    public void StepZoom_MovesOffARungItIsAlreadyOn()
    {
        // The tolerance earns its keep here: comparing against the rung itself would return 1 and
        // the button would appear dead.
        Assert.Equal(1.5d, ComparisonGeometry.StepZoom(1d, 1), Precision);
        Assert.Equal(0.67d, ComparisonGeometry.StepZoom(1d, -1), Precision);
    }

    [Fact]
    public void StepZoom_RoundsAnAwkwardZoomToTheNextRung()
    {
        // A wheel leaves you on numbers like this; the buttons put you back on round ones.
        Assert.Equal(0.5d, ComparisonGeometry.StepZoom(0.41d, 1), Precision);
        Assert.Equal(0.33d, ComparisonGeometry.StepZoom(0.41d, -1), Precision);
    }

    [Fact]
    public void StepZoom_StandsStillBelowTheLowestRung()
    {
        // Where the panorama above starts. Stepping out has nowhere to go, and the alternative is a
        // "zoom out" button that zooms in.
        Assert.Equal(0.02d, ComparisonGeometry.StepZoom(0.02d, -1), Precision);
    }

    [Fact]
    public void StepZoom_StopsAtTheCeiling() =>
        Assert.Equal(ComparisonGeometry.MaxZoom, ComparisonGeometry.StepZoom(8d, 1), Precision);

    // ==== Placement ===========================================================================

    [Fact]
    public void ContentRect_CentresWhenNothingHasBeenDragged()
    {
        var rect = ComparisonGeometry.ContentRect(new Size(400, 200), new Size(800, 600), 1d, default);

        Assert.Equal(new Rect(200, 200, 400, 200), rect);
    }

    [Fact]
    public void ContentRect_ScalesAboutTheCentre()
    {
        var rect = ComparisonGeometry.ContentRect(new Size(400, 200), new Size(800, 600), 2d, default);

        Assert.Equal(new Rect(0, 100, 800, 400), rect);
    }

    [Fact]
    public void ContentRect_AppliesThePan()
    {
        var rect = ComparisonGeometry.ContentRect(new Size(400, 200), new Size(800, 600), 1d, new Vector(-50, 25));

        Assert.Equal(new Rect(150, 225, 400, 200), rect);
    }

    // ==== Panning =============================================================================

    [Fact]
    public void ClampPan_PinsAPictureThatAlreadyFits()
    {
        // No slack in either direction, so the offset is forced to zero and the picture stays
        // centred instead of being shoved into a corner.
        var pan = ComparisonGeometry.ClampPan(new Size(400, 200), new Size(800, 600), 1d, new Vector(300, -400));

        Assert.Equal(default, pan);
    }

    [Fact]
    public void ClampPan_StopsAtTheEdgeOfAMagnifiedPicture()
    {
        // 400 x 2 = 800 wide in a 400 stage: 200 of travel each way before the far edge comes in.
        var pan = ComparisonGeometry.ClampPan(new Size(400, 400), new Size(400, 400), 2d, new Vector(999, -999));

        Assert.Equal(new Vector(200, -200), pan);
    }

    [Fact]
    public void ClampPan_TreatsNaNAsCentred() =>
        Assert.Equal(default, ComparisonGeometry.ClampPan(new Size(400, 400), new Size(400, 400), 2d, new Vector(double.NaN, double.NaN)));

    [Fact]
    public void PanForZoomAbout_KeepsThePointUnderThePointer()
    {
        // The whole point of zooming about the pointer: pick a detail, magnify, and it has not
        // moved. Without this, every zoom drifts towards the middle and inspecting a corner is
        // zoom, drag back, zoom, drag back.
        var content = new Size(400, 400);
        var viewport = new Size(400, 400);
        var anchor = new Point(100, 100);

        var pan = ComparisonGeometry.PanForZoomAbout(content, viewport, 1d, 2d, default, anchor);
        var after = ComparisonGeometry.ContentRect(content, viewport, 2d, pan);

        // The anchor was over picture pixel (100, 100); at 2x that pixel sits 200 into the rect.
        Assert.Equal(anchor.X, after.X + 200, Precision);
        Assert.Equal(anchor.Y, after.Y + 200, Precision);
    }

    [Fact]
    public void PanForZoomAbout_ClampsWhenTheAnchorWouldPushThePictureOff()
    {
        // Zooming out about a corner asks for more travel than the shrinking picture has. It gets
        // clamped rather than left hanging off the edge — here to nothing, since 400 x 1 fits.
        var pan = ComparisonGeometry.PanForZoomAbout(
            new Size(400, 400), new Size(400, 400), 4d, 1d, new Vector(600, 600), new Point(0, 0));

        Assert.Equal(default, pan);
    }

    [Fact]
    public void PanForZoomAbout_FallsBackToClampingWhenAZoomIsDegenerate()
    {
        // Before the first fit is computed. Dividing by a zoom of zero is the alternative.
        var pan = ComparisonGeometry.PanForZoomAbout(
            new Size(400, 400), new Size(400, 400), 0d, 2d, new Vector(999, 0), new Point(10, 10));

        Assert.Equal(new Vector(200, 0), pan);
    }

    // ==== The seam ============================================================================

    [Fact]
    public void SplitAt_IsAFractionOfTheStage() =>
        Assert.Equal(0.25d, ComparisonGeometry.SplitAt(200, 800), Precision);

    [Fact]
    public void SplitAt_ClampsADragPastTheEdge()
    {
        // The surface captures the mouse, so a drag carries on outside the stage. Both ends have to
        // be reachable, and neither may be overshot.
        Assert.Equal(0d, ComparisonGeometry.SplitAt(-300, 800), Precision);
        Assert.Equal(1d, ComparisonGeometry.SplitAt(1100, 800), Precision);
    }

    [Fact]
    public void SplitAt_FallsBackToTheMiddleWithoutAStage() =>
        Assert.Equal(0.5d, ComparisonGeometry.SplitAt(100, 0), Precision);
}
