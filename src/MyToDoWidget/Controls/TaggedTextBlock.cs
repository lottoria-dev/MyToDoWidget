using System.Windows.Documents;

namespace MyToDoWidget.Controls;

/// <summary>Plain text only. Hashtags become colored Runs; no markup is interpreted.</summary>
public sealed class TaggedTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(nameof(ContentText), typeof(string), typeof(TaggedTextBlock), new FrameworkPropertyMetadata("", Refresh));
    public static readonly DependencyProperty TagForegroundProperty = DependencyProperty.Register(nameof(TagForeground), typeof(Brush), typeof(TaggedTextBlock), new FrameworkPropertyMetadata(Brushes.SteelBlue, Refresh));
    public string ContentText { get => (string)GetValue(ContentTextProperty); set => SetValue(ContentTextProperty, value); }
    public Brush TagForeground { get => (Brush)GetValue(TagForegroundProperty); set => SetValue(TagForegroundProperty, value); }
    private static void Refresh(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((TaggedTextBlock)sender).Refresh();
    private void Refresh()
    {
        string text = ContentText ?? ""; int offset = 0; Inlines.Clear();
        foreach (var span in TaggedText.TagSpans(text))
        {
            if (span.Start > offset) Inlines.Add(new Run(text[offset..span.Start]));
            Inlines.Add(new Run(text.Substring(span.Start, span.Length)) { Foreground = TagForeground });
            offset = span.Start + span.Length;
        }
        if (offset < text.Length) Inlines.Add(new Run(text[offset..]));
    }
}
