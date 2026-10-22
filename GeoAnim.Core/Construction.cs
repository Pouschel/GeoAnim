using GeoAnim.Formulas;

namespace GeoAnim;

/// <summary>
/// A geometric construction as an animation. Objects defined before the first <see cref="Step"/> form the starting
/// picture; objects defined in a step are drawn there (unless hidden), one after the other. Actions take effect when
/// they are called, so later definitions see the positions after earlier moves.
/// </summary>
/// <example>
/// <code>
/// var c = new Construction { Title = "Perpendicular bisector" };
/// var a = c.Point("A", 2, 3);
/// var b = c.Point("B", 8, 3);
/// c.Step("Draw the segment AB.");
/// c.Segment("AB", a, b);
/// c.Step("Circles around A and B.");
/// var k1 = c.Circle("k1", a, b);
/// var k2 = c.Circle("k2", b, a);
/// c.Step("The intersections.");
/// var (s, t) = c.Intersect("S", "T", k1, k2);
/// c.Line("g", s, t, new Options { Color = Color.Parse("red") });
/// string json = c.Build().ToJson();
/// </code>
/// </example>
public sealed class Construction
{
    /// <summary>Time between two computed positions while points move, in milliseconds.</summary>
    internal const double SampleInterval = 40;

    /// <summary>Length of a step without actions, in milliseconds.</summary>
    internal const double EmptyStepDuration = 500;

    private readonly Scene scene = new();
    private readonly Dictionary<string, GeoObject> objectsByName = new(StringComparer.Ordinal);
    private readonly List<GeoObject> objects = new();
    private readonly List<AnimationStep> steps = new();
    private ViewWindow view = ViewWindow.Default;
    private RenderContext? context;
    private List<TimelineAction>? together;
    private double time;
    private AnimationStep? currentStep;
    private bool built;
    private double speed = 1;
    private string? title;

    /// <summary>
    /// The description of an exported page for search engines (meta description); without it, the page describes itself
    /// with the beginning of its step descriptions.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>The title; like all texts it may contain formulas in AsciiMath between $…$ ("\$" for a dollar sign).</summary>
    public string? Title
    {
        get => title;
        set => title = value == null ? null : CheckText(value);
    }

    public Color Background { get; set; } = Color.Names["white"];

    /// <summary>Distance of the grid lines in units, or null for no grid.</summary>
    public double? GridStep { get; set; }

    /// <summary>Whether the coordinate axes are drawn.</summary>
    public bool Axes { get; set; }

    /// <summary>
    /// Speed of the whole animation: 1 as given by the durations, 0.5 half as fast (everything takes twice as long),
    /// 2 twice as fast. Applied when the animation is built; <see cref="Time"/> stays unscaled.
    /// </summary>
    public double Speed
    {
        get => speed;
        set => speed = value > 0 && double.IsFinite(value) ? value : throw new GeoException("The speed must be greater than 0.");
    }

    /// <summary>The visible window. Only before the first object, since lines are cut at its edges.</summary>
    public ViewWindow View
    {
        get => view;
        set
        {
            if (context != null)
            {
                throw new GeoException("The view must be set before the first object.");
            }
            value.Validate();
            view = value;
        }
    }

    /// <summary>The objects by name.</summary>
    public IReadOnlyDictionary<string, GeoObject> Objects => objectsByName;

    /// <summary>The current time in milliseconds.</summary>
    public double Time => time;

    /// <summary>Whether the first step has begun (before it, everything belongs to the starting picture).</summary>
    public bool InSteps => currentStep != null;

    private RenderContext Context => context ??= new RenderContext(scene, view);

    // ------------------------------------------------------------------ objects

    public GeoPoint Point(string name, double x, double y, Options? options = null) =>
        Define(GeoPoint.Free(name, new Vec(x, y), options ?? Options.None));

