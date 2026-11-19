using System.Globalization;
using System.Text;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// geoanim sheet &lt;file.geo.md&gt; [-o &lt;folder&gt;] [-pdf]: a test or worksheet ("type: test" or "type: worksheet" in
    /// its head) as its sheets – for every group the students' (name-A.html, without the solutions) and one file with the
    /// solutions of all groups (name-Lsg.html); with -pdf also as PDF (name-A.pdf …, name-Lsg.pdf) through a Typst project
    /// (name-typst). Default folder: the file's. Questions for ILIAS ("type: ilias"): for every group its pool to import
    /// (name-U__qti_1.zip) and an HTML file to check it (name-U.html).
    /// </summary>
    internal static int Sheet(List<string> arguments)
    {
        string file = Path.GetFullPath(arguments[0]);
        string? output = null;
        bool pdf = false;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] is "-o" or "--output" && i + 1 < arguments.Count)
            {
                output = Path.GetFullPath(arguments[++i]);
            }
            else if (arguments[i] is "-pdf" or "--pdf")
            {
                pdf = true;
            }
            else
            {
                throw new ArgumentException("Unknown option " + arguments[i]);
            }
        }
        if (!File.Exists(file))
        {
            Console.Error.WriteLine("File not found: " + file);
            return 1;
        }
        string text = File.ReadAllText(file);
        if (MarkdownPage.Parse(text, file).Document is not { } head)
        {
            Console.Error.WriteLine($"{Path.GetFileName(file)} is no test or worksheet: its head needs \"type: test\" (or worksheet).");
            return 1;
        }
        string folder = output ?? Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(folder);
        string stem = Path.GetFileName(file)[..^".geo.md".Length];
        if (head.IsIlias)
        {
            // questions for ILIAS: their pools and the files to check them (no PDF)
            return Ilias(file, text, head, folder, stem);
        }
        if (head.IsDocument)
        {
            return Document(file, text, head, folder, stem, pdf);
        }
        int groups = Math.Max(1, head.Groups.Count);
        var progress = new Progress(groups * 2 * (pdf ? 2 : 1));
        int errors = new HtmlBookWriter(Path.GetDirectoryName(file)!).WriteSheets(file, text, head, folder, stem, progress);
        if (!pdf)
        {
            progress.Finish($"Wrote {groups} sheet{(groups == 1 ? "" : "s")} and the solutions to {folder}");
            return errors > 0 ? 1 : 0;
        }
        string project = Path.Combine(folder, stem + "-typst");
        List<string> mains = new TypstWriter(Path.GetDirectoryName(file)!).WriteSheets(file, text, head, project, stem, progress);
        progress.Finish($"Wrote {groups} sheet{(groups == 1 ? "" : "s")} and the solutions to {folder}; the Typst project: {project}");
        if (errors > 0)
        {
            return 1;
        }
        foreach (string main in mains)
        {
            if (CompilePdf(project, Path.Combine(folder, Path.ChangeExtension(main, ".pdf")), main) != 0)
            {
                return 1;
            }
        }
        return 0;
    }

    /// <summary>A document ("type: document"): name.html (for screens and boards) and with -pdf name.pdf.</summary>
    private static int Document(string file, string text, DocumentHead head, string folder, string stem, bool pdf)
    {
        var progress = new Progress(pdf ? 2 : 1);
        progress.Step("HTML");
        MarkdownPage page = MarkdownPage.Parse(text, file, Daemon.LoadScript);
        foreach (MarkdownProblem problem in page.Errors)
        {
            progress.Error($"{Path.GetFileName(file)}:{problem.Line}:{problem.Column}: {problem.Message}");
        }
        new HtmlBookWriter(Path.GetDirectoryName(file)!).WriteControl(file, page, head, Path.Combine(folder, stem + ".html"));
        if (!pdf)
        {
            progress.Finish($"Wrote {stem}.html to {folder}");
            return page.Errors.Count > 0 ? 1 : 0;
        }
        string project = Path.Combine(folder, stem + "-typst");
        List<string> mains = new TypstWriter(Path.GetDirectoryName(file)!).WriteSheets(file, text, head, project, stem, progress);
        progress.Finish($"Wrote {stem}.html to {folder}; the Typst project: {project}");
        return page.Errors.Count > 0 ? 1 : CompilePdf(project, Path.Combine(folder, stem + ".pdf"), mains[0]);
    }

    private sealed partial class TypstWriter
    {
        /// <summary>
        /// The Typst project of a test or worksheet: a page for every sheet (each group's for the students and with the
        /// solutions), and the main files: one per group (name-A.typ) and one with the solutions of all groups
        /// (name-Lsg.typ). Returns the main files.
        /// </summary>
        public List<string> WriteSheets(string file, string text, DocumentHead head, string output, string stem, Progress progress)
        {
            List<string?> groups = head.Groups.Count > 0 ? head.Groups.Cast<string?>().ToList() : [null];
            var pages = new Dictionary<string, string>(StringComparer.Ordinal);
            var mains = new Dictionary<string, string>(StringComparer.Ordinal);
            var solutions = new List<string>();
            int index = 0;
            foreach (string? group in groups)
            {
                // (a document: one, without solutions)
                foreach (bool solution in head.IsDocument ? new[] { false } : [false, true])
                {
                    progress.Step("PDF: " + (group != null ? "Gruppe " + group : "Blatt") + (solution ? ", Lösung" : ""));
                    MarkdownPage page = MarkdownPage.Parse(text, file, Daemon.LoadScript, variant: new SheetVariant(group, solution));
                    string name = stem + (group != null ? "-" + group : "") + (solution ? "-Lsg" : "") + ".typ";
                    var entry = new TypstPage(Path.GetFileName(file), file, name, ++index);
                    pages[name] = "#import \"../style.typ\": *\n\n" + Convert(page, entry, [entry], head.Title ?? stem, progress);
                    if (solution)
                    {
                        solutions.Add(name);
                    }
                    else
                    {
                        mains[stem + (group != null ? "-" + group : "") + ".typ"] = Main([name]);
                    }
                }
            }
            if (!head.IsDocument)
            {
                mains[stem + "-Lsg.typ"] = Main(solutions, several: solutions.Count > 1);
            }
            Directory.CreateDirectory(output);
            WriteFiles(output, pages, Path.GetDirectoryName(file)!);
            foreach ((string name, string markup) in mains)
            {
                File.WriteAllText(Path.Combine(output, name), markup, new UTF8Encoding(false));
            }
            return [.. mains.Keys];

            // the sheets one after another, each on new pages
            // (several: each sheet counts its pages from 1)
            static string Main(List<string> sheets, bool several = false) =>
                "#import \"style.typ\": *\n\n#show: sheet" + (several ? ".with(numbering: \"1\")" : "") + "\n\n"
                + string.Join("\n#pagebreak()\n", sheets.Select(s => (several ? "#counter(page).update(1)\n" : "") + "#include \"pages/" + s + "\"\n")) + "\n";
        }
    }
}
