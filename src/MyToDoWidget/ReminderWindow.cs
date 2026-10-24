using MyToDoWidget.Services;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

/// <summary>One digest window for missed reminders. Closing is never completion.</summary>
public sealed class ReminderWindow : Window
{
    private readonly ListBox _list = new();
    private readonly Dictionary<string, MemoItem> _items = [];
    private readonly TextBlock _body = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _count = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    public ReminderWindow(IEnumerable<MemoItem> items, Action<string> view, Action<string> complete, Action<string, DateTimeOffset> snooze)
    {
        Title = "MyToDo2 · 다시 확인"; Width = 370; Height = 390; MinWidth = 320; MinHeight = 320;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Theme.ApplySettings(Resources);
        Background = (Brush)Resources["PanelBrush"];
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_count);
        Grid.SetRow(_list, 1); root.Children.Add(_list);
        _list.Foreground = Brushes.Black; _list.Background = Brushes.White;
        _list.DisplayMemberPath = nameof(MemoItem.DisplayTitle);
        _list.SelectionChanged += (_, _) => _body.Text = (_list.SelectedItem as MemoItem)?.Body ?? "";
        var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var actions = new UniformGrid { Columns = 3 };
        var viewButton = new Button { Content = "보기", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(5, 2, 5, 2) };
        var doneButton = new Button { Content = "완료", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(5, 2, 5, 2) };
        var snoozeButton = new Button { Content = "미루기 ▾", Padding = new Thickness(5, 2, 5, 2) };
        viewButton.Click += (_, _) => { if (_list.SelectedItem is MemoItem item) view(item.Id); };
        doneButton.Click += (_, _) => { if (_list.SelectedItem is MemoItem item) complete(item.Id); };
        var menu = new ContextMenu { Background = Brushes.White, Foreground = Brushes.Black };
        void AddSnooze(string label, Func<DateTimeOffset> time)
        {
            var choice = new MenuItem { Header = label };
            choice.Click += (_, _) => { if (_list.SelectedItem is MemoItem item) snooze(item.Id, time()); };
            menu.Items.Add(choice);
        }
        AddSnooze("15분 뒤", () => DateTimeOffset.Now.AddMinutes(15));
        AddSnooze("1시간 뒤", () => DateTimeOffset.Now.AddHours(1));
        AddSnooze("내일 오전 9시", () => new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(9)));
        snoozeButton.Click += (_, _) => { menu.PlacementTarget = snoozeButton; menu.IsOpen = true; };
        actions.Children.Add(viewButton); actions.Children.Add(doneButton); actions.Children.Add(snoozeButton);
        Grid.SetRow(actions, 3); root.Children.Add(actions);
        var hint = new TextBlock { Text = "닫기는 완료가 아닙니다. 항목은 위젯에 남습니다.", FontSize = 11, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(hint, 4); root.Children.Add(hint);
        Content = root; Add(items);
        Loaded += (_, _) =>
        {
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this); var work = screen.WorkingArea;
            Left = Math.Max(work.Left / dpi.DpiScaleX, work.Right / dpi.DpiScaleX - Width - 16);
            Top = Math.Max(work.Top / dpi.DpiScaleY, work.Bottom / dpi.DpiScaleY - Height - 16);
        };
    }
    public void Add(IEnumerable<MemoItem> items)
    {
        string? selected = (_list.SelectedItem as MemoItem)?.Id;
        foreach (var item in items) _items[item.Id] = item.Copy();
        _list.ItemsSource = _items.Values.OrderBy(i => i.ReminderAt).ToList();
        _list.SelectedItem = selected is null ? _list.Items.Cast<MemoItem>().FirstOrDefault() : _items.GetValueOrDefault(selected);
        _count.Text = $"확인할 알림 {_items.Count}개";
    }
    public void Prune(IReadOnlyList<MemoItem> current)
    {
        var active = current.Where(i => !i.IsDeleted && !i.IsCompleted && i.ConflictOf is null).ToDictionary(i => i.Id);
        foreach (string id in _items.Keys.ToList())
        {
            if (!active.TryGetValue(id, out var item) || item.ReminderToken != _items[id].ReminderToken) _items.Remove(id);
            else _items[id] = item.Copy();
        }
        if (_items.Count == 0) { Close(); return; }
        Add([]);
    }
}
