namespace GeoAnim;

/// <summary>
/// Something that happens in the animation. Actions run one after the other; a "together" block starts several at the
/// same time. A block without movement is written as animated keys with easing; a block that moves points is computed
/// in small time steps, so that everything that depends on the moving points stays exact.
/// </summary>
internal abstract class TimelineAction(double duration, Easing easing)
{
    /// <summary>How long it takes in milliseconds (a "together" block with a duration of its own scales it).</summary>
    public double Duration { get; set; } = duration;

    public Easing Easing { get; } = easing;

    /// <summary>Whether the action changes positions (then the block is computed in small time steps).</summary>
    public virtual bool MovesPoints => false;

    /// <summary>The objects whose drawing the action changes.</summary>
    public abstract IEnumerable<GeoObject> Objects { get; }

    /// <summary>Sets the state at the start of the action and draws it at once.</summary>
    public abstract void Begin(RenderContext context, double time);

    /// <summary>Sets the state at the end of the action and animates to it during <paramref name="span"/>.</summary>
    public abstract void End(RenderContext context, Span span);

    /// <summary>Sets the state for the eased progress <paramref name="progress"/> (0 to 1); the caller draws.</summary>
    public abstract void Apply(double progress);
}

/// <summary>Draws an object for the first time: a segment grows, a circle is drawn like with a compass, a point appears.</summary>
internal sealed class DrawAction(GeoObject target, double duration, Easing easing) : TimelineAction(duration, easing)
{
    public override IEnumerable<GeoObject> Objects => [target];

    public override void Begin(RenderContext context, double time)
    {
        target.Reveal = 0;
        target.Opacity = 1;
        target.Render(context, Span.At(time));
    }

    public override void End(RenderContext context, Span span)
    {
        target.Reveal = 1;
        target.Render(context, span);
    }

    public override void Apply(double progress) => target.Reveal = progress;
}

/// <summary>Fades an object in or out (show after hide, hide).</summary>
internal sealed class FadeAction(GeoObject target, double toOpacity, double duration, Easing easing) : TimelineAction(duration, easing)
{
    private double fromOpacity;

    public override IEnumerable<GeoObject> Objects => [target];

    public override void Begin(RenderContext context, double time) => fromOpacity = target.Opacity;

    public override void End(RenderContext context, Span span)
    {
        target.Opacity = toOpacity;
        target.Render(context, span);
    }

    public override void Apply(double progress) => target.Opacity = fromOpacity + (toOpacity - fromOpacity) * progress;
}

/// <summary>Fades the label of a drawn object in or out ("show label", "hide label").</summary>
internal sealed class LabelFadeAction(GeoObject target, double toOpacity, double duration, Easing easing) : TimelineAction(duration, easing)
{
    private double fromOpacity;

    public override IEnumerable<GeoObject> Objects => [target];

    public override void Begin(RenderContext context, double time)
    {
        fromOpacity = target.LabelOpacity;
        if (!target.LabelUsed)
        {
            // the label's primitive appears (still invisible) at the start
            target.LabelUsed = true;
            target.Render(context, Span.At(time));
        }
    }

    public override void End(RenderContext context, Span span)
    {
        target.LabelOpacity = toOpacity;
        target.Render(context, span);
    }

    public override void Apply(double progress) => target.LabelOpacity = fromOpacity + (toOpacity - fromOpacity) * progress;
}

/// <summary>Moves a free point; everything that depends on it follows.</summary>
internal sealed class MoveAction(GeoPoint point, Vec target, double duration, Easing easing) : TimelineAction(duration, easing)
{
    private Vec from;

    public override bool MovesPoints => true;

    public override IEnumerable<GeoObject> Objects => [point];

    public override void Begin(RenderContext context, double time) => from = point.Position;

    public override void End(RenderContext context, Span span) => throw new InvalidOperationException("A move is always sampled.");

    public override void Apply(double progress) => point.MoveTo(Vec.Lerp(from, target, progress));
}

/// <summary>Moves a point along its circle (angle in degrees) or straight (parameter); dependent objects follow.</summary>
internal sealed class GlideAction(GeoPoint point, double target, double duration, Easing easing) : TimelineAction(duration, easing)
{
    private double from;

    public override bool MovesPoints => true;

    public override IEnumerable<GeoObject> Objects => [point];

    public override void Begin(RenderContext context, double time) => from = point.Parameter;

    public override void End(RenderContext context, Span span) => throw new InvalidOperationException("A move is always sampled.");

    public override void Apply(double progress) => point.MoveAlong(from + (target - from) * progress);
}

/// <summary>Moves free points by the vector from one point to another (taken at the start); dependent objects follow.</summary>
internal sealed class TranslateAction(IReadOnlyList<GeoPoint> points, GeoPoint from, GeoPoint to, double duration, Easing easing)
    : TimelineAction(duration, easing)
{
    private Vec[] starts = [];
    private Vec shift;

    public override bool MovesPoints => true;

    public override IEnumerable<GeoObject> Objects => points;

    public override void Begin(RenderContext context, double time)
    {
        if (!from.IsDefined || !to.IsDefined)
        {
            throw new GeoException($"The vector {from.Name}{to.Name} does not exist for the current positions.");
        }
        starts = points.Select(p => p.Position).ToArray();
        shift = to.Position - from.Position;
    }

    public override void End(RenderContext context, Span span) => throw new InvalidOperationException("A move is always sampled.");

    public override void Apply(double progress)
    {
        for (int i = 0; i < points.Count; i++)
        {
            points[i].MoveTo(starts[i] + progress * shift);
        }
    }
}

/// <summary>Turns free points around a centre (taken at the start) by an angle in degrees; dependent objects follow.</summary>
internal sealed class RotateAction(IReadOnlyList<GeoPoint> points, GeoPoint center, double degrees, double duration, Easing easing)
    : TimelineAction(duration, easing)
{
    private Vec[] starts = [];
    private Vec pivot;

    public override bool MovesPoints => true;

    public override IEnumerable<GeoObject> Objects => points;

    public override void Begin(RenderContext context, double time)
    {
        if (!center.IsDefined)
        {
            throw new GeoException($"The centre {center.Name} does not exist for the current positions.");
        }
        starts = points.Select(p => p.Position).ToArray();
        pivot = center.Position;
    }

    public override void End(RenderContext context, Span span) => throw new InvalidOperationException("A rotation is always sampled.");

    // on arcs around the centre: in between always turned by a part of the angle, never deformed
    public override void Apply(double progress)
    {
        double radians = progress * degrees * Math.PI / 180;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        for (int i = 0; i < points.Count; i++)
        {
            Vec d = starts[i] - pivot;
            points[i].MoveTo(pivot + new Vec(cos * d.X - sin * d.Y, sin * d.X + cos * d.Y));
        }
    }
}

/// <summary>Nothing happens for a while.</summary>
internal sealed class PauseAction(double duration) : TimelineAction(duration, Easing.Linear)
{
    public override IEnumerable<GeoObject> Objects => [];

    public override void Begin(RenderContext context, double time)
    {
    }

    public override void End(RenderContext context, Span span)
    {
    }

    public override void Apply(double progress)
    {
    }
}
