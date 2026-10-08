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
        MemberGroupDocument document = CreateDocument(
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
        MemberGroupDocument document = CreateDocument(
            Shape(
                1,
                "Run()",
                "public",
                MemberReceiver.Static));

        string title = MemberGroupPresentation.FormatTitle(
            document.Subject,
            "Example.C",
            1);

        Assert.Equal(
            "method Example.C.Run (1 overload)",
            title);
    }

    [Fact]
    public void WriteTree_RejectsCountAndRowsThatDescribeDifferentPopulations()
    {
        MemberGroupDocument complete = CreateDocument(
            Shape(
                1,
                "Run()",
                "public",
                MemberReceiver.This));
        var document = new MemberGroupDocument(
            complete.Subject,
            new(
                complete.Overloads.Binding,
                new MemberOverloadCountOutcome.Counted(2),
                complete.Overloads.Rows));
        using var output = new StringWriter();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                MemberGroupPresentation.WriteTree(
                    document,
                    "Example.C",
                    output));

        Assert.Equal(
            "Member-group Count and Rows must describe the same population.",
            exception.Message);
        Assert.Empty(output.ToString());
    }

    private static MemberGroupDocument CreateDocument(
        params MemberOverloadShape[] rows)
    {
        MetadataTypeDefinitionName type = Name("Example", "C");
        var subject = new MemberGroupSubject(type, "Run");
        MemberOverloadPopulationBinding binding = Binding(type);
        var result = new MemberOverloadPopulationResult(
            binding,
            new MemberOverloadCountOutcome.Counted(rows.Length),
            new MemberOverloadRowsOutcome.Read(
                MemberOverloadOrdering.Metadata,
                [.. rows],
                Continuation: null));
        return new(subject, result);
    }

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
