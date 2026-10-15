using DotnetInspector.Libraries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Presentation.Tests;

public class MemberGroupPresentationTests
{
    [Fact]
    public void WriteTree_StreamsTheCompleteOrderedOverloadPopulation()
    {
        MemberOverviewDocument document = CreateOverview(
            Shape(
                1,
                "Run()",
                "public",
                MemberReceiver.This),
            Shape(
                2,
                "Run<T>(T value)",
                "public",
                MemberReceiver.Static),
            Shape(
                3,
                "Run(this C value)",
                "internal",
                MemberReceiver.Extension));
        using var output = new StringWriter();

        MemberGroupPresentation.WriteTree(
            document,
            "Example.C",
            output);

        Assert.Equal(
            """
            method Example.C.Run (3 overloads)
            ├─ public Run()
            ├─ public static Run<T>(T value)
            └─ internal extension Run(this C value)

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void FormatTitle_UsesTheSingularOverloadLabel()
    {
        MemberDocument document = CreateExact(
            Shape(
                1,
                "Run()",
                "public",
                MemberReceiver.Static));

        string title = MemberGroupPresentation.FormatTitle(
            document.Subject.Group,
            "Example.C",
            1);

        Assert.Equal(
            "method Example.C.Run (1 overload)",
            title);
    }

    [Fact]
    public void WriteTree_RendersExactMemberAsSingletonPopulation()
    {
        MemberDocument document = CreateExact(
            Shape(
                1,
                "Run()",
                "public",
                MemberReceiver.Static));
        using var output = new StringWriter();

        MemberGroupPresentation.WriteTree(
            document,
            "Example.C",
            output);

        Assert.Equal(
            """
            method Example.C.Run (1 overload)
            └─ public static Run()

            """.ReplaceLineEndings(),
            output.ToString());
    }

    private static MemberOverviewDocument CreateOverview(
        params MemberOverloadShape[] rows)
    {
        MetadataTypeDefinitionName type = Name("Example", "C");
        var subject = new MemberGroupSubject(type, "Run");
        MemberOverloadPopulationBinding binding = Binding(type);
        return new(
            subject,
            binding,
            [
                .. rows.Select(row =>
                    Declaration(subject, row)),
            ]);
    }

    private static MemberDocument CreateExact(MemberOverloadShape row)
    {
        MemberGroupSubject subject =
            new(Name("Example", "C"), "Run");
        MemberDeclaration declaration = Declaration(subject, row);
        return new(
            declaration.Subject,
            declaration.DisplaySignature,
            declaration.CanonicalSignature,
            declaration.Accessibility,
            declaration.Receiver);
    }

    private static MemberDeclaration Declaration(
        MemberGroupSubject subject,
        MemberOverloadShape row) =>
        new(
            new(
                subject,
                row.Binding,
                row.MetadataToken,
                row.Anchor,
                row.BaselineOrdinal,
                row.Fingerprint,
                row.DocumentationId),
            row.DisplaySignature,
            row.CanonicalSignature,
            row.Accessibility,
            row.Receiver);

    private static MemberOverloadShape Shape(
        int ordinal,
        string displaySignature,
        string accessibility,
        MemberReceiver receiver)
    {
        MemberOverloadPopulationBinding binding =
            Binding(Name("Example", "C"));
        string canonicalSignature =
            $"void Example.C.{displaySignature}";
        string fingerprint = ordinal.ToString("x10");
        return new(
            MetadataToken: 0x06000000 + ordinal,
            new(
                $"Run:{ordinal}",
                canonicalSignature,
                fingerprint,
                "Example.C",
                "Run"),
            BaselineOrdinal: ordinal,
            Text(displaySignature),
            Text(canonicalSignature),
            Text($"M:Example.C.Run{ordinal}"),
            Text(fingerprint),
            Text(accessibility),
            MemberGroupRole.Declared,
            receiver,
            binding);
    }

    private static MemberOverloadPopulationBinding Binding(
        MetadataTypeDefinitionName type) =>
        new(
            new LibraryAssemblyIdentity(
                Text("Example"),
                new Version(1, 0, 0, 0),
                null,
                null),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            type,
            typeDefinitionToken: 0x02000001,
            "Run",
            MemberGroupCategory.Method,
            MemberGroupRole.Declared,
            MemberOverloadOrdering.Metadata);

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
