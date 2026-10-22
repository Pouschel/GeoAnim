namespace GeoAnim.Script;

/// <summary>Turns a script (the text of a .geo file) into an animation.</summary>
public static class ScriptCompiler
{
    /// <summary>Compiles the script; throws <see cref="ScriptError"/> with line and column for any mistake.</summary>
    public static AnimationData Compile(string source) => Compile(ScriptSource.FromText(source));

    /// <summary>Compiles a script file (with the files it includes and uses).</summary>
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
            var executor = new Executor(construction, source, source.MainFile == null ? [] : [source.MainFile], new UseCounter(), null);
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

    private sealed class UseCounter
    {
        public int Next;
    }

    /// <summary>
    /// A used file while it runs: its inputs (bound to the caller's objects), its outputs (with the caller's names and
    /// options) and how it appears. Its other objects get names of their own ("Prop1#1.k1") that the caller cannot see.
    /// </summary>
    private sealed class Module(string prefix, UseMode mode, Dictionary<string, GeoObject> inputs, Dictionary<string, (string Name, Options Options)> outputs)
    {
        public string Prefix { get; } = prefix;

        public UseMode Mode { get; } = mode;

        public Dictionary<string, GeoObject> Inputs { get; } = inputs;

        public Dictionary<string, (string Name, Options Options)> Outputs { get; } = outputs;

        /// <summary>Objects of the file that are not results (faded out at the end with draw and steps).</summary>
        public List<GeoObject> Internals { get; } = new();
    }

    private sealed class Executor(Construction c, ScriptSource source, List<string> files, UseCounter counter, Module? module)
    {
        private bool hadObject;

        // "default kind… options": options that come first for every object of the kind ("" for all kinds)
        private readonly Dictionary<string, List<OptionValue>> defaults = new(StringComparer.Ordinal);

