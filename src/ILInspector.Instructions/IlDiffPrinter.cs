using System.Collections.Immutable;
using System.Text;

namespace ILInspector.Instructions;

public sealed record IlDiffDisplayRow(
    int HunkId,
    IlDiffKind Kind,
    string Marker,
    int RawOffset,
    string Offset,
    string OpcodeFamily,
    IlOperandIdentityKind? OperandKind,
    string? OperandValue,
    string Operation,
    string Message)
{
    public string UnifiedLine => $"h{HunkId} {Marker} {Offset} {Operation}";
}

public sealed record IlDiffDisplayResult(
    string? Failure,
    ImmutableArray<IlDiffDisplayRow> Rows)
{
    public bool IsEmpty => Failure is null && Rows.IsEmpty;
}

/// <summary>
/// Producer-owned display projection for IL body diff evidence.
/// </summary>
public static class IlDiffPrinter
{
    public static IlDiffDisplayRow ToDisplayRow(IlDiffRow row)
        => new(
            row.HunkId,
            row.Kind,
            Marker(row.Kind),
            row.Operation.Offset,
            $"IL_{row.Operation.Offset:X4}",
            row.Operation.OpcodeFamily,
            row.Operation.Operand?.Kind,
            row.Operation.Operand?.Value,
            row.Operation.Display,
            row.Message);

    public static ImmutableArray<IlDiffDisplayRow> ToDisplayRows(IEnumerable<IlDiffRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(ToDisplayRow)];
    }

    public static IlDiffDisplayResult ToDisplayResult(IlBodyDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new IlDiffDisplayResult(
            result.Failure is { Length: > 0 } failure ? $"IL diff failed: {failure}" : null,
            ToDisplayRows(result.Rows));
    }

    public static ImmutableArray<string> ToUnifiedLines(IlBodyDiffResult result)
    {
        var display = ToDisplayResult(result);
        var rows = display.Rows.Select(row => row.UnifiedLine);
        return display.Failure is { } failure
            ? [failure, .. rows]
            : [.. rows];
    }

    public static string RenderUnified(IlBodyDiffResult result)
    {
        var lines = ToUnifiedLines(result);
        if (lines.IsEmpty)
            return "";

        var builder = new StringBuilder();
        foreach (string line in lines)
            builder.AppendLine(line);
        return builder.ToString().TrimEnd();
    }

    static string Marker(IlDiffKind kind)
        => kind switch
        {
            IlDiffKind.Add => "+",
            IlDiffKind.Remove => "-",
            IlDiffKind.Context => " ",
            _ => "?",
        };
}
