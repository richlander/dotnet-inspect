using CSharpText;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

public static class MemberGroupPresentation
{
    public static void WriteTree(
        MemberGroupDocument document,
        string declaringTypeDisplay,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeDisplay);
        ArgumentNullException.ThrowIfNull(output);

        MemberOverloadCountOutcome.Counted count = document.Overloads.Count
            as MemberOverloadCountOutcome.Counted
            ?? throw new InvalidOperationException(
                "Member-group Tree presentation requires a Count.");
        MemberOverloadRowsOutcome.Read rows = document.Overloads.Rows
            as MemberOverloadRowsOutcome.Read
            ?? throw new InvalidOperationException(
                "Member-group Tree presentation requires Rows.");

        if (!rows.IsComplete)
        {
            throw new InvalidOperationException(
                "Member-group Tree presentation requires complete Rows.");
        }
        if (rows.Items.Length != count.Value)
        {
            throw new InvalidOperationException(
                "Member-group Count and Rows must describe the same population.");
        }

        output.WriteLine(
            FormatTitle(document.Subject, declaringTypeDisplay, count.Value));
        var writer = new MarkoutWriter(output, new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            for (int index = 0; index < rows.Items.Length; index++)
            {
                tree.WriteNode(
                    FormatOverload(rows.Items[index]),
                    isLastSibling: index == rows.Items.Length - 1);
            }
        });
        writer.Flush();
    }

    public static string FormatTitle(
        MemberGroupSubject subject,
        string declaringTypeDisplay,
        int count)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeDisplay);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        string countSuffix = count == 1 ? "1 overload" : $"{count} overloads";
        return $"{subject.Category.ToString().ToLowerInvariant()} "
            + $"{CSharpIdentifier.ContainRenderedText(declaringTypeDisplay)}."
            + $"{CSharpIdentifier.ContainRenderedText(subject.Name)} "
            + $"({countSuffix})";
    }

    public static string FormatOverload(MemberOverloadShape row)
    {
        ArgumentNullException.ThrowIfNull(row);

        string receiverPrefix = row.Receiver switch
        {
            MemberReceiver.Static => "static ",
            MemberReceiver.Extension => "extension ",
            MemberReceiver.This => "",
            _ => throw new InvalidOperationException(
                "Unknown Member receiver."),
        };
        return CSharpIdentifier.ContainRenderedText(
            $"{row.Accessibility} {receiverPrefix}{row.DisplaySignature}");
    }
}
