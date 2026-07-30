using System.Collections.Immutable;
using System.Text;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

public sealed record CSharpDiffDisplayRow(
    string AssemblyIdentity,
    string StableMemberKey,
    MemberAnchor Anchor,
    string Member,
    int HunkId,
    CSharpDiffKind Kind,
    string Marker,
    int? Line,
    string? SourceCoordinate,
    string Fidelity,
    CSharpDiffOperationKind? OperationKind,
    string? OldValue,
    string? NewValue,
    string Operation,
    string Text,
    string Message)
{
    public string UnifiedLine => SourceCoordinate is { Length: > 0 } coordinate
        ? $"h{HunkId} {Marker} {coordinate} {Operation}"
        : $"h{HunkId} {Marker} {Operation}";
}

public sealed record CSharpDiffDisplayFailureRow(
    string AssemblyIdentity,
    string StableMemberKey,
    MemberAnchor Anchor,
    string Member,
    CSharpDiffFailureKind Kind,
    string Message,
    string? Side,
    string? Detail,
    int? HunkId)
{
    public string UnifiedLine => $"C# diff failed: {Message}";
}

public sealed record CSharpDiffDisplayResult(
    ImmutableArray<CSharpDiffDisplayRow> Rows,
    ImmutableArray<CSharpDiffDisplayFailureRow> FailureRows = default)
{
    public bool IsEmpty => Rows.IsDefaultOrEmpty && FailureRows.IsDefaultOrEmpty;
}

/// <summary>
/// Producer-owned display projection for C# body diff evidence.
/// </summary>
public static class CSharpDiffPrinter
{
    public static CSharpDiffDisplayRow ToDisplayRow(CSharpDiffRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new CSharpDiffDisplayRow(
            row.AssemblyIdentity,
            row.StableMemberKey,
            row.Anchor,
            row.Member,
            row.HunkId,
            row.Kind,
            Marker(row.Kind),
            row.Line,
            row.SourceCoordinate,
            row.Fidelity,
            row.NewOperation?.Kind ?? row.OldOperation?.Kind,
            row.OldOperation?.Value,
            row.NewOperation?.Value,
            Operation(row),
            row.Text,
            row.Message);
    }

    public static ImmutableArray<CSharpDiffDisplayRow> ToDisplayRows(IEnumerable<CSharpDiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(ToDisplayRow)];
    }

    public static CSharpDiffDisplayFailureRow ToDisplayFailureRow(CSharpDiffFailureRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new CSharpDiffDisplayFailureRow(
            row.AssemblyIdentity,
            row.StableMemberKey,
            row.Anchor,
            row.Member,
            row.Kind,
            row.Message,
            row.Side,
            row.Detail,
            row.HunkId);
    }

    public static ImmutableArray<CSharpDiffDisplayFailureRow> ToDisplayFailureRows(IEnumerable<CSharpDiffFailureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(ToDisplayFailureRow)];
    }

    public static CSharpDiffDisplayResult ToDisplayResult(CSharpBodyDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new CSharpDiffDisplayResult(
            result.Rows.IsDefault ? [] : ToDisplayRows(result.Rows),
            result.FailureRows.IsDefault ? [] : ToDisplayFailureRows(result.FailureRows));
    }

    public static ImmutableArray<string> ToUnifiedLines(CSharpBodyDiffResult result)
        => ToUnifiedLines(ToDisplayResult(result));

    public static ImmutableArray<string> ToUnifiedLines(CSharpDiffDisplayResult display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var failureRows = display.FailureRows.IsDefault
            ? []
            : display.FailureRows.Select(row => row.UnifiedLine);
        var rows = display.Rows.IsDefault
            ? []
            : display.Rows.Select(row => row.UnifiedLine);
        return [.. failureRows, .. rows];
    }

    public static string RenderUnified(CSharpBodyDiffResult result)
        => RenderUnified(ToDisplayResult(result));

    public static string RenderUnified(CSharpDiffDisplayResult result)
    {
        var lines = ToUnifiedLines(result);
        if (lines.IsEmpty)
            return "";

        var builder = new StringBuilder();
        foreach (string line in lines)
            builder.AppendLf(line);
        return builder.ToString().TrimEnd();
    }

    static string Operation(CSharpDiffRow row)
        => row.Kind switch
        {
            CSharpDiffKind.Changed => $"{Display(row.OldOperation, row.Text)} => {Display(row.NewOperation, row.Text)}",
            CSharpDiffKind.Add => Display(row.NewOperation, row.Text),
            CSharpDiffKind.Remove => Display(row.OldOperation, row.Text),
            _ => row.Text,
        };

    static string Display(CSharpDiffOperation? operation, string fallback)
        => operation?.Display ?? fallback;

    static string Marker(CSharpDiffKind kind)
        => kind switch
        {
            CSharpDiffKind.Add => "+",
            CSharpDiffKind.Remove => "-",
            CSharpDiffKind.Changed => "~",
            _ => "?",
        };
}
