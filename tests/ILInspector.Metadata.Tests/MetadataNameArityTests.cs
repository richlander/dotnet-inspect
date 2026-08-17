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
    // ECMA-335 II.22.20 gives GenericParam a 2-byte *zero-based* Number, so the
    // highest index is 65535 and a name can declare 65536 parameters. 65536 is
    // therefore the largest count this recognises; 65537 is not an arity.
    [InlineData("Wide`65535", "Wide", 65535)]
    [InlineData("Wide`65536", "Wide", 65536)]
    [InlineData("Wide`65537", "Wide`65537", 0)]
    [InlineData("Wide`99999", "Wide`99999", 0)]
    [InlineData("Wide`99999999999", "Wide`99999999999", 0)]
    [InlineData("Wide`2147483647", "Wide`2147483647", 0)]
    [InlineData("Wide`2147483648", "Wide`2147483648", 0)]
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
    // A nested metadata name nests with '+' only, and each segment carries its own
    // arity.
    [InlineData("Outer`1+Inner`2", "Outer+Inner")]
    [InlineData("Outer`1+Inner", "Outer+Inner")]
    [InlineData("Outer+Inner`1", "Outer+Inner")]
    [InlineData("List`1", "List")]
    // A '.' is an ordinary character in a metadata name, not a boundary. The
    // trailing component of these names carries no canonical suffix, so nothing
    // is stripped and the identity survives — the whole point of #4217's
    // boundary split.
    [InlineData("<>c__DisplayClass1`1.Foo", "<>c__DisplayClass1`1.Foo")]
    [InlineData("A`1.B", "A`1.B")]
    [InlineData("Outer`1+Inner`1.Trailing", "Outer+Inner`1.Trailing")]
    [InlineData("Odd.Name`2", "Odd.Name")]
    // A literal backtick in one segment leaves that segment — and only it — intact.
    [InlineData("Outer`Literal+Inner`1", "Outer`Literal+Inner")]
    [InlineData("Widget", "Widget")]
    public void NestedMetadataName_ParsesOnlyPlusBoundaries(string name, string expected)
        => Assert.Equal(expected, MetadataNameArity.StripFromNestedName(name));

    [Theory]
    // ApiType.Name flattens a nested chain with '.', so that spelling — and only
    // that spelling — parses dot boundaries. A namespace is never part of it.
    [InlineData("Outer`1.Inner`2", "Outer.Inner")]
    [InlineData("Outer`1.Inner", "Outer.Inner")]
    [InlineData("List`1", "List")]
    [InlineData("Widget`Literal", "Widget`Literal")]
    // '+' is name text in this spelling, so a literal '+' is preserved and its
    // component is parsed as a whole.
    [InlineData("Weird+Name`1", "Weird+Name")]
    [InlineData("Weird`1+Name", "Weird`1+Name")]
    public void DottedChain_ParsesOnlyDotBoundaries(string chain, string expected)
        => Assert.Equal(expected, MetadataNameArity.StripFromDottedChain(chain));

    /// <summary>
    /// The two nesting spellings answer differently for the same text, which is
    /// exactly why the contract is split: a caller that picks the wrong one
    /// rewrites a name it was supposed to preserve.
    /// </summary>
    [Fact]
    public void NestedAndDottedContracts_DisagreeWhereTheDelimiterIsNameText()
    {
        Assert.Equal("A`1.B", MetadataNameArity.StripFromNestedName("A`1.B"));
        Assert.Equal("A.B", MetadataNameArity.StripFromDottedChain("A`1.B"));
        Assert.Equal("A+B", MetadataNameArity.StripFromNestedName("A`1+B"));
        Assert.Equal("A`1+B", MetadataNameArity.StripFromDottedChain("A`1+B"));

        // A name whose text contains a dot keeps its identity under the metadata
        // contract, so it cannot be mistaken for the nested pair (A`1, B).
        Assert.NotEqual(
            MetadataNameArity.StripFromNestedName("A`1.B"),
            MetadataNameArity.StripFromNestedName("A`1+B"));
    }

    [Theory]
    // Flattened search/display text: both delimiters are treated as boundaries,
    // and a component is only ever shortened by its own canonical suffix — the
    // rest of the name is never truncated.
    [InlineData("Ns.Widget`1", "Ns.Widget")]
    [InlineData("Ns.Outer`1+Inner`1", "Ns.Outer+Inner")]
    [InlineData("Ns`1.Widget", "Ns.Widget")]
    [InlineData("Ns.Widget`1Extra", "Ns.Widget`1Extra")]
    public void FlattenedName_StripsEveryComponentWithoutTruncating(string name, string expected)
        => Assert.Equal(expected, MetadataNameArity.StripFromFlattenedName(name));

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
            MetadataNameArity.StripFromDottedChain("Ns.Widget"),
            MetadataNameArity.StripFromDottedChain("Ns.Widget`Literal"));
    }

    /// <summary>
    /// The component walk every rewriting consumer shares: boundaries, canonical
    /// arity, and the exact span each component contributes.
    /// </summary>
    [Fact]
    public void ComponentWalk_ReportsBoundariesAndCanonicalArity()
    {
        const string name = "Ns.Outer`2+Inner`Literal";
        var components = new List<(string Component, string SimpleName, int Arity, char? Delimiter)>();
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(name))
        {
            components.Add((
                name.Substring(component.Start, component.Length),
                name.Substring(component.Start, component.SimpleNameLength),
                component.Arity,
                component.Delimiter));
        }

        Assert.Equal(
            [
                ("Ns", "Ns", 0, '.'),
                ("Outer`2", "Outer", 2, '+'),
                ("Inner`Literal", "Inner`Literal", 0, null),
            ],
            components);
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

    [Fact]
    public void MemberAnchor_PreservesDeclaredArityWhenGenericParamCountDisagrees()
    {
        byte[] image = BuildImageWithMismatchedArityNames();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();

        var anchors = new List<MemberAnchor>();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) is not ("Widget`1" or "Widget`2"))
                continue;
            MethodDefinitionHandle methodHandle = Assert.Single(type.GetMethods());
            anchors.Add(
                ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    typeHandle,
                    reader.GetMethodDefinition(methodHandle)));
        }

        Assert.Equal(2, anchors.Count);
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "Ns.Widget<T>");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "Ns.Widget`2<T>");
        Assert.Equal(
            2,
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void MemberAnchor_EscapesLiteralBoundariesAndUsesIntroducedArity()
    {
        byte[] image = BuildImageWithStructuredAnchorNames();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();

        var anchors = reader.TypeDefinitions
            .SelectMany(typeHandle =>
                reader.GetTypeDefinition(typeHandle)
                    .GetMethods()
                    .Select(methodHandle =>
                        ApiMemberIdentity.CreateMethodAnchor(
                            reader,
                            typeHandle,
                            reader.GetMethodDefinition(methodHandle))))
            .ToArray();

        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == @"Ns.A\.B");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "Ns.A+B");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName
                == "Ns.Outer<T>+Inner<U>");
        Assert.Equal(
            anchors.Length,
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Distinct(StringComparer.Ordinal)
                .Count());

        MemberAnchor[] modelAnchors = reader.TypeDefinitions
            .SelectMany(typeHandle =>
            {
                ApiType type = MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    typeHandle,
                    includeNonPublicMembers: true);
                return type.Members.Select(
                    member => ApiMemberIdentity.GetMemberAnchor(type, member));
            })
            .ToArray();
        Assert.Equal(
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Order(StringComparer.Ordinal),
            modelAnchors.Select(anchor => anchor.CanonicalSignature)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NestedMismatchedArity_LiveAndModelAnchorsAgree()
    {
        byte[] image = BuildImageWithNestedMismatchedArity();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle innerHandle = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "Inner`5");
        TypeDefinition inner = reader.GetTypeDefinition(innerHandle);
        MethodDefinitionHandle methodHandle = Assert.Single(inner.GetMethods());

        MemberAnchor live = ApiMemberIdentity.CreateMethodAnchor(
            reader,
            innerHandle,
            reader.GetMethodDefinition(methodHandle));
        ApiType model = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            innerHandle,
            includeNonPublicMembers: true);
        MemberAnchor projected = ApiMemberIdentity.GetMemberAnchor(
            model,
            Assert.Single(model.Members));

        Assert.Equal([1, 1], model.IntroducedTypeParameterCounts);
        Assert.Equal(live, projected);
    }

    [Fact]
    public void DecreasingNestedGenericParameterCounts_AreRejected()
    {
        byte[] image = BuildImageWithDecreasingGenericParameterCounts();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle leafHandle = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "Leaf`1");
        TypeDefinition leaf = reader.GetTypeDefinition(leafHandle);
        MethodDefinitionHandle methodHandle = Assert.Single(leaf.GetMethods());

        Assert.Throws<BadImageFormatException>(() =>
            ApiMemberIdentity.CreateMethodAnchor(
                reader,
                leafHandle,
                reader.GetMethodDefinition(methodHandle)));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                leafHandle,
                includeNonPublicMembers: true));
    }

    [Theory]
    [InlineData((ushort)1, (ushort)0)]
    [InlineData((ushort)0, (ushort)0)]
    [InlineData((ushort)0, (ushort)2)]
    public void NonCanonicalGenericParameterIndices_AreRejected(
        ushort firstIndex,
        ushort secondIndex)
    {
        byte[] image = BuildImageWithGenericParameterIndices(
            firstIndex,
            secondIndex);
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name) == "Broken`2");
        MethodDefinitionHandle methodHandle = Assert.Single(
            reader.GetTypeDefinition(typeHandle).GetMethods());

        Assert.Throws<BadImageFormatException>(() =>
            ApiMemberIdentity.CreateMethodAnchor(
                reader,
                typeHandle,
                reader.GetMethodDefinition(methodHandle)));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeParameters(
                reader,
                reader.GetTypeDefinition(typeHandle)));
    }

    [Fact]
    public void MemberAnchor_SeparatesStructuralAndMissingArityBoundaries()
    {
        byte[] image = BuildImageWithAnchorBoundaryCollisions();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();

        MemberAnchor[] anchors = reader.TypeDefinitions
            .SelectMany(typeHandle =>
                reader.GetTypeDefinition(typeHandle)
                    .GetMethods()
                    .Select(methodHandle =>
                        ApiMemberIdentity.CreateMethodAnchor(
                            reader,
                            typeHandle,
                            reader.GetMethodDefinition(methodHandle))))
            .ToArray();

        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "N.A.B");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "N.A+B");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "N.Widget:0<T>");
        Assert.Contains(
            anchors,
            anchor => anchor.TypeFullName == "N.Widget<T>");
        Assert.Equal(
            anchors.Length,
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Distinct(StringComparer.Ordinal)
                .Count());

        var modelSignatures = reader.TypeDefinitions
            .SelectMany(typeHandle =>
            {
                ApiType type = MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    typeHandle,
                    includeNonPublicMembers: true);
                return type.Members.Select(
                    member => ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .CanonicalSignature);
            })
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            anchors.Select(anchor => anchor.CanonicalSignature)
                .Order(StringComparer.Ordinal),
            modelSignatures);
    }

    [Fact]
    public void ExactDecorationLikeSegment_RemainsRawThroughGenericDecoders()
    {
        byte[] image = BuildImageWithDecorationLikeTypeName();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle definition =
            reader.TypeDefinitions.Single(
                handle => reader.GetString(
                    reader.GetTypeDefinition(handle).Name) == "Inner`2[]");
        TypeReferenceHandle reference = Assert.Single(reader.TypeReferences);
        ImmutableArray<string> arguments = ["int", "string"];

        var stringDecoder = new SignatureDecoder();
        string definitionName = stringDecoder.GetTypeFromDefinition(
            reader,
            definition,
            0);
        string referenceName = stringDecoder.GetTypeFromReference(
            reader,
            reference,
            0);
        Assert.Equal(
            "Ns.Inner`2[]<int, string>",
            stringDecoder.GetGenericInstantiation(
                definitionName,
                arguments));
        Assert.Equal(
            "Ns.Inner`2[]<int, string>",
            stringDecoder.GetGenericInstantiation(
                referenceName,
                arguments));

        TypeNodeProvider nodeProvider = TypeNodeProvider.Instance;
        ImmutableArray<TypeNode> nodeArguments =
        [
            nodeProvider.GetPrimitiveType(PrimitiveTypeCode.Int32),
            nodeProvider.GetPrimitiveType(PrimitiveTypeCode.String),
        ];
        Assert.Equal(
            "Ns.Inner`2[]<int, string>",
            nodeProvider.GetGenericInstantiation(
                nodeProvider.GetTypeFromDefinition(reader, definition, 0),
                nodeArguments)
                .Render());
        Assert.Equal(
            "Ns.Inner`2[]<int, string>",
            nodeProvider.GetGenericInstantiation(
                nodeProvider.GetTypeFromReference(reader, reference, 0),
                nodeArguments)
                .Render());
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

    static byte[] BuildImageWithMismatchedArityNames()
    {
        var metadata = CreateMetadata("MismatchedArityNames");
        BlobHandle signatureHandle = AddVoidMethodSignature(metadata);
        var types = new List<TypeDefinitionHandle>();
        foreach (string typeName in (string[])["Widget`1", "Widget`2"])
        {
            MethodDefinitionHandle first = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signatureHandle,
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            types.Add(metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Interface,
                metadata.GetOrAddString("Ns"),
                metadata.GetOrAddString(typeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: first));
        }

        foreach (TypeDefinitionHandle type in types)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }
        return Serialize(metadata);
    }

    static byte[] BuildImageWithNestedMismatchedArity()
    {
        var metadata = CreateMetadata("NestedMismatchedArity");
        BlobHandle signature = AddVoidMethodSignature(metadata);
        MethodDefinitionHandle method = AddMethod(metadata, signature);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Inner`5"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        metadata.AddNestedType(inner, outer);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            index: 1);

        return Serialize(metadata);
    }

    static byte[] BuildImageWithDecreasingGenericParameterCounts()
    {
        var metadata = CreateMetadata("DecreasingGenericParameterCounts");
        BlobHandle signature = AddVoidMethodSignature(metadata);
        MethodDefinitionHandle method = AddMethod(metadata, signature);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer`2"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        TypeDefinitionHandle middle = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Middle"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        TypeDefinitionHandle leaf = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Leaf`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        metadata.AddNestedType(middle, outer);
        metadata.AddNestedType(leaf, middle);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("A"),
            index: 0);
        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("B"),
            index: 1);
        metadata.AddGenericParameter(
            middle,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("A"),
            index: 0);
        metadata.AddGenericParameter(
            leaf,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("A"),
            index: 0);
        metadata.AddGenericParameter(
            leaf,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("C"),
            index: 1);

        return Serialize(metadata);
    }

    static byte[] BuildImageWithGenericParameterIndices(
        ushort firstIndex,
        ushort secondIndex)
    {
        var metadata = CreateMetadata("GenericParameterIndices");
        BlobHandle signature = AddVoidMethodSignature(metadata);
        MethodDefinitionHandle method = AddMethod(metadata, signature);
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Broken`2"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: method);
        metadata.AddGenericParameter(
            type,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("A"),
            firstIndex);
        metadata.AddGenericParameter(
            type,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("B"),
            secondIndex);
        return Serialize(metadata);
    }

    static byte[] BuildImageWithDecorationLikeTypeName()
    {
        var metadata = CreateMetadata("DecorationLikeTypeName");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Inner`2[]"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle scope = metadata.AddAssemblyReference(
            metadata.GetOrAddString("DecorationLikeTypeName"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddTypeReference(
            scope,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Inner`2[]"));
        return Serialize(metadata);
    }

    static byte[] BuildImageWithStructuredAnchorNames()
    {
        var metadata = CreateMetadata("StructuredAnchorNames");
        BlobHandle signature = AddVoidMethodSignature(metadata);
        MethodDefinitionHandle literalMethod = AddMethod(metadata, signature);
        MethodDefinitionHandle nestedMethod = AddMethod(metadata, signature);
        MethodDefinitionHandle genericMethod = AddMethod(metadata, signature);

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("A.B"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: literalMethod);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("A"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: nestedMethod);
        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("B"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: nestedMethod);
        metadata.AddNestedType(nested, outer);

        TypeDefinitionHandle genericOuter = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Outer`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: genericMethod);
        TypeDefinitionHandle genericInner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Inner`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: genericMethod);
        metadata.AddNestedType(genericInner, genericOuter);
        metadata.AddGenericParameter(
            genericOuter,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            genericInner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        metadata.AddGenericParameter(
            genericInner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            index: 1);
        return Serialize(metadata);
    }

    static byte[] BuildImageWithAnchorBoundaryCollisions()
    {
        var metadata = CreateMetadata("AnchorBoundaryCollisions");
        BlobHandle signature = AddVoidMethodSignature(metadata);
        MethodDefinitionHandle namespaceMethod = AddMethod(metadata, signature);
        MethodDefinitionHandle nestedMethod = AddMethod(metadata, signature);
        MethodDefinitionHandle missingArityMethod = AddMethod(metadata, signature);
        MethodDefinitionHandle canonicalArityMethod = AddMethod(metadata, signature);

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N.A"),
            metadata.GetOrAddString("B"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: namespaceMethod);
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: nestedMethod);
        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("B"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: nestedMethod);
        metadata.AddNestedType(nested, outer);

        TypeDefinitionHandle missingArity = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Widget"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: missingArityMethod);
        TypeDefinitionHandle canonicalArity = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Widget`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: canonicalArityMethod);
        foreach (TypeDefinitionHandle type in
            (TypeDefinitionHandle[])[missingArity, canonicalArity])
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }

        return Serialize(metadata);
    }

    static MethodDefinitionHandle AddMethod(
        MetadataBuilder metadata,
        BlobHandle signature)
        => metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            signature,
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

    static MetadataBuilder CreateMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
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
        return metadata;
    }

    static BlobHandle AddVoidMethodSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
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
