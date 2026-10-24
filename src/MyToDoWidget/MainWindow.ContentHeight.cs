using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public partial class MainWindow
{
    private bool _heightFitQueued, _fittingHeight;

    private void RequestContentHeight()
    {
        if (_closed || !IsLoaded || _state.IsRolledUp || _isResizing || _pointerMove is not null || _draggingCard || _heightFitQueued) return;
        _heightFitQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _heightFitQueued = false;
            FitContentHeight();
        }));
    }

    private void FitContentHeight()
    {
        if (_closed || !IsLoaded || _state.IsRolledUp || _isResizing || _pointerMove is not null || _draggingCard || _fittingHeight) return;
        if (NoteArea.ActualWidth <= 0 || FindScrollViewer(NoteList) is not { } scroll) return;
        _fittingHeight = true;
        try
        {
            // Only automatic rows are chrome. The remaining star row is the viewport,
            // not a minimum window height or a font-size input.
            double chrome = Surface.BorderThickness.Top + Surface.BorderThickness.Bottom
                + TitleBar.ActualHeight + NoteArea.Margin.Top + NoteArea.Margin.Bottom
                + NoteArea.RowDefinitions.Take(2).Sum(row => row.ActualHeight);
            double content;
            if (NoteList.Items.Count == 0)
            {
                EmptyPanel.Measure(new Size(NoteArea.ActualWidth, double.PositiveInfinity));
                content = EmptyPanel.DesiredSize.Height;
            }
            else
            {
                double listFrame = NoteList.Padding.Top + NoteList.Padding.Bottom
                    + NoteList.BorderThickness.Top + NoteList.BorderThickness.Bottom;
                double measured = 0;
                bool allMeasured = true;
                for (int i = 0; i < NoteList.Items.Count; i++)
                {
                    if (NoteList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item || item.DesiredSize.Height <= 0)
                    { allMeasured = false; break; }
                    measured += item.DesiredSize.Height;
                }
                // Some scroll panels keep their extent at least as large as the viewport.
                // Natural measured item sizes allow a short list to shrink immediately.
                content = (allMeasured ? measured : scroll.ExtentHeight) + listFrame;
            }
            var handle = new WindowInteropHelper(this).Handle;
            var area = Forms.Screen.FromHandle(handle).WorkingArea;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            double top = PointToScreen(new Point()).Y;
            double available = (area.Bottom - Math.Max(area.Top, top) - 6 * dpi) / dpi;
            double target = WidgetHeight.Fit(chrome, content, available, MaxHeight);
            _expandedHeight = target;
            if (Math.Abs(Height - target) > 0.5) Height = target;
            UpdateScrollButtons();
        }
        finally { _fittingHeight = false; }
    }
}