    /// <summary>A point on a circle at <paramref name="angle"/> degrees (counterclockwise from the positive x direction).</summary>
    public GeoPoint PointOn(string name, GeoCircle circle, double angle, Options? options = null) =>
        Define(GeoPoint.OnCircle(name, circle, angle, options ?? Options.None));

    /// <summary>A point A + t·(B − A) on a segment, line or ray (t = 0 is A, t = 1 is B).</summary>
    public GeoPoint PointOn(string name, GeoStraight straight, double t, Options? options = null) =>
        Define(GeoPoint.OnStraight(name, straight, t, options ?? Options.None));

    public GeoPoint Midpoint(string name, GeoPoint a, GeoPoint b, Options? options = null) =>
        Define(GeoPoint.Midpoint(name, a, b, options ?? Options.None));

    /// <summary>The intersections of two objects (segments, lines, rays, circles), one name per intersection wanted.</summary>
    public IReadOnlyList<GeoPoint> Intersect(IReadOnlyList<string> names, GeoObject a, GeoObject b, Options? options = null) =>
        Intersect(names, a, b, names.Select(_ => options ?? Options.None).ToList());

    /// <summary>The intersections of two objects with options for each point.</summary>
    public IReadOnlyList<GeoPoint> Intersect(IReadOnlyList<string> names, GeoObject a, GeoObject b, IReadOnlyList<Options> options)
    {
        if (options.Count != names.Count)
        {
            throw new ArgumentException("One set of options per name.", nameof(options));
        }
        int max = Intersections.MaxCount(a, b);
        if (max == 0)
        {
            throw new GeoException($"Cannot intersect {a} and {b} (only segments, lines, rays and circles).");
        }
        if (names.Count == 0 || names.Count > max)
        {
            throw new GeoException($"{a} and {b} have at most {max} intersection{(max == 1 ? "" : "s")}; give 1{(max == 2 ? " or 2" : "")} names.");
        }
        // which of the (up to 2) intersections exist now; on a segment or ray one of them may lie outside
        List<int> existing = Enumerable.Range(0, max).Where(i => Intersections.Find(a, b, i) != null).ToList();
        if (existing.Count == 0)
        {
            throw new GeoException($"{a.Name} and {b.Name} do not intersect.");
        }
        if (names.Count > existing.Count)
        {
            throw new GeoException($"{a.Name} and {b.Name} have only one intersection; give one name.");
        }
        // one name: the first intersection that exists (it keeps its number while points move)
        List<int> indices = names.Count == max ? Enumerable.Range(0, max).ToList() : existing.Take(names.Count).ToList();
        var points = new List<GeoPoint>();
        for (int i = 0; i < names.Count; i++)
        {
            GeoPoint point = GeoPoint.Intersection(names[i], a, b, indices[i], options[i]);
            point.Compute();
            points.Add(point);
        }
        // define all first, then draw them together
        foreach (GeoPoint point in points)
        {
            Register(point);
        }
        RunOrQueue(points.Where(p => !p.Options.Hidden).Select(p => (TimelineAction)DrawActionFor(p, p.Options)).ToList());
        return points;
    }

    public (GeoPoint First, GeoPoint Second) Intersect(string first, string second, GeoObject a, GeoObject b, Options? options = null)
    {
        IReadOnlyList<GeoPoint> points = Intersect([first, second], a, b, options);
        return (points[0], points[1]);
    }

    public GeoStraight Segment(string name, GeoPoint a, GeoPoint b, Options? options = null) =>
        Define(new GeoStraight(StraightKind.Segment, name, a, b, options ?? Options.None));

    public GeoStraight Line(string name, GeoPoint a, GeoPoint b, Options? options = null) =>
        Define(new GeoStraight(StraightKind.Line, name, a, b, options ?? Options.None));

    public GeoStraight Ray(string name, GeoPoint a, GeoPoint b, Options? options = null) =>
        Define(new GeoStraight(StraightKind.Ray, name, a, b, options ?? Options.None));

