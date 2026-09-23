using DotnetInspect.Cli.Models;
using InertText;

namespace DotnetInspect.Cli.Output;

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
    private const int ExactTransferBufferSize = 64 * 1024;

    public static bool ValidateBeforeAcquisition(ProjectionDestination destination)
        => ValidateBeforeDestinationMutation(destination);

    public static bool ValidateBeforeDestinationMutation(
        ProjectionDestination destination)
    {
        bool hasLineWindow =
            CommandLineBuilder.HeadLines is not null
            || CommandLineBuilder.TailLines is not null;
        if (!destination.ExactTransfer
            || !IsFile(destination)
            || !hasLineWindow
            || (destination.RowWindow is not null
                && !CommandLineBuilder.LineWindowExplicitlySet))
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

    public static async Task WriteExactBytesAsync(
        ProjectionDestination destination,
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (IsFile(destination))
        {
            await using var output = new FileStream(
                destination.OutputPath!,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await input.CopyToAsync(
                    output,
                    ExactTransferBufferSize,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        Stream standardOutput = Console.OpenStandardOutput();
        await input.CopyToAsync(
                standardOutput,
                ExactTransferBufferSize,
                cancellationToken)
            .ConfigureAwait(false);
        await standardOutput.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public static bool IsFile(ProjectionDestination destination)
        => !string.IsNullOrEmpty(destination.OutputPath);
}
