using InertText;

namespace DotnetInspector.Output;

/// <summary>
/// The destination contract shared by printable and scalar/URL/path projections.
/// A semantic row window has already been applied before projection, so its
/// presence prevents the destination from reinterpreting the active count as a
/// rendered-line window. Enforced by
/// <c>ProjectionDestination_DoesNotApplyALineWindowAfterSemanticRows</c>.
/// </summary>
public readonly record struct ProjectionDestination(
    string? OutputPath,
    RowWindow? RowWindow = null,
    bool ExactTransfer = false);

internal static class ProjectionDestinationWriter
{
    public static bool ValidateBeforeAcquisition(ProjectionDestination destination)
    {
        if (!destination.ExactTransfer
            || !IsFile(destination)
            || destination.RowWindow is not null
            || (CommandLineBuilder.HeadLines is null
                && CommandLineBuilder.TailLines is null))
        {
            return true;
        }

        CommandError.Write(
            "a rendered line limit cannot be combined with exact --out transfer because it would change the payload bytes.");
        return false;
    }

    public static void WriteText(ProjectionDestination destination, string output)
        => WriteText(destination, writer => writer.Write(output));

    public static void WriteRenderedText(
        ProjectionDestination destination,
        string output)
        => WriteText(
            destination,
            new InertString(TextPolicy.Prose, output).ToString());

    public static void WriteText(
        ProjectionDestination destination,
        Action<TextWriter> write)
    {
        OutputDestination.Write(
            destination.OutputPath,
            destination.RowWindow,
            writer =>
            {
                var normalized = new LfTextWriter(writer);
                write(normalized);
                normalized.Flush();
            });
    }

    public static void WriteExactBytes(ProjectionDestination destination, byte[] output)
    {
        if (!IsFile(destination))
            throw new InvalidOperationException("Exact projection bytes require an output path.");

        File.WriteAllBytes(destination.OutputPath!, output);
    }

    public static bool IsFile(ProjectionDestination destination)
        => !string.IsNullOrWhiteSpace(destination.OutputPath);
}
