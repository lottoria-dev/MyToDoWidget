namespace MyToDoWidget.Controls;

/// <summary>One filled outline prevents seams when the body and tail are translucent.</summary>
public sealed class SpeechBubble : Decorator
{
    private const double TailWidth = 8, PaddingY = 12, Border = 1;
    private Geometry? _outline;
    private Size _outlineSize;
    private BubbleTail _outlineTail;
    private BubbleShape _outlineShape;

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SpeechBubble),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SpeechBubble),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TailSideProperty = DependencyProperty.Register(
        nameof(TailSide), typeof(BubbleTail), typeof(SpeechBubble),
        new FrameworkPropertyMetadata(BubbleTail.Left, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(BubbleShape), typeof(SpeechBubble),
        new FrameworkPropertyMetadata(BubbleShape.Rounded, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public BubbleTail TailSide { get => (BubbleTail)GetValue(TailSideProperty); set => SetValue(TailSideProperty, value); }
    public BubbleShape Shape { get => (BubbleShape)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    private double Tail => TailSide == BubbleTail.None ? 0 : TailWidth;
    private double PaddingX => Shape == BubbleShape.Capsule ? 20 : 14;
    private double InsetX => Tail + 2 * (PaddingX + Border);
    private const double InsetY = 2 * (PaddingY + Border);

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(Math.Max(0, availableSize.Width - InsetX), Math.Max(0, availableSize.Height - InsetY)));
        var content = Child?.DesiredSize ?? new Size();
        return new Size(content.Width + InsetX, content.Height + InsetY);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double leftTail = TailSide == BubbleTail.Left ? Tail : 0;
        Child?.Arrange(new Rect(leftTail + PaddingX + Border, PaddingY + Border,
            Math.Max(0, finalSize.Width - InsetX), Math.Max(0, finalSize.Height - InsetY)));
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= Tail + 2 || ActualHeight <= 2) return;
        if (_outline is null || _outlineSize != RenderSize || _outlineTail != TailSide || _outlineShape != Shape)
        {
            _outline = CreateOutline();
            _outlineSize = RenderSize; _outlineTail = TailSide; _outlineShape = Shape;
        }
        drawingContext.DrawGeometry(Fill, new Pen(Stroke, Border), _outline);
    }

    private Geometry CreateOutline()
    {
        // Draw a canonical left-tail shape, then mirror geometry only (never the text).
        double left = Tail + 0.5, top = 0.5, right = ActualWidth - 0.5, bottom = ActualHeight - 0.5;
        double radius = Shape switch
        {
            BubbleShape.Square => 0,
            BubbleShape.Droplet => 24,
            BubbleShape.Capsule => 32,
            BubbleShape.CutCorner => 10,
            _ => 14
        };
        radius = Math.Min(radius, Math.Min((right - left) / 2, (bottom - top) / 2));
        double firstRadius = Shape == BubbleShape.Droplet ? Math.Min(2, radius) : radius;
        bool cut = Shape == BubbleShape.CutCorner;
        var body = new StreamGeometry();
        using (var path = body.Open())
        {
            path.BeginFigure(new Point(left + firstRadius, top), true, true);
            path.LineTo(new Point(right - radius, top), true, false);
            Corner(path, new Point(right, top), new Point(right, top + radius), cut);
            path.LineTo(new Point(right, bottom - radius), true, false);
            Corner(path, new Point(right, bottom), new Point(right - radius, bottom), cut);
            path.LineTo(new Point(left + radius, bottom), true, false);
            Corner(path, new Point(left, bottom), new Point(left, bottom - radius), cut);
            path.LineTo(new Point(left, top + firstRadius), true, false);
            Corner(path, new Point(left, top), new Point(left + firstRadius, top), cut);
        }
        Geometry result = body;
        if (TailSide != BubbleTail.None)
        {
            double center = Math.Min(24, ActualHeight / 2);
            double root = Math.Min(right, left + Math.Max(12, radius));
            var tail = new StreamGeometry();
            using (var path = tail.Open())
            {
                path.BeginFigure(new Point(root, center - 7), true, true);
                path.LineTo(new Point(0.5, center - 9), true, false);
                path.QuadraticBezierTo(new Point(1.5, center + 5), new Point(root, center + 9), true, false);
            }
            result = Geometry.Combine(body, tail, GeometryCombineMode.Union, null);
        }
        if (TailSide == BubbleTail.Right)
        {
            result = result.Clone();
            result.Transform = new MatrixTransform(-1, 0, 0, 1, ActualWidth, 0);
        }
        result.Freeze();
        return result;
    }

    private static void Corner(StreamGeometryContext path, Point control, Point end, bool cut)
    {
        if (cut) path.LineTo(end, true, false);
        else path.QuadraticBezierTo(control, end, true, false);
    }
}