        private bool OnlyResult => module?.Mode == UseMode.Result;

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
            // a used file: its settings and steps belong to it running alone
            if (module != null && statement is TitleStatement or DescriptionStatement or ViewStatement or BackgroundStatement or GridStatement or AxesStatement or SpeedStatement)
            {
                return;
            }
            switch (statement)
            {
                case TitleStatement s:
                    RequireSetup(s, "title");
                    c.Title = s.Title;
                    break;
                case DescriptionStatement s:
                    RequireSetup(s, "description");
                    c.Description = s.Description;
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
                    if (module == null || module.Mode == UseMode.Steps)
                    {
                        c.Step(s.Caption);
                    }
                    break;
                case InputStatement s:
                    // when the file runs alone, an input is an ordinary object; when it is used, it is bound already
                    if (module == null)
                    {
                        Define(s.Define);
                    }
                    break;
                case OutputStatement s:
                    if (s.Define != null)
                    {
                        Define(s.Define);
                    }
                    // the results must be constructed by now: a used file ends with its last output line
                    foreach (NameRef name in s.Names)
                    {
                        if (!(module != null && module.Inputs.ContainsKey(name.Name)) && !c.Objects.ContainsKey(Global(name.Name)))
                        {
                            throw new ScriptError(name.Line, name.Column,
                                $"{name.Name} is not defined yet: \"output\" comes after the construction of its objects (a used file ends with its last output line).");
                        }
                    }
                    break;
                case DefineStatement s:
                    Define(s);
                    break;
                case ShowStatement s:
                    {
                        if (OnlyResult)
                        {
                            break;
                        }
                        List<GeoObject> targets = s.Targets.Select(t => NotInput(t, s.Show ? "show" : "hide")).ToList();
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
                        if (OnlyResult)
                        {
                            break;
                        }
                        GeoPoint point = NotInput(s.Point, "move") as GeoPoint
                            ?? throw new ScriptError(s.Point.Line, s.Point.Column, $"{s.Point.Name} is a {Resolve(s.Point).Kind}, not a point.");
                        Options options = ToOptions(s.Options, "move", ["duration", "easing"]);
                        if (s.TargetPoint != null)
                        {
                            c.Move(point, ResolvePoint(s.TargetPoint), options.Duration, options.Easing);
                        }
                        else if (s.Target != null)
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
                    // a used file drawn in one step goes without its pauses
                    if (module == null || module.Mode == UseMode.Steps)
                    {
                        c.Pause(s.Duration);
                    }
                    break;
                case TogetherStatement s:
                    if (OnlyResult)
                    {
                        // nothing is animated: just the definitions
                        foreach (Statement inner in s.Body)
                        {
                            Execute(inner);
                        }
                        break;
                    }
                    c.Together(
                        () =>
                        {
                            foreach (Statement inner in s.Body)
                            {
                                Execute(inner);
                            }
                        },
                        ToOptions(s.Options, "together", ["duration"]).Duration);
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

        // ------------------------------------------------------------------ names

        /// <summary>The name in the construction: the caller's name for an output, a name of its own for the rest of a used file.</summary>
        private string Global(string name) =>
            module == null ? name : module.Outputs.TryGetValue(name, out var output) ? output.Name : module.Prefix + "." + name;

        private GeoObject Resolve(NameRef name)
        {
            if (module != null && module.Inputs.TryGetValue(name.Name, out GeoObject? input))
            {
                return input;
            }
            return c.Objects.TryGetValue(Global(name.Name), out GeoObject? item)
                ? item
                : throw new ScriptError(name.Line, name.Column, $"Unknown object \"{name.Name}\".");
        }

        private GeoPoint ResolvePoint(NameRef name) =>
            Resolve(name) as GeoPoint
                ?? throw new ScriptError(name.Line, name.Column, $"{name.Name} is a {Resolve(name).Kind}, not a point.");

        // a used file must leave the caller's objects alone
        private GeoObject NotInput(NameRef name, string what) =>
            module != null && module.Inputs.ContainsKey(name.Name)
                ? throw new ScriptError(name.Line, name.Column, $"A used file cannot {what} its input {name.Name}.")
                : Resolve(name);

        // ------------------------------------------------------------------ definitions

        private static string[] AllowedOptions(string kind) => kind switch
        {
            "point" => ["color", "size", "labelsize", "label", "offset", "draw"],
            "polygon" => ["color", "width", "fill", "opacity", "draw"],
            "text" => ["color", "size", "offset", "draw"],
            "segment" or "line" or "ray" => ["color", "width", "label", "labelsize", "labelpos", "offset", "draw"],
            _ => ["color", "width", "draw"],
        };

        /// <summary>The options of an object of this file: an output gets the caller's options on top; with "use … " (only the result) the rest is hidden.</summary>
        private Options FinalOptions(string name, Options own, string kind)
        {
            if (module == null)
            {
                return own;
            }
            if (module.Outputs.TryGetValue(name, out var output))
            {
                Options caller = output.Options;
                return new Options
                {
                    Color = caller.Color ?? own.Color,
                    Width = caller.Width ?? own.Width,
                    Fill = caller.Fill ?? own.Fill,
                    FillOpacity = caller.FillOpacity ?? own.FillOpacity,
                    Size = caller.Size ?? own.Size,
                    LabelSize = caller.LabelSize ?? own.LabelSize,
                    LabelPosition = caller.LabelPosition ?? own.LabelPosition,
                    // a point's label follows the caller's name
                    Label = kind == "point" ? caller.Label : caller.Label ?? own.Label,
                    NoLabel = caller.NoLabel || (own.NoLabel && caller.Label == null),
                    Offset = caller.Offset ?? own.Offset,
                    Hidden = caller.Hidden || (own.Hidden && module.Mode != UseMode.Result),
                    Duration = caller.Duration ?? own.Duration,
                    Easing = caller.Easing ?? own.Easing,
                };
            }
            // a point's label shows its name in the file, not the name of its own ("Prop1#1.C")
            if (kind == "point")
            {
                own = own with { Label = own.Label ?? "$" + name + "$" };
            }
            return module.Mode == UseMode.Result ? own with { Hidden = true } : own;
        }

        private void Define(DefineStatement s)
        {
            foreach (NameRef name in s.Names.Where(n => n.Name != "_"))
            {
                if (c.Objects.ContainsKey(Global(name.Name)) || (module != null && module.Inputs.ContainsKey(name.Name)))
                {
                    throw new ScriptError(name.Line, name.Column, $"The name {name.Name} is already used.");
                }
            }
            hadObject = true;
            if (s.Definition is UseDefinition use)
            {
                Use(s, use);
                return;
            }
            string local0 = s.Names[0].Name;
            string name0 = Global(local0);
            Options options = FinalOptions(local0, ToOptions(s.Options, s.Kind, AllowedOptions(s.Kind)), s.Kind);
            GeoObject created;
            switch (s.Definition)
            {
                case CoordinatesDefinition d:
                    created = c.Point(name0, d.Position.X, d.Position.Y, options);
                    break;
                case OnDefinition d:
                    {
                        GeoObject path = Resolve(d.Path);
                        created = path switch
                        {
                            GeoCircle circle => c.PointOn(name0, circle, d.Parameter, options),
                            GeoStraight straight => c.PointOn(name0, straight, d.Parameter, options),
                            _ => throw new ScriptError(d.Path.Line, d.Path.Column, $"A point can lie on a circle, segment, line or ray, not on the {path.Kind} {path.Name}."),
                        };
                        break;
                    }
                case CopyDefinition d:
                    {
                        GeoPoint original = ResolvePoint(d.Source);
                        if (!original.IsDefined)
                        {
                            throw new ScriptError(d.Source.Line, d.Source.Column, $"{d.Source.Name} does not exist for the current positions.");
                        }
                        // a free point: it can move without changing the construction
                        created = c.Point(name0, original.Position.X, original.Position.Y, options);
                        break;
                    }
                case MidpointDefinition d:
                    created = c.Midpoint(name0, ResolvePoint(d.A), ResolvePoint(d.B), options);
                    break;
                case IntersectDefinition d:
                    {
                        // each point with its own options (one may be a result, the other a hidden helper)
                        Options own = ToOptions(s.Options, "point", AllowedOptions("point"));
                        IReadOnlyList<GeoPoint> points = c.Intersect(
                            s.Names.Select(n => Global(n.Name)).ToList(), Resolve(d.A), Resolve(d.B),
                            s.Names.Select(n => FinalOptions(n.Name, own, "point")).ToList());
                        foreach ((NameRef name, GeoPoint point) in s.Names.Zip(points))
                        {
                            Remember(name.Name, point);
                        }
                        return;
                    }
                case TwoPointsDefinition d:
                    {
                        GeoPoint a = ResolvePoint(d.A);
                        GeoPoint b = ResolvePoint(d.B);
                        created = s.Kind switch
                        {
                            "segment" => c.Segment(name0, a, b, options),
                            "ray" => c.Ray(name0, a, b, options),
                            _ => c.Line(name0, a, b, options),
                        };
                        break;
                    }
                case CircleThroughDefinition d:
                    created = c.Circle(name0, ResolvePoint(d.Center), ResolvePoint(d.Through), options);
                    break;
                case CircleRadiusDefinition d:
                    created = c.Circle(name0, ResolvePoint(d.Center), d.Radius, options);
                    break;
                case PolygonDefinition d:
                    created = c.Polygon(name0, d.Vertices.Select(ResolvePoint).ToList(), options);
                    break;
                case TextDefinition d:
                    created = d.Anchor != null
                        ? c.Text(name0, d.Content, ResolvePoint(d.Anchor), options)
                        : c.Text(name0, d.Content, d.Position!.Value.X, d.Position.Value.Y, options);
                    break;
                default:
                    throw new InvalidOperationException("Unknown definition");
            }
            Remember(local0, created);
        }

        private void Remember(string local, GeoObject item)
        {
            if (module != null && !module.Outputs.ContainsKey(local))
            {
                module.Internals.Add(item);
            }
        }

        // ------------------------------------------------------------------ use

        /// <summary>"point D = use Prop1.geo A B": runs the file with A and B as its inputs; its output becomes D.</summary>
        private void Use(DefineStatement s, UseDefinition d)
        {
            string path = Path.GetFullPath(Path.Combine(source.DirectoryOf(s.Line), d.Path));
            ScriptError AtPath(string message) => new(d.PathLine, d.PathColumn, message);
            if (!File.Exists(path))
            {
                throw AtPath($"File not found: {d.Path} ({path}).");
            }
            if (files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                throw AtPath($"{d.Path} is already being used (circle: {string.Join(" → ", files.Append(path).Select(Path.GetFileName))}).");
            }
            ScriptSource moduleSource;
            List<Statement> statements;
            try
            {
                moduleSource = ScriptSource.FromFile(path);
                statements = Parser.Parse(moduleSource.Text);
            }
            catch (ScriptError e)
            {
                throw InModule(d, null, e);
            }

            // inputs and outputs in their order, also inside "together" blocks
            List<DefineStatement> inputs = AllStatements(statements).OfType<InputStatement>().Select(i => i.Define).ToList();
            List<NameRef> outputs = AllStatements(statements).OfType<OutputStatement>().SelectMany(o => o.Names).ToList();
            if (outputs.Count == 0)
            {
                throw AtPath($"{d.Path} has no \"output\" line, so it gives no results.");
            }
            // which outputs the names take: with a kind ("point D = use …") those of that kind, without ("D s = use …") all,
            // in their order; "_" skips one, names may be missing at the end
            Dictionary<string, string> kinds = OutputKinds(statements);
            List<NameRef> offered = s.Kind == ""
                ? outputs
                : outputs.Where(o => KindOf(kinds, o.Name) is var kind && (kind == s.Kind || kind == "object")).ToList();
            string described = string.Join(", ", outputs.Select(o => $"{KindOf(kinds, o.Name)} {o.Name}"));
            if (offered.Count == 0)
            {
                throw new ScriptError(s.Line, s.Column, $"{d.Path} has no {s.Kind} among its results ({described}).");
            }
            if (s.Names.Count > offered.Count)
            {
                throw new ScriptError(s.Names[offered.Count].Line, s.Names[offered.Count].Column,
                    $"{d.Path} gives only {Count(offered.Count, s.Kind == "" ? "result" : s.Kind)} ({described}).");
            }
            List<(NameRef Output, NameRef Name)> taken = offered.Zip(s.Names).Where(p => p.Second.Name != "_").ToList();
            if (d.Arguments.Count != inputs.Count)
            {
                throw AtPath($"{d.Path} needs {Count(inputs.Count, "input")} ({string.Join(" ", inputs.Select(i => i.Kind + " " + i.Names[0].Name))}), not {d.Arguments.Count}.");
            }

            // inputs: the caller's objects, of the right kind
            var bound = new Dictionary<string, GeoObject>(StringComparer.Ordinal);
            for (int i = 0; i < inputs.Count; i++)
            {
                GeoObject argument = Resolve(d.Arguments[i]);
                if (argument.Kind != inputs[i].Kind)
                {
                    throw new ScriptError(d.Arguments[i].Line, d.Arguments[i].Column,
                        $"The input {inputs[i].Names[0].Name} of {d.Path} is a {inputs[i].Kind}; {argument.Name} is a {argument.Kind}.");
                }
                bound[inputs[i].Names[0].Name] = argument;
            }

            // outputs: the caller's names and options (inside a used file, as that file's names and options); without a
            // kind, options that do not fit a result are left out for it
            var results = new Dictionary<string, (string Name, Options Options)>(StringComparer.Ordinal);
            foreach ((NameRef output, NameRef name) in taken)
            {
                string kind = KindOf(kinds, output.Name) is "object" && s.Kind != "" ? s.Kind : KindOf(kinds, output.Name);
                OptionList list = s.Kind == "" ? new OptionList(s.Options.Values.Select(v => v with { Var = v.Var ?? "use" }).ToList()) : s.Options;
                results[output.Name] = (Global(name.Name), FinalOptions(name.Name, ToOptions(list, kind, AllowedOptions(kind)), kind));
            }

            // within "use … " (only the result) everything used inside shows only its result, too
            UseMode mode = OnlyResult ? UseMode.Result : d.Mode;
            var inner = new Module($"{Path.GetFileNameWithoutExtension(path)}#{++counter.Next}", mode, bound, results);
            var executor = new Executor(c, moduleSource, [.. files, path], counter, inner);
            // the file ends with its last output line (what follows, e.g. a proof, is for running it alone)
            int last = statements.FindLastIndex(s => AllStatements([s]).Any(x => x is OutputStatement));
            foreach (Statement statement in statements.Take(last + 1))
            {
                try
                {
                    executor.Execute(statement);
                }
                catch (ScriptError e)
                {
                    throw InModule(d, moduleSource, e, inner.Prefix);
                }
            }

            foreach ((NameRef output, NameRef name) in taken)
            {
                // an input given back: the caller's own object under the new name
                if (bound.TryGetValue(output.Name, out GeoObject? input) && !c.Objects.ContainsKey(results[output.Name].Name))
                {
                    c.Alias(results[output.Name].Name, input);
                }
                if (!c.Objects.TryGetValue(results[output.Name].Name, out GeoObject? result))
                {
                    throw AtPath($"{d.Path} does not define its output {output.Name}.");
                }
                if (s.Kind != "" && result.Kind != s.Kind)
                {
                    throw new ScriptError(name.Line, name.Column, $"The output {output.Name} of {d.Path} is a {result.Kind}, not a {s.Kind}.");
                }
                Remember(name.Name, result);
            }
            if (mode != UseMode.Result)
            {
                // the helpers of the construction go, the results stay
                c.HideVisible(inner.Internals);
            }
            // a later "hide" of an internal object of the used file is not possible: its names are its own
        }

        /// <summary>All statements in their order, those inside "together" blocks included.</summary>
        private static IEnumerable<Statement> AllStatements(IEnumerable<Statement> statements)
        {
            foreach (Statement statement in statements)
            {
                yield return statement;
                if (statement is TogetherStatement together)
                {
                    foreach (Statement inner in AllStatements(together.Body))
                    {
                        yield return inner;
                    }
                }
            }
        }

        /// <summary>The kinds of the objects a file defines (looked up before it runs): name → "point", "segment", …</summary>
        private static Dictionary<string, string> OutputKinds(IEnumerable<Statement> statements)
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Statement statement in statements)
            {
                switch (statement)
                {
                    case InputStatement input:
                        kinds[input.Define.Names[0].Name] = input.Define.Kind;
                        break;
                    case DefineStatement { Kind: not "" } define:
                        foreach (NameRef name in define.Names)
                        {
                            kinds[name.Name] = define.Kind;
                        }
                        break;
                    case OutputStatement { Define: { Kind: not "" } define }:
                        foreach (NameRef name in define.Names)
                        {
                            kinds[name.Name] = define.Kind;
                        }
                        break;
                    case TogetherStatement together:
                        foreach ((string name, string kind) in OutputKinds(together.Body))
                        {
                            kinds[name] = kind;
                        }
                        break;
                }
            }
            return kinds;
        }

        // the kind of an output; one taken without a kind from another file ("D s = use …") is unknown here
        private static string KindOf(Dictionary<string, string> kinds, string name) => kinds.GetValueOrDefault(name, "object");

        // an error inside a used file, reported at the "use" with the file and line where it happened
        private static ScriptError InModule(UseDefinition d, ScriptSource? moduleSource, ScriptError e, string? prefix = null)
        {
            ScriptError located = moduleSource?.Locate(e) ?? e;
            string where = located.File == null ? d.Path : $"{d.Path} ({located.File})";
            string message = prefix == null ? located.Message : located.Message.Replace(prefix + ".", string.Empty);
            return new ScriptError(d.PathLine, d.PathColumn, $"In {where}, line {located.Line}, column {located.Column}: {message}");
        }

        private static string Count(int n, string what) => n + " " + what + (n == 1 ? "" : "s");

        // ------------------------------------------------------------------ options

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
                    "labelpos" => options with { LabelPosition = (double)option.Value! },
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
    }
}
