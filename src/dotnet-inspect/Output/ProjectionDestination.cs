using DotnetInspector.Models;
using InertText;

namespace DotnetInspector.Output;

/// <summary>
/// The destination contract shared by printable and scalar/URL/path projections.
/// </summary>
/// <remarks>
/// Item windows select declared rows before projection. Rendered-line windows
/// are a separate dimension and still apply to the projected destination.
/// Exact transfers reject line windows before acquisition instead of changing
/// payload bytes.
/// </remarks>
public readonly record struct ProjectionDestination(
    string? OutputPath,
    bool ExactTransfer = false);

internal static class ProjectionDestinationWriter
{
    public static bool ValidateBeforeAcquisition(ProjectionDestination destination)
    {
        if (!destination.ExactTransfer
            || !IsFile(destination)
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

    public static void WriteSelectedText(
        ProjectionDestination destination,
        ContainmentSelectedText output)
        => WriteText(destination, output.ToString());

    public static void WriteText(
        ProjectionDestination destination,
        Action<TextWriter> write)
    {
        OutputDestination.Write(
            destination.OutputPath,
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
        => !string.IsNullOrEmpty(destination.OutputPath);
}
