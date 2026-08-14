using System.Globalization;

namespace DotnetInspector.Output;

internal static class OutputPathWriter
{
    public static void Write(
        string outputPath,
        string output,
        bool applyLineWindow = true)
    {
        if (!applyLineWindow
            || (CommandLineBuilder.HeadLines is null
                && CommandLineBuilder.TailLines is null))
        {
            File.WriteAllText(outputPath, output);
            return;
        }

        using var buffer = new StringWriter(CultureInfo.InvariantCulture)
        {
            NewLine = "\n",
        };

        if (CommandLineBuilder.HeadLines is int headLines)
        {
            using var writer = new LineLimitingTextWriter(buffer, headLines);
            writer.Write(output);
        }
        else
        {
            using var writer = new TailLineLimitingTextWriter(
                buffer,
                CommandLineBuilder.TailLines!.Value);
            writer.Write(output);
        }

        File.WriteAllText(outputPath, buffer.ToString());
    }
}
