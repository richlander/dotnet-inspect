using System.Text;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Output;

internal static class OutputDestination
{
    public static void Write(
        string? outputPath,
        RowWindow? rowWindow,
        Action<TextWriter> write)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            write(Console.Out);
            return;
        }

        using var output = new StreamWriter(
            outputPath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };
        TextWriter destination = new LfTextWriter(output);

        TailLineLimitingTextWriter? tailWriter = null;
        bool hasLineWindow = false;
        if ((rowWindow is null
                || CommandLineBuilder.LineWindowExplicitlySet)
            && CommandLineBuilder.HeadLines is int headLines)
        {
            destination = new LineLimitingTextWriter(
                destination,
                headLines);
            hasLineWindow = true;
        }

        if ((rowWindow is null
                || CommandLineBuilder.LineWindowExplicitlySet)
            && CommandLineBuilder.TailLines is int tailLines)
        {
            tailWriter = new TailLineLimitingTextWriter(
                destination,
                tailLines);
            destination = tailWriter;
            hasLineWindow = true;
        }

        if (hasLineWindow)
            destination = TextWriter.Synchronized(destination);

        write(destination);
        tailWriter?.FlushTail();
        destination.Flush();
    }
}
