using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>
/// The C# generators of tests and worksheets ("generators: ../MintTemplates/MintTemplates.csproj" in the head): ordinary
/// projects, built when their sources are newer than what was built (dotnet build), and loaded with everything they use
/// (NuGet packages, other projects, DLLs) in a context of their own – from a copy of their output, so that the project can
/// be built again while it is loaded; a newer build replaces it. A class of theirs is made with a public constructor of
/// the parameters Random (the group's chance), int (the group's number, from 0) and string (the group's name), in any
/// order and any of them; its public properties, fields and methods are what the sheet uses.
/// </summary>
internal static partial class SheetGenerators
{
    private sealed class Project
    {
        public required string TargetPath { get; init; }

        // the newest source at the last build (not built again for the same)
        public DateTime BuiltSources { get; set; }

        // why that build failed (null: it did not)
        public string? BuildError { get; set; }

        // the time of the loaded build
        public DateTime Loaded { get; set; }

        public GeneratorContext? Context { get; set; }

        public Assembly? Assembly { get; set; }

        public string? Copy { get; set; }
    }

    private static readonly Dictionary<string, Project> projects = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock gate = new();
    private static bool cleaned;

    /// <summary>The copies of the loaded builds.</summary>
    private static string CopiesFolder => Path.Combine(Path.GetTempPath(), "GeoAnim", "generators");

