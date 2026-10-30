using System.Globalization;

namespace GeoAnim.Viewer;

/// <summary>How far a counter counts before it starts again: through the book, in each chapter, on each page.</summary>
public enum NumberScope
{
    Book,
    Chapter,
    Page,
}

/// <summary>
/// How a book numbers its figures and statements ("numbering:" in index.yaml): every kind either has a counter of its own
/// or counts with the shared one, each counter in its scope (<see cref="Within"/> unless the kind says otherwise); in a
/// chapter the numbers have the chapter's in front ("Satz 2.14"), through the book not ("Axiom 5"). With
/// <see cref="Headings"/> chapters and pages have numbers ("2 Abstandsgeometrie", "2.1 Abstandsaxiome"). Standard:
/// axioms through the book, figures, examples and formulas (with a mark) with counters of their own in each chapter, the
/// other statements sharing one.
/// </summary>
public sealed record BookNumbering
{
    /// <summary>The kinds that can be numbered (the numbered statements and figures).</summary>
    public static readonly IReadOnlyList<string> Kinds = ["axiom", "definition", "theorem", "lemma", "corollary", "example", "figure", "equation"];

    /// <summary>The name of the shared counter.</summary>
    public const string Shared = "shared";

    public NumberScope Within { get; init; } = NumberScope.Chapter;

    /// <summary>The kinds with a counter of their own, with its scope (null: <see cref="Within"/>).</summary>
    public IReadOnlyDictionary<string, NumberScope?> Own { get; init; } = new Dictionary<string, NumberScope?>
    {
        ["axiom"] = NumberScope.Book,
        ["figure"] = null,
        ["example"] = null,
        ["equation"] = null,
    };

    /// <summary>The scope of the shared counter (null: <see cref="Within"/>).</summary>
    public NumberScope? SharedScope { get; init; }

    /// <summary>Chapters and pages numbered.</summary>
    public bool Headings { get; init; } = true;

    public static BookNumbering Default { get; } = new();

    /// <summary>The counter a kind counts with: its own (its name) or the shared one.</summary>
    public string CounterOf(string kind) => Own.ContainsKey(kind) ? kind : Shared;

    /// <summary>How far a counter counts.</summary>
    public NumberScope ScopeOf(string counter) =>
        (counter == Shared ? SharedScope : Own.GetValueOrDefault(counter)) ?? Within;

    /// <summary>What stays the same as long as the numbering does (for caches).</summary>
    public string Key => string.Join(";", new[] { Within.ToString(), (SharedScope?.ToString() ?? "-"), Headings.ToString() }
        .Concat(Own.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + (o.Value?.ToString() ?? "-"))));
}

/// <summary>
/// The numbers of a page in its book: its section ("2.1"; null for a page outside the chapters, such as a preface in the
/// book's folder), its chapter ("2"), how many of each counter the pages before it have in the counter's scope, and the book's
/// numbering.
/// </summary>
public sealed record PageNumbering(BookNumbering Book, string? Section, string? Chapter, IReadOnlyDictionary<string, int> Before)
{
    /// <summary>A page alone (no book): numbers from 1, without chapter.</summary>
    public static PageNumbering None { get; } = new(BookNumbering.Default, null, null, new Dictionary<string, int>());

    /// <summary>The number of the <paramref name="index"/>-th item (from 1) of a counter on this page: "2.14", "5".</summary>
    public string NumberOf(string counter, int index)
    {
        int number = Before.GetValueOrDefault(counter) + index;
        string? prefix = Book.ScopeOf(counter) switch
        {
            NumberScope.Chapter => Chapter,
            NumberScope.Page => Section,
            _ => null,
        };
        return (prefix != null ? prefix + "." : "") + number.ToString(CultureInfo.InvariantCulture);
    }
}
