namespace ILInspector.CSharp;

/// <summary>
/// A zero-based character range within a rendered C# source artifact.
/// </summary>
public readonly record struct CSharpSourceRange
{
    public CSharpSourceRange(int start, int length)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);
}

/// <summary>
/// An immutable C# compilation unit whose replaceable member body was identified
/// by the product while rendering.
/// </summary>
public sealed class CSharpSourceArtifact
{
    readonly string? _replaceableBodyIndent;

    internal CSharpSourceArtifact(
        string source,
        CSharpSourceRange? replaceableBodyRange,
        string? replaceableBodyIndent)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (replaceableBodyRange is { } range && range.End > source.Length)
            throw new ArgumentOutOfRangeException(nameof(replaceableBodyRange));
        if ((replaceableBodyRange is null) != (replaceableBodyIndent is null))
        {
            throw new ArgumentException(
                "A replaceable body range and its indentation must be supplied together.",
                nameof(replaceableBodyRange));
        }

        Source = source;
        ReplaceableBodyRange = replaceableBodyRange;
        _replaceableBodyIndent = replaceableBodyIndent;
    }

    /// <summary>
    /// The frozen compilation-unit source.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The exact block, including braces, that the renderer selected for
    /// replacement. Null when the print request did not select a body.
    /// </summary>
    public CSharpSourceRange? ReplaceableBodyRange { get; }

    /// <summary>
    /// Replaces only the selected body block while retaining every other byte of
    /// the rendered compilation unit.
    /// </summary>
    public string ReplaceBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (ReplaceableBodyRange is not { } range || _replaceableBodyIndent is null)
        {
            throw new InvalidOperationException(
                "The C# source artifact does not contain a replaceable body.");
        }

        string replacement = CSharpSourceLayout.RenderReplacementBlock(
            body,
            _replaceableBodyIndent);
        return Source[..range.Start] + replacement + Source[range.End..];
    }
}

static class CSharpSourceLayout
{
    internal static string RenderBlock(string source, string indent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(indent);

        var lines = source.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
            return $"{indent}{{\n{indent}}}";

        string bodyIndent = indent + "    ";
        return $"{indent}{{\n{string.Join('\n', lines.Select(line => bodyIndent + line))}\n{indent}}}";
    }

    internal static string RenderReplacementBlock(string source, string indent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(indent);

        return source.Length == 0
            ? $"{indent}{{\n{indent}}}"
            : $"{indent}{{\n{source}\n{indent}}}";
    }
}
