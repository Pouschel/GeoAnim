using System.Globalization;
using System.Text;

namespace GeoAnim.Script;

internal enum TokenKind
{
    Name,
    Number,
    String,
    Color,
    Symbol,
    NewLine,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Line, int Column)
{
    public double Number => double.Parse(Text, CultureInfo.InvariantCulture);

    public bool Is(string text) => (Kind == TokenKind.Name || Kind == TokenKind.Symbol) && Text == text;

    public string Describe() => Kind switch
    {
        TokenKind.NewLine => "end of line",
        TokenKind.End => "end of file",
        TokenKind.String => $"\"{Text}\"",
        _ => $"\"{Text}\"",
    };
}

/// <summary>
/// Splits a script into tokens: names (letters, digits, _ and trailing primes like A'), numbers (with a decimal point),
/// strings in double quotes, colours (#rgb or #rrggbb), the symbols = and .. and line ends. A # that does not start a
/// colour starts a comment until the end of the line. After "title" or "step" at the start of a line, the rest of the
/// line (trimmed, taken literally, # included) is one string. Values are separated by spaces only: commas and parentheses
/// are errors with a hint.
/// </summary>
internal static class Lexer
{
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int line = 1;
        int lineStart = 0;
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            int column = i - lineStart + 1;
            if (c == '\n')
            {
                tokens.Add(new Token(TokenKind.NewLine, "\n", line, column));
                i++;
                line++;
                lineStart = i;
            }
            else if (c == ' ' || c == '\t' || c == '\r' || c == '﻿')
            {
                i++;
            }
            else if (c == '#')
            {
                int length = ColorLength(source, i);
                if (length > 0)
                {
                    tokens.Add(new Token(TokenKind.Color, source.Substring(i, length), line, column));
                    i += length;
                }
                else
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        i++;
                    }
                }
            }
            else if (c == '"')
            {
                var text = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= source.Length || source[i] == '\n')
                    {
                        throw new ScriptError(line, column, "The text has no closing quote (\").");
                    }
                    if (source[i] == '\\' && i + 1 < source.Length && (source[i + 1] == '"' || source[i + 1] == '\\'))
                    {
                        text.Append(source[i + 1]);
                        i += 2;
                    }
                    else if (source[i] == '"')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        text.Append(source[i++]);
                    }
                }
                tokens.Add(new Token(TokenKind.String, text.ToString(), line, column));
            }
            else if (char.IsDigit(c) || StartsNumber(source, i))
            {
                int start = i;
                i++;
                if (source[start] == '-' && source[i] == '.')
                {
                    i++;
                }
                while (i < source.Length && char.IsDigit(source[i]))
                {
                    i++;
                }
                // a decimal point, but not the ".." of a range
                if (i + 1 < source.Length && source[i] == '.' && char.IsDigit(source[i + 1]))
                {
                    i++;
                    while (i < source.Length && char.IsDigit(source[i]))
                    {
                        i++;
                    }
                }
                if (i < source.Length && IsNameChar(source[i]))
                {
                    throw new ScriptError(line, column, $"\"{source.Substring(start, i - start + 1)}…\" is neither a number nor a name.");
                }
                tokens.Add(new Token(TokenKind.Number, source.Substring(start, i - start), line, column));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && IsNameChar(source[i]))
                {
                    i++;
                }
                while (i < source.Length && source[i] == '\'')
                {
                    i++;
                }
                string word = source.Substring(start, i - start);
                bool startsLine = tokens.Count == 0 || tokens[^1].Kind == TokenKind.NewLine;
                tokens.Add(new Token(TokenKind.Name, word, line, column));
                if (startsLine && word is "title" or "step")
                {
                    tokens.Add(RestOfLine(source, ref i, line, lineStart));
                }
                else if (word == "use")
                {
                    // the file: everything up to the next space ("../Book1/Prop1.geo")
                    while (i < source.Length && source[i] is ' ' or '\t')
                    {
                        i++;
                    }
                    int pathStart = i;
                    while (i < source.Length && !char.IsWhiteSpace(source[i]))
                    {
                        i++;
                    }
                    if (i > pathStart)
                    {
                        tokens.Add(new Token(TokenKind.String, source.Substring(pathStart, i - pathStart), line, pathStart - lineStart + 1));
                    }
                }
            }
            else if (c == '.' && i + 1 < source.Length && source[i + 1] == '.')
            {
                tokens.Add(new Token(TokenKind.Symbol, "..", line, column));
                i += 2;
            }
            else if (c == ',')
            {
                throw new ScriptError(line, column, "No commas: separate values with spaces, e.g. \"point S T = intersect k1 k2\".");
            }
            else if (c is '(' or ')')
            {
                throw new ScriptError(line, column, "Coordinates are written without parentheses and comma, e.g. \"point A = 2 3\".");
            }
            else if (c == '=')
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString(), line, column));
                i++;
            }
            else
            {
                throw new ScriptError(line, column, $"Unexpected character '{c}'.");
            }
        }
        tokens.Add(new Token(TokenKind.NewLine, "\n", line, i - lineStart + 1));
        tokens.Add(new Token(TokenKind.End, string.Empty, line + 1, 1));
        return tokens;
    }

    // the text after "title" or "step": up to the end of the line, without the spaces around it
    private static Token RestOfLine(string source, ref int i, int line, int lineStart)
    {
        while (i < source.Length && source[i] is ' ' or '\t')
        {
            i++;
        }
        int start = i;
        while (i < source.Length && source[i] != '\n')
        {
            i++;
        }
        return new Token(TokenKind.String, source.Substring(start, i - start).TrimEnd(), line, start - lineStart + 1);
    }

    // "-2", "-.5" or ".5" (but not the second dot of "..")
    private static bool StartsNumber(string source, int i)
    {
        bool Digit(int k) => k < source.Length && char.IsDigit(source[k]);
        char c = source[i];
        return c == '-' && (Digit(i + 1) || i + 2 < source.Length && source[i + 1] == '.' && Digit(i + 2))
            || c == '.' && Digit(i + 1) && !(i > 0 && source[i - 1] == '.');
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The length of a colour #rgb or #rrggbb at <paramref name="start"/>, or 0.</summary>
    private static int ColorLength(string source, int start)
    {
        int i = start + 1;
        while (i < source.Length && Uri.IsHexDigit(source[i]))
        {
            i++;
        }
        int digits = i - start - 1;
        bool ends = i >= source.Length || !IsNameChar(source[i]);
        return (digits == 3 || digits == 6) && ends ? digits + 1 : 0;
    }
}
