using System.Windows.Documents;
using System.Windows.Controls.Primitives;

namespace MyToDoWidget;

public partial class MainWindow
{
    private const string CardDragFormat = "MyToDoWidget.SharedCardOrder";
    private sealed record CardDrag(string Id, MemoKind Kind, string Token);
    private CardDrag? _cardDrag;
    private Point _cardPointerStart;
    private bool _draggingCard;
    private long _lastDragScroll;

    private static DependencyObject? InteractionParent(DependencyObject node) => node switch
    {
        FrameworkContentElement content => content.Parent ?? ContentOperations.GetParent(content),
        ContentElement content => ContentOperations.GetParent(content),
        Visual visual => VisualTreeHelper.GetParent(visual) ?? LogicalTreeHelper.GetParent(visual),
        System.Windows.Media.Media3D.Visual3D visual => VisualTreeHelper.GetParent(visual),
        _ => LogicalTreeHelper.GetParent(node)
    };

    private ListBoxItem? CardContainer(DependencyObject? node, bool ignoreActions)
    {
        while (node is not null && node != NoteList)
        {
            if (ignoreActions && node is (ButtonBase or TextBoxBase or Hyperlink)) return null;
            if (node is ListBoxItem item)
                return ItemsControl.ItemsControlFromItemContainer(item) == NoteList ? item : null;
            node = InteractionParent(node);
        }
        return null;
    }

    private void NoteList_PointerDown(object sender, MouseButtonEventArgs e)
    {
        _cardDrag = null;
        if (e.ChangedButton != MouseButton.Left || _draggingCard) return;
        var container = CardContainer(e.OriginalSource as DependencyObject, ignoreActions: true);
        if (container?.DataContext is not MemoCard card) return;
        if (e.ClickCount == 2)
        {
            // Use the hit card, not selection: activating the editor clears widget focus.
            e.Handled = true;
            OpenEditor(card.Item);
            return;
        }
        if (e.ClickCount != 1) return;
        _cardPointerStart = e.GetPosition(NoteList);
        _cardDrag = new(card.Id, card.Item.Kind, Guid.NewGuid().ToString("N"));
    }

    private void NoteList_PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingCard) _cardDrag = null;
    }

    private void NoteList_PointerMove(object sender, MouseEventArgs e)
    {
        if (_draggingCard || _cardDrag is not { } card) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _cardDrag = null; return; }
        Point pointer = e.GetPosition(NoteList);
        if (Math.Abs(pointer.X - _cardPointerStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pointer.Y - _cardPointerStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _draggingCard = true;
        _lastDragScroll = 0;
        e.Handled = true;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(CardDragFormat, card.Token);
            System.Windows.DragDrop.DoDragDrop(NoteList, data, System.Windows.DragDropEffects.Move);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { ShowNotice("항목을 옮기지 못했습니다. 다시 끌어 주세요."); }
        finally
        {
            _draggingCard = false; _cardDrag = null;
            DropIndicator.Visibility = Visibility.Collapsed;
            RequestContentHeight();
        }
    }

    private bool IsOwnCardDrag(System.Windows.DragEventArgs e) => _draggingCard && _cardDrag is { } card
        && card.Kind == _state.ActiveTab && e.Data.GetDataPresent(CardDragFormat, false)
        && e.Data.GetData(CardDragFormat, false) is string token
        && string.Equals(token, card.Token, StringComparison.Ordinal);

    private (MemoCard Card, bool After, double Line)? DropTarget(Point pointer)
    {
        if (pointer.X < 0 || pointer.X > NoteList.ActualWidth || pointer.Y < 0 || pointer.Y > NoteList.ActualHeight) return null;
        ListBoxItem? best = null;
        double distance = double.PositiveInfinity, itemTop = 0;
        // This also handles the gap between cards and the empty space after the last card.
        // Only realized containers are inspected; the list remains virtualized when large.
        for (int i = 0; i < NoteList.Items.Count; i++)
        {
            if (NoteList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item || item.ActualHeight <= 0) continue;
            double top = item.TranslatePoint(new Point(), NoteList).Y;
            double end = top + item.ActualHeight;
            if (end < 0 || top > NoteList.ActualHeight) continue;
            double gap = pointer.Y < top ? top - pointer.Y : pointer.Y > end ? pointer.Y - end : 0;
            if (gap >= distance) continue;
            best = item; distance = gap; itemTop = top;
        }
        if (best?.DataContext is not MemoCard card) return null;
        bool after = pointer.Y >= itemTop + best.ActualHeight / 2;
        return (card, after, Math.Clamp(itemTop + (after ? best.ActualHeight : 0), 0, Math.Max(0, NoteList.ActualHeight - 2)));
    }

    private void NoteList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true; e.Effects = System.Windows.DragDropEffects.None;
        if (!IsOwnCardDrag(e)) { DropIndicator.Visibility = Visibility.Collapsed; return; }
        Point point = e.GetPosition(NoteList);
        if (Environment.TickCount64 - _lastDragScroll >= 100 && FindScrollViewer(NoteList) is { } scroll)
        {
            double offset = point.Y < 24 ? -24 : point.Y > NoteList.ActualHeight - 24 ? 24 : 0;
            if (offset != 0)
            {
                scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset + offset, 0, scroll.ScrollableHeight));
                _lastDragScroll = Environment.TickCount64;
            }
        }
        if (DropTarget(point) is not { } target) { DropIndicator.Visibility = Visibility.Collapsed; return; }
        e.Effects = System.Windows.DragDropEffects.Move;
        DropIndicator.Margin = new Thickness(3, target.Line, 3, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void NoteList_DragLeave(object sender, System.Windows.DragEventArgs e) => DropIndicator.Visibility = Visibility.Collapsed;

    private void NoteList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true; e.Effects = System.Windows.DragDropEffects.None;
        DropIndicator.Visibility = Visibility.Collapsed;
        if (!IsOwnCardDrag(e) || _cardDrag is not { } drag || DropTarget(e.GetPosition(NoteList)) is not { } target) return;
        var current = MemoOrdering.ForTab(_state.Items, drag.Kind, _state.ItemOrders.GetValueOrDefault(drag.Kind)).Select(i => i.Id);
        var next = MemoOrdering.Move(_state.Items, drag.Kind, _state.ItemOrders.GetValueOrDefault(drag.Kind), drag.Id, target.Card.Id, target.After);
        if (!current.SequenceEqual(next))
            CommitAction(() => _workspace.MoveItem(drag.Id, target.Card.Id, target.After));
        e.Effects = System.Windows.DragDropEffects.Move;
    }

    private void ResetOrder_Click(object sender, RoutedEventArgs e) => CommitAction(() => _workspace.ResetOrder(_state.ActiveTab));
}