    /// <summary>The project's assembly, built and loaded if it is not (or older than its sources).</summary>
    public static Assembly Load(string project)
    {
        lock (gate)
        {
            if (!File.Exists(project))
            {
                throw new FormatException($"The generators' project {project} does not exist.");
            }
            CleanUp();
            if (!projects.TryGetValue(project, out Project? known))
            {
                known = new Project { TargetPath = TargetPath(project) };
                projects[project] = known;
            }
            DateTime sources = NewestSource(project, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            DateTime built = File.Exists(known.TargetPath) ? File.GetLastWriteTimeUtc(known.TargetPath) : DateTime.MinValue;
            if (sources > built && sources != known.BuiltSources)
            {
                known.BuiltSources = sources;
                known.BuildError = null;
                try
                {
                    Build(project);
                }
                catch (FormatException e)
                {
                    known.BuildError = e.Message;
                    throw;
                }
                built = File.Exists(known.TargetPath) ? File.GetLastWriteTimeUtc(known.TargetPath)
                    : throw new FormatException($"The build of {Path.GetFileName(project)} made no {known.TargetPath}.");
            }
            if (sources > built && known.BuildError != null)
            {
                // the same sources as at the failed build
                throw new FormatException(known.BuildError);
            }
            if (built == DateTime.MinValue)
            {
                throw new FormatException($"{Path.GetFileName(project)} has not been built ({known.TargetPath}).");
            }
            if (known.Assembly == null || built != known.Loaded)
            {
                // a copy: the project's own output stays free for the next build
                string copy = Path.Combine(CopiesFolder, Path.GetFileNameWithoutExtension(project) + "-" + Guid.NewGuid().ToString("N")[..8]);
                CopyFolder(Path.GetDirectoryName(known.TargetPath)!, copy);
                string dll = Path.Combine(copy, Path.GetFileName(known.TargetPath));
                var context = new GeneratorContext(dll);
                Assembly assembly = context.LoadFromAssemblyPath(dll);
                string? old = known.Copy;
                known.Context?.Unload();
                (known.Context, known.Assembly, known.Loaded, known.Copy) = (context, assembly, built, copy);
                TryDelete(old);
            }
            return known.Assembly;
        }
    }

    // where the project builds its assembly to
    private static string TargetPath(string project)
    {
        (int exit, string output) = Dotnet("msbuild", project, "-nologo", "-getProperty:TargetPath");
        string path = output.Trim().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        if (exit != 0 || !Path.IsPathRooted(path))
        {
            throw new FormatException($"{Path.GetFileName(project)} is no project that can be read: {Shorten(output)}");
        }
        return path;
    }

    private static void Build(string project)
    {
        (int exit, string output) = Dotnet("build", project, "-nologo", "-v:q", "-clp:NoSummary");
        if (exit == 0)
        {
            return;
        }
        // its errors, short: "Datei.cs(12): CS1002: ; expected"
        var errors = output.Split('\n')
            .Select(l => BuildError().Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => $"{Path.GetFileName(m.Groups[1].Value)}({m.Groups[2].Value}): {m.Groups[3].Value}: {m.Groups[4].Value}")
            .Distinct()
            .ToList();
        throw new FormatException($"{Path.GetFileName(project)} does not build: "
            + (errors.Count > 0 ? string.Join("; ", errors.Take(5)) + (errors.Count > 5 ? $" (and {errors.Count - 5} more)" : "") : Shorten(output)));
    }

    [GeneratedRegex(@"^(.*?)\((\d+),\d+\): error (\w+): (.*?)(?:\s+\[[^\]]*\])?$")]
    private static partial Regex BuildError();

    private static string Shorten(string output)
    {
        string text = Regex.Replace(output.Trim(), @"\s+", " ");
        return text.Length > 300 ? text[..300] + " …" : text;
    }

    private static (int Exit, string Output) Dotnet(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        // not the settings of the process that runs GeoAnim (its own msbuild, say)
        start.Environment.Remove("MSBuildExtensionsPath");
        start.Environment.Remove("MSBuildSDKsPath");
        start.Environment.Remove("MSBUILD_EXE_PATH");
        using Process process = Process.Start(start) ?? throw new FormatException("dotnet does not start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            throw new FormatException("dotnet " + arguments[0] + " takes too long.");
        }
        return (process.ExitCode, output + error.Result);
    }

    // the newest of the project's sources and those of the projects it uses
    private static DateTime NewestSource(string project, HashSet<string> seen)
    {
        if (!seen.Add(Path.GetFullPath(project)) || !File.Exists(project))
        {
            return DateTime.MinValue;
        }
        string folder = Path.GetDirectoryName(Path.GetFullPath(project))!;
        DateTime newest = File.GetLastWriteTimeUtc(project);
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current))
            {
                if (Path.GetExtension(file).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets" or ".resx" or ".json" or ".txt")
                {
                    DateTime time = File.GetLastWriteTimeUtc(file);
                    newest = time > newest ? time : newest;
                }
            }
            foreach (string sub in Directory.EnumerateDirectories(current))
            {
                if (Path.GetFileName(sub).ToLowerInvariant() is not ("bin" or "obj" or ".vs" or ".git" or "node_modules"))
                {
                    pending.Push(sub);
                }
            }
        }
        foreach (Match reference in ProjectReference().Matches(File.ReadAllText(project)))
        {
            string other = Path.GetFullPath(Path.Combine(folder, reference.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
            DateTime time = NewestSource(other, seen);
            newest = time > newest ? time : newest;
        }
        return newest;
    }

    [GeneratedRegex(@"<ProjectReference\s+Include\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectReference();

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDelete(string? folder)
    {
        if (folder == null)
        {
            return;
        }
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // still loaded (or in use by another GeoAnim): later
        }
    }

    // copies of earlier runs (a day old: no longer loaded)
    private static void CleanUp()
    {
        if (cleaned)
        {
            return;
        }
        cleaned = true;
        if (Directory.Exists(CopiesFolder))
        {
            foreach (string folder in Directory.EnumerateDirectories(CopiesFolder))
            {
                if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
                {
                    TryDelete(folder);
                }
            }
        }
    }

    /// <summary>A context of its own for a project's assembly and everything it uses (from its .deps.json).</summary>
    private sealed class GeneratorContext(string dll) : AssemblyLoadContext("GeoAnim generators " + Path.GetFileName(dll), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver resolver = new(dll);

        protected override Assembly? Load(AssemblyName name) =>
            resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;

        protected override IntPtr LoadUnmanagedDll(string name) =>
            resolver.ResolveUnmanagedDllToPath(name) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }

    // ------------------------------------------------------------------ classes and their members

    /// <summary>The class of this name ("Info10.BinDec", or "BinDec" when only one has it), or null.</summary>
    public static Type? FindType(IReadOnlyList<Assembly> assemblies, string name)
    {
        foreach (Assembly assembly in assemblies)
        {
            if (assembly.GetType(name) is { IsPublic: true } type)
            {
                return type;
            }
        }
        if (name.Contains('.'))
        {
            return null;
        }
        var named = assemblies.SelectMany(a => a.GetExportedTypes()).Where(t => t.Name == name && !t.IsNested).ToList();
        return named.Count switch
        {
            1 => named[0],
            0 => null,
            _ => throw new FormatException($"\"{name}\" is the name of several classes ({string.Join(", ", named.Select(t => t.FullName))}): write which."),
        };
    }

    /// <summary>
    /// An object of the class for a group: with the public constructor of the most parameters of the types Random (its
    /// chance), int (the group's number, from 0) and string (the group's name).
    /// </summary>
    public static object Create(Type type, Random random, int group, string? name)
    {
        if (type.IsAbstract || type.ContainsGenericParameters)
        {
            throw new FormatException($"{type.FullName} is {(type.IsAbstract ? "abstract" : "generic")}: no object can be made of it.");
        }
        ConstructorInfo? constructor = type.GetConstructors()
            .Where(c => c.GetParameters().All(p => p.ParameterType == typeof(Random) || p.ParameterType == typeof(int) || p.ParameterType == typeof(string) || p.HasDefaultValue))
            .OrderByDescending(c => c.GetParameters().Count(p => !p.HasDefaultValue))
            .FirstOrDefault();
        if (constructor == null)
        {
            throw new FormatException($"{type.FullName} needs a public constructor with the parameters (Random random, int group, string name) – any of them, or none.");
        }
        object?[] arguments = constructor.GetParameters().Select(p =>
            p.ParameterType == typeof(Random) ? random
            : p.ParameterType == typeof(int) ? group
            : p.ParameterType == typeof(string) ? name ?? ""
            : p.DefaultValue).ToArray();
        return Invoke(type, type.Name, () => constructor.Invoke(arguments))!;
    }

    /// <summary>
    /// A member of an object (or with <paramref name="target"/> null: a static one of the class): a property or field, or
    /// a method with these arguments (null: none written – a property, a field or a method without parameters).
    /// </summary>
    public static object? Member(Type type, object? target, string name, IReadOnlyList<SheetScript.Value>? arguments)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | (target == null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
        if (arguments == null)
        {
            if (type.GetProperty(name, flags) is { } property && property.GetIndexParameters().Length == 0 && property.CanRead)
            {
                return Invoke(type, name, () => property.GetValue(target));
            }
            if (type.GetField(name, flags) is { } field)
            {
                return field.GetValue(target);
            }
        }
        var methods = type.GetMethods(flags).Where(m => m.Name == name && !m.ContainsGenericParameters).ToList();
        if (methods.Count == 0)
        {
            throw new FormatException(NoMember(type, name, target == null));
        }
        arguments ??= [];
        foreach (MethodInfo method in methods.OrderBy(m => m.GetParameters().Length != arguments.Count))
        {
            if (Arguments(method.GetParameters(), arguments) is { } values)
            {
                return Invoke(type, name, () => method.Invoke(method.IsStatic ? null : target, values));
            }
        }
        throw new FormatException($"{type.Name}.{name} does not take ({string.Join(", ", arguments.Select(a => a.Text != null ? "\"" + a.Text + "\"" : a.ToString()))}): "
            + string.Join(" or ", methods.Select(m => "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")")) + ".");
    }

