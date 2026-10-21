namespace GeoAnim.Script;

/// <summary>Turns a script (the text of a .geo file) into an animation.</summary>
public static class ScriptCompiler
{
    /// <summary>Compiles the script; throws <see cref="ScriptError"/> with line and column for any mistake.</summary>
    public static AnimationData Compile(string source) => Compile(ScriptSource.FromText(source));

    /// <summary>Compiles a script file (with the files it includes).</summary>
    public static AnimationData CompileFile(string path) => Compile(ScriptSource.FromFile(path));

    public static AnimationData Compile(ScriptSource source)
    {
        Construction construction = CompileConstruction(source);
        try
        {
            return construction.Build();
        }
        catch (GeoException e)
        {
            // building renders the formulas; their texts were checked before
            throw new ScriptError(1, 1, e.Message);
        }
    }

    /// <summary>Compiles the script into a construction that is not yet built.</summary>
    public static Construction CompileConstruction(string source) => CompileConstruction(ScriptSource.FromText(source));

    public static Construction CompileConstruction(ScriptSource source)
    {
        try
        {
            List<Statement> statements = Parser.Parse(source.Text);
            var construction = new Construction();
            var executor = new Executor(construction);
            foreach (Statement statement in statements)
            {
                executor.Execute(statement);
            }
            return construction;
        }
        catch (ScriptError e)
        {
            throw source.Locate(e);
        }
    }

    private sealed class Executor(Construction c)
    {
        private bool hadObject;

        // "default kind… options": options that come first for every object of the kind ("" for all kinds)
        private readonly Dictionary<string, List<OptionValue>> defaults = new(StringComparer.Ordinal);

        public void Execute(Statement statement)
        {
            try
            {
                Run(statement);
            }
            catch (GeoException e)
            {
                throw new ScriptError(statement.Line, statement.Column, e.Message);
            }
        }

        private void Run(Statement statement)
        {
            switch (statement)
            {
                case TitleStatement s:
                    RequireSetup(s, "title");
                    c.Title = s.Title;
                    break;
                case ViewStatement s:
                    if (hadObject)
                    {
                        throw new ScriptError(s.Line, s.Column, "\"view\" must come before the first object.");
                    }
                    c.View = s.View;
                    break;
                case BackgroundStatement s:
                    RequireSetup(s, "background");
                    c.Background = s.Color;
                    break;
                case GridStatement s:
                    RequireSetup(s, "grid");
                    c.GridStep = s.Step;
                    break;
                case AxesStatement s:
                    RequireSetup(s, "axes");
                    c.Axes = true;
                    break;
                case DefaultStatement s:
                    foreach (string kind in s.Kinds.Count == 0 ? [""] : s.Kinds)
                    {
                        if (!defaults.TryGetValue(kind, out List<OptionValue>? list))
                        {
                            defaults[kind] = list = new List<OptionValue>();
                        }
                        // like a var: what does not fit an object is left out
                        list.AddRange(s.Options.Values.Select(v => v with { Var = v.Var ?? "default" }));
                    }
                    break;
                case SpeedStatement s:
                    RequireSetup(s, "speed");
                    c.Speed = s.Speed;
                    break;
                case StepStatement s:
                    c.Step(s.Caption);
                    break;
                case DefineStatement s:
                    Define(s);
                    break;
                case ShowStatement s:
                    {
                        List<GeoObject> targets = s.Targets.Select(Resolve).ToList();
                        Options options = ToOptions(s.Options, s.Show ? "show" : "hide", ["duration", "easing"]);
                        if (s.Show)
                        {
                            c.Show(targets, options.Duration, options.Easing);
                        }
                        else
                        {
                            c.Hide(targets, options.Duration, options.Easing);
                        }
                        break;
                    }
                case MoveStatement s:
                    {
                        GeoPoint point = ResolvePoint(s.Point);
                        Options options = ToOptions(s.Options, "move", ["duration", "easing"]);
                        if (s.Target != null)
                        {
                            c.Move(point, s.Target.Value, options.Duration, options.Easing);
                        }
                        else
                        {
                            c.Move(point, s.Parameter!.Value, options.Duration, options.Easing);
                        }
                        break;
                    }
                case PauseStatement s:
                    c.Pause(s.Duration);
                    break;
                case TogetherStatement s:
                    c.Together(() =>
                    {
                        foreach (Statement inner in s.Body)
                        {
                            Execute(inner);
                        }
                    });
                    break;
                default:
                    throw new InvalidOperationException("Unknown statement " + statement.GetType().Name);
            }
        }

        private void RequireSetup(Statement s, string what)
        {
            if (c.InSteps)
            {
                throw new ScriptError(s.Line, s.Column, $"\"{what}\" must come before the first step.");
            }
        }

