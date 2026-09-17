using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

public partial class ReturnToSenderPrototypeTests
{
    [Fact]
    public async Task CompileBackTargets_ResolvesBySignatureOverridingOrdinal()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Pick(int value) => value + 1;

                public int Pick(string value) => value.Length;
            }
            """);
        try
        {
            // Ordinal 0 is Pick(int) in count-all metadata order; the signature must win
            // and select Pick(string) instead, proving identity no longer depends on the
            // ordinal position of same-name members.
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget(
                        "Class1",
                        "Pick",
                        Overload: 0,
                        Signature: CanonicalMethodSignature("void Pick(string value);")),
                ]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal(1, result.Plan.TargetMethod.Overload);
            Assert.Contains("public int Pick(string value)", result.Source);
            Assert.DoesNotContain("public int Pick(int value)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task CompileBackTargets_LegacySignatureCannotOverrideOrdinal()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Pick(int value) => value + 1;

                public int Pick(string value) => value.Length;
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Pick", Overload: 0, Signature: "`0(string)")]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            Assert.Equal(0, result.Plan.TargetMethod.Overload);
            Assert.Contains("public int Pick(int value)", result.Source);
            Assert.DoesNotContain("public int Pick(string value)", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task ReturnToSenderSourceProbe_MatchesSourceBySignatureWhenDeclarationOrderDiffers()
    {
        // The compiled assembly declares Pick(int) before Pick(string); the source slice
        // reverses that order. Ordinal correlation would pair Pick(int)'s decompiled body
        // with Pick(string)'s source (a false ValidDifferent); the canonical shape
        // pairs them correctly.
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Pick(int value) => value + 1;

                public int Pick(string value) => value.Length;
            }
            """);
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"rts-signature-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "ReversedOrder.cs");
        File.WriteAllText(sourcePath, """
            public class Class1
            {
                public int Pick(string value) => value.Length;

                public int Pick(int value) => value + 1;
            }
            """);
        try
        {
            var withSignature = Assert.Single(await ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                [
                    new ReturnToSender.RequestedTarget(
                        "Class1",
                        "Pick",
                        Overload: 0,
                        Signature: CanonicalMethodSignature("void Pick(int value);")),
                ],
                [sourcePath]));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, withSignature.Outcome);

            var ordinalOnly = Assert.Single(await ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1", "Pick", Overload: 0)],
                [sourcePath]));

            Assert.Equal(ReturnToSenderSourceOutcome.ValidDifferent, ordinalOnly.Outcome);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            try
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void SourceSignatureCorrespondence_RefusesFunctionPointerTupleCrossMatch()
    {
        const string source = """
            public unsafe class C
            {
                public static void M(
                    delegate* unmanaged[SuppressGCTransition]<
                        (int, int, int, int, int, int, int, (short, byte)),
                        void> callback) { }

                public static void M(
                    delegate* unmanaged<
                        global::System.ValueTuple<
                            int, int, int, int, int, int, int, (short, byte)>,
                        void> callback) { }
            }
            """;
        string assemblyPath = CompileFixture(source, allowUnsafe: true);
        string sourcePath = WriteTempSource(
            "FunctionPointerTupleCollision.cs",
            source,
            out string sourceDirectory);
        try
        {
            ReturnToSender.RequestedTarget target =
                ReturnToSenderSourceProbe.DiscoverTargets(assemblyPath, int.MaxValue)
                    .Select(row => row.Target)
                    .Single(row => row is { Type: "C", Method: "M", Overload: 0 });
            Assert.NotNull(target.Signature);
            ReturnToSenderSourceIndex index =
                ReturnToSenderSourceIndex.TryCreate([sourcePath])
                ?? throw new InvalidOperationException("Expected a source index.");

            MemberSignatureCorrespondence<ReturnToSenderSourceMember> result =
                index.ResolveBySignature(target);

            Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
            Assert.Contains(
                "cannot be represented consistently",
                result.UnavailableReason,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void SourceSignatureCorrespondence_ReportsUnavailableCandidate()
    {
        string sourcePath = WriteTempSource(
            "UnavailableSignature.cs",
            """
            public class Widget { }

            public class Class1
            {
                public int Pick(int value) => value;
                public int Pick(Widget value) => 0;
            }
            """,
            out string sourceDirectory);
        try
        {
            ReturnToSenderSourceIndex index =
                ReturnToSenderSourceIndex.TryCreate([sourcePath])
                ?? throw new InvalidOperationException("Expected a source index.");

            MemberSignatureCorrespondence<ReturnToSenderSourceMember> result =
                index.ResolveBySignature(
                    new ReturnToSender.RequestedTarget(
                        "Class1",
                        "Pick",
                        Overload: 0,
                        Signature: CanonicalMethodSignature("void Pick(int value);")));

            Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
            Assert.Contains(
                "not globally qualified",
                result.UnavailableReason,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public void SourceSignatureCorrespondence_ReportsAmbiguousCandidates()
    {
        string sourcePath = WriteTempSource(
            "AmbiguousSignature.cs",
            """
            public class Class1
            {
                public int Pick(int value) => value;
                public int Pick(int other) => other;
            }
            """,
            out string sourceDirectory);
        try
        {
            ReturnToSenderSourceIndex index =
                ReturnToSenderSourceIndex.TryCreate([sourcePath])
                ?? throw new InvalidOperationException("Expected a source index.");

            MemberSignatureCorrespondence<ReturnToSenderSourceMember> result =
                index.ResolveBySignature(
                    new ReturnToSender.RequestedTarget(
                        "Class1",
                        "Pick",
                        Overload: 0,
                        Signature: CanonicalMethodSignature("void Pick(int value);")));

            Assert.Equal(MemberSignatureCorrespondenceKind.Ambiguous, result.Kind);
            Assert.Equal(2, result.Candidates.Count);
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public void SourceSignatureCorrespondence_RejectsLegacyCandidateSelection()
    {
        string sourcePath = WriteTempSource(
            "LegacySignature.cs",
            """
            public class Class1
            {
                public int Pick(int value) => value;
                public int Pick(string value) => value.Length;
            }
            """,
            out string sourceDirectory);
        try
        {
            ReturnToSenderSourceIndex index =
                ReturnToSenderSourceIndex.TryCreate([sourcePath])
                ?? throw new InvalidOperationException("Expected a source index.");

            MemberSignatureCorrespondence<ReturnToSenderSourceMember> result =
                index.ResolveBySignature(
                    new ReturnToSender.RequestedTarget(
                        "Class1",
                        "Pick",
                        Overload: 0,
                        Signature: "`0(int)"));

            Assert.Equal(MemberSignatureCorrespondenceKind.Unavailable, result.Kind);
            Assert.Contains("canonical", result.UnavailableReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverTargets_DistinguishesUserNullableFromSystemNullable()
    {
        // The structural metadata adapter keeps a user type named Nullable<T>
        // distinct from System.Nullable<T>; the old simple-name projection collapsed
        // these shapes and had to discard both signatures.
        var assemblyPath = CompileFixture("""
            namespace Sample { public readonly struct Nullable<T> { } }

            public class Class1
            {
                public int Pick(Sample.Nullable<int> value) => 1;

                public int Pick(int? value) => 2;
            }
            """);
        try
        {
            var pickTargets = ReturnToSenderSourceProbe.DiscoverTargets(assemblyPath, int.MaxValue)
                .Where(target => target.Target is { Type: "Class1", Method: "Pick" })
                .ToArray();

            Assert.Equal(2, pickTargets.Length);
            Assert.All(pickTargets, target => Assert.NotNull(target.Target.Signature));
            Assert.NotEqual(
                pickTargets[0].Target.Signature,
                pickTargets[1].Target.Signature);

            // The ordinary source spelling Sample.Nullable<int> is unresolved without
            // semantic context and therefore falls back to ordinal. The exact int?
            // shape still correlates structurally.
            var results = await ReturnToSenderSourceProbe.EvaluateTargets(
                assemblyPath,
                pickTargets.Select(target => target.Target).ToArray(),
                [WriteTempSource(
                    "NullableCollision.cs",
                    """
                    namespace Sample { public readonly struct Nullable<T> { } }

                    public class Class1
                    {
                        public int Pick(Sample.Nullable<int> value) => 1;

                        public int Pick(int? value) => 2;
                    }
                    """,
                    out var sourceDirectory)]);

            try
            {
                Assert.All(results, result => Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome));
            }
            finally
            {
                TryDeleteDirectory(sourceDirectory);
            }
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public async Task ReturnToSenderSourceProbe_MatchesMixedRankArrayOverloadsBySignature()
    {
        // int[][,] and int[,][] must not cross-match: source lists ranks outer-to-inner
        // while metadata builds them inner-to-outer. With the source slice in reversed
        // declaration order, only a rank-consistent signature pairs each overload with
        // its own body.
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public int Rank(int[][,] value) => value.Length + 1;

                public int Rank(int[,][] value) => value.Length + 2;
            }
            """);
        var reversedSource = WriteTempSource(
            "MixedRankArrays.cs",
            """
            public class Class1
            {
                public int Rank(int[,][] value) => value.Length + 2;

                public int Rank(int[][,] value) => value.Length + 1;
            }
            """,
            out var sourceDirectory);
        try
        {
            var targets = ReturnToSenderSourceProbe.DiscoverTargets(assemblyPath, int.MaxValue)
                .Where(target => target.Target is { Type: "Class1", Method: "Rank" })
                .Select(target => target.Target)
                .ToArray();

            Assert.Equal(2, targets.Length);
            Assert.All(targets, target => Assert.NotNull(target.Signature));

            var results = await ReturnToSenderSourceProbe.EvaluateTargets(assemblyPath, targets, [reversedSource]);

            Assert.All(results, result => Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_ClassifiesBodyDefectWhenAuthoredBodyCompiles()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("BodyDefect.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var result = TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return 42;");

            Assert.NotNull(result);
            Assert.Equal(ReturnToSender.FaultIsolationKind.BodyDefect, result.Kind);
            Assert.Equal(sourcePath, result.SourcePath);
            Assert.Contains("authored body compiled", result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_ClassifiesShellOrClosureDefectWhenAuthoredBodyAlsoFails()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource(
            "ShellOrClosureDefect.cs",
            """
            public class Class1
            {
                public int M() { return Missing.Symbol; }
            }
            """,
            out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var result = TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return AlsoMissing.Symbol;",
                "return Missing.Symbol;");

            Assert.NotNull(result);
            Assert.Equal(ReturnToSender.FaultIsolationKind.ShellOrClosureDefect, result.Kind);
            Assert.Equal(sourcePath, result.SourcePath);
            Assert.Contains("CS0103", result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    /// <summary>
    /// Gates the corpus path for #3804: the authored body is pre-correlated to
    /// the exact metadata method token, so source declaration order is irrelevant.
    /// </summary>
    [Fact]
    public void TryIsolateRecompileFailure_AttributesCorrelatedOverloadByMetadataToken()
    {
        const string assemblySource = """
            public class Class1
            {
                public int Pick(int value) { return value + 1; }

                public int Pick(string value) { return value.Length; }
            }
            """;
        var sourcePath = WriteTempSource(
            "ReversedOverloads.cs",
            """
            public class Class1
            {
                public int Pick(string value) { return value.Length; }

                public int Pick(int value) { return value + 1; }
            }
            """,
            out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            var result = TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return value + 1;",
                methodName: "Pick",
                overload: 0);

            Assert.NotNull(result);
            Assert.Equal(ReturnToSender.FaultIsolationKind.BodyDefect, result.Kind);
            Assert.Equal(sourcePath, result.SourcePath);
            Assert.Contains("authored body compiled", result.Detail);
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    /// <summary>
    /// Raw source parsing has no exact metadata identity and therefore cannot
    /// support fault attribution, even for a unique method.
    /// </summary>
    [Fact]
    public void TryIsolateRecompileFailure_DeclinesRawSourceIndex()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("RawSource.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Null(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                authoredBody: null,
                useRawSourceIndex: true));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void PdbMappedSourceIndex_IsIneligibleForFaultAttribution()
    {
        var target = new ReturnToSender.RequestedTarget(
            "Sample.Widget",
            "M",
            0,
            "mss1:synthetic");
        var member = new ReturnToSenderSourceMember(
            target.Type,
            target.Method,
            target.Overload,
            target.Signature!,
            "Widget.cs",
            "return 42;",
            MetadataToken: 0x06000001,
            ModuleVersionId: Guid.NewGuid());

        ReturnToSenderSourceIndex index =
            ReturnToSenderSourceIndex.FromPdbMappedMembers([member]);

        Assert.True(index.TryFind(target, out var found));
        Assert.Equal(member, found);
        Assert.False(index.TryFindForAttribution(
            target,
            member.MetadataToken!.Value,
            out _));
    }

    /// <summary>
    /// Raw source declaration order can differ from metadata order. Without an
    /// exact token correlation, fault attribution must fail closed (#3804).
    /// </summary>
    [Fact]
    public void TryIsolateRecompileFailure_DeclinesReorderedRawOverloads()
    {
        const string assemblySource = """
            public class Class1
            {
                public int Pick(int value) { return value + 1; }

                public int Pick(string value) { return value.Length; }
            }
            """;
        var sourcePath = WriteTempSource(
            "ReorderedRawOverloads.cs",
            """
            public class Class1
            {
                public int Pick(string value) { return value.Length; }

                public int Pick(int value) { return value + 1; }
            }
            """,
            out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Null(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                authoredBody: null,
                useRawSourceIndex: true,
                methodName: "Pick",
                overload: 0));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Theory]
    [InlineData(0x06ffffff)]
    [InlineData(0x02000001)]
    public void TryIsolateRecompileFailure_RejectsInvalidCorrelatedToken(int metadataToken)
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("OutOfRangeToken.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Throws<InvalidDataException>(() => TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return 42;",
                correlatedMetadataTokenOverride: metadataToken));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task AuthoredCorpusBenchmark_RejectsMismatchedModuleCorrelation()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("CorpusCorrelation.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        var corpusPath = Path.Combine(sourceDirectory, "corpus.jsonl");
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            var reader = pe.GetMetadataReader();
            var (typeHandle, methodHandle) = FindMethod(reader, "Class1", "M");
            var type = reader.GetTypeDefinition(typeHandle);
            string signature = MetadataMemberSignatureShape.Create(reader, methodHandle).Shape is { } shape
                ? MemberSignatureShapeCodec.Encode(shape)
                : throw new InvalidOperationException("Expected a method signature.");
            var (assemblyName, assemblyVersion) = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath);
            var record = new AuthoredSourceHarvest.CorpusRecord(
                assemblyName,
                assemblyVersion,
                "test",
                "Class1",
                "M",
                0,
                signature,
                MetadataTokens.GetToken(methodHandle),
                0,
                reader.GetMethodDefinition(methodHandle).RelativeVirtualAddress,
                sourcePath,
                null,
                null,
                "return 42;",
                Guid.NewGuid());
            File.WriteAllText(
                corpusPath,
                JsonSerializer.Serialize(record, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }));

            Assert.Equal(1, await AuthoredCorpusBenchmark.Run([assemblyPath], corpusPath, json: true));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    /// <summary>
    /// Raw source is parsed without the original build's preprocessor symbols.
    /// A unique syntax member can therefore still be the wrong compiled body.
    /// </summary>
    [Fact]
    public void TryIsolateRecompileFailure_DeclinesRawConditionalSource()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource(
            "ConditionalSource.cs",
            """
            public class Class1
            {
                public int M()
                {
            #if FEATURE
                    return 42;
            #else
                    return Missing.Symbol;
            #endif
                }
            }
            """,
            out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Null(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return AlsoMissing.Symbol;",
                authoredBody: null,
                useRawSourceIndex: true));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_RejectsCorrelatedMemberFromDifferentModule()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("DifferentModule.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Throws<InvalidDataException>(() => TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return 42;",
                correlatedModuleVersionIdOverride: Guid.NewGuid()));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public void TryIsolateRecompileFailure_RejectsDuplicateCorrelatedTokens()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("DuplicateToken.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            Assert.Throws<InvalidDataException>(() => TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return 42;",
                addDuplicateCorrelatedToken: true));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    /// <summary>
    /// Gates the invariant the compile-back floor's safety argument rests on (#3783):
    /// fault isolation is never produced for a source member with no authored body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bodyless source member is exactly the input that drives
    /// <c>ReturnToSenderSourceProbe.AddBodylessSourceResult</c>, which is a second
    /// producer of <see cref="ReturnToSenderSourceOutcome.Invalid"/> reachable with a
    /// successful floor status. That path could only contaminate `invalidBreakdown`
    /// if such a row could also carry attribution — and it cannot, because the
    /// producer below requires an authored body while the bodyless path requires the
    /// absence of one. The two are mutually exclusive on the same index lookup.
    /// </para>
    /// <para>
    /// This test isolates that guard: the request and assembly are real, and only the
    /// source index is substituted so the lookup succeeds with a null body. Without
    /// the substitution a miss would return null for the wrong reason and prove nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TryIsolateRecompileFailure_ReturnsNullWhenTheSourceMemberHasNoAuthoredBody()
    {
        const string assemblySource = """
            public class Class1
            {
                public int M() { return 42; }
            }
            """;
        var sourcePath = WriteTempSource("Bodyless.cs", assemblySource, out var sourceDirectory);
        var assemblyPath = CompileFixture(assemblySource, sourceDirectory);
        try
        {
            // Same target and same rejected body that yields BodyDefect against a
            // correlated member with a body; only the authored body is absent.
            Assert.Null(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                authoredBody: null));

            Assert.NotNull(TryIsolateRecompileFailureForMethod(
                assemblyPath,
                sourcePath,
                "return Missing.Symbol;",
                "return 42;"));
        }
        finally
        {
            DeleteFixture(assemblyPath);
            TryDeleteDirectory(sourceDirectory);
        }
    }

    [Fact]
    public async Task CompileBackTargets_EmitsNestedTargetMemberRequirement()
    {
        var assemblyPath = CompileFixture("""
            public class Class1
            {
                public class Inner
                {
                    public int FromSibling() => GetValue();
                    public int GetValue() => 42;
                }
            }
            """);
        try
        {
            var result = Assert.Single(await ReturnToSender.CompileBackTargets(
                assemblyPath,
                [new ReturnToSender.RequestedTarget("Class1.Inner", "FromSibling", 0)]));

            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
            var inner = Assert.Single(result.Plan.Types, type => type.Name == "Inner");
            Assert.Contains(inner.Members, member => member.Name == "GetValue"
                && member.SourceFacts.Any(fact => fact.Id == "typed-closure-method" && fact.Detail == "GetValue"));
            Assert.DoesNotContain(inner.SourceFacts, fact => fact.Producer == "roslyn" && fact.Id == "closure-member");
            Assert.Contains("public int GetValue()", result.Source);
        }
        finally
        {
            DeleteFixture(assemblyPath);
        }
    }
}
