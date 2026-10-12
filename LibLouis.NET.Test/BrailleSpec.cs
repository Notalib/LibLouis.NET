using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace LibLouis.NET.Test;

/// <summary>
/// One translation case from an upstream braille spec.
/// </summary>
public sealed record BrailleSpecCase(
    string SpecFile,
    int Line,
    string TableQuery,
    string? AssertMatch,
    string DisplayTable,
    string Input,
    string Expected,
    TestDirection Direction,
    bool ExpectedToFail)
{
    public override string ToString() =>
        $"{SpecFile}:{Line} {Direction} {Describe(Input)} -> {Describe(Expected)}";

    // Braille output is mostly U+28xx, which is unreadable in a test runner's output, so show the
    // code points for anything outside printable ASCII.
    private static string Describe(string value) =>
        value.All(c => c is >= ' ' and <= '~')
            ? $"\"{value}\""
            : string.Concat(value.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}

/// <summary>
/// A parsed spec: the cases it yields, and how many spec entries were dropped for each construct
/// that was recognised but not driven, so what is being skipped stays visible rather than becoming
/// invisible coverage loss.
/// </summary>
public sealed record BrailleSpec(
    IReadOnlyList<BrailleSpecCase> Cases,
    IReadOnlyDictionary<string, int> SkippedConstructs);

public enum TestDirection
{
    Forward,
    Backward,
}

/// <summary>
/// How a spec entry's two values are used. The distinction matters: the backward leg of
/// bothDirections swaps input and expected (lou_checkyaml.c:900-902), while an explicit
/// backward testmode does not (lou_checkyaml.c:892-895).
/// </summary>
public enum TestMode
{
    Forward,
    Backward,
    BothDirections,

    /// <summary>A liblouis feature this harness recognises but does not drive yet.</summary>
    Unsupported,
}

/// <summary>
/// Reads liblouis braille spec files.
/// </summary>
/// <remarks>
/// These files are not YAML mappings and cannot be deserialised. A single document repeats
/// <c>table</c>, <c>flags</c> and <c>tests</c> at the same level, which duplicate key handling
/// would collapse or reject. lou_checkyaml treats the file as an event stream where each key
/// mutates parser state, and <c>tests</c> executes against whatever is current
/// (tools/lou_checkyaml.c:1087-1139), so this reader does the same over YamlDotNet's IParser.
///
/// Consecutive <c>table</c> keys accumulate rather than replace: the following <c>tests</c> block
/// runs once per accumulated table. A <c>flags</c> block, by contrast, applies to the single
/// <c>tests</c> block that follows it and no further: lou_checkyaml declares
/// <c>int testmode = MODE_DEFAULT;</c> <em>inside</em> its loop over these keys
/// (tools/lou_checkyaml.c:1138), so a <c>tests</c> block with no <c>flags</c> of its own runs
/// forward rather than inheriting whatever came before it.
///
/// Constructs fall into three groups. Those the harness drives are turned into cases. Those
/// liblouis supports but the harness does not drive yet are counted in
/// <see cref="BrailleSpec.SkippedConstructs"/> and their cases dropped. Anything else throws, so a
/// spec using something nobody has looked at fails loudly rather than quietly testing less than it
/// appears to.
/// </remarks>
public static class BrailleSpecReader
{
    public static BrailleSpec Read(string path)
    {
        string specFile = Path.GetFileName(path);
        var cases = new List<BrailleSpecCase>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);

        using var reader = new StreamReader(path);
        var parser = new Parser(reader);

        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        parser.Consume<MappingStart>();

        string displayTable = string.Empty;
        var tables = new List<(string Query, string? AssertMatch)>();

        // The mode a tests block runs in when it carries no flags of its own: MODE_DEFAULT, which
        // is MODE_TRANSLATION_FORWARD (tools/lou_checkyaml.c:56).
        const TestMode defaultMode = TestMode.Forward;

        TestMode mode = defaultMode;
        string? unsupportedMode = null;

        // Consecutive table keys accumulate, but the first one after a tests block starts a fresh
        // set rather than adding to the one just used.
        bool tablesUsed = false;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;

            switch (key)
            {
                case "display":
                    displayTable = ReadTableValue(parser).Query;
                    break;

                case "table":
                    if (tablesUsed)
                    {
                        tables.Clear();
                        tablesUsed = false;
                    }

                    tables.Add(ReadTableValue(parser));
                    break;

                case "flags":
                    (mode, unsupportedMode) = ReadFlags(parser);
                    break;

                case "tests":
                    ReadTests(parser, specFile, tables, displayTable, mode, unsupportedMode, cases, skipped);
                    tablesUsed = true;

                    // A flags block is spent by the tests block it precedes. Carrying it further
                    // would run a bare tests block in the wrong direction, which is not a
                    // disagreement the spec can express: en-us-g2.yaml follows a bothDirections
                    // block with a bare one whose cases only make sense forward.
                    (mode, unsupportedMode) = (defaultMode, null);
                    break;

                default:
                    throw new NotSupportedException(
                        $"{specFile}: unsupported top level key '{key}'. See lou_checkyaml.c for the " +
                        "full format.");
            }
        }

        return new BrailleSpec(cases, skipped);
    }

    /// <summary>
    /// A table value is either a file name or a query mapping. Queries are passed to lou_findTable
    /// as "key:value key:value"; __assert-match is a harness directive, not part of the query.
    /// </summary>
    private static (string Query, string? AssertMatch) ReadTableValue(IParser parser)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext();
            return (scalar.Value, null);
        }

        parser.Consume<MappingStart>();

        var terms = new List<string>();
        string? assertMatch = null;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;

            if (key == "__assert-match")
            {
                assertMatch = value;
            }
            else
            {
                terms.Add($"{key}:{value}");
            }
        }

        parser.Consume<MappingEnd>();

        return (string.Join(' ', terms), assertMatch);
    }

    private static (TestMode Mode, string? Unsupported) ReadFlags(IParser parser)
    {
        parser.Consume<MappingStart>();

        TestMode mode = TestMode.Forward;
        string? unsupported = null;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;

            if (key != "testmode")
            {
                throw new NotSupportedException($"unsupported flag '{key}'");
            }

            (mode, unsupported) = ParseTestMode(value);
        }

        parser.Consume<MappingEnd>();

        return (mode, unsupported);
    }

    /// <summary>
    /// hyphenate and display are liblouis features this harness does not drive yet — they map to
    /// <c>Hyphenate</c> and <c>DotsToCharacters</c>/<c>CharactersToDots</c> — so they come back as
    /// <see cref="TestMode.Unsupported"/>, named so the entries they drop can be counted. An
    /// unrecognised mode still throws.
    /// </summary>
    private static (TestMode Mode, string? Unsupported) ParseTestMode(string value) => value switch
    {
        "forward" => (TestMode.Forward, null),
        "backward" => (TestMode.Backward, null),
        "bothDirections" => (TestMode.BothDirections, null),
        "hyphenate" or "hyphenateBraille" or "display" => (TestMode.Unsupported, $"testmode: {value}"),
        _ => throw new NotSupportedException($"unsupported testmode '{value}'"),
    };

    private static void ReadTests(
        IParser parser,
        string specFile,
        List<(string Query, string? AssertMatch)> tables,
        string displayTable,
        TestMode mode,
        string? unsupportedMode,
        List<BrailleSpecCase> cases,
        IDictionary<string, int> skipped)
    {
        parser.Consume<SequenceStart>();

        while (parser.Current is not SequenceEnd)
        {
            SequenceStart entryStart = parser.Consume<SequenceStart>();
            int line = (int)entryStart.Start.Line;

            // An entry is [input, expected] or [description, input, expected], the second form
            // labelling a group of cases. They are told apart by what follows the first two
            // scalars: another scalar means the first was a label.
            string first = Unescape(parser.Consume<Scalar>().Value);
            string second = Unescape(parser.Consume<Scalar>().Value);
            string input, expected;

            if (parser.Current is Scalar)
            {
                input = second;
                expected = Unescape(parser.Consume<Scalar>().Value);
            }
            else
            {
                input = first;
                expected = second;
            }

            var xfail = XFail.None;
            TestMode entryMode = mode;
            string? unsupported = unsupportedMode;

            if (parser.Current is MappingStart)
            {
                (xfail, entryMode, unsupported) = ReadTestOptions(parser, mode, unsupportedMode);
            }

            parser.Consume<SequenceEnd>();

            // Counted once per dropped entry, under the construct that dropped it, so the tally says
            // how much of the spec went unchecked.
            if (unsupported is not null)
            {
                skipped[unsupported] = skipped.TryGetValue(unsupported, out int n) ? n + 1 : 1;
                continue;
            }

            foreach ((string query, string? assertMatch) in tables)
            {
                // Forward compares translate(input) with expected. An explicit backward testmode
                // means the entry is already written braille-first, so it is not swapped. The
                // backward leg of bothDirections is: the expected braille is the input, and the
                // original text is what back translation should produce.
                if (entryMode is TestMode.Forward or TestMode.BothDirections)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        input, expected, TestDirection.Forward, xfail.HasFlag(XFail.Forward)));
                }

                if (entryMode == TestMode.Backward)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        input, expected, TestDirection.Backward, xfail.HasFlag(XFail.Backward)));
                }
                else if (entryMode == TestMode.BothDirections)
                {
                    cases.Add(new BrailleSpecCase(
                        specFile, line, query, assertMatch, displayTable,
                        expected, input, TestDirection.Backward, xfail.HasFlag(XFail.Backward)));
                }
            }
        }

        parser.Consume<SequenceEnd>();
    }

    [Flags]
    private enum XFail
    {
        None = 0,
        Forward = 1,
        Backward = 2,
        Both = Forward | Backward,
    }

    private static (XFail XFail, TestMode Mode, string? Unsupported) ReadTestOptions(
        IParser parser, TestMode mode, string? unsupportedMode)
    {
        parser.Consume<MappingStart>();

        var xfail = XFail.None;

        // Kept apart from the mode: a testmode later in the same options may replace the mode, but
        // must not bring back an entry an unsupported option has already dropped.
        string? unsupportedOption = null;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;

            switch (key)
            {
                case "xfail":
                    xfail = ReadXFail(parser);
                    break;

                case "testmode":
                    (mode, unsupportedMode) = ParseTestMode(parser.Consume<Scalar>().Value);
                    break;

                // Options liblouis supports that this harness does not drive. Each changes what the
                // expected output means, so running the case without honouring it would compare
                // against the wrong thing. The value is consumed and the case dropped.
                case "typeform":
                case "mode":
                case "inputPos":
                case "outputPos":
                case "cursorPos":
                case "cursorOutPos":
                case "maxOutputLength":
                case "realInputLength":
                case "expected_typeform":
                    parser.SkipThisAndNestedEvents();
                    unsupportedOption ??= $"test option: {key}";
                    break;

                default:
                    throw new NotSupportedException($"unsupported test option '{key}'");
            }
        }

        parser.Consume<MappingEnd>();

        return (xfail, mode, unsupportedOption ?? unsupportedMode);
    }

    /// <summary>
    /// xfail is either a scalar, where only "false" and "off" are falsy
    /// (tools/lou_checkyaml.c:379-389), or a mapping naming the failing directions.
    /// </summary>
    private static XFail ReadXFail(IParser parser)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.MoveNext();
            return scalar.Value is "false" or "off" ? XFail.None : XFail.Both;
        }

        parser.Consume<MappingStart>();

        var xfail = XFail.None;

        while (parser.Current is not MappingEnd)
        {
            string key = parser.Consume<Scalar>().Value;
            string value = parser.Consume<Scalar>().Value;
            bool set = value is not ("false" or "off");

            if (set)
            {
                xfail |= key switch
                {
                    "forward" => XFail.Forward,
                    "backward" => XFail.Backward,
                    _ => throw new NotSupportedException($"unsupported xfail direction '{key}'"),
                };
            }
        }

        parser.Consume<MappingEnd>();

        return xfail;
    }

    /// <summary>
    /// The specs use single quoted scalars, where YAML performs no escape processing at all, and
    /// rely on liblouis to interpret the escapes itself. This mirrors liblouis's own parser,
    /// <c>parseCharsInternal</c> (liblouis/compileTranslationTable.c:1263), which lou_checkyaml
    /// reaches through <c>_lou_extParseChars</c> for both the input and the expected value of every
    /// case (tools/brl_checks.c:162 and :222). Without the <c>\\</c> case, 'at\\bliver' parses as
    /// two backslashes and translates to two cells where one is expected.
    /// </summary>
    /// <remarks>
    /// Note what is deliberately absent. <c>\u</c> is not a liblouis escape at all: the specs write
    /// it only in double quoted scalars, where YAML decodes it before the reader sees it, exactly as
    /// libyaml does for upstream. <c>\"</c> is the same — a double quoted <c>"\""</c> arrives here as
    /// a bare quote — and a <c>\"</c> that does survive YAML is QUOTESUB to liblouis, its stand-in
    /// for a double quote inside a table string, not a quote.
    ///
    /// <c>\y</c> and <c>\z</c> take five and eight hex digits and need a UCS-4 build, which is what
    /// build_runtime_packages.sh configures. A value above the BMP becomes a surrogate pair here,
    /// which the marshaller turns back into the single widechar upstream gives liblouis.
    ///
    /// Upstream treats a malformed escape as a compile error and returns an empty string, which
    /// would quietly reduce a case to a comparison of two empty strings. This throws instead, for
    /// the same reason the reader throws on a construct nobody has looked at.
    /// </remarks>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\')
            {
                builder.Append(value[i]);
                continue;
            }

            if (i + 1 >= value.Length)
            {
                throw new NotSupportedException(
                    $"'{value}' ends in a backslash, which is not a complete escape.");
            }

            // i is the escape letter from here on, so the digits of \x and friends start at i + 1.
            char escape = value[++i];

            int single = escape switch
            {
                '\\' => '\\',
                'e' => 0x1b,
                'f' => 12,
                'n' => 10,
                'r' => 13,
                's' => ' ',
                't' => 9,
                'v' => 11,
                'w' => 0xffff, // LOU_ENDSEGMENT
                '"' => 28,     // QUOTESUB
                _ => -1,
            };

            if (single >= 0)
            {
                builder.Append((char)single);
                continue;
            }

            // The capitals are the deprecated spellings; liblouis warns but accepts them.
            int digits = escape switch
            {
                'x' or 'X' => 4,
                'y' or 'Y' => 5,
                'z' or 'Z' => 8,
                _ => 0,
            };

            if (digits == 0)
            {
                throw new NotSupportedException(
                    $"unsupported escape '\\{escape}' in '{value}'. See parseCharsInternal in " +
                    "liblouis/compileTranslationTable.c for the set liblouis accepts.");
            }

            if (i + digits >= value.Length || !TryParseHex(value.AsSpan(i + 1, digits), out uint code))
            {
                throw new NotSupportedException(
                    $"'\\{escape}' in '{value}' needs {digits} hexadecimal digits.");
            }

            i += digits;

            if (code <= 0xffff)
            {
                // A cast rather than ConvertFromUtf32, so an unpaired surrogate survives the way it
                // does in a widechar.
                builder.Append((char)code);
            }
            else if (code <= 0x10ffff)
            {
                builder.Append(char.ConvertFromUtf32((int)code));
            }
            else
            {
                throw new NotSupportedException(
                    $"'\\{escape}' in '{value}' is U+{code:X}, which a .NET string cannot hold.");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Exactly the digits <c>hexValue</c> accepts (compileTranslationTable.c:1233), rather than
    /// <see cref="NumberStyles.HexNumber"/>, which would also allow surrounding white space.
    /// </summary>
    private static bool TryParseHex(ReadOnlySpan<char> digits, out uint value)
    {
        value = 0;

        foreach (char digit in digits)
        {
            int parsed = digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'a' and <= 'f' => digit - 'a' + 10,
                >= 'A' and <= 'F' => digit - 'A' + 10,
                _ => -1,
            };

            if (parsed < 0)
            {
                return false;
            }

            value = (value << 4) | (uint)parsed;
        }

        return true;
    }
}