    /// <summary>A circle around <paramref name="center"/> through <paramref name="through"/>.</summary>
    public GeoCircle Circle(string name, GeoPoint center, GeoPoint through, Options? options = null) =>
        Define(new GeoCircle(name, center, through, options ?? Options.None));

    /// <summary>A circle around <paramref name="center"/> with a fixed radius.</summary>
    public GeoCircle Circle(string name, GeoPoint center, double radius, Options? options = null) =>
        Define(new GeoCircle(name, center, radius, options ?? Options.None));

    public GeoPolygon Polygon(string name, IReadOnlyList<GeoPoint> vertices, Options? options = null) =>
        Define(new GeoPolygon(name, vertices, options ?? Options.None));

    public GeoText Text(string name, string content, double x, double y, Options? options = null) =>
        Define(new GeoText(name, CheckText(content), new Vec(x, y), options ?? Options.None));

    /// <summary>A text next to a point; it moves with the point.</summary>
    public GeoText Text(string name, string content, GeoPoint anchor, Options? options = null) =>
        Define(new GeoText(name, CheckText(content), anchor, options ?? Options.None));

    // the formulas of a text must be complete ($…$)
    private static string CheckText(string text)
    {
        try
        {
            MathText.Split(text);
            return text;
        }
        catch (FormatException e)
        {
            throw new GeoException(e.Message);
        }
    }

    private T Define<T>(T item) where T : GeoObject
    {
        if (item.Options.Label != null)
        {
            CheckText(item.Options.Label);
        }
        item.Compute();
        if (!item.IsDefined)
        {
            throw new GeoException($"{item} cannot be constructed from the current positions.");
        }
        Register(item);
        if (!item.Options.Hidden)
        {
            RunOrQueue([DrawActionFor(item, item.Options)]);
        }
        return item;
    }

    private void Register(GeoObject item)
    {
        if (built)
        {
            throw new InvalidOperationException("The construction has been built.");
        }
        if (objectsByName.ContainsKey(item.Name))
        {
            throw new GeoException($"The name {item.Name} is already used.");
        }
        objectsByName[item.Name] = item;
        objects.Add(item);
    }

    /// <summary>A second name for an object (e.g. an input a used file gives back as its result); nothing is drawn.</summary>
    public void Alias(string name, GeoObject item)
    {
        if (objectsByName.ContainsKey(name))
        {
            throw new GeoException($"The name {name} is already used.");
        }
        objectsByName[name] = item;
    }

    private static DrawAction DrawActionFor(GeoObject item, Options? options) =>
        new(item, options?.Duration ?? item.DefaultDuration, options?.Easing ?? item.DefaultEasing);

    // ------------------------------------------------------------------ actions

