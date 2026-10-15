using CSharpText;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

public static class MemberGroupPresentation
{
    public static void WriteTree(
        MemberOverviewDocument document,
        string declaringTypeDisplay,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        WriteTree(
            document.Subject,
            document.Members,
            declaringTypeDisplay,
            output);
    }

    public static void WriteTree(
        MemberDocument document,
        string declaringTypeDisplay,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        WriteTree(
            document.Subject.Group,
            [document],
            declaringTypeDisplay,
            output);
    }

    private static void WriteTree(
        MemberGroupSubject subject,
        IReadOnlyList<MemberDeclaration> members,
        string declaringTypeDisplay,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringTypeDisplay);
        ArgumentNullException.ThrowIfNull(output);
        if (members.Count == 0)
            throw new ArgumentException(
                "Member-group Tree presentation requires at least one declaration.",
                nameof(members));

        output.WriteLine(
            FormatTitle(subject, declaringTypeDisplay, members.Count));
        var writer = new MarkoutWriter(output, new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            for (int index = 0; index < members.Count; index++)
            {
                tree.WriteNode(
                    FormatOverload(members[index]),
                    isLastSibling: index == members.Count - 1);
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
        return FormatOverload(
            row.Accessibility.ToString(),
            row.Receiver,
            row.DisplaySignature.ToString());
    }

    public static string FormatOverload(MemberDeclaration member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return FormatOverload(
            member.Accessibility.ToString(),
            member.Receiver,
            member.DisplaySignature.ToString());
    }

    private static string FormatOverload(
        string accessibility,
        MemberReceiver receiver,
        string displaySignature)
    {
        string receiverPrefix = receiver switch
        {
            MemberReceiver.Static => "static ",
            MemberReceiver.Extension => "extension ",
            MemberReceiver.This => "",
            _ => throw new InvalidOperationException(
                "Unknown Member receiver."),
        };
        return CSharpIdentifier.ContainRenderedText(
            $"{accessibility} {receiverPrefix}{displaySignature}");
    }
}
