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

        void OnRendering(object? s, EventArgs e)
        {
            var current = viewer.VerticalOffset;
            var delta = target - current;
            if (Math.Abs(delta) < 0.5)
            {
                viewer.ScrollToVerticalOffset(target);
                CompositionTarget.Rendering -= OnRendering;
                animating = false;
                return;
            }
            viewer.ScrollToVerticalOffset(current + delta * 0.22);
        }

        viewer.PreviewMouseWheel += (s, e) =>
        {
            if (e.Delta == 0) return;
            // let nested scrollables (dropdown popups etc.) handle their own wheel
            if (e.OriginalSource is System.Windows.DependencyObject src && IsInsidePopup(src)) return;
            e.Handled = true;
            if (!synced || !animating) { target = viewer.VerticalOffset; synced = true; }
            target = Math.Clamp(target - e.Delta * 1.05, 0, Math.Max(0, viewer.ScrollableHeight));
            if (!animating)
            {
                animating = true;
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