        private void Define(DefineStatement s)
        {
            foreach (NameRef name in s.Names)
            {
                if (c.Objects.ContainsKey(name.Name))
                {
                    throw new ScriptError(name.Line, name.Column, $"The name {name.Name} is already used.");
                }
            }
            string name0 = s.Names[0].Name;
            hadObject = true;
            switch (s.Definition)
            {
                case CoordinatesDefinition d:
                    c.Point(name0, d.Position.X, d.Position.Y, PointOptions(s));
                    break;
                case OnDefinition d:
                    {
                        GeoObject path = Resolve(d.Path);
                        if (path is GeoCircle circle)
                        {
                            c.PointOn(name0, circle, d.Parameter, PointOptions(s));
                        }
                        else if (path is GeoStraight straight)
                        {
                            c.PointOn(name0, straight, d.Parameter, PointOptions(s));
                        }
                        else
                        {
                            throw new ScriptError(d.Path.Line, d.Path.Column, $"A point can lie on a circle, segment, line or ray, not on the {path.Kind} {path.Name}.");
                        }
                        break;
                    }
                case MidpointDefinition d:
                    c.Midpoint(name0, ResolvePoint(d.A), ResolvePoint(d.B), PointOptions(s));
                    break;
                case IntersectDefinition d:
                    c.Intersect(s.Names.Select(n => n.Name).ToList(), Resolve(d.A), Resolve(d.B), PointOptions(s));
                    break;
                case TwoPointsDefinition d:
                    {
                        GeoPoint a = ResolvePoint(d.A);
                        GeoPoint b = ResolvePoint(d.B);
                        Options options = ToOptions(s.Options, s.Kind, ["color", "width", "draw"]);
                        if (s.Kind == "segment")
                        {
                            c.Segment(name0, a, b, options);
                        }
                        else if (s.Kind == "ray")
                        {
                            c.Ray(name0, a, b, options);
                        }
                        else
                        {
                            c.Line(name0, a, b, options);
                        }
                        break;
                    }
                case CircleThroughDefinition d:
                    c.Circle(name0, ResolvePoint(d.Center), ResolvePoint(d.Through), ToOptions(s.Options, "circle", ["color", "width", "draw"]));
                    break;
                case CircleRadiusDefinition d:
                    c.Circle(name0, ResolvePoint(d.Center), d.Radius, ToOptions(s.Options, "circle", ["color", "width", "draw"]));
                    break;
                case PolygonDefinition d:
                    c.Polygon(name0, d.Vertices.Select(ResolvePoint).ToList(),
                        ToOptions(s.Options, "polygon", ["color", "width", "fill", "opacity", "draw"]));
                    break;
                case TextDefinition d:
                    {
                        Options options = ToOptions(s.Options, "text", ["color", "size", "offset", "draw"]);
                        if (d.Anchor != null)
                        {
                            c.Text(name0, d.Content, ResolvePoint(d.Anchor), options);
                        }
                        else
                        {
                            c.Text(name0, d.Content, d.Position!.Value.X, d.Position.Value.Y, options);
                        }
                        break;
                    }
                default:
                    throw new InvalidOperationException("Unknown definition");
            }
        }

        private Options PointOptions(DefineStatement s) => ToOptions(s.Options, "point", ["color", "size", "labelsize", "label", "offset", "draw"]);

        /// <summary>
        /// The options of a statement after the defaults for all kinds and for <paramref name="what"/>;
        /// <paramref name="allowed"/> names groups ("draw" = duration, easing, hidden).
        /// </summary>
        private Options ToOptions(OptionList list, string what, string[] allowed)
        {
            var options = new Options();
            IEnumerable<OptionValue> values = defaults.GetValueOrDefault("", [])
                .Concat(defaults.GetValueOrDefault(what, []))
                .Concat(list.Values);
            foreach (OptionValue option in values)
            {
                string group = option.Name switch
                {
                    "duration" => allowed.Contains("draw") ? "draw" : "duration",
                    "hidden" => "draw",
                    "linear" or "ease" or "easein" or "easeout" or "easeinout" => allowed.Contains("draw") ? "draw" : "easing",
                    "nolabel" => "label",
                    _ => option.Name,
                };
                if (!allowed.Contains(group))
                {
                    if (option.Var != null)
                    {
                        // a var serves objects of all kinds: what does not fit is left out
                        continue;
                    }
                    throw new ScriptError(option.Line, option.Column, $"The option \"{option.Name}\" is not possible for {what}.");
                }
                options = option.Name switch
                {
                    "duration" => options with { Duration = (double)option.Value! },
                    "color" => options with { Color = (Color)option.Value! },
                    "width" => options with { Width = (double)option.Value! },
                    "fill" => options with { Fill = (Color)option.Value! },
                    "opacity" => options with { FillOpacity = (double)option.Value! },
                    "size" => options with { Size = (double)option.Value! },
                    "labelsize" => options with { LabelSize = (double)option.Value! },
                    "label" => options with { Label = (string)option.Value! },
                    "nolabel" => options with { NoLabel = true },
                    "offset" => options with { Offset = (Vec)option.Value! },
                    "hidden" => options with { Hidden = true },
                    "linear" => options with { Easing = Easing.Linear },
                    "easein" => options with { Easing = Easing.EaseIn },
                    "easeout" => options with { Easing = Easing.EaseOut },
                    _ => options with { Easing = Easing.EaseInOut },
                };
            }
            return options;
        }

        private GeoObject Resolve(NameRef name) =>
            c.Objects.TryGetValue(name.Name, out GeoObject? item)
                ? item
                : throw new ScriptError(name.Line, name.Column, $"Unknown object \"{name.Name}\".");

        private GeoPoint ResolvePoint(NameRef name) =>
            Resolve(name) as GeoPoint
                ?? throw new ScriptError(name.Line, name.Column, $"{name.Name} is a {Resolve(name).Kind}, not a point.");
    }
}
