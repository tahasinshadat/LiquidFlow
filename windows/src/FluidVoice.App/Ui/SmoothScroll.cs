using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FluidVoice.Ui;

/// <summary>
/// Trackpad-style inertial wheel scrolling for a ScrollViewer: wheel events set a
/// target offset and a per-frame lerp glides toward it, instead of WPF's default
/// 3-line jumps.
/// </summary>
public static class SmoothScroll
{
    public static void Attach(ScrollViewer viewer)
    {
        double target = 0;
        bool animating = false;
        bool synced = false;
        TimeSpan lastRender = TimeSpan.Zero;

        // Time constant of the glide (seconds). Smaller = snappier, larger = floatier.
        const double Tau = 0.11;

        void OnRendering(object? s, EventArgs e)
        {
            // Frame-rate-independent ease-out: move a fraction of the remaining distance
            // proportional to the real elapsed time, so it feels identical at 60/120/144 Hz.
            var now = (e as System.Windows.Media.RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            double dt = lastRender == TimeSpan.Zero ? 1.0 / 60 : (now - lastRender).TotalSeconds;
            lastRender = now;
            if (dt <= 0) dt = 1.0 / 60;
            if (dt > 0.05) dt = 0.05; // cap after a stall so we don't jump

            var current = viewer.VerticalOffset;
            var delta = target - current;
            if (Math.Abs(delta) < 0.3)
            {
                viewer.ScrollToVerticalOffset(target);
                CompositionTarget.Rendering -= OnRendering;
                animating = false;
                lastRender = TimeSpan.Zero;
                return;
            }
            double factor = 1 - Math.Exp(-dt / Tau);
            viewer.ScrollToVerticalOffset(current + delta * factor);
        }

        viewer.PreviewMouseWheel += (s, e) =>
        {
            if (e.Delta == 0) return;
            // let nested scrollables (dropdown popups etc.) handle their own wheel
            if (e.OriginalSource is System.Windows.DependencyObject src && IsInsidePopup(src)) return;
            e.Handled = true;
            if (!synced || !animating) { target = viewer.VerticalOffset; synced = true; }
            // ~2.5 lines of travel per notch, accumulated so fast flicks stack momentum
            target = Math.Clamp(target - e.Delta * 1.15, 0, Math.Max(0, viewer.ScrollableHeight));
            if (!animating)
            {
                animating = true;
                lastRender = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
            }
        };

        // external jumps (ScrollToTop on navigation) shouldn't animate from stale targets
        viewer.ScrollChanged += (s, e) =>
        {
            if (!animating) target = viewer.VerticalOffset;
        };
    }

    private static bool IsInsidePopup(System.Windows.DependencyObject d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.Primitives.Popup) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : System.Windows.LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