    /// <summary>Sets a property or field of the object (in ::: let: "bc.Count = 5").</summary>
    public static void Set(Type type, object target, string name, SheetScript.Value value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        if (type.GetProperty(name, flags) is { CanWrite: true } property && property.GetIndexParameters().Length == 0)
        {
            object? converted = Convert(value, property.PropertyType, out bool ok);
            Invoke(type, name, () => { property.SetValue(target, ok ? converted : throw new FormatException($"{type.Name}.{name} is a {property.PropertyType.Name}, not \"{value}\".")); return null; });
        }
        else if (type.GetField(name, flags) is { IsInitOnly: false, IsLiteral: false } field)
        {
            object? converted = Convert(value, field.FieldType, out bool ok);
            field.SetValue(target, ok ? converted : throw new FormatException($"{type.Name}.{name} is a {field.FieldType.Name}, not \"{value}\"."));
        }
        else
        {
            throw new FormatException($"{type.Name} has no public property or field \"{name}\" that can be set.");
        }
    }

    private static string NoMember(Type type, string name, bool statics)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Static | (statics ? 0 : BindingFlags.Instance);
        var names = type.GetMembers(flags)
            .Where(m => m is PropertyInfo or FieldInfo || m is MethodInfo { IsSpecialName: false } method && method.DeclaringType != typeof(object))
            .Select(m => m.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return $"{type.Name} has no public {(statics ? "static " : "")}member \"{name}\""
            + (names.Count > 0 ? $" ({string.Join(", ", names.Take(15))}{(names.Count > 15 ? ", …" : "")})." : ".");
    }

