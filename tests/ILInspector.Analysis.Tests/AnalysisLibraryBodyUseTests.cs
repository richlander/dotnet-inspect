using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using AnalysisBodyUseFixtures;
using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

public sealed class AnalysisLibraryBodyUseTests
{
    static string FixturePath =>
        typeof(BodyUseSource).Assembly.Location;

    [Fact]
    public void ExecutePath_PublishesTypedLocalOccurrences()
    {
        AnalysisLibraryBodyUseOutcome.Available available =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken));
        AnalysisLibraryBodyUseResult result = available.Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Qualified,
            result.Disposition);
        Assert.NotEqual(Guid.Empty, result.Receipt.ModuleVersionId);
        Assert.Contains(
            result.Types,
            static type =>
                Name(type.Name)
                    == "AnalysisBodyUseFixtures.BodyUseSource");
        Assert.Contains(
            result.Types,
            static type =>
                Name(type.Name)
                    == "AnalysisBodyUseFixtures.BodyUseTarget");

        AnalysisLibraryBodyUseOccurrence[] uses =
            [.. result.Occurrences.Where(
                static occurrence =>
                    Name(occurrence.SourceType)
                        == "AnalysisBodyUseFixtures.BodyUseSource"
                    && Name(occurrence.TargetType)
                        == "AnalysisBodyUseFixtures.BodyUseTarget")];
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Call);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Field);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Array);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Cast);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.TypeToken);
        Assert.Contains(
            uses,
            static occurrence =>
                occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind
                        .GenericMethodInstantiation);

        ProducerParticipation participation =
            result.Receipt.Work.Producers.Single();
        Assert.Equal("AnalysisLibraryBodyUse", participation.Producer);
        Assert.True(
            participation.Layers.Single(
                static layer => layer.Layer == "Body").Acquired > 0);
        Assert.True(
            participation.Layers.Single(
                static layer => layer.Layer == "ModuleLookup").Acquired > 0);
    }

    [Fact]
    public void ExecutePath_AttributesAsyncBodyToDeclaredType()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Contains(
            result.Occurrences,
            static occurrence =>
                Name(occurrence.SourceType)
                    == "AnalysisBodyUseFixtures.BodyUseSource"
                && Name(occurrence.TargetType)
                    == "AnalysisBodyUseFixtures.BodyUseTarget"
                && occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Call);
        Assert.DoesNotContain(
            result.Occurrences,
            static occurrence =>
                Name(occurrence.SourceType).Contains(
                    "<AsyncUse>d__",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutePath_ReportsExactInstructionLimit()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(
                        new(
                            MaximumInstructionsPerBody: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseDiagnostic[] diagnostics =
            [.. result.Diagnostics.Where(
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind.Limit)];
        Assert.NotEmpty(diagnostics);
        Assert.All(
            diagnostics,
            static diagnostic =>
            {
                Assert.Equal(1, diagnostic.Limit);
                Assert.True(
                    diagnostic.AttemptedCharge > diagnostic.Limit);
            });
        Assert.True(result.Coverage.BodiesLimited > 0);
    }

    [Fact]
    public void ExecuteImage_ReportsExactOccurrenceLimit()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    FixturePath,
                    [.. File.ReadAllBytes(FixturePath)],
                    new(
                        new(
                            MaximumOccurrences: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(
                result.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                            == AnalysisLibraryBodyUseDiagnosticKind.Limit
                        && diagnostic.Detail.Contains(
                            "Library body-use occurrence",
                            StringComparison.Ordinal));
        Assert.Equal(1, diagnostic.Limit);
        Assert.True(diagnostic.AttemptedCharge > diagnostic.Limit);
        Assert.True(result.Occurrences.Length <= 1);
    }

    [Fact]
    public void ExecuteImage_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => AnalysisLibraryBodyUseService.ExecuteImage(
                FixturePath,
                [.. File.ReadAllBytes(FixturePath)],
                new(),
                cancellation.Token));
    }

    [Fact]
    public void ExecuteImage_ContainsMalformedBodyAndRetainsHealthyBody()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [0x2A],
                        unreadableSecondBody: true),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(2, result.Coverage.BodiesConsidered);
        Assert.Equal(1, result.Coverage.BodiesExamined);
        Assert.Equal(1, result.Coverage.BodiesUnavailable);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .MalformedBody);
    }

    [Fact]
    public void ExecuteImage_ContainsInvalidTypedOperandToken()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Ldtoken,
                            0xFF, 0xFF, 0x00, 0x02,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ret,
                        ]),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(1, result.Coverage.OperandsConsidered);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(
                result.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind
                            .UnresolvedOperand);
        Assert.Contains(
            "0x0200FFFF",
            diagnostic.Detail,
            StringComparison.Ordinal);
    }

    static string Name(
        ILInspector.Metadata.MetadataTypeDefinitionName name) =>
        name.ToEscapedFullName();

    static AnalysisLibraryBodyUseOutcome.Available Available(
        AnalysisLibraryBodyUseOutcome outcome) =>
        outcome is AnalysisLibraryBodyUseOutcome.Available available
            ? available
            : throw new Xunit.Sdk.XunitException(
                outcome is AnalysisLibraryBodyUseOutcome.Rejected rejected
                    ? $"{rejected.Kind}: {rejected.Detail}"
                    : $"Unexpected outcome {outcome.GetType().Name}.");

    static ImmutableArray<byte> BuildIndependentImage(
        byte[] firstBody,
        bool unreadableSecondBody = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("IndependentEcma335.dll"),
            metadata.GetOrAddGuid(
                new Guid("89425fd2-8ab8-4194-aacd-a17392851399")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("IndependentEcma335"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Independent"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddMethod("Healthy", firstBody, readable: true);
        if (unreadableSecondBody)
            AddMethod("Malformed", [0x2A], readable: false);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());

        void AddMethod(
            string name,
            byte[] il,
            bool readable)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            int offset = encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
            if (!readable)
                offset = 0x00FF_FFF0;

            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature()
                .Parameters(
                    0,
                    static returnType => returnType.Void(),
                    static _ => { });
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                offset,
                MetadataTokens.ParameterHandle(1));
        }
    }
}
