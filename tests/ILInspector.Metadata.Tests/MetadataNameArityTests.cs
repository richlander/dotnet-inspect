using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The canonical generic-arity rule (#4217). A backtick is a legal metadata-name
/// character, so recognising an arity suffix requires proving the canonical
/// <c>`N</c> form rather than truncating at the first backtick; truncation folds
/// distinct metadata names onto one another.
/// </summary>
public class MetadataNameArityTests
{
    [Theory]
    // Canonical: a backtick plus a decimal count, at the end of the segment.
    [InlineData("List`1", "List", 1)]
    [InlineData("Dictionary`2", "Dictionary", 2)]
    [InlineData("Wide`10", "Wide", 10)]
    // ECMA-335 II.22.20 gives GenericParam a 2-byte Number, so 65535 is the
    // largest count an image can declare — and the largest one this recognises.
    [InlineData("Wide`65535", "Wide", 65535)]
    [InlineData("Wide`65536", "Wide`65536", 0)]
    [InlineData("Wide`99999999999", "Wide`99999999999", 0)]
    // No suffix at all.
    [InlineData("Widget", "Widget", 0)]
    [InlineData("", "", 0)]
    // A literal, non-numeric suffix: the whole name is the identity.
    [InlineData("Widget`Literal", "Widget`Literal", 0)]
    [InlineData("Widget`1Extra", "Widget`1Extra", 0)]
    [InlineData("Widget`", "Widget`", 0)]
    // A metadata writer emits no suffix for a non-generic name, so `0 and any
    // leading zero are not the canonical form.
    [InlineData("Widget`0", "Widget`0", 0)]
    [InlineData("Widget`01", "Widget`01", 0)]
    // An empty simple name is not a generic name with a suffix.
    [InlineData("`1", "`1", 0)]
    // int.TryParse accepts surrounding whitespace and a sign; the canonical form
    // admits neither. char.IsDigit accepts non-ASCII digits; this admits only ASCII.
    [InlineData("Widget` 1", "Widget` 1", 0)]
    [InlineData("Widget`+1", "Widget`+1", 0)]
    [InlineData("Widget`\u0661", "Widget`\u0661", 0)]
    // Multiple backticks: the last one governs, so the earlier backtick stays in
    // the simple name and the name still round-trips.
    [InlineData("A`1`2", "A`1", 2)]
    [InlineData("A`Literal`1", "A`Literal", 1)]
    [InlineData("A`1`Literal", "A`1`Literal", 0)]
    public void Segment_RecognisesOnlyTheCanonicalAritySuffix(
        string segment,
        string expectedSimpleName,
        int expectedArity)
    {
        Assert.Equal(expectedSimpleName, MetadataNameArity.StripFromSegment(segment));
        Assert.Equal(expectedArity, MetadataNameArity.OfSegment(segment));
        Assert.Equal(
            expectedArity != 0,
            MetadataNameArity.TryReadSuffix(segment, out int arity, out int simpleNameLength));
        Assert.Equal(expectedArity, arity);
        Assert.Equal(expectedSimpleName.Length, simpleNameLength);
    }

    [Theory]
    // Each nested segment carries its own arity, and each is stripped on its own.
    [InlineData("Outer`1+Inner`2", "Outer+Inner")]
    [InlineData("Outer`1+Inner", "Outer+Inner")]
    [InlineData("Outer+Inner`1", "Outer+Inner")]
    [InlineData("Outer`1.Inner`2", "Outer.Inner")]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic.List")]
    [InlineData("System.Collections.Generic.Dictionary`2.Enumerator", "System.Collections.Generic.Dictionary.Enumerator")]
    // A literal backtick in one segment leaves that segment — and only it — intact.
    [InlineData("Outer`Literal+Inner`1", "Outer`Literal+Inner")]
    [InlineData("Ns.Widget`Literal", "Ns.Widget`Literal")]
    [InlineData("Widget", "Widget")]
    public void NestedName_StripsEachSegmentIndependently(string name, string expected)
        => Assert.Equal(expected, MetadataNameArity.StripFromNestedName(name));

    /// <summary>
    /// The collision the first-backtick truncation produced: two distinct metadata
    /// names reduced to one simple name, so anything keyed on that name — display
    /// candidates, shadowing sets, anchors — treated them as the same type.
    /// </summary>
    [Fact]
    public void DistinctMetadataNames_DoNotShareASimpleName()
    {
        Assert.NotEqual(
            MetadataNameArity.StripFromSegment("Widget"),
            MetadataNameArity.StripFromSegment("Widget`Literal"));
        Assert.NotEqual(
            MetadataNameArity.StripFromNestedName("Ns.Widget"),
            MetadataNameArity.StripFromNestedName("Ns.Widget`Literal"));
    }

    /// <summary>
    /// The same collision at the member-anchor identity that
    /// <see cref="ApiMemberIdentity"/> builds from a live image. The two type
    /// names cannot come from a C# compiler — a backtick is not a C# identifier
    /// character — so the image is written directly.
    /// </summary>
    [Fact]
    public void MemberAnchor_SeparatesTypesWhoseNamesDifferOnlyByALiteralBacktick()
    {
        byte[] image = BuildImageWithBacktickTypeNames();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();

        var anchors = new List<MemberAnchor>();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) is not ("Widget" or "Widget`Literal"))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                anchors.Add(
                    ApiMemberIdentity.CreateMethodAnchor(
                        reader,
                        typeHandle,
                        reader.GetMethodDefinition(methodHandle)));
            }
        }

        Assert.Equal(2, anchors.Count);
        Assert.Contains(anchors, anchor => anchor.TypeFullName == "Ns.Widget");
        Assert.Contains(anchors, anchor => anchor.TypeFullName == "Ns.Widget`Literal");
        Assert.Equal(
            2,
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    /// <summary>
    /// A minimal image declaring <c>Ns.Widget</c> and <c>Ns.Widget`Literal</c>,
    /// each with one <c>void M()</c>. Only the metadata is read, so the methods
    /// need no bodies.
    /// </summary>
    static byte[] BuildImageWithBacktickTypeNames()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("BacktickNames.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("BacktickNames"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(0, returnType => returnType.Void(), parameters => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);

        foreach (string typeName in (string[])["Widget", "Widget`Literal"])
        {
            MethodDefinitionHandle first = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signatureHandle,
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
                metadata.GetOrAddString("Ns"),
                metadata.GetOrAddString(typeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: first);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var builder = new BlobBuilder();
        pe.Serialize(builder);
        return builder.ToArray();
    }
}
