namespace GeoAnim;

/// <summary>
/// For an editor ("geoanim daemon"): every object over time, also the hidden ones, as a "ghost" (its shape, whether it
/// is shown or not) with a short description of its current values (position, colour, shown or hidden). The VS Code
/// preview shows a ghost while the cursor or the mouse is on the object's name; the tooltip shows the description.
/// </summary>
internal sealed class Inspector
{
    private readonly Dictionary<GeoObject, Primitive> ghosts = new();
    private readonly List<(GeoObject Item, Primitive Ghost)> order = new();

    public IReadOnlyList<(GeoObject Item, Primitive Ghost)> Ghosts => order;

    /// <summary>Writes the object's state into its ghost, changing during <paramref name="span"/>.</summary>
    public void Record(GeoObject item, ViewWindow view, Span span)
    {
        if (!ghosts.TryGetValue(item, out Primitive? ghost))
        {
            ghost = new Primitive(order.Count + 1, TypeOf(item), 0);
            ghosts[item] = ghost;
            order.Add((item, ghost));
        }
        bool defined = item.IsDefined;
        switch (item)
        {
            case GeoPoint point:
                if (defined)
                {
                    ghost.Animate("x", span, point.Position.X);
                    ghost.Animate("y", span, point.Position.Y);
                }
                break;
            case GeoStraight straight:
                var part = defined ? straight.VisiblePart(view) : null;
                defined = part != null;
                if (part is ((Vec start, Vec end)))
                {
                    ghost.Animate("x1", span, start.X);
                    ghost.Animate("y1", span, start.Y);
                    ghost.Animate("x2", span, end.X);
                    ghost.Animate("y2", span, end.Y);
                }
                break;
            case GeoCircle circle:
                if (defined)
                {
                    ghost.Animate("cx", span, circle.Center.Position.X);
                    ghost.Animate("cy", span, circle.Center.Position.Y);
                    ghost.Animate("r", span, circle.Radius);
                }
                break;
            case GeoArc arc:
                if (defined)
                {
                    ghost.Animate("cx", span, arc.Circle.Center.Position.X);
                    ghost.Animate("cy", span, arc.Circle.Center.Position.Y);
                    ghost.Animate("r", span, arc.Circle.Radius);
                    ghost.Animate("start", span, arc.Start);
                    ghost.Animate("sweep", span, arc.Degrees);
                }
                break;
            case GeoPolygon polygon:
                if (defined)
                {
                    ghost.Animate("points", span, polygon.Vertices.SelectMany(v => new[] { v.Position.X, v.Position.Y }).ToArray());
                }
                break;
            case GeoAngle angle:
                if (defined)
                {
                    ghost.Animate("x", span, angle.Vertex.Position.X);
                    ghost.Animate("y", span, angle.Vertex.Position.Y);
                    ghost.Animate("start", span, angle.Start);
                    ghost.Animate("sweep", span, angle.Degrees);
                }
                ghost.Animate("radius", span, angle.Options.Radius ?? 0);
                break;
        }
        ghost.Animate("opacity", span, defined ? 1.0 : 0.0);
        ghost.Animate("info", span, Describe(item));
    }

    // the player draws a ghost like an object of this type (numbers and texts have none)
    private static string TypeOf(GeoObject item) => item switch
    {
        GeoPoint => "dot",
        GeoStraight => "line",
        GeoCircle => "arc",
        GeoArc => "arc",
        GeoPolygon => "polygon",
        GeoAngle => "angle",
        _ => "info",
    };

    /// <summary>The object's current values in a line or two of Markdown, as the tooltip shows them.</summary>
    internal static string Describe(GeoObject item)
    {
        string values = item switch
        {
            GeoPoint point => Coordinates(point),
            GeoStraight { StraightKind: StraightKind.Segment } s => $"{Position(s.A)} – {Position(s.B)}, Länge {F(s.Direction.Length)}",
            GeoStraight s => $"durch {Position(s.A)} und {Position(s.B)}",
            GeoCircle circle => $"Mittelpunkt {Position(circle.Center)}, Radius {F(circle.Radius)}",
            GeoAngle angle => $"Scheitel {Position(angle.Vertex)}, {F(angle.Degrees)}°",
            GeoArc arc => $"von {Position(arc.From)} bis {Position(arc.To)}, {F(arc.Degrees)}°, " + (arc.IsSector ? $"Fläche {F(arc.Area)}" : $"Länge {F(arc.Length)}"),
            GeoPolygon polygon => "Ecken " + string.Join(", ", polygon.Vertices.Select(Position)),
            GeoNumber number => "Wert " + F(number.Value),
            GeoText text => "„" + text.Content + "“",
            _ => "",
        };
        if (!item.IsDefined)
        {
            values = item is GeoPoint or GeoStraight or GeoCircle or GeoArc or GeoAngle or GeoPolygon ? "existiert hier nicht (zuletzt " + values + ")" : "existiert hier nicht";
        }
        var notes = new List<string>();
        if (item is not GeoNumber)
        {
            notes.Add(!item.IsDrawn ? "nicht gezeichnet (hidden)"
                : item.Opacity <= 1e-3 ? "ausgeblendet"
                : item.Opacity < 1 - 1e-3 ? "halb eingeblendet"
                : "sichtbar");
            Color color = item.BaseColorOr(item is GeoPoint ? Defaults.PointColor : Defaults.LineColor);
            string? name = Color.Names.Where(n => n.Value == color).Select(n => n.Key).FirstOrDefault();
            notes.Add("Farbe " + (name ?? color.ToHex()) + (item.Options.Color == null ? " (Standard)" : ""));
        }
        return notes.Count == 0 ? values : values + "  \n" + string.Join(" · ", notes);
    }

    private static string Position(GeoPoint point) => point.Name + " " + Coordinates(point);

    private static string Coordinates(GeoPoint point) => $"({F(point.Position.X)} | {F(point.Position.Y)})";

    private static string F(double value) => NumberText.Format(value, 2).TrimEnd('0').TrimEnd(',');
}
