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

public sealed record IlDiffDisplayFailureRow(
    IlDiffFailureKind Kind,
    string Message,
    string? Side,
    string? Detail)
{
    public string UnifiedLine => $"IL diff failed: {Message}";
}

public sealed record IlDiffDisplayResult(
    string? Failure,
    ImmutableArray<IlDiffDisplayRow> Rows,
    ImmutableArray<IlDiffDisplayFailureRow> FailureRows = default)
{
    public bool IsEmpty => Failure is null && Rows.IsDefaultOrEmpty && FailureRows.IsDefaultOrEmpty;
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

    public static IlDiffDisplayFailureRow ToDisplayFailureRow(IlDiffFailureRow row)
        => new(row.Kind, row.Message, row.Side, row.Detail);

    public static ImmutableArray<IlDiffDisplayFailureRow> ToDisplayFailureRows(IEnumerable<IlDiffFailureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(ToDisplayFailureRow)];
    }

    public static IlDiffDisplayResult ToDisplayResult(IlBodyDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ImmutableArray<IlDiffDisplayFailureRow> failureRows = result.FailureRows.IsDefault
            ? []
            : ToDisplayFailureRows(result.FailureRows);
        string? failure = !failureRows.IsDefaultOrEmpty
            ? failureRows[0].UnifiedLine
            : result.Failure is { Length: > 0 } legacyFailure ? $"IL diff failed: {legacyFailure}" : null;
        return new IlDiffDisplayResult(
            failure,
            result.Rows.IsDefault ? [] : ToDisplayRows(result.Rows),
            failureRows);
    }

    public static ImmutableArray<string> ToUnifiedLines(IlBodyDiffResult result)
        => ToUnifiedLines(ToDisplayResult(result));

    public static ImmutableArray<string> ToUnifiedLines(IlDiffDisplayResult display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var rows = display.Rows.IsDefault
            ? []
            : display.Rows.Select(row => row.UnifiedLine);
        var failureRows = display.FailureRows.IsDefault
            ? []
            : display.FailureRows.Select(row => row.UnifiedLine);
        if (failureRows.Any())
            return [.. failureRows, .. rows];
        return display.Failure is { } failure
            ? [failure, .. rows]
            : [.. rows];
    }

    public static string RenderUnified(IlBodyDiffResult result)
        => RenderUnified(ToDisplayResult(result));

    public static string RenderUnified(IlDiffDisplayResult result)
    {
        var lines = ToUnifiedLines(result);
        if (lines.IsEmpty)
            return "";

        var builder = new StringBuilder();
        foreach (string line in lines)
            builder.AppendLf(line);
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
