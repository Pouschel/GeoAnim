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
    private Inspector? inspector;
    private double speed = 1;
    private string? title;

    private string? description;

    /// <summary>
    /// The description of an exported page for search engines (meta description), also shown below the starting picture;
    /// without it, the page describes itself with the beginning of its step descriptions.
    /// </summary>
    public string? Description
    {
        get => description;
        set => description = value == null ? null : CheckText(value);
    }

    private readonly List<PageKeyword> keywords = [];

    /// <summary>
    /// Adds keywords of the page ("Punkt/e/en/es, Satz/es des Pythagoras", separated by commas): "geoanim exportdir" links their first
    /// occurrence in each step of the other pages to this one; before the first step to the page, inside a step to the
    /// end of that step.
    /// </summary>
    /// <param name="file">The file of the keyword line (for messages), null for the script itself.</param>
    /// <param name="line">The line of the keyword line in <paramref name="file"/>.</param>
    public void Keyword(string text, string? file = null, int line = 0)
    {
        int step = currentStep == null ? 0 : steps.Count + 1;
        foreach ((string term, List<string> forms) in GeoAnim.Keywords.ParseTerms(text))
        {
            foreach (string form in forms)
            {
                if (!keywords.Any(k => k.Form == form))
                {
                    keywords.Add(new PageKeyword(form, step) { Term = term, File = file, Line = line });
                }
            }
        }
    }

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

    /// <summary>The size of the axes' numbers in points (null: 15); the distances to the axes grow with it.</summary>
    public double? AxesSize { get; set; }

    /// <summary>The colour of the axes and their numbers (null: grey).</summary>
    public Color? AxesColor { get; set; }

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

    /// <summary>A circle around <paramref name="center"/> with the distance of <paramref name="a"/> and <paramref name="b"/> as its radius (it follows them).</summary>
    public GeoCircle Circle(string name, GeoPoint center, GeoPoint a, GeoPoint b, Options? options = null) =>
        Define(new GeoCircle(name, center, () => a.IsDefined && b.IsDefined ? (b.Position - a.Position).Length : null, options ?? Options.None, a, b));

    /// <summary>A circle around <paramref name="center"/> with a number as its radius (it follows it when it is measured).</summary>
    public GeoCircle Circle(string name, GeoPoint center, GeoNumber radius, Options? options = null) =>
        Define(new GeoCircle(name, center, () => radius.IsDefined ? radius.Value : null, options ?? Options.None, radius));

    /// <summary>
    /// The point C + (B − A) for the straight <paramref name="straight"/> AB: with C, a parallel to it
    /// (<paramref name="perpendicular"/>: turned by 90°, a perpendicular).
    /// </summary>
    public GeoPoint Parallel(string name, GeoStraight straight, GeoPoint through, bool perpendicular = false, Options? options = null) =>
        Define(GeoPoint.Offset(name, through, straight, perpendicular, options ?? Options.None));

    /// <summary>
    /// A segment, ray or line through <paramref name="through"/> parallel (<paramref name="perpendicular"/>: at right
    /// angles) to <paramref name="straight"/>: from it to a hidden point C + (B − A) (turned); it follows both.
    /// </summary>
    public GeoStraight Parallel(StraightKind kind, string name, GeoStraight straight, GeoPoint through, bool perpendicular = false, Options? options = null)
    {
        GeoPoint direction = Parallel(name + "#direction", straight, through, perpendicular, new Options { Hidden = true, NoLabel = true });
        return Define(new GeoStraight(kind, name, through, direction, options ?? Options.None));
    }

    /// <summary>The angle at <paramref name="vertex"/>, counterclockwise from the ray to <paramref name="a"/> to the ray to <paramref name="c"/>.</summary>
    public GeoAngle Angle(string name, GeoPoint a, GeoPoint vertex, GeoPoint c, Options? options = null) =>
        Define(new GeoAngle(name, a, vertex, c, options ?? Options.None));

    /// <summary>An arc of <paramref name="circle"/> from the direction of <paramref name="from"/> to that of <paramref name="to"/> (points or degrees; counterclockwise, with the option Clockwise clockwise).</summary>
    public GeoArc Arc(string name, GeoCircle circle, ArcEnd from, ArcEnd to, Options? options = null) =>
        Define(new GeoArc(name, circle, from, to, isSector: false, options ?? Options.None));

    /// <summary>A sector of <paramref name="circle"/>: the radii to the directions of <paramref name="from"/> and <paramref name="to"/> and the arc between (counterclockwise).</summary>
    public GeoArc Sector(string name, GeoCircle circle, ArcEnd from, ArcEnd to, Options? options = null) =>
        Define(new GeoArc(name, circle, from, to, isSector: true, options ?? Options.None));

    /// <summary>A number: the length of a segment (it follows the segment when it changes).</summary>
    public GeoNumber Length(string name, GeoStraight segment) => Define(GeoNumber.Length(name, segment));

    /// <summary>A number: the distance of two points.</summary>
    public GeoNumber Distance(string name, GeoPoint a, GeoPoint b) => Define(GeoNumber.Distance(name, a, b));

    /// <summary>A number: the size of an angle in degrees.</summary>
    public GeoNumber Degrees(string name, GeoAngle angle) => Define(GeoNumber.Degrees(name, angle));

    /// <summary>A number: the length of an arc (or of a sector's arc).</summary>
    public GeoNumber Length(string name, GeoArc arc) => Define(GeoNumber.ArcLength(name, arc));

    /// <summary>A number: the angle at the centre of an arc or sector in degrees.</summary>
    public GeoNumber Degrees(string name, GeoArc arc) => Define(GeoNumber.Degrees(name, arc));

    /// <summary>A number: the area of a polygon or circle.</summary>
    public GeoNumber Area(string name, GeoObject item) => Define(GeoNumber.Area(name, item));

    /// <summary>A number: the perimeter of a polygon or the circumference of a circle.</summary>
    public GeoNumber Perimeter(string name, GeoObject item) => Define(GeoNumber.Perimeter(name, item));

    /// <summary>A number: the radius of a circle.</summary>
    public GeoNumber RadiusOf(string name, GeoCircle circle) => Define(GeoNumber.RadiusOf(name, circle));

    /// <summary>
    /// A number computed from other numbers (<paramref name="operands"/>: the expression's variables): constant if they
    /// are, else it follows them.
    /// </summary>
    public GeoNumber Calculate(string name, Expression expression, IReadOnlyDictionary<string, GeoNumber> operands) =>
        Define(GeoNumber.Calculation(name, expression, operands));

    public GeoPolygon Polygon(string name, IReadOnlyList<GeoPoint> vertices, Options? options = null) =>
        Define(new GeoPolygon(name, vertices, options ?? Options.None));

    public GeoText Text(string name, string content, double x, double y, Options? options = null) =>
        Define(new GeoText(name, CheckText(content), new Vec(x, y), options ?? Options.None));

    /// <summary>A text next to a point; it moves with the point.</summary>
    public GeoText Text(string name, string content, GeoPoint anchor, Options? options = null) =>
        Define(new GeoText(name, CheckText(content), anchor, options ?? Options.None));

    /// <summary>
    /// A text one line below <paramref name="above"/> (a text at coordinates or below one), with the same alignment;
    /// <paramref name="gap"/> units further down. It moves with the text above.
    /// </summary>
    public GeoText Text(string name, string content, GeoText above, double gap = 0, Options? options = null) =>
        Define(new GeoText(name, CheckText(content), above, gap, options ?? Options.None));

    // the formulas of a text must be complete ($…$)
    private static string CheckText(string text)
    {
        try
        {
            // a number changes while points move; a formula is set once for the page
            foreach (TextRun run in MathText.Split(text).Where(r => r.IsFormula))
            {
                foreach ((string name, _, _) in NumberText.Find(run.Text))
                {
                    throw new GeoException($"The number {{{name}}} stands inside a formula ${run.Text}$; numbers go between the pieces of a formula: \"$|AB| =$ {{l}} cm\".");
                }
            }
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
        if (item is not GeoNumber)
        {
            RequireDrawing("a new object");
        }
        item.Compute();
        if (!item.IsDefined)
        {
            throw new GeoException($"{item} cannot be constructed from the current positions.");
        }
        Register(item);
        Record(item, Span.At(time));
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

    /// <summary>
    /// Shows objects: draws them if they were defined hidden, fades them in if they were hidden. An object that is
    /// visible already (or is being drawn) is left alone; if nothing is to do, it takes no time.
    /// </summary>
    public void Show(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items.Distinct())
        {
            if (!item.IsDrawn && !IsQueued(item))
            {
                actions.Add(new DrawAction(item, duration ?? item.DefaultDuration, easing ?? item.DefaultEasing));
            }
            else if (item.IsDrawn && item.Opacity < 1)
            {
                actions.Add(new FadeAction(item, 1, duration ?? 400, easing ?? Easing.EaseOut));
            }
        }
        RunOrQueue(actions);
    }

    /// <summary>
    /// Fades objects out (their dependent objects stay). An object that is not to be seen (hidden already, or never
    /// shown) is left alone; if nothing is to do, it takes no time.
    /// </summary>
    public void Hide(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items.Distinct())
        {
            if ((item.IsDrawn && item.Opacity > 0) || IsQueued(item))
            {
                actions.Add(new FadeAction(item, 0, duration ?? 400, easing ?? Easing.EaseOut));
            }
        }
        RunOrQueue(actions);
    }

    /// <summary>Fades out those of the objects that are visible or about to be drawn; the others are left alone.</summary>
    /// <summary>
    /// Shows or hides the labels of objects (fading in 400 ms). The label of an object that is not drawn yet changes at
    /// once: it appears (or not) when the object is drawn.
    /// </summary>
    public void ShowLabels(IEnumerable<GeoObject> items, bool show, double? duration = null, Easing? easing = null)
    {
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items)
        {
            if (!item.HasLabel)
            {
                throw new GeoException($"{item} has no label.");
            }
            if (item.IsDrawn)
            {
                // a label that is there already (or not there) is left alone
                if (item.LabelOpacity != (show ? 1 : 0) || (show && !item.LabelUsed))
                {
                    actions.Add(new LabelFadeAction(item, show ? 1 : 0, duration ?? 400, easing ?? Easing.EaseOut));
                }
            }
            else
            {
                // no primitive yet: a label shown later gets one then
                item.LabelOpacity = show ? 1 : 0;
                item.LabelUsed = show;
            }
        }
        RunOrQueue(actions);
    }

    /// <summary>
    /// Hides everything visible (<paramref name="items"/>: all objects, or a part), e.g. at the start of a step that
    /// begins a new picture; with <paramref name="labelsOnly"/> only the labels. Numbers are not drawn anyway.
    /// </summary>
    public void HideAll(IEnumerable<GeoObject>? items = null, bool labelsOnly = false, double? duration = null, Easing? easing = null)
    {
        RequireStep("hide all");
        List<GeoObject> candidates = (items ?? objects).Where(item => item is not GeoNumber).Distinct().ToList();
        if (labelsOnly)
        {
            List<GeoObject> labelled = candidates.Where(item => item.HasLabel && item.IsDrawn && item.LabelUsed && item.LabelOpacity > 0).ToList();
            if (labelled.Count > 0)
            {
                ShowLabels(labelled, false, duration, easing);
            }
            return;
        }
        HideVisible(candidates, duration, easing);
    }

    /// <summary>
    /// Moves the labels of objects to the shift <paramref name="shift"/> from their default place (units), e.g. after a
    /// movement that brought a label too close to something; a label not drawn yet takes it at once.
    /// </summary>
    /// <summary>
    /// Turns the labels of points round them to the direction <paramref name="direction"/> (degrees, counterclockwise from
    /// east), with <paramref name="gap"/> also to another space from the point (points); a label not shown yet just gets it.
    /// </summary>
    public void DirectLabels(IEnumerable<GeoObject> items, double direction, double? gap = null, double? duration = null, Easing? easing = null)
    {
        RequireStep("move label");
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items)
        {
            GeoPoint point = item as GeoPoint
                ?? throw new GeoException($"A direction is for the label of a point; the label of the {item.Kind} {item.Name} moves by a shift (move label {item.Name} to 0 0.3).");
            if (point.IsDrawn)
            {
                actions.Add(new LabelDirectionAction(point, direction, gap, duration ?? 400, easing ?? Easing.EaseInOut));
            }
            else
            {
                point.LabelDirection = direction;
                point.LabelGap = gap ?? point.LabelGap;
            }
        }
        RunOrQueue(actions);
    }

    public void ShiftLabels(IEnumerable<GeoObject> items, Vec shift, double? duration = null, Easing? easing = null)
    {
        RequireStep("move label");
        var actions = new List<TimelineAction>();
        foreach (GeoObject item in items)
        {
            if (!item.HasLabel && item is not GeoText)
            {
                throw new GeoException($"{item} has no label.");
            }
            if (item.IsDrawn)
            {
                actions.Add(new LabelShiftAction(item, shift, duration ?? 400, easing ?? Easing.EaseInOut));
            }
            else
            {
                item.LabelShift = shift;
            }
        }
        RunOrQueue(actions);
    }

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

    /// <summary>Moves a text at coordinates to <paramref name="target"/> (its centre); a text next to a point moves with the point.</summary>
    public void Move(GeoText text, Vec target, double? duration = null, Easing? easing = null) =>
        Move(OwnPositionOf(text), target, duration, easing);

    /// <summary>Moves a text at coordinates to where <paramref name="target"/> is at this moment.</summary>
    public void Move(GeoText text, GeoPoint target, double? duration = null, Easing? easing = null) =>
        Move(OwnPositionOf(text), target, duration, easing);

    private static GeoPoint OwnPositionOf(GeoText text) => text.OwnPosition
        ?? throw new GeoException(text.Above != null
            ? $"The text {text.Name} stands below the text {text.Above.Name} and moves with it: move that one (or shift this one with \"move label {text.Name} to …\")."
            : $"The text {text.Name} stands next to the point {text.Parents[0].Name} and moves with it: move the point (or place the text at coordinates).");

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
    /// Moves objects by the vector from <paramref name="from"/> to <paramref name="to"/> (as it is when the movement
    /// starts): the free points they are built from move, everything that depends on them follows.
    /// </summary>
    public void Translate(IEnumerable<GeoObject> items, GeoPoint from, GeoPoint to, double? duration = null, Easing? easing = null)
    {
        RequireStep("move");
        RunOrQueue([new TranslateAction(FreePointsOf(items), from, to, duration ?? 1000, easing ?? Easing.EaseInOut)]);
    }

    /// <summary>
    /// Turns objects around <paramref name="center"/> (where it is when the rotation starts) by <paramref name="degrees"/>
    /// (counterclockwise, negative: clockwise; also more than 360): the free points they are built from move on arcs,
    /// everything that depends on them follows.
    /// </summary>
    public void Rotate(IEnumerable<GeoObject> items, GeoPoint center, double degrees, double? duration = null, Easing? easing = null)
    {
        RequireStep("rotate");
        RunOrQueue([new RotateAction(FreePointsOf(items), center, degrees, duration ?? 1000, easing ?? Easing.EaseInOut)]);
    }

    /// <summary>
    /// Reflects objects in the line through <paramref name="a"/> and <paramref name="b"/> (where they are when it starts):
    /// the free points they are built from fold over onto their images, as if the figure turned over in space around the
    /// axis (<paramref name="straight"/>: straight across instead); everything that depends on them follows. Angles and the
    /// labels of segments keep their side: after the reflection an angle is measured clockwise.
    /// </summary>
    public void Reflect(IEnumerable<GeoObject> items, GeoPoint a, GeoPoint b, bool straight = false, double? duration = null, Easing? easing = null)
    {
        RequireStep("reflect");
        List<GeoPoint> points = FreePointsOf(items);
        // what is reflected as a whole changes its sense of rotation
        List<GeoObject> turned = objects.Where(o => o is GeoAngle or GeoStraight or GeoArc)
            .Where(o => FreePoints(o) is { Count: > 0 } own && own.All(points.Contains))
            .ToList();
        RunOrQueue([new ReflectAction(points, a, b, straight, turned, duration ?? 1000, easing ?? Easing.EaseInOut)]);
    }

    /// <summary>
    /// Reflects objects in the point <paramref name="center"/> (where it is when it starts): a rotation by 180°
    /// (<paramref name="straight"/>: the free points run straight through the centre instead).
    /// </summary>
    public void Reflect(IEnumerable<GeoObject> items, GeoPoint center, bool straight = false, double? duration = null, Easing? easing = null)
    {
        RequireStep("reflect");
        List<GeoPoint> points = FreePointsOf(items);
        TimelineAction action = straight
            ? new PointReflectAction(points, center, duration ?? 1000, easing ?? Easing.EaseInOut)
            : new RotateAction(points, center, 180, duration ?? 1000, easing ?? Easing.EaseInOut);
        RunOrQueue([action]);
    }

    // the free points an object is built from (a free point itself; a segment: its free end points; a midpoint: the free
    // points of its two points; …), each once
    private static List<GeoPoint> FreePoints(GeoObject item)
    {
        var found = new List<GeoPoint>();
        static void Collect(GeoObject item, List<GeoPoint> into)
        {
            if (item is GeoPoint { IsFree: true } point)
            {
                if (!into.Contains(point))
                {
                    into.Add(point);
                }
                return;
            }
            foreach (GeoObject parent in item.Parents)
            {
                Collect(parent, into);
            }
        }
        Collect(item, found);
        return found;
    }

    // the free points of objects, each once; an object without any cannot move
    private static List<GeoPoint> FreePointsOf(IEnumerable<GeoObject> items)
    {
        var found = new List<GeoPoint>();
        foreach (GeoObject item in items)
        {
            List<GeoPoint> own = FreePoints(item);
            if (own.Count == 0)
            {
                throw new GeoException($"{item} is not built from free points, so it cannot move.");
            }
            found.AddRange(own.Where(point => !found.Contains(point)));
        }
        return found;
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

    /// <summary>
    /// Highlights objects for a moment (two pulses: coloured, thicker or larger), or until <see cref="Unhighlight"/>
    /// (<paramref name="keep"/>); <paramref name="color"/>: the colour of the highlighting (default orange).
    /// </summary>
    public void Highlight(IEnumerable<GeoObject> items, bool keep = false, Color? color = null, double? duration = null, Easing? easing = null)
    {
        RequireStep("highlight");
        List<GeoObject> targets = HighlightTargets(items);
        RunOrQueue([new HighlightAction(targets, keep ? HighlightMode.Keep : HighlightMode.Pulse, color,
            duration ?? (keep ? 400 : Defaults.HighlightDuration), easing ?? (keep ? Easing.EaseInOut : Easing.Linear))]);
    }

    /// <summary>Ends the highlighting of objects that <see cref="Highlight"/> with keep highlighted.</summary>
    public void Unhighlight(IEnumerable<GeoObject> items, double? duration = null, Easing? easing = null)
    {
        RequireStep("unhighlight");
        RunOrQueue([new HighlightAction(HighlightTargets(items), HighlightMode.Off, null, duration ?? 400, easing ?? Easing.EaseInOut)]);
    }

    private static List<GeoObject> HighlightTargets(IEnumerable<GeoObject> items)
    {
        List<GeoObject> targets = items.Distinct().ToList();
        if (targets.FirstOrDefault(t => t is GeoNumber) is { } number)
        {
            throw new GeoException($"{number} is not drawn; highlight the text that shows it.");
        }
        return targets;
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

    /// <summary>
    /// Begins a new step with its description; <paramref name="mark"/> names it for the pages of a book (unique in the
    /// construction).
    /// </summary>
    public void Step(string caption, string? mark = null)
    {
        if (together != null)
        {
            throw new GeoException("A step cannot begin inside a \"together\" block.");
        }
        if (mark != null && (steps.Any(s => s.Mark == mark) || currentStep?.Mark == mark))
        {
            throw new GeoException($"Another step has the mark \"#{mark}\" already.");
        }
        CloseStep();
        currentStep = new AnimationStep(CheckText(caption), time, time) { Mark = mark };
    }

    /// <summary>
    /// Begins a step with text only: the player shows its explanation instead of the picture, which stays as it is for
    /// the next step. Nothing may be drawn or moved in it (a pause may set how long it lasts; without one it lasts a
    /// reading time after its length).
    /// </summary>
    public void TextStep(string caption, string? mark = null)
    {
        Step(caption, mark);
        currentStep = currentStep! with { TextOnly = true };
    }

    // in a text step: only pauses (and numbers, which are not drawn)
    private void RequireDrawing(string what)
    {
        if (currentStep?.TextOnly == true)
        {
            throw new GeoException($"A textstep shows only text: {what} belongs to a step (pause is allowed).");
        }
    }

    /// <summary>An explanation for the current step, shown below its description; a second one becomes a new paragraph.</summary>
    public void Info(string text)
    {
        if (currentStep == null)
        {
            throw new GeoException("\"info\" belongs to a step: it comes after the step line.");
        }
        CheckText(text);
        currentStep = currentStep with { Info = currentStep.Info == null ? text : currentStep.Info + "\n" + text };
    }

    private void RequireStep(string what)
    {
        if (currentStep == null)
        {
            throw new GeoException($"\"{what}\" is only possible inside a step.");
        }
    }

    private bool IsQueued(GeoObject item) => together?.Any(a => a is DrawAction && a.Objects.Contains(item)) ?? false;

    internal static double ReadingTime(string text) => 3000 + 250 * text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void CloseStep()
    {
        if (currentStep == null)
        {
            return;
        }
        if (time - currentStep.Start < 1e-6)
        {
            // a text step without a pause: a reading time (3 s and a quarter of a second for each word)
            Run([new PauseAction(currentStep.TextOnly ? ReadingTime(currentStep.Caption + " " + currentStep.Info) : EmptyStepDuration)]);
        }
        steps.Add(currentStep with { End = time });
    }

    private void RunOrQueue(List<TimelineAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }
        if (actions.Any(a => a is not PauseAction && !a.Objects.All(o => o is GeoNumber)))
        {
            RequireDrawing("drawing, showing, hiding or moving");
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
            RecordAll(Span.At(0));
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
                RecordAll(new Span(previous, now));
                previous = now;
            }
        }
        else
        {
            foreach (TimelineAction action in actions)
            {
                action.End(ctx, new Span(start, start + action.Duration, action.Easing));
            }
            RecordAll(Span.At(start + length));
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

    /// <summary>
    /// Whether the animation also describes every object over time, the hidden ones too (<see cref="AnimationData.Inspection"/>),
    /// for an editor; set it before the first object.
    /// </summary>
    public bool Inspect
    {
        get => inspector != null;
        set => inspector = value ? inspector ?? new Inspector() : null;
    }

    private void Record(GeoObject item, Span span) => inspector?.Record(item, view, span);

    private void RecordAll(Span span)
    {
        if (inspector != null)
        {
            foreach (GeoObject item in objects)
            {
                inspector.Record(item, view, span);
            }
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
            AxesSize = AxesSize,
            AxesColor = AxesColor,
            Description = Description,
            Keywords = keywords.ToList(),
            Inspection = inspector == null ? [] : inspector.Ghosts
                .Select(g => new InspectedObject(objectsByName.Where(n => n.Value == g.Item).Select(n => n.Key).ToList(), g.Ghost) { Label = g.Label })
                .Where(g => g.Names.Count > 0)
                .ToList(),
            // for an editor: the names of the object each primitive belongs to (a click in the preview finds it)
            Owners = inspector == null ? new Dictionary<int, IReadOnlyList<string>>() : scene.Primitives
                .Where(p => p.Owner != null)
                .Select(p => (p.Id, Names: (IReadOnlyList<string>)objectsByName.Where(n => n.Value == p.Owner).Select(n => n.Key).ToList()))
                .Where(o => o.Names.Count > 0)
                .ToDictionary(o => o.Id, o => o.Names),
        };
    }

    // all formulas of the title, the step descriptions and the texts in the picture, as SVG
    private Dictionary<string, Formula> RenderFormulas(List<AnimationStep> stepList)
    {
        IEnumerable<string> texts = stepList.Select(s => s.Caption)
            .Concat(stepList.Where(s => s.Info != null).Select(s => s.Info!))
            .Concat(scene.Primitives
                // in an editor also the ghosts' labels (of objects not shown yet)
                .Concat(inspector == null ? [] : inspector.Ghosts.SelectMany(g => new[] { g.Ghost, g.Label }).OfType<Primitive>())
                .Where(p => p.Tracks.ContainsKey("text"))
                .SelectMany(p => p.Tracks["text"].Select(key => (string)key.Value)));
        if (Title != null)
        {
            texts = texts.Prepend(Title);
        }
        if (Description != null)
        {
            texts = texts.Prepend(Description);
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

/// <summary>An object with all its names and its ghost (see <see cref="Construction.Inspect"/>).</summary>
internal sealed record InspectedObject(IReadOnlyList<string> Names, Primitive Ghost)
{
    /// <summary>The ghost of its label (a point's or line's), or null.</summary>
    public Primitive? Label { get; init; }
}

/// <summary>A word form that links to a page: to its start (step 0) or to the end of a step (1, 2, …).</summary>
public sealed record PageKeyword(string Form, int Step)
{
    /// <summary>The term the form belongs to, as the keyword index names it ("Punkt" for Punkte).</summary>
    public string Term { get; init; } = Form;

    /// <summary>The file of its keyword line (full path), or null if not known.</summary>
    public string? File { get; init; }

    /// <summary>The line of its keyword line in <see cref="File"/> (0: not known).</summary>
    public int Line { get; init; }
}

/// <summary>A step of the animation: its description and its time span in milliseconds.</summary>
public sealed record AnimationStep(string Caption, double Start, double End)
{
    /// <summary>An explanation shown below the description (paragraphs separated by "\n"), or null.</summary>
    public string? Info { get; init; }

    /// <summary>A step with text only ("textstep"): the player shows its explanation where the picture is.</summary>
    public bool TextOnly { get; init; }

    /// <summary>The step's mark ("step Gleiche Punkte #gleich" → "gleich"), by which a page refers to it, or null.</summary>
    public string? Mark { get; init; }
}
