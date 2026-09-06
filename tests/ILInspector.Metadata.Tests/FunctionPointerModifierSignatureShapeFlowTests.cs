using CSharpText;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class FunctionPointerModifierSignatureShapeFlowTests
{
    const string ValueTransport = "mss1:0(1:vf7:managedp11:System.Void1:p12:System.Int32)n";
    const string ReferenceTransport = "mss1:0(1:vf7:managedp11:System.Void1:&p12:System.Int32)n";

    static readonly MemberSignatureShape ValueShape = new(
        0, new([new MemberParameterSignatureShape(
            ParameterPassingKind.Value, new FunctionPointerTypeSignatureShape(
                "managed", new PrimitiveTypeSignatureShape("System.Void"),
                new([new PrimitiveTypeSignatureShape("System.Int32")])))]));
    static readonly MemberSignatureShape ReferenceShape = new(
        0, new([new MemberParameterSignatureShape(
            ParameterPassingKind.Value, new FunctionPointerTypeSignatureShape(
                "managed", new PrimitiveTypeSignatureShape("System.Void"),
                new([new ByReferenceTypeSignatureShape(
                    new PrimitiveTypeSignatureShape("System.Int32"))])))]));

    static readonly Specimen[] Specimens =
    [
        new("RefValue", "Ref", "delegate*<int, void>", false, ValueShape, ValueTransport),
        new("RefReference", "Ref", "delegate*<ref int, void>", true, ReferenceShape, ReferenceTransport),
        new("OutValue", "Out", "delegate*<int, void>", false, ValueShape, ValueTransport),
        new("OutReference", "Out", "delegate*<out int, void>", true, ReferenceShape, ReferenceTransport,
            SignatureTypeCode.RequiredModifier, "System.Runtime.InteropServices.OutAttribute"),
        new("InValue", "In", "delegate*<int, void>", false, ValueShape, ValueTransport),
        new("InReference", "In", "delegate*<in int, void>", true, ReferenceShape, ReferenceTransport,
            SignatureTypeCode.RequiredModifier, "System.Runtime.InteropServices.InAttribute"),
        new("ReadOnlyValue", "ReadOnly", "delegate*<int, void>", false, ValueShape, ValueTransport),
        new("ReadOnlyReference", "ReadOnly", "delegate*<ref readonly int, void>", true,
            ReferenceShape, ReferenceTransport,
            SignatureTypeCode.OptionalModifier, "System.Runtime.CompilerServices.RequiresLocationAttribute",
            SourceAvailable: false),
    ];

    public static TheoryData<string> FlowCases => new(Specimens.Select(specimen => specimen.Name));

    public static TheoryData<string> ModifiedCases =>
        new(Specimens.Where(specimen => specimen.ModifierKind is not null).Select(specimen => specimen.Name));

    [Theory]
    [MemberData(nameof(FlowCases))]
    public void FunctionPointerFlow_RecordsEncodingShapeTransportAndRefusal(string specimenName)
    {
        Specimen specimen = FindSpecimen(specimenName);
        Specimen[] group = Specimens.Where(candidate => candidate.MethodName == specimen.MethodName).ToArray();
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        foreach (Specimen candidate in group)
            AssertCompilerEncoding(reader, ExpectedHandle(candidate), candidate);

        MethodDefinitionHandle expected = ExpectedHandle(specimen);
        var metadataCandidates = MetadataCandidates(reader, specimen.MethodName);
        MemberSignatureShapeResult metadata = Assert.Single(
            metadataCandidates, candidate => candidate.Candidate == expected).Shape;
        MemberSignatureShapeResult restoredMetadata = AssertStages(metadata, specimen);
        var restoredCandidates = metadataCandidates
            .Select(candidate => (candidate.Candidate, Shape: Transport(candidate.Shape)))
            .ToArray();
        AssertUnique(MemberSignatureShapeMatcher.Match(metadata, metadataCandidates), expected);
        AssertUnique(MemberSignatureShapeMatcher.Match(restoredMetadata, restoredCandidates), expected);

        MemberSignatureShapeResult source = SourceShape(specimen);
        MemberSignatureShapeResult restoredSource = AssertSourceStages(source, specimen);
        if (specimen.SourceAvailable)
        {
            AssertUnique(MemberSignatureShapeMatcher.Match(source, metadataCandidates), expected);
            AssertUnique(MemberSignatureShapeMatcher.Match(restoredSource, restoredCandidates), expected);
        }
        else
        {
            AssertUnavailable(MemberSignatureShapeMatcher.Match(source, metadataCandidates));
            AssertUnavailable(MemberSignatureShapeMatcher.Match(restoredSource, restoredCandidates));
        }

        var sourceCandidates = group
            .Select(candidate => (Candidate: candidate.Name, Shape: SourceShape(candidate)))
            .ToArray();
        var restoredSourceCandidates = sourceCandidates
            .Select(candidate => (
                candidate.Candidate, Shape: AssertSourceStages(candidate.Shape, FindSpecimen(candidate.Candidate))))
            .ToArray();
        if (group.Any(candidate => !candidate.SourceAvailable))
        {
            AssertUnavailable(MemberSignatureShapeMatcher.Match(metadata, sourceCandidates));
            AssertUnavailable(MemberSignatureShapeMatcher.Match(restoredMetadata, restoredSourceCandidates));
        }
        else
        {
            AssertUnique(MemberSignatureShapeMatcher.Match(metadata, sourceCandidates), specimen.Name);
            AssertUnique(MemberSignatureShapeMatcher.Match(restoredMetadata, restoredSourceCandidates), specimen.Name);
        }

        AssertUnavailable(MemberSignatureShapeMatcher.Match(
            restoredMetadata, restoredCandidates.Where(candidate => candidate.Candidate != expected).ToArray()));
    }

    [Theory]
    [MemberData(nameof(ModifiedCases))]
    public void DistinctFunctionPointerModifierEncoding_DoesNotBecomeSignatureIdentity(string specimenName)
    {
        Specimen modified = FindSpecimen(specimenName);
        Specimen plainReference = FindSpecimen("RefReference");
        using var peReader = OpenFixture();
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle modifiedHandle = ExpectedHandle(modified);
        MethodDefinitionHandle plainHandle = ExpectedHandle(plainReference);
        AssertCompilerEncoding(reader, modifiedHandle, modified);
        AssertCompilerEncoding(reader, plainHandle, plainReference);
        Assert.False(reader.GetBlobBytes(reader.GetMethodDefinition(modifiedHandle).Signature).AsSpan()
            .SequenceEqual(reader.GetBlobBytes(reader.GetMethodDefinition(plainHandle).Signature)));

        MemberSignatureShapeResult metadata = AssertStages(
            MetadataMemberSignatureShape.Create(reader, modifiedHandle), modified);
        MemberSignatureShapeResult plainMetadata = AssertStages(
            MetadataMemberSignatureShape.Create(reader, plainHandle), plainReference);
        Assert.Equal(plainMetadata.Shape, metadata.Shape);

        MemberSignatureShapeResult original = AssertSourceStages(SourceShape(modified), modified);
        MemberSignatureShapeResult alternative = AssertStages(
            SourceMemberSignatureShape.Create(
                $"void {modified.MethodName}(delegate*<ref int, void> reference);",
                SourceMemberSignatureKind.Method), modified);
        Specimen value = Specimens.Single(candidate =>
            candidate.MethodName == modified.MethodName && !candidate.ByReferenceArgument);
        MemberSignatureShapeResult valueSource = AssertStages(SourceShape(value), value);

        // Independent source-version scenarios, not one legal overload group.
        AssertUnique(
            MemberSignatureShapeMatcher.Match(metadata, [("value", valueSource), ("alternative", alternative)]),
            "alternative");
        MemberSignatureCorrespondence<string> result = MemberSignatureShapeMatcher.Match(
            metadata, [("value", valueSource), ("original", original), ("alternative", alternative)]);
        if (modified.SourceAvailable)
        {
            Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, result.Kind);
            Assert.Equal(["original", "alternative"], result.Candidates);
            Assert.Null(result.Match);
            Assert.Null(result.UnavailableReason);
        }
        else
        {
            AssertUnavailable(result);
        }
    }

    static void AssertCompilerEncoding(
        MetadataReader reader, MethodDefinitionHandle handle, Specimen specimen)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        BlobReader signature = reader.GetBlobReader(method.Signature);
        Assert.Equal(0x00, signature.ReadByte()); // Static, non-generic default calling convention.
        Assert.Equal(1, signature.ReadCompressedInteger());
        Assert.Equal(SignatureTypeCode.Void, signature.ReadSignatureTypeCode());
        Assert.Equal(SignatureTypeCode.FunctionPointer, signature.ReadSignatureTypeCode());
        Assert.Equal(0x00, signature.ReadByte()); // Managed function pointer, not the enclosing method.
        Assert.Equal(1, signature.ReadCompressedInteger());
        Assert.Equal(SignatureTypeCode.Void, signature.ReadSignatureTypeCode());
        if (specimen.ModifierKind is { } modifierKind)
        {
            Assert.Equal(modifierKind, signature.ReadSignatureTypeCode());
            EntityHandle modifier = signature.ReadTypeHandle();
            (StringHandle Namespace, StringHandle Name) modifierName = modifier.Kind switch
            {
                HandleKind.TypeDefinition => (
                    reader.GetTypeDefinition((TypeDefinitionHandle)modifier).Namespace,
                    reader.GetTypeDefinition((TypeDefinitionHandle)modifier).Name),
                HandleKind.TypeReference => (
                    reader.GetTypeReference((TypeReferenceHandle)modifier).Namespace,
                    reader.GetTypeReference((TypeReferenceHandle)modifier).Name),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected compiler modifier handle: {modifier.Kind}."),
            };
            Assert.Equal(specimen.ModifierName,
                $"{reader.GetString(modifierName.Namespace)}.{reader.GetString(modifierName.Name)}");
        }
        if (specimen.ByReferenceArgument)
            Assert.Equal(SignatureTypeCode.ByReference, signature.ReadSignatureTypeCode());
        Assert.Equal(SignatureTypeCode.Int32, signature.ReadSignatureTypeCode());
        Assert.Equal(0, signature.RemainingBytes);

        Parameter parameter = method.GetParameters().Select(reader.GetParameter)
            .Single(parameter => parameter.SequenceNumber == 1);
        Assert.Equal(ParameterAttributes.None, parameter.Attributes);
    }

    static (MethodDefinitionHandle Candidate, MemberSignatureShapeResult Shape)[] MetadataCandidates(
        MetadataReader reader, string methodName)
    {
        var typeHandle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(
            typeof(FunctionPointerModifierFlowSamples).MetadataToken);
        return reader.GetTypeDefinition(typeHandle).GetMethods()
            .Where(handle => reader.StringComparer.Equals(reader.GetMethodDefinition(handle).Name, methodName))
            .Select(handle => (Candidate: handle, Shape: MetadataMemberSignatureShape.Create(reader, handle)))
            .ToArray();
    }

    static MethodDefinitionHandle ExpectedHandle(Specimen specimen)
    {
        // Fixture parameter names locate the overload independently of signature projection.
        MethodInfo method = typeof(FunctionPointerModifierFlowSamples)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Single(method => method.Name == specimen.MethodName
                && method.GetParameters().Single().Name == specimen.ParameterName);
        return (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken);
    }

    static MemberSignatureShapeResult SourceShape(Specimen specimen) =>
        SourceMemberSignatureShape.Create(
            $"void {specimen.MethodName}({specimen.ParameterType} {specimen.ParameterName});",
            SourceMemberSignatureKind.Method);

    static MemberSignatureShapeResult AssertSourceStages(MemberSignatureShapeResult result, Specimen specimen)
    {
        if (specimen.SourceAvailable)
            return AssertStages(result, specimen);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Shape);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
        return result;
    }

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

    static void AssertUnavailable<T>(MemberSignatureCorrespondence<T> result)
    {
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
        Assert.Equal(default, result.Match);
        Assert.Empty(result.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    static Specimen FindSpecimen(string name) => Specimens.Single(specimen => specimen.Name == name);

    static PEReader OpenFixture() =>
        new(File.OpenRead(typeof(FunctionPointerModifierFlowSamples).Assembly.Location));

    sealed record Specimen(
        string Name,
        string MethodName,
        string ParameterType,
        bool ByReferenceArgument,
        MemberSignatureShape ExpectedShape,
        string ExpectedTransport,
        SignatureTypeCode? ModifierKind = null,
        string? ModifierName = null,
        bool SourceAvailable = true)
    {
        public string ParameterName => ByReferenceArgument ? "reference" : "value";
    }
}
