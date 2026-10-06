using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeMemberInspectionDocumentTests
{
    [Fact]
    public void TypeOverviewDocument_RoundTripsCompactPopulation()
    {
        DocumentFixture fixture = CreateFixture();
        var original = new TypeOverviewDocument(
            fixture.Type,
            new(
                fixture.TypePopulation,
                null,
                new TypeMemberGroupRowsOutcome.Read(
                    TypeMemberGroupOrdering.Metadata,
                    [
                        new(
                            new(
                                fixture.TypePopulation,
                                Text("M"),
                                MemberGroupCategory.Method,
                                MemberGroupRole.Declared),
                            BaselineOrdinal: 1,
                            MemberGroupReceiverForms.This,
                            ExactMemberCount: 1),
                    ],
                    null),
                null,
                null),
            assemblyBytes: 123);

        string json = JsonSerializer.Serialize(
            original,
            TypeMemberInspectionDocumentJsonContext.Default
                .TypeOverviewDocument);
        TypeOverviewDocument copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .TypeOverviewDocument)!;

        Assert.Equal(original.Subject.Assembly, copy.Subject.Assembly);
        Assert.Equal(original.Subject.Type, copy.Subject.Type);
        Assert.Null(copy.Subject.LibraryCorrespondence);
        TypeMemberGroupRowsOutcome.Read rows =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                copy.Members.Rows);
        TypeMemberGroupShape row = Assert.Single(rows.Items);
        Assert.Equal(1, row.ExactMemberCount);
        Assert.Equal(
            TypeMemberGroupSpelling.CSharp,
            row.Binding.Population.Spelling);
        Assert.Contains("\"kind\":\"read\"", json);
    }

    [Fact]
    public void TypeDocument_RoundTripsCompleteMemberDeclarations()
    {
        DocumentFixture fixture = CreateFixture();
        var original = new TypeDocument(
            fixture.Type,
            fixture.TypePopulation,
            [fixture.Declaration],
            assemblyBytes: 123);

        string json = JsonSerializer.Serialize(
            original,
            TypeMemberInspectionDocumentJsonContext.Default.TypeDocument);
        TypeDocument copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .TypeDocument)!;

        MemberDeclaration member = Assert.Single(copy.Members);
        Assert.Equal(
            fixture.Declaration.CanonicalSignature,
            member.CanonicalSignature);
        Assert.Equal(
            fixture.Declaration.Subject.Population,
            member.Subject.Population);
        Assert.Equal(123, copy.AssemblyBytes);
    }

    [Fact]
    public void MemberOverviewDocument_RoundTripsExactPopulation()
    {
        DocumentFixture fixture = CreateFixture();
        var original = new MemberOverviewDocument(
            fixture.Group,
            fixture.MemberPopulation,
            [fixture.Declaration]);

        string json = JsonSerializer.Serialize(
            original,
            TypeMemberInspectionDocumentJsonContext.Default
                .MemberOverviewDocument);
        MemberOverviewDocument copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .MemberOverviewDocument)!;

        Assert.Equal(original.Subject, copy.Subject);
        Assert.Equal(original.Population, copy.Population);
        Assert.Equal(
            fixture.Declaration.DisplaySignature,
            Assert.Single(copy.Members).DisplaySignature);
    }

    [Fact]
    public void MemberDocument_RoundTripsOneExactDeclaration()
    {
        DocumentFixture fixture = CreateFixture();
        var original = new MemberDocument(
            fixture.Member,
            fixture.Declaration.DisplaySignature,
            fixture.Declaration.CanonicalSignature,
            fixture.Declaration.Accessibility,
            fixture.Declaration.Receiver);

        string json = JsonSerializer.Serialize(
            original,
            TypeMemberInspectionDocumentJsonContext.Default.MemberDocument);
        MemberDocument copy =
            JsonSerializer.Deserialize(
                json,
                TypeMemberInspectionDocumentJsonContext.Default
                    .MemberDocument)!;

        Assert.Equal(original.Subject, copy.Subject);
        Assert.Equal(original.CanonicalSignature, copy.CanonicalSignature);
        Assert.Equal(MemberReceiver.This, copy.Receiver);
        Assert.DoesNotContain("\"documentation\":", json);
        Assert.DoesNotContain("\"source\":", json);
    }

    [Fact]
    public void TypeDocument_RejectsMemberFromAnotherExactType()
    {
        DocumentFixture fixture = CreateFixture();
        MemberOverloadPopulationBinding wrongPopulation =
            new(
                fixture.MemberPopulation.Assembly,
                fixture.MemberPopulation.ModuleVersionId,
                fixture.MemberPopulation.DeclaringType,
                fixture.MemberPopulation.TypeDefinitionToken + 1,
                fixture.MemberPopulation.Name,
                fixture.MemberPopulation.Category,
                fixture.MemberPopulation.Role,
                fixture.MemberPopulation.Ordering,
                fixture.MemberPopulation.Accessibility,
                fixture.MemberPopulation.Receiver,
                fixture.MemberPopulation.IncludeHidden,
                fixture.MemberPopulation.Spelling);
        var wrongSubject = new MemberSubject(
            fixture.Group,
            wrongPopulation,
            fixture.Member.MetadataToken,
            fixture.Member.Anchor,
            fixture.Member.BaselineOrdinal,
            fixture.Member.Fingerprint,
            fixture.Member.DocumentationId);
        var wrongDeclaration = new MemberDeclaration(
            wrongSubject,
            fixture.Declaration.DisplaySignature,
            fixture.Declaration.CanonicalSignature,
            fixture.Declaration.Accessibility,
            fixture.Declaration.Receiver);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocument(
                    fixture.Type,
                    fixture.TypePopulation,
                    [wrongDeclaration],
                    assemblyBytes: 123));

        Assert.Equal("members", exception.ParamName);
    }

    [Fact]
    public void TypeDocument_RejectsDifferentPopulationIntent()
    {
        DocumentFixture fixture = CreateFixture();
        var allAccessibility =
            new TypeMemberGroupPopulationBinding(
                fixture.TypePopulation.Assembly,
                fixture.TypePopulation.ModuleVersionId,
                fixture.TypePopulation.Type,
                fixture.TypePopulation.TypeDefinitionToken,
                fixture.TypePopulation.Spelling,
                fixture.TypePopulation.IncludeHidden,
                TypeMemberGroupAccessibilityFilter.All,
                fixture.TypePopulation.Receiver,
                fixture.TypePopulation.Ordering);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => new TypeDocument(
                    fixture.Type,
                    allAccessibility,
                    [fixture.Declaration],
                    assemblyBytes: 123));

        Assert.Equal("members", exception.ParamName);
    }

    [Fact]
    public void TypeDocument_AcceptsMatchingNonExtensionIntent()
    {
        DocumentFixture fixture = CreateFixture();
        var typePopulation =
            new TypeMemberGroupPopulationBinding(
                fixture.TypePopulation.Assembly,
                fixture.TypePopulation.ModuleVersionId,
                fixture.TypePopulation.Type,
                fixture.TypePopulation.TypeDefinitionToken,
                fixture.TypePopulation.Spelling,
                fixture.TypePopulation.IncludeHidden,
                fixture.TypePopulation.Accessibility,
                TypeMemberGroupReceiverFilter.NonExtension,
                fixture.TypePopulation.Ordering);
        var memberPopulation =
            new MemberOverloadPopulationBinding(
                fixture.MemberPopulation.Assembly,
                fixture.MemberPopulation.ModuleVersionId,
                fixture.MemberPopulation.DeclaringType,
                fixture.MemberPopulation.TypeDefinitionToken,
                fixture.MemberPopulation.Name,
                fixture.MemberPopulation.Category,
                fixture.MemberPopulation.Role,
                fixture.MemberPopulation.Ordering,
                fixture.MemberPopulation.Accessibility,
                MemberOverloadReceiverFilter.NonExtension,
                fixture.MemberPopulation.IncludeHidden,
                fixture.MemberPopulation.Spelling);
        var member = new MemberSubject(
            fixture.Group,
            memberPopulation,
            fixture.Member.MetadataToken,
            fixture.Member.Anchor,
            fixture.Member.BaselineOrdinal,
            fixture.Member.Fingerprint,
            fixture.Member.DocumentationId);
        var declaration = new MemberDeclaration(
            member,
            fixture.Declaration.DisplaySignature,
            fixture.Declaration.CanonicalSignature,
            fixture.Declaration.Accessibility,
            fixture.Declaration.Receiver);

        var document = new TypeDocument(
            fixture.Type,
            typePopulation,
            [declaration],
            assemblyBytes: 123);

        Assert.Equal(
            MemberOverloadReceiverFilter.NonExtension,
            Assert.Single(document.Members)
                .Subject.Population.Receiver);
    }

    [Fact]
    public void
        MemberDeclaration_ComparesCanonicalSignatureAfterInertEncoding()
    {
        DocumentFixture fixture = CreateFixture();
        const string rawCanonical = "void N.C.\nM()";
        string fingerprint =
            MemberAnchor.ComputeFingerprint(rawCanonical);
        var member = new MemberSubject(
            fixture.Group,
            fixture.MemberPopulation,
            fixture.Member.MetadataToken,
            new(
                "M()",
                rawCanonical,
                fingerprint,
                "N.C",
                "M"),
            fixture.Member.BaselineOrdinal,
            Text(fingerprint),
            fixture.Member.DocumentationId);

        var declaration = new MemberDeclaration(
            member,
            fixture.Declaration.DisplaySignature,
            Text(rawCanonical),
            fixture.Declaration.Accessibility,
            fixture.Declaration.Receiver);

        Assert.NotEqual(
            rawCanonical,
            declaration.CanonicalSignature.ToString());
    }

    private static DocumentFixture CreateFixture()
    {
        MetadataTypeDefinitionName type = Name("N", "C");
        var assembly = new LibraryAssemblyIdentity(
            Text("Example"),
            new Version(1, 2, 3, 4),
            null,
            null);
        Guid moduleVersionId =
            Guid.Parse("11111111-2222-3333-4444-555555555555");
        const int typeDefinitionToken = 0x02000001;
        var subject = new TypeSubject(
            assembly,
            moduleVersionId,
            type,
            typeDefinitionToken,
            new TypeDocumentDeclarationSignature([]),
            MetadataTypeDeclarationCategory.Class,
            TypeAttributes.Public,
            isByRefLike: false,
            isReadOnly: false,
            definesCoreLibraryRoot: false,
            declaringTypeDefinitionToken: null);
        var typePopulation = new TypeMemberGroupPopulationBinding(
            assembly,
            moduleVersionId,
            type,
            typeDefinitionToken,
            TypeMemberGroupSpelling.CSharp,
            includeHidden: false,
            TypeMemberGroupAccessibilityFilter.Public,
            TypeMemberGroupReceiverFilter.All,
            TypeMemberGroupOrdering.Metadata);
        var group = new MemberGroupSubject(
            type,
            "M",
            MemberGroupCategory.Method,
            MemberGroupRole.Declared,
            TypeMemberGroupSpelling.CSharp);
        var memberPopulation = new MemberOverloadPopulationBinding(
            assembly,
            moduleVersionId,
            type,
            typeDefinitionToken,
            "M",
            MemberGroupCategory.Method,
            MemberGroupRole.Declared,
            MemberOverloadOrdering.Metadata,
            spelling: TypeMemberGroupSpelling.CSharp);
        var member = new MemberSubject(
            group,
            memberPopulation,
            metadataToken: 0x06000001,
            new(
                "M()",
                "void N.C.M()",
                "0123456789",
                "N.C",
                "M"),
            baselineOrdinal: 1,
            Text("0123456789"),
            Text("M:N.C.M"));
        var declaration = new MemberDeclaration(
            member,
            Text("void M()"),
            Text("void N.C.M()"),
            Text("public"),
            MemberReceiver.This);
        return new(
            subject,
            typePopulation,
            group,
            memberPopulation,
            member,
            declaration);
    }

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

    private sealed record DocumentFixture(
        TypeSubject Type,
        TypeMemberGroupPopulationBinding TypePopulation,
        MemberGroupSubject Group,
        MemberOverloadPopulationBinding MemberPopulation,
        MemberSubject Member,
        MemberDeclaration Declaration);
}
