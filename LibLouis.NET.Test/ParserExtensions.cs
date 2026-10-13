using System;

using SharpYaml;
using SharpYaml.Events;

namespace LibLouis.NET.Test;

/// <summary>
/// The two <see cref="IParser"/> helpers YamlDotNet shipped as extension methods and SharpYaml does
/// not, reimplemented against the same event stream.
/// </summary>
/// <remarks>
/// Both are mechanical. They are here rather than inlined at the call sites because the reader is
/// written as a recursive descent over the event stream - the shape upstream's lou_checkyaml uses -
/// and that reads far better with "consume this event, it must be a scalar" than with an explicit
/// type test and MoveNext at every step.
/// </remarks>
internal static class ParserExtensions
{
    /// <summary>
    /// Returns the current event, which must be a <typeparamref name="T"/>, and advances.
    /// </summary>
    public static T Consume<T>(this IParser parser)
        where T : ParsingEvent
    {
        // A freshly created parser sits before the first event, with a null Current. YamlDotNet
        // primed it inside Accept<T>, so the reader never had to, and it does not here either.
        if (parser.Current is null)
        {
            parser.MoveNext();
        }

        if (parser.Current is not T expected)
        {
            throw new YamlException(
                $"Expected {typeof(T).Name} but found {parser.Current?.GetType().Name ?? "the end of the stream"}.");
        }

        parser.MoveNext();
        return expected;
    }

    /// <summary>
    /// Skips the current node, including everything nested inside it.
    /// </summary>
    /// <remarks>
    /// Driven by NestingIncrease rather than by matching start and end events, because a scalar is
    /// a complete node on its own and contributes zero. That makes one loop handle a scalar, a
    /// sequence and a mapping alike, which is why YamlDotNet wrote it this way too.
    /// </remarks>
    public static void SkipThisAndNestedEvents(this IParser parser)
    {
        if (parser.Current is null)
        {
            parser.MoveNext();
        }

        int depth = 0;

        do
        {
            depth += parser.Current?.NestingIncrease
                ?? throw new YamlException("Cannot skip past the end of the stream.");
            parser.MoveNext();
        }
        while (depth > 0);
    }
}
