using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class MemberExplanationOutput
{
    internal static int WritePrimary(
        InspectionEnvelope<MemberContextualExplanationDocument> explanation,
        OutputFormat format)
    {
        if (format is not (
                OutputFormat.Markdown
                or OutputFormat.PlainText))
        {
            CommandError.Write(
                "Member explanation supports Markdown and plain text output.");
            return 1;
        }

        MarkoutSerializer.Serialize(
            ResourceExplanationView.Create(explanation.Content),
            Console.Out,
            format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(),
            ResourceExplanationViewContext.Default);
        return 0;
    }

    internal static void WriteCompanion(
        InspectionEnvelope<MemberContextualExplanationDocument> explanation)
    {
        Console.Out.Flush();
        using var buffer = new StringWriter();
        MarkoutSerializer.Serialize(
            ResourceExplanationView.Create(explanation.Content),
            buffer,
            new PlainTextFormatter(),
            ResourceExplanationViewContext.Default);

        CommandError.WriteBlankLine();
        foreach (string line in buffer.ToString()
            .ReplaceLineEndings("\n")
            .Split('\n'))
        {
            CommandError.WriteLine(line);
        }
    }
}