    /// <summary>Shows objects: draws them if they were defined hidden, fades them in if they were hidden.</summary>
    public void Show(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items)
        {
            if (!item.IsDrawn && !IsQueued(item))
            {
                actions.Add(new DrawAction(item, duration ?? item.DefaultDuration, easing ?? item.DefaultEasing));
            }
            else
            {
                actions.Add(new FadeAction(item, 1, duration ?? 400, easing ?? Easing.EaseOut));
            }
        }
        RunOrQueue(actions);
    }

    /// <summary>Fades objects out (their dependent objects stay).</summary>
    public void Hide(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items)
        {
            if (!item.IsDrawn && !IsQueued(item))
            {
                throw new GeoException($"{item} has not been shown yet.");
            }
            actions.Add(new FadeAction(item, 0, duration ?? 400, easing ?? Easing.EaseOut));
        }
        RunOrQueue(actions);
    }

    /// <summary>Fades out those of the objects that are visible or about to be drawn; the others are left alone.</summary>
    public void HideVisible(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        List<GeoObject> visible = items.Where(item => (item.IsDrawn || IsQueued(item)) && item.Opacity > 0).ToList();
        if (visible.Count > 0)
        {
            Hide(visible, duration, easing);
        }
    }

    /// <summary>Moves a free point to where <paramref name="target"/> is now (it does not follow it later).</summary>
    public void Move(GeoPoint point, GeoPoint target, double? duration = null, Easing? easing = null)
    {
        if (!target.IsDefined)
        {
            throw new GeoException($"{target} does not exist for the current positions.");
        }
        Move(point, target.Position, duration, easing);
    }

    /// <summary>Moves a free point to <paramref name="target"/>; dependent objects follow.</summary>
    public void Move(GeoPoint point, Vec target, double? duration = null, Easing? easing = null)
    {
        if (!point.IsFree)
        {
            throw new GeoException(point.Path != null
                ? $"{point.Name} lies on {point.Path.Name}: move it to a number (the angle on a circle, the position on a straight)."
                : $"Only free points can move; {point.Name} is constructed from other objects.");
        }
        RequireStep("move");
        RunOrQueue([new MoveAction(point, target, duration ?? 1000, easing ?? Easing.EaseInOut)]);
    }

    /// <summary>
    /// Moves a point on a circle to the angle <paramref name="target"/> (degrees; going beyond 360 runs round further) or a
    /// point on a straight to the parameter <paramref name="target"/>; dependent objects follow.
    /// </summary>
    public void Move(GeoPoint point, double target, double? duration = null, Easing? easing = null)
    {
        if (point.Path == null)
        {
            throw new GeoException(point.IsFree
                ? $"{point.Name} is a free point: move it to coordinates like (2, 3)."
                : $"{point.Name} is constructed from other objects and cannot move.");
        }
        if (point.Path is GeoStraight straight)
        {
            (double min, double max) = straight.ParameterRange;
            if (target < min || target > max)
            {
                throw new GeoException($"The position {target} is not on the {straight.Kind} {straight.Name} (allowed: {min} to {max}).");
            }
        }
        RequireStep("move");
        RunOrQueue([new GlideAction(point, target, duration ?? 1000, easing ?? Easing.EaseInOut)]);
    }

    /// <summary>Waits.</summary>
    public void Pause(double duration = 500)
    {
        if (!(duration >= 0))
        {
            throw new GeoException("A pause cannot be negative.");
        }
        RequireStep("pause");
        RunOrQueue([new PauseAction(duration)]);
    }

    /// <summary>
    /// Starts everything done in <paramref name="body"/> at the same time. With <paramref name="duration"/> the block takes
    /// exactly that long: all its actions are stretched or compressed by the same factor.
    /// </summary>
    public void Together(Action body, double? duration = null)
    {
        if (together != null)
        {
            throw new GeoException("\"together\" blocks cannot be nested.");
        }
        if (duration < 0)
        {
            throw new GeoException("The duration cannot be negative.");
        }
        RequireStep("together");
        together = new List<TimelineAction>();
        try
        {
            body();
            if (duration != null)
            {
                double length = together.Count == 0 ? 0 : together.Max(a => a.Duration);
                if (length > 0)
                {
                    foreach (TimelineAction action in together)
                    {
                        action.Duration *= duration.Value / length;
                    }
                }
                else
                {
                    // nothing that takes time: the block waits
                    together.Add(new PauseAction(duration.Value));
                }
            }
            Run(together);
        }
        finally
        {
            together = null;
        }
    }

    /// <summary>Begins a new step with its description.</summary>
    public void Step(string caption)
    {
        if (together != null)
        {
            throw new GeoException("A step cannot begin inside a \"together\" block.");
        }
        CloseStep();
        currentStep = new AnimationStep(CheckText(caption), time, time);
    }

    private void RequireStep(string what)
    {
        if (currentStep == null)
        {
            throw new GeoException($"\"{what}\" is only possible inside a step.");
        }
    }

    private bool IsQueued(GeoObject item) => together?.Any(a => a is DrawAction && a.Objects.Contains(item)) ?? false;

    private void CloseStep()
    {
        if (currentStep == null)
        {
            return;
        }
        if (time - currentStep.Start < 1e-6)
        {
            Run([new PauseAction(EmptyStepDuration)]);
        }
        steps.Add(currentStep with { End = time });
    }

    private void RunOrQueue(List<TimelineAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }
        if (together != null)
        {
            together.AddRange(actions);
        }
        else
        {
            Run(actions);
        }
    }

    /// <summary>Runs actions that start at the same time (one action, or a together block).</summary>
    private void Run(List<TimelineAction> actions)
    {
        // an empty together block (or one with hidden definitions only) takes no time
        if (actions.Count == 0)
        {
            return;
        }
        RenderContext ctx = Context;
        if (currentStep == null)
        {
            // the starting picture: everything at once, at time 0
            foreach (TimelineAction action in actions)
            {
                action.Begin(ctx, 0);
                action.End(ctx, Span.At(0));
            }
            return;
        }
        double start = time;
        double length = actions.Max(a => a.Duration);
        foreach (TimelineAction action in actions)
        {
            action.Begin(ctx, start);
        }
        if (actions.Any(a => a.MovesPoints))
        {
            int count = Math.Max(1, (int)Math.Ceiling(length / SampleInterval));
            double previous = start;
            for (int i = 1; i <= count; i++)
            {
                double now = start + length * i / count;
                foreach (TimelineAction action in actions)
                {
                    double progress = action.Duration <= 0 ? 1 : (now - start) / action.Duration;
                    action.Apply(EasingFunctions.Apply(action.Easing, progress));
                }
                RecomputeAll();
                RenderAll(new Span(previous, now));
                previous = now;
            }
        }
        else
        {
            foreach (TimelineAction action in actions)
            {
                action.End(ctx, new Span(start, start + action.Duration, action.Easing));
            }
        }
        time = start + length;
    }

    private void RecomputeAll()
    {
        foreach (GeoObject item in objects)
        {
            item.Compute();
        }
    }

    private void RenderAll(Span span)
    {
        foreach (GeoObject item in objects.Where(o => o.IsDrawn))
        {
            item.Render(Context, span);
        }
    }

    /// <summary>Finishes the construction and returns the animation (a construction is built once).</summary>
    public AnimationData Build()
    {
        if (!built)
        {
            CloseStep();
            currentStep = null;
            built = true;
        }
        double scale = 1 / speed;
        List<AnimationStep> scaledSteps = steps.Select(s => s with { Start = s.Start * scale, End = s.End * scale }).ToList();
        return new AnimationData(Title, view, Background, GridStep, Axes, time * scale, scaledSteps, scene.Primitives, scale, RenderFormulas(scaledSteps))
        {
            Description = Description,
        };
    }

    // all formulas of the title, the step descriptions and the texts in the picture, as SVG
    private Dictionary<string, Formula> RenderFormulas(List<AnimationStep> stepList)
    {
        IEnumerable<string> texts = stepList.Select(s => s.Caption)
            .Concat(scene.Primitives
                .Where(p => p.Type == "text" && p.Tracks.ContainsKey("text"))
                .SelectMany(p => p.Tracks["text"].Select(key => (string)key.Value)));
        if (Title != null)
        {
            texts = texts.Prepend(Title);
        }
        var formulas = new Dictionary<string, Formula>(StringComparer.Ordinal);
        foreach (string formula in texts.SelectMany(MathText.Formulas).Distinct(StringComparer.Ordinal))
        {
            try
            {
                formulas[formula] = MathRenderer.Render(formula);
            }
            catch (FormatException e)
            {
                throw new GeoException(e.Message);
            }
        }
        return formulas;
    }
}

/// <summary>A step of the animation: its description and its time span in milliseconds.</summary>
public sealed record AnimationStep(string Caption, double Start, double End);
