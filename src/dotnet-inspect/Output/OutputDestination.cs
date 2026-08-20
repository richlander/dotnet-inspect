using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Options;

namespace DotnetInspector.Output;

internal static class OutputDestination
{
    public static void Write(
        string? outputPath,
        RowWindow? rowWindow,
        Action<TextWriter> write)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            var console = new LfTextWriter(Console.Out);
            write(console);
            console.Flush();
            return;
        }

        using var output = new StreamWriter(
            outputPath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };
        CountingTextWriter? countingWriter = null;
        TextWriter destination = output;
        if (InfoTracker.Enabled)
        {
            countingWriter = new CountingTextWriter(output);
            destination = countingWriter;
        }
        destination = new LfTextWriter(destination);

        TailLineLimitingTextWriter? tailWriter = null;
        bool hasLineWindow = false;
        if (rowWindow is null
            && CommandLineBuilder.HeadLines is int headLines)
        {
            destination = new LineLimitingTextWriter(
                destination,
                headLines);
            hasLineWindow = true;
        }

        if (rowWindow is null
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

        try
        {
            write(destination);
            tailWriter?.FlushTail();
            destination.Flush();
        }
        finally
        {
            if (countingWriter is not null)
                InfoTracker.RecordOutputChars(countingWriter.CharCount);
        }
    }
}
