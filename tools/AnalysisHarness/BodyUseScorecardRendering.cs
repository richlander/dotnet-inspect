using System.Globalization;
using System.Text;

namespace ILInspector.AnalysisHarness;

static class BodyUseScorecardRendering
{
    internal static void AppendWorkShapes(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardWorkShape> workShapes)
    {
        text.AppendLine(
            "End-to-end work shape. Timings include Type-inventory admission, "
                + "method traversal, body decoding, logical-owner attribution, "
                + "operand binding, occurrence admission, diagnostics, and "
                + "terminal closing.");
        text.AppendLine();
        text.AppendLine(
            "| Asset | Terminal | Result | Disposition "
                + "| Bodies considered | Operands considered |");
        text.AppendLine(
            "| --- | --- | ---: | --- | ---: | ---: |");
        foreach (BodyUseScorecardWorkShape shape in workShapes)
        {
            text.Append("| ")
                .Append(shape.Asset)
                .Append(" | ")
                .Append(shape.Closing)
                .Append(" | ")
                .Append(FormatTerminalValue(shape))
                .Append(" | ")
                .Append(shape.Disposition)
                .Append(" | ")
                .Append(
                    shape.Coverage.BodiesConsidered.ToString(
                        "N0",
                        CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(
                    shape.Coverage.OperandsConsidered.ToString(
                        "N0",
                        CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }
        text.AppendLine();
    }

    internal static void AppendDeltas(
        StringBuilder text,
        IReadOnlyList<BodyUseScorecardCell> cells)
    {
        text.AppendLine();
        text.AppendLine(
            "Absolute end-to-end deltas from Planner; positive values are "
                + "slower or allocate more.");
        text.AppendLine();
        text.AppendLine(
            "| Asset | Terminal | Column | Time delta (us) "
                + "| Allocation delta (bytes) |");
        text.AppendLine("| --- | --- | --- | ---: | ---: |");
        foreach (BodyUseScorecardCell cell in cells)
        {
            if (cell.Column == BodyUseScorecardColumn.Planner)
                continue;
            BodyUseScorecardCell baseline =
                cells.Single(candidate =>
                    candidate.AssetIndex == cell.AssetIndex
                    && candidate.Closing == cell.Closing
                    && candidate.Column
                        == BodyUseScorecardColumn.Planner);
            text.Append("| ")
                .Append(cell.Asset)
                .Append(" | ")
                .Append(cell.Closing)
                .Append(" | ")
                .Append(Name(cell.Column))
                .Append(" | ")
                .Append(
                    FormatSigned(
                        cell.MedianMicroseconds
                            - baseline.MedianMicroseconds))
                .Append(" | ")
                .Append(
                    FormatSigned(
                        cell.MedianAllocatedBytes
                            - baseline.MedianAllocatedBytes))
                .AppendLine(" |");
        }
        text.AppendLine();
    }

    internal static void WriteWorkShapeFields(
        TextWriter writer,
        BodyUseScorecardCell cell,
        IReadOnlyList<BodyUseScorecardWorkShape> workShapes)
    {
        BodyUseScorecardWorkShape shape =
            workShapes.Single(candidate =>
                candidate.Asset == cell.Asset
                && candidate.Closing == cell.Closing);
        writer.Write('\t');
        writer.Write(
            shape.TerminalValue.ToString(
                CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(shape.Disposition);
        writer.Write('\t');
        writer.Write(
            shape.Coverage.BodiesConsidered.ToString(
                CultureInfo.InvariantCulture));
        writer.Write('\t');
        writer.Write(
            shape.Coverage.OperandsConsidered.ToString(
                CultureInfo.InvariantCulture));
    }

    static string FormatTerminalValue(
        BodyUseScorecardWorkShape shape) =>
        shape.Closing == BodyUseScorecardClosing.Exists
            ? (shape.TerminalValue != 0).ToString()
            : shape.TerminalValue.ToString(
                "N0",
                CultureInfo.InvariantCulture);

    static string FormatSigned(double value) =>
        value.ToString(
            "+#,0.0;-#,0.0;0.0",
            CultureInfo.InvariantCulture);

    static string FormatSigned(long value) =>
        value > 0
            ? "+" + value.ToString(
                "N0",
                CultureInfo.InvariantCulture)
            : value.ToString(
                "N0",
                CultureInfo.InvariantCulture);

    static string Name(BodyUseScorecardColumn column) =>
        column == BodyUseScorecardColumn.Linq
            ? "LINQ"
            : column.ToString();
}
