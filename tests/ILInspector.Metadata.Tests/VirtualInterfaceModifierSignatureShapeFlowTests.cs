using CSharpText;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class VirtualInterfaceModifierSignatureShapeFlowTests
{
    const string IsReadOnlyAttribute = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
    const string RequiresLocationAttribute = "System.Runtime.CompilerServices.RequiresLocationAttribute";
    const string ValueTransport = "mss1:0(1:vp12:System.Int32)n";
    const string ReferenceTransport = "mss1:0(1:rp12:System.Int32)n";

    static readonly MemberSignatureShape ValueShape = new(
        0, new([new MemberParameterSignatureShape(
            ParameterPassingKind.Value, new PrimitiveTypeSignatureShape("System.Int32"))]));
    static readonly MemberSignatureShape ReferenceShape = new(
        0, new([new MemberParameterSignatureShape(
            ParameterPassingKind.ByReference, new PrimitiveTypeSignatureShape("System.Int32"))]));

    static readonly Type[] FixtureTypes =
        [typeof(VirtualModifierFlowSamples), typeof(IInterfaceModifierFlowSamples)];

    static readonly Specimen[] Specimens =
    [
        new("RefValue", "Ref", "int value", typeof(int), ValueShape, ValueTransport),
        new("RefReference", "Ref", "ref int value", typeof(int).MakeByRefType(),
            ReferenceShape, ReferenceTransport),
        new("OutValue", "Out", "int value", typeof(int), ValueShape, ValueTransport),
        new("OutReference", "Out", "out int value", typeof(int).MakeByRefType(),
            ReferenceShape, ReferenceTransport, Attributes: ParameterAttributes.Out),
        new("InValue", "In", "int value", typeof(int), ValueShape, ValueTransport),
        new("InReference", "In", "in int value", typeof(int).MakeByRefType(),
            ReferenceShape, ReferenceTransport, RequiredInModifier: true,
            Attributes: ParameterAttributes.In, MarkerAttribute: IsReadOnlyAttribute),
        new("ReadOnlyValue", "ReadOnly", "int value", typeof(int), ValueShape, ValueTransport),
        new("ReadOnlyReference", "ReadOnly", "ref readonly int value", typeof(int).MakeByRefType(),
            ReferenceShape, ReferenceTransport, RequiredInModifier: true,
            Attributes: ParameterAttributes.In, MarkerAttribute: RequiresLocationAttribute),
    ];

    public static TheoryData<Type, string> FlowCases => Cases(Specimens);

    public static TheoryData<Type, string> ModifiedCases =>
        Cases(Specimens.Where(specimen => specimen.RequiredInModifier));

    [Theory]
    [MemberData(nameof(FlowCases))]
    public void ModifierFlow_RecordsMetadataShapeTransportAndCandidate(Type fixtureType, string specimenName)
    {
        Specimen specimen = FindSpecimen(specimenName);
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle expected = ExpectedHandle(fixtureType, specimen);
        AssertCompilerEncoding(reader, expected, specimen);

        var metadataCandidates = MetadataCandidates(reader, fixtureType, specimen.MethodName);
        Assert.Equal(2, metadataCandidates.Length);
        MemberSignatureShapeResult metadata = Assert.Single(
            metadataCandidates, candidate => candidate.Candidate == expected).Shape;
        MemberSignatureShapeResult restoredMetadata = AssertStages(metadata, specimen);
        MemberSignatureShapeResult source = SourceShape(specimen);
        MemberSignatureShapeResult restoredSource = AssertStages(source, specimen);
        var restoredCandidates = metadataCandidates
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();

        AssertUnique(MemberSignatureShapeMatcher.Match(source, metadataCandidates), expected);
        AssertUnique(MemberSignatureShapeMatcher.Match(restoredSource, restoredCandidates), expected);
        AssertUnique(MemberSignatureShapeMatcher.Match(restoredMetadata, restoredCandidates), expected);

        var sourceCandidates = Specimens
            .Where(candidate => candidate.MethodName == specimen.MethodName)
            .Select(candidate => (
                Candidate: candidate.Name, Shape: AssertStages(SourceShape(candidate), candidate)))
            .ToArray();
        AssertUnique(MemberSignatureShapeMatcher.Match(restoredMetadata, sourceCandidates), specimen.Name);

        MemberSignatureCorrespondence<MethodDefinitionHandle> oppositePassing =
            MemberSignatureShapeMatcher.Match(
                restoredSource, restoredCandidates.Where(candidate => candidate.Candidate != expected).ToArray());
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, oppositePassing.Kind);
        Assert.True(oppositePassing.Match.IsNil);
        Assert.Empty(oppositePassing.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(oppositePassing.UnavailableReason));
    }

    [Theory]
    [MemberData(nameof(ModifiedCases))]
    public void DistinctModifierEncoding_DoesNotBecomeSignatureIdentity(Type fixtureType, string specimenName)
    {
        Specimen modified = FindSpecimen(specimenName);
        Specimen plainReference = FindSpecimen("RefReference");
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle modifiedHandle = ExpectedHandle(fixtureType, modified);
        MethodDefinitionHandle plainHandle = ExpectedHandle(fixtureType, plainReference);
        AssertCompilerEncoding(reader, modifiedHandle, modified);
        AssertCompilerEncoding(reader, plainHandle, plainReference);
        Assert.False(reader.GetBlobBytes(reader.GetMethodDefinition(modifiedHandle).Signature).AsSpan()
            .SequenceEqual(reader.GetBlobBytes(reader.GetMethodDefinition(plainHandle).Signature)));

        MemberSignatureShapeResult metadata = AssertStages(
            MetadataMemberSignatureShape.Create(reader, modifiedHandle), modified);
        MemberSignatureShapeResult plainMetadata = AssertStages(
            MetadataMemberSignatureShape.Create(reader, plainHandle), plainReference);
        Assert.Equal(plainMetadata.Shape, metadata.Shape);

        MemberSignatureShapeResult original = AssertStages(SourceShape(modified), modified);
        MemberSignatureShapeResult alternative = AssertStages(
            SourceMemberSignatureShape.Create(
                $"void {modified.MethodName}(ref int value);", SourceMemberSignatureKind.Method),
            modified);
        Specimen value = Specimens.Single(specimen =>
            specimen.MethodName == modified.MethodName && specimen.RuntimeParameterType == typeof(int));
        MemberSignatureShapeResult valueSource = AssertStages(SourceShape(value), value);

        // Alternative source versions, not legal overloads differing only in direction/readonly.
        AssertUnique(
            MemberSignatureShapeMatcher.Match(metadata, [("value", valueSource), ("alternative", alternative)]),
            "alternative");
        MemberSignatureCorrespondence<string> ambiguous = MemberSignatureShapeMatcher.Match(
            metadata, [("value", valueSource), ("original", original), ("alternative", alternative)]);
        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(["original", "alternative"], ambiguous.Candidates);
        Assert.Null(ambiguous.Match);
        Assert.Null(ambiguous.UnavailableReason);
    }

    static void AssertCompilerEncoding(
        MetadataReader reader, MethodDefinitionHandle handle, Specimen specimen)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        Assert.True((method.Attributes & MethodAttributes.Virtual) != 0);
        BlobReader signature = reader.GetBlobReader(method.Signature);
        Assert.Equal(0x20, signature.ReadByte()); // Instance, non-generic default calling convention.
        Assert.Equal(1, signature.ReadCompressedInteger());
        Assert.Equal(SignatureTypeCode.Void, signature.ReadSignatureTypeCode());
        if (specimen.RequiredInModifier)
        {
            Assert.Equal(SignatureTypeCode.RequiredModifier, signature.ReadSignatureTypeCode());
            EntityHandle modifier = signature.ReadTypeHandle();
            Assert.Equal(HandleKind.TypeReference, modifier.Kind);
            TypeReference modifierType = reader.GetTypeReference((TypeReferenceHandle)modifier);
            Assert.Equal("System.Runtime.InteropServices", reader.GetString(modifierType.Namespace));
            Assert.Equal("InAttribute", reader.GetString(modifierType.Name));
        }
        if (specimen.RuntimeParameterType.IsByRef)
            Assert.Equal(SignatureTypeCode.ByReference, signature.ReadSignatureTypeCode());
        Assert.Equal(SignatureTypeCode.Int32, signature.ReadSignatureTypeCode());
        Assert.Equal(0, signature.RemainingBytes);

        Parameter parameter = method.GetParameters().Select(reader.GetParameter)
            .Single(parameter => parameter.SequenceNumber == 1);
        Assert.Equal(specimen.Attributes, parameter.Attributes);
        foreach (string marker in new[] { IsReadOnlyAttribute, RequiresLocationAttribute })
        {
            Assert.Equal(
                specimen.MarkerAttribute == marker,
                AttributeReader.HasAttribute(reader, parameter.GetCustomAttributes(), marker));
        }
    }

    static TheoryData<Type, string> Cases(IEnumerable<Specimen> specimens)
    {
        var cases = new TheoryData<Type, string>();
        foreach (Type fixtureType in FixtureTypes)
            foreach (Specimen specimen in specimens)
                cases.Add(fixtureType, specimen.Name);
        return cases;
    }

    static (MethodDefinitionHandle Candidate, MemberSignatureShapeResult Shape)[] MetadataCandidates(
        MetadataReader reader, Type fixtureType, string methodName)
    {
        var typeHandle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(fixtureType.MetadataToken);
        return reader.GetTypeDefinition(typeHandle).GetMethods()
            .Where(handle => reader.StringComparer.Equals(reader.GetMethodDefinition(handle).Name, methodName))
            .Select(handle => (
                Candidate: handle, Shape: MetadataMemberSignatureShape.Create(reader, handle)))
            .ToArray();
    }

    static MethodDefinitionHandle ExpectedHandle(Type fixtureType, Specimen specimen)
    {
        // Locate the compiled overload independently of the shape projection.
        MethodInfo method = fixtureType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == specimen.MethodName
                && method.GetParameters().Single().ParameterType == specimen.RuntimeParameterType);
        return (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken);
    }

    static MemberSignatureShapeResult SourceShape(Specimen specimen) =>
        SourceMemberSignatureShape.Create(
            $"void {specimen.MethodName}({specimen.ParameterDeclaration});", SourceMemberSignatureKind.Method);

    static MemberSignatureShapeResult AssertStages(MemberSignatureShapeResult result, Specimen specimen)
    {
        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(specimen.ExpectedShape, result.Shape);
        string text = MemberSignatureShapeCodec.Encode(result.Shape!);
        Assert.Equal(specimen.ExpectedTransport, text);
        MemberSignatureShapeResult restored = MemberSignatureShapeCodec.Decode(text);
        Assert.True(restored.IsAvailable, restored.UnavailableReason);
        Assert.Null(restored.UnavailableReason);
        Assert.Equal(specimen.ExpectedShape, restored.Shape);
        return restored;
    }

    static MemberSignatureShapeResult Transport(MemberSignatureShapeResult result)
    {
        Assert.True(result.IsAvailable, result.UnavailableReason);
        MemberSignatureShapeResult restored =
            MemberSignatureShapeCodec.Decode(MemberSignatureShapeCodec.Encode(result.Shape!));
        Assert.True(restored.IsAvailable, restored.UnavailableReason);
        return restored;
    }

    static void AssertUnique<T>(MemberSignatureCorrespondence<T> result, T expected)
    {
        Assert.Equal(MemberSignatureCorrespondenceKind.Unique, result.Kind);
        Assert.Equal(expected, result.Match);
        Assert.Empty(result.Candidates);
        Assert.Null(result.UnavailableReason);
    }

    static Specimen FindSpecimen(string name) => Specimens.Single(specimen => specimen.Name == name);

    static PEReader OpenFixture() =>
        new(File.OpenRead(typeof(VirtualModifierFlowSamples).Assembly.Location));

    sealed record Specimen(
        string Name,
        string MethodName,
        string ParameterDeclaration,
        Type RuntimeParameterType,
        MemberSignatureShape ExpectedShape,
        string ExpectedTransport,
        bool RequiredInModifier = false,
        ParameterAttributes Attributes = ParameterAttributes.None,
        string? MarkerAttribute = null);
}
