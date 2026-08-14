using CSharpText;

namespace CSharpText.Tests;

public class MemberSignatureShapeTests
{
    [Fact]
    public void SourceShape_ModelsGenericParametersArraysPointersAndTuples()
    {
        const string source = """
            public unsafe (int left, string right) M<T>(
                T value,
                int[][,] first,
                int[,][] second,
                int* pointer)
            {
                return default;
            }
            """;

        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            source,
            SourceMemberSignatureKind.Method,
            ["TOuter"]);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(1, result.Shape!.GenericArity);
        Assert.Equal(4, result.Shape.Parameters.Count);
        Assert.Equal(
            new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Method,
                0),
            result.Shape.Parameters[0].Type);
        Assert.NotEqual(
            result.Shape.Parameters[1].Type,
            result.Shape.Parameters[2].Type);
        Assert.Equal(
            new PointerTypeSignatureShape(
                new PrimitiveTypeSignatureShape("System.Int32")),
            result.Shape.Parameters[3].Type);
    }

    [Fact]
    public void SourceShape_ErasesTupleElementNames()
    {
        MemberSignatureShapeResult named = SourceMemberSignatureShape.Create(
            "void M((int left, string right) value);",
            SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult unnamed = SourceMemberSignatureShape.Create(
            "void M((int, string) value);",
            SourceMemberSignatureKind.Method);

        Assert.True(named.IsAvailable, named.UnavailableReason);
        Assert.Equal(named.Shape, unnamed.Shape);
    }

    [Fact]
    public void SourceShape_UsesPositionalContainingAndMethodGenericParameters()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "TMethod M<TMethod>(TOuter outer, TMethod value);",
            SourceMemberSignatureKind.Method,
            ["TOuter"]);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Type,
                0),
            result.Shape!.Parameters[0].Type);
        Assert.Equal(
            new GenericParameterTypeSignatureShape(
                SignatureGenericParameterKind.Method,
                0),
            result.Shape.Parameters[1].Type);
    }

    [Theory]
    [InlineData("string?", "System.String")]
    [InlineData("object?", "System.Object")]
    [InlineData("dynamic?", "System.Object")]
    public void SourceShape_ErasesKnownReferenceNullability(string sourceType, string clrType)
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            $"void M({sourceType} value);",
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new PrimitiveTypeSignatureShape(clrType),
            result.Shape!.Parameters[0].Type);
    }

    [Fact]
    public void SourceShape_PreservesKnownValueNullability()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "void M(int? value);",
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new NullableTypeSignatureShape(
                new PrimitiveTypeSignatureShape("System.Int32")),
            result.Shape!.Parameters[0].Type);
    }

    [Fact]
    public void SourceShape_PreservesStructConstrainedGenericNullability()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "void M<T>(T? value) where T : struct;",
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new NullableTypeSignatureShape(
                new GenericParameterTypeSignatureShape(
                    SignatureGenericParameterKind.Method,
                    0)),
            result.Shape!.Parameters[0].Type);
    }

    [Fact]
    public void SourceShape_AcceptsKnownValueTypeContainingParameterNullability()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "void M(T? value);",
            SourceMemberSignatureKind.Method,
            ["T"],
            new HashSet<string>(StringComparer.Ordinal) { "T" });

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new NullableTypeSignatureShape(
                new GenericParameterTypeSignatureShape(
                    SignatureGenericParameterKind.Type,
                    0)),
            result.Shape!.Parameters[0].Type);
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("Models.Widget")]
    [InlineData("T")]
    public void SourceShape_RefusesSemanticallyUnresolvedNullableTypes(string sourceType)
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            $"void M({sourceType}? value);",
            SourceMemberSignatureKind.Method,
            sourceType == "T" ? ["T"] : []);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void SourceShape_RefusesNonGlobalNamedTypes()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "void M(System.Threading.Tasks.Task<int> task);",
            SourceMemberSignatureKind.Method);

        Assert.False(result.IsAvailable);
        Assert.Contains("not globally qualified", result.UnavailableReason);
    }

    [Fact]
    public void SourceShape_AcceptsGlobalNamedTypes()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "void M(global::System.Collections.Generic.List<int> values);",
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        var named = Assert.IsType<NamedTypeSignatureShape>(result.Shape!.Parameters[0].Type);
        Assert.Equal("System.Collections.Generic", named.Namespace);
        Assert.Equal("List", Assert.Single(named.Segments).Name);
    }

    [Fact]
    public void SourceShape_RefusesTypeBeyondTransportDepthLimit()
    {
        string type = string.Concat(
                Enumerable.Repeat(
                    "global::Container<",
                    MemberSignatureShapeCodec.MaxDepth + 1))
            + "int"
            + new string('>', MemberSignatureShapeCodec.MaxDepth + 1);

        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            $"void M({type} value);",
            SourceMemberSignatureKind.Method);

        Assert.False(result.IsAvailable);
        Assert.Contains("depth limit", result.UnavailableReason);
    }

    [Fact]
    public void SourceShape_RefusesMemberBeyondTransportNodeLimit()
    {
        string parameters = string.Join(
            ",",
            Enumerable.Range(0, 1_024).Select(
                index => $"global::A<global::B<global::C<global::D<int>>>> p{index}"));

        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            $"void M({parameters});",
            SourceMemberSignatureKind.Method);

        Assert.False(result.IsAvailable);
        Assert.Contains("safety limit", result.UnavailableReason);
    }

    [Fact]
    public void Matcher_DottedGlobalTypeBoundaryCannotProduceFalseUnique()
    {
        MemberSignatureShapeResult source = SourceMemberSignatureShape.Create(
            "void M(global::Sample.C value);",
            SourceMemberSignatureKind.Method);
        Assert.True(source.IsAvailable, source.UnavailableReason);

        var completeCandidates = new[]
        {
            (Candidate: "top-level", Shape: source),
            (Candidate: "nested", Shape: source),
        };
        MemberSignatureCorrespondence<string> topLevel =
            MemberSignatureShapeMatcher.Match(source, completeCandidates);

        var nestedTarget = MemberSignatureShapeResult.Available(
            new MemberSignatureShape(
                0,
                new(
                [
                    new MemberParameterSignatureShape(
                        ParameterPassingKind.Value,
                        new NamedTypeSignatureShape(
                            "",
                            new(
                            [
                                new NamedTypeSegment(
                                    "Sample",
                                    0,
                                    SignatureShapeList<TypeSignatureShape>.Empty),
                                new NamedTypeSegment(
                                    "C",
                                    0,
                                    SignatureShapeList<TypeSignatureShape>.Empty),
                            ]))),
                ]),
                null));
        MemberSignatureCorrespondence<string> nested =
            MemberSignatureShapeMatcher.Match(nestedTarget, completeCandidates);

        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, topLevel.Kind);
        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, nested.Kind);
    }

    [Fact]
    public void SourceShape_DirectiveInBodyDoesNotHideSignature()
    {
        const string source = """
            void M(int value)
            {
            #if FEATURE
                Use(value);
            #endif
            }
            """;

        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            source,
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
    }

    [Fact]
    public void SourceShape_DirectiveInHeaderIsUnavailable()
    {
        const string source = """
            void M(
            #if FEATURE
                int value
            #endif
                );
            """;

        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            source,
            SourceMemberSignatureKind.Method);

        Assert.False(result.IsAvailable);
        Assert.Contains("directive", result.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceShape_ModelsConversionReturnType()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "public static implicit operator int(global::Sample.C value) => 1;",
            SourceMemberSignatureKind.ConversionOperator);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        Assert.Equal(
            new PrimitiveTypeSignatureShape("System.Int32"),
            result.Shape!.ConversionReturnType);
    }

    [Fact]
    public void SourceShape_ModelsFunctionPointers()
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            "unsafe void M(delegate* unmanaged[Cdecl]<int, string> callback);",
            SourceMemberSignatureKind.Method);

        Assert.True(result.IsAvailable, result.UnavailableReason);
        var pointer = Assert.IsType<FunctionPointerTypeSignatureShape>(
            result.Shape!.Parameters[0].Type);
        Assert.Equal("CDecl", pointer.CallingConvention);
        Assert.Equal(
            new PrimitiveTypeSignatureShape("System.String"),
            pointer.ReturnType);
    }

    [Fact]
    public void Codec_RoundTripsCanonicalTextAndNormalizesLegacyInput()
    {
        MemberSignatureShapeResult source = SourceMemberSignatureShape.Create(
            "void M(int[] values);",
            SourceMemberSignatureKind.Method);
        string canonical = MemberSignatureShapeCodec.Encode(source.Shape!);

        MemberSignatureShapeResult decoded = MemberSignatureShapeCodec.Decode(canonical);
        MemberSignatureShapeResult legacy = MemberSignatureShapeCodec.Normalize(
            "`0(int[])",
            out string? normalized);

        Assert.Equal(source.Shape, decoded.Shape);
        Assert.Equal(source.Shape, legacy.Shape);
        Assert.Equal(canonical, normalized);
    }

    [Theory]
    [InlineData("mss1:0(1:vp0:)n")]
    [InlineData("mss1:0(1:vf0:p12:System.Int320:)n")]
    public void Codec_MalformedCanonicalTextIsUnavailable(string text)
    {
        MemberSignatureShapeResult result = MemberSignatureShapeCodec.Decode(text);

        Assert.False(result.IsAvailable);
    }

    [Theory]
    [InlineData("`0(`-1)")]
    [InlineData("`0(``-1)")]
    public void Codec_MalformedLegacyGenericPositionIsUnavailable(string text)
    {
        MemberSignatureShapeResult decoded = MemberSignatureShapeCodec.Decode(text);
        MemberSignatureShapeResult normalized =
            MemberSignatureShapeCodec.Normalize(text, out string? canonical);

        Assert.False(decoded.IsAvailable);
        Assert.False(normalized.IsAvailable);
        Assert.Null(canonical);
    }

    [Fact]
    public void Codec_RejectsDeepLegacyInputWithinTheTextLimit()
    {
        string text = "`0("
            + string.Concat(Enumerable.Repeat("A<", 5_000))
            + "B"
            + new string('>', 5_000)
            + ")";

        MemberSignatureShapeResult result = MemberSignatureShapeCodec.Decode(text);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Codec_RejectsLegacySuffixDepthWithoutAllocationAmplification()
    {
        string text = "`0(A" + new string('*', 60_000) + ")";
        long before = GC.GetAllocatedBytesForCurrentThread();

        MemberSignatureShapeResult result = MemberSignatureShapeCodec.Decode(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(result.IsAvailable);
        Assert.True(allocated < 1024 * 1024, $"Decode allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Codec_RejectsCollectionAmplificationBeforeAllocatingIt()
    {
        string text = "mss1:0(1:v"
            + string.Concat(Enumerable.Repeat("u4096:", 4_096));
        long before = GC.GetAllocatedBytesForCurrentThread();

        MemberSignatureShapeResult result = MemberSignatureShapeCodec.Decode(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(result.IsAvailable);
        Assert.True(allocated < 1024 * 1024, $"Decode allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Codec_RejectsOversizedOutputBeforeGrowingTheBuilder()
    {
        string name = new('A', 5_000_000);
        var shape = new MemberSignatureShape(
            0,
            new(
            [
                new MemberParameterSignatureShape(
                    ParameterPassingKind.Value,
                    new PrimitiveTypeSignatureShape(name)),
            ]),
            null);
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<ArgumentException>(() => MemberSignatureShapeCodec.Encode(shape));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"Encode allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Codec_RejectsUndefinedEnumValues()
    {
        var type = new PrimitiveTypeSignatureShape("System.Int32");
        var invalidPassing = new MemberSignatureShape(
            0,
            new(
            [
                new MemberParameterSignatureShape(
                    (ParameterPassingKind)int.MaxValue,
                    type),
            ]),
            null);
        var invalidGenericKind = new MemberSignatureShape(
            0,
            new(
            [
                new MemberParameterSignatureShape(
                    ParameterPassingKind.Value,
                    new GenericParameterTypeSignatureShape(
                        (SignatureGenericParameterKind)int.MaxValue,
                        0)),
            ]),
            null);

        Assert.Throws<ArgumentException>(
            () => MemberSignatureShapeCodec.Encode(invalidPassing));
        Assert.Throws<ArgumentException>(
            () => MemberSignatureShapeCodec.Encode(invalidGenericKind));
    }

    [Fact]
    public void Codec_IsInjectiveForMixedArrayRanks()
    {
        MemberSignatureShapeResult first = SourceMemberSignatureShape.Create(
            "void M(int[][,] value);",
            SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult second = SourceMemberSignatureShape.Create(
            "void M(int[,][] value);",
            SourceMemberSignatureKind.Method);

        Assert.NotEqual(
            MemberSignatureShapeCodec.Encode(first.Shape!),
            MemberSignatureShapeCodec.Encode(second.Shape!));
    }

    [Fact]
    public void Codec_NormalizesLegacyNamedTypesWithoutTreatingThemAsExact()
    {
        MemberSignatureShapeResult legacy = MemberSignatureShapeCodec.Normalize(
            "`0(Task<int>,ref Widget,int?):long",
            out string? canonical);

        Assert.True(legacy.IsAvailable, legacy.UnavailableReason);
        Assert.NotNull(canonical);
        Assert.Equal(legacy.Shape, MemberSignatureShapeCodec.Decode(canonical).Shape);
        Assert.Equal(
            ParameterPassingKind.ByReference,
            legacy.Shape!.Parameters[1].Passing);
        Assert.Equal(
            new PrimitiveTypeSignatureShape("System.Int64"),
            legacy.Shape.ConversionReturnType);

        MemberSignatureCorrespondence<int> correspondence =
            MemberSignatureShapeMatcher.Match(
                legacy,
                [(1, legacy)]);
        Assert.Equal(
            MemberSignatureCorrespondenceKind.Unavailable,
            correspondence.Kind);
        Assert.Contains("unresolved", correspondence.UnavailableReason);
    }

    [Fact]
    public void Matcher_DoesNotReturnUniqueWhenAnotherCandidateIsUnavailable()
    {
        MemberSignatureShapeResult target = SourceMemberSignatureShape.Create(
            "void M(int value);",
            SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult unavailable = SourceMemberSignatureShape.Create(
            "void M(Widget value);",
            SourceMemberSignatureKind.Method);

        MemberSignatureCorrespondence<int> result = MemberSignatureShapeMatcher.Match(
            target,
            [
                (1, target),
                (2, unavailable),
            ]);

        Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
    }

    [Fact]
    public void Matcher_PreservesUniqueAndAmbiguousOutcomes()
    {
        MemberSignatureShapeResult integer = SourceMemberSignatureShape.Create(
            "void M(int value);",
            SourceMemberSignatureKind.Method);
        MemberSignatureShapeResult text = SourceMemberSignatureShape.Create(
            "void M(string value);",
            SourceMemberSignatureKind.Method);

        MemberSignatureCorrespondence<int> unique =
            MemberSignatureShapeMatcher.Match(
                integer,
                [
                    (1, text),
                    (2, integer),
                ]);
        MemberSignatureCorrespondence<int> ambiguous =
            MemberSignatureShapeMatcher.Match(
                integer,
                [
                    (1, integer),
                    (2, integer),
                ]);

        Assert.Equal(MemberSignatureCorrespondenceKind.Unique, unique.Kind);
        Assert.Equal(2, unique.Match);
        Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, ambiguous.Kind);
        Assert.Equal([1, 2], ambiguous.Candidates);
    }
}