    // the generator's own exceptions as errors of the sheet
    private static object? Invoke(Type type, string name, Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException e) when (e.InnerException is { } inner)
        {
            throw new FormatException($"{type.Name}.{name}: {inner.GetType().Name}: {inner.Message}");
        }
    }

    // the arguments for these parameters (with their defaults, "params" at the end), or null if they do not fit
    private static object?[]? Arguments(ParameterInfo[] parameters, IReadOnlyList<SheetScript.Value> arguments)
    {
        var values = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (i == parameters.Length - 1 && parameter.IsDefined(typeof(ParamArrayAttribute)) && !(arguments.Count == parameters.Length && arguments[i].List != null))
            {
                Type element = parameter.ParameterType.GetElementType()!;
                var rest = Array.CreateInstance(element, Math.Max(0, arguments.Count - i));
                for (int k = i; k < arguments.Count; k++)
                {
                    object? item = Convert(arguments[k], element, out bool fits);
                    if (!fits)
                    {
                        return null;
                    }
                    rest.SetValue(item, k - i);
                }
                values[i] = rest;
                return values;
            }
            if (i >= arguments.Count)
            {
                if (!parameter.HasDefaultValue)
                {
                    return null;
                }
                values[i] = parameter.DefaultValue;
                continue;
            }
            values[i] = Convert(arguments[i], parameter.ParameterType, out bool ok);
            if (!ok)
            {
                return null;
            }
        }
        return arguments.Count <= parameters.Length ? values : null;
    }

    // a value of the sheet as what C# wants
    private static object? Convert(SheetScript.Value value, Type type, out bool ok)
    {
        ok = true;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value.Object != null && type.IsInstanceOfType(value.Object))
        {
            return value.Object;
        }
        if (type == typeof(string))
        {
            return value.ToString();
        }
        if (type == typeof(object))
        {
            return value.Object ?? (value.Number is double n ? n : value.Text ?? (object)value.List!.Select(v => v.ToString()).ToList());
        }
        if (type == typeof(bool))
        {
            if (value.Text?.Trim().ToLowerInvariant() is "true" or "wahr" or "ja" or "w")
            {
                return true;
            }
            if (value.Text?.Trim().ToLowerInvariant() is "false" or "falsch" or "nein" or "f")
            {
                return false;
            }
            if (value.Number is double number)
            {
                return number != 0;
            }
            ok = false;
            return null;
        }
        if (type.IsEnum)
        {
            if (value.Text != null && Enum.TryParse(type, value.Text.Trim(), ignoreCase: true, out object? named))
            {
                return named;
            }
            if (value.Number is double number && number == Math.Round(number))
            {
                return Enum.ToObject(type, (long)number);
            }
            ok = false;
            return null;
        }
        if (type == typeof(char))
        {
            ok = value.Text is { Length: 1 };
            return ok ? value.Text![0] : null;
        }
        if (type.IsPrimitive || type == typeof(decimal))
        {
            double number;
            try
            {
                number = value.AsNumber();
            }
            catch (FormatException)
            {
                ok = false;
                return null;
            }
            bool whole = type != typeof(double) && type != typeof(float) && type != typeof(decimal);
            if (whole && Math.Abs(number - Math.Round(number)) > 1e-9)
            {
                ok = false;
                return null;
            }
            try
            {
                return System.Convert.ChangeType(whole ? Math.Round(number) : number, type, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                ok = false;
                return null;
            }
        }
        // lists: T[], List<T>, IEnumerable<T>, IReadOnlyList<T> …
        Type? itemType = type.IsArray ? type.GetElementType()
            : type.IsGenericType && type.GetGenericArguments().Length == 1 && type.IsAssignableFrom(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) ? type.GetGenericArguments()[0]
            : null;
        if (itemType != null)
        {
            IReadOnlyList<SheetScript.Value> items = value.AsList();
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            foreach (SheetScript.Value item in items)
            {
                list.Add(Convert(item, itemType, out bool fits));
                if (!fits)
                {
                    ok = false;
                    return null;
                }
            }
            if (type.IsArray)
            {
                Array array = Array.CreateInstance(itemType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }
            return list;
        }
        ok = false;
        return null;
    }

    /// <summary>What C# gave as a value of the sheet: texts, numbers, true/false ("wahr"/"falsch"), lists, else the object.</summary>
    public static SheetScript.Value ValueOf(object? result) => result switch
    {
        null => SheetScript.Value.Of(""),
        string text => SheetScript.Value.Of(text),
        bool truth => SheetScript.Value.Of(truth ? "wahr" : "falsch"),
        char c => SheetScript.Value.Of(c.ToString()),
        Enum e => SheetScript.Value.Of(e.ToString()),
        double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort => SheetScript.Value.Of(System.Convert.ToDouble(result, CultureInfo.InvariantCulture)),
        IEnumerable items => SheetScript.Value.Of(items.Cast<object?>().Select(ValueOf).ToList()),
        _ => new SheetScript.Value(null, null, null, result),
    };
}
