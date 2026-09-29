using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using AnalysisBodyUseFixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

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
    public void ExecutePath_AttributesIteratorBodyToDeclaredType()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        using var image = new PEReader(
            File.OpenRead(FixturePath));
        MetadataReader reader = image.GetMetadataReader();
        MethodDefinitionHandle kickoff = reader.MethodDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(BodyUseSource.IteratorUse)));
        StateMachineRelationshipResult.Resolved relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                StateMachineRelationshipIndex
                    .Create(reader)
                    .GetByKickoff(kickoff));
        Assert.Equal(
            StateMachineClaimKind.Iterator,
            relationship.Relationship.Kind);
        Assert.True(
            relationship.Relationship.TryGetMethod(
                StateMachineMethodRole.MoveNext,
                out var moveNext));

        Assert.Contains(
            result.Occurrences,
            occurrence =>
                Name(occurrence.SourceType)
                    == "AnalysisBodyUseFixtures.BodyUseSource"
                && Name(occurrence.TargetType)
                    == "AnalysisBodyUseFixtures.BodyUseTarget"
                && occurrence.PhysicalMethodToken == moveNext.Token
                && occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Constructor);
        Assert.DoesNotContain(
            result.Occurrences,
            static occurrence =>
                Name(occurrence.SourceType).Contains(
                    "<IteratorUse>d__",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExecutePath_AttributesAsyncLambdaToDeclaredType()
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
                    "<>",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(BodyUseLiftedShapes.CapturingLambdaInAsync))]
    [InlineData(nameof(BodyUseLiftedShapes.AsyncLambdaInCache))]
    [InlineData(nameof(BodyUseLiftedShapes.LocalFunctionInLambda))]
    [InlineData(nameof(BodyUseLiftedShapes.LambdaInIterator))]
    public void ExecutePath_AttributesRoslynLiftedShapeToDeclaringType(
        string source)
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;
        using var image = new PEReader(File.OpenRead(FixturePath));
        MetadataReader reader = image.GetMetadataReader();

        // Every generated body produced for the source method: a lifted
        // method named for it, or a member of a Type named for it.
        string marker = $"<{source}>";
        HashSet<int> generated =
        [
            .. reader.MethodDefinitions
                .Where(handle =>
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(handle);
                    return reader.GetString(method.Name).Contains(
                            marker,
                            StringComparison.Ordinal)
                        || reader.GetString(
                                reader.GetTypeDefinition(
                                    method.GetDeclaringType()).Name)
                            .Contains(marker, StringComparison.Ordinal);
                })
                .Select(static handle => MetadataTokens.GetToken(handle)),
        ];
        Assert.NotEmpty(generated);

        AnalysisLibraryBodyUseOccurrence[] uses =
            [.. result.Occurrences.Where(
                occurrence => generated.Contains(
                    occurrence.PhysicalMethodToken))];
        Assert.Contains(
            uses,
            static occurrence =>
                Name(occurrence.TargetType)
                    == "AnalysisBodyUseFixtures.BodyUseTarget"
                && occurrence.OperandKind
                    == AnalysisLibraryBodyUseOperandKind.Constructor);
        Assert.All(
            uses,
            static occurrence => Assert.Equal(
                "AnalysisBodyUseFixtures.BodyUseLiftedShapes",
                Name(occurrence.SourceType)));
    }

    [Fact]
    public void ExecutePath_BindsSharedMethodSpecAcrossCallerArities()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;
        using var image = new PEReader(File.OpenRead(FixturePath));
        MetadataReader reader = image.GetMetadataReader();
        string[] callers =
        [
            nameof(BodyUseSharedInstantiation.FromNonGeneric),
            nameof(BodyUseSharedInstantiation.FromGeneric),
            nameof(BodyUseSharedInstantiation.FromTwoGeneric),
        ];

        // Roslyn emits one MethodSpec for Pick<BodyUseTarget>, reached from
        // callers of generic arity 0, 1, and 2. Each binds it identically.
        var bindings = callers
            .Select(caller =>
            {
                int token = MetadataTokens.GetToken(
                    reader.MethodDefinitions.Single(handle =>
                        reader.StringComparer.Equals(
                            reader.GetMethodDefinition(handle).Name,
                            caller)));
                Assert.DoesNotContain(
                    result.Diagnostics,
                    diagnostic => diagnostic.MethodToken == token);
                return result.Occurrences
                    .Where(occurrence =>
                        occurrence.PhysicalMethodToken == token
                        && occurrence.OperandKind
                            == AnalysisLibraryBodyUseOperandKind
                                .GenericMethodInstantiation)
                    .Select(static occurrence => (
                        occurrence.OperandToken,
                        occurrence.OccurrenceOrdinal,
                        Target: Name(occurrence.TargetType)))
                    .ToArray();
            })
            .ToArray();

        Assert.Equal(
            [
                (bindings[0][0].OperandToken, 0,
                    "AnalysisBodyUseFixtures.BodyUseSharedInstantiation"),
                (bindings[0][0].OperandToken, 1,
                    "AnalysisBodyUseFixtures.BodyUseTarget"),
            ],
            bindings[0]);
        Assert.Equal(0x2B, bindings[0][0].OperandToken >>> 24);
        Assert.All(
            bindings,
            binding => Assert.Equal(bindings[0], binding));
    }

    [Fact]
    public void ExecuteImage_FailsMethodSpecOutsideLaterCallerScope()
    {
        // One MethodSpec, G<!!0>, is valid from the generic caller visited
        // first and invalid from the non-generic caller visited second. The
        // cached valid binding must not answer for the invalid caller.
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "MethodSpecScope.dll",
                    BuildMethodSpecScopeImage(),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(0x06000002, occurrence.PhysicalMethodToken);
        Assert.Equal(0x2B000001, occurrence.OperandToken);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(
            AnalysisLibraryBodyUseDiagnosticKind.UnresolvedOperand,
            diagnostic.Kind);
        Assert.Equal(0x06000003, diagnostic.MethodToken);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
    }

    [Fact]
    public void ExecuteImage_ExcludesModuleOwnedBodies()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentModuleInitializer.dll",
                    BuildIndependentModuleInitializerImage(),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(1, result.Coverage.BodiesConsidered);
        AnalysisLibraryBodyUsePhysicalEvidence evidence =
            Assert.Single(result.PhysicalEvidence);
        Assert.Equal(0x06000002, evidence.PhysicalMethodToken);
        Assert.DoesNotContain(
            result.Occurrences,
            static occurrence =>
                occurrence.PhysicalMethodToken == 0x06000001);
    }

    [Fact]
    public void ExecuteImage_ExcludesModuleTargetAndRetainsHealthyEvidence()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentGlobalMethod.dll",
                    BuildIndependentGlobalMethodImage(),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Complete,
            result.Disposition);
        Assert.Equal(2, result.Coverage.OperandsConsidered);
        Assert.Equal(2, result.Coverage.OperandsExamined);
        Assert.Equal(0, result.Coverage.OperandsUnavailable);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(0, occurrence.IlOffset);
        Assert.Equal(
            "Independent.Target",
            Name(occurrence.TargetType));
    }

    [Fact]
    public void ExecuteImage_IncludesNestedTypeNamedModule()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentNestedModule.dll",
                    BuildIndependentNestedModuleImage(),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Complete,
            result.Disposition);
        Assert.Equal(1, result.Coverage.BodiesConsidered);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(
            "Independent.Owner+<Module>",
            Name(occurrence.SourceType));
        Assert.Equal(
            "Independent.Target",
            Name(occurrence.TargetType));
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
    public void ExecutePath_AttributesLiftedBodyWithoutDecodingItsOwner()
    {
        using var image = new PEReader(File.OpenRead(FixturePath));
        MetadataReader reader = image.GetMetadataReader();
        MethodDefinitionHandle owner = Assert.Single(
            reader.MethodDefinitions,
            handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(BodyUseSource.BoundedLiftedOwner)));
        MethodDefinitionHandle lifted = Assert.Single(
            reader.MethodDefinitions,
            handle => reader.GetString(
                    reader.GetMethodDefinition(handle).Name)
                .Contains(
                    "<BoundedLiftedOwner>g__Local",
                    StringComparison.Ordinal));

        // The owner exceeds the two-instruction limit; the lifted body
        // (newobj, ret) does not, and its attribution decodes no other body.
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(
                        new(
                            MaximumInstructionsPerBody: 2)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken == MetadataTokens.GetToken(owner)
                && diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind.Limit);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic =>
                diagnostic.MethodToken == MetadataTokens.GetToken(lifted));
        Assert.Equal(
            AnalysisLibraryBodyUseFidelity.LogicalOwner,
            Assert.Single(
                result.PhysicalEvidence,
                evidence =>
                    evidence.PhysicalMethodToken
                        == MetadataTokens.GetToken(lifted)).Fidelity);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.PhysicalMethodToken
                    == MetadataTokens.GetToken(lifted)
                && Name(occurrence.SourceType)
                    == "AnalysisBodyUseFixtures.BodyUseSource"
                && Name(occurrence.TargetType)
                    == "AnalysisBodyUseFixtures.BodyUseTarget");
    }

    [Fact]
    public void ExecutePath_AttributesOnlyStateMachineRoleBodies()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecutePath(
                    FixturePath,
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        using var image = new PEReader(
            File.OpenRead(FixturePath));
        MetadataReader reader = image.GetMetadataReader();
        MethodDefinitionHandle kickoff = reader.MethodDefinitions.Single(
            handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(BodyUseSource.IteratorUse)));
        StateMachineRelationshipResult.Resolved relationship =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                StateMachineRelationshipIndex
                    .Create(reader)
                    .GetByKickoff(kickoff));
        var roles = relationship.Relationship.Roles
            .OfType<StateMachineRoleDisposition.Present>()
            .Select(static role => role.Method.Token)
            .ToHashSet();
        Assert.Equal(2, roles.Count);

        // Roslyn's iterator role bodies (MoveNext, Dispose) belong to the
        // kickoff's declaring Type; its other generated members do not.
        TypeDefinitionHandle stateMachine =
            reader.GetMethodDefinition(
                    MetadataTokens.MethodDefinitionHandle(roles.First()))
                .GetDeclaringType();
        foreach (MethodDefinitionHandle method
            in reader.GetTypeDefinition(stateMachine).GetMethods())
        {
            int token = MetadataTokens.GetToken(method);
            AnalysisLibraryBodyUsePhysicalEvidence evidence =
                Assert.Single(
                    result.PhysicalEvidence,
                    candidate => candidate.PhysicalMethodToken == token);
            Assert.Equal(
                roles.Contains(token)
                    ? AnalysisLibraryBodyUseFidelity.LogicalOwner
                    : AnalysisLibraryBodyUseFidelity.PhysicalOnly,
                evidence.Fidelity);
        }
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
        AnalysisLibraryBodyUseDiagnostic[] diagnostics =
            [.. result.Diagnostics.Where(
                static diagnostic =>
                    diagnostic.Kind
                            == AnalysisLibraryBodyUseDiagnosticKind.Limit
                        && diagnostic.Detail.Contains(
                            "Library body-use occurrence",
                            StringComparison.Ordinal))];
        Assert.NotEmpty(diagnostics);
        Assert.All(
            diagnostics,
            static diagnostic =>
            {
                Assert.Equal(1, diagnostic.Limit);
                Assert.True(
                    diagnostic.AttemptedCharge > diagnostic.Limit);
            });
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

    [Fact]
    public void ExecuteImage_ContainsInvalidMemberTokenPerOperand()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Ldtoken,
                            0x02, 0x00, 0x00, 0x02,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ldtoken,
                            0xFF, 0xFF, 0x00, 0x0A,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ret,
                        ]),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(2, result.Coverage.OperandsConsidered);
        Assert.Equal(1, result.Coverage.OperandsExamined);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(0, occurrence.IlOffset);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .UnresolvedOperand
                && diagnostic.IlOffset == 6);
    }

    [Fact]
    public void ExecuteImage_RejectsMethodMemberRefForFieldOperand()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Ldsfld,
                            0x01, 0x00, 0x00, 0x0A,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ret,
                        ],
                        methodMemberReference: true),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(1, result.Coverage.OperandsConsidered);
        Assert.Equal(0, result.Coverage.OperandsExamined);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        Assert.Empty(result.Occurrences);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .UnresolvedOperand);
    }

    [Fact]
    public void ExecuteImage_ReportsTruncatedMethodSignatureOperand()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Call,
                            0x01, 0x00, 0x00, 0x0A,
                            (byte)ILOpCode.Ret,
                        ],
                        truncatedMethodMemberReference: true),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Empty(result.Occurrences);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .UnresolvedOperand
                && diagnostic.IlOffset == 0);
    }

    [Fact]
    public void ExecuteImage_ReportsBodyWithTruncatedOwnSignature()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [(byte)ILOpCode.Ret],
                        truncatedOwnSignature: true),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(1, result.Coverage.BodiesUnavailable);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind.MalformedBody);
    }

    [Fact]
    public void ExecuteImage_DecodesRepeatedMalformedSignatureOnce()
    {
        // 256 calls to one MemberRef whose 8 KiB signature is truncated. Each
        // call fails visibly, but the signature is decoded once.
        const int calls = 256;
        byte[] il = new byte[(calls * 5) + 1];
        for (int i = 0; i < calls; i++)
        {
            il[i * 5] = (byte)ILOpCode.Call;
            il[(i * 5) + 1] = 0x01;
            il[(i * 5) + 4] = 0x0A;
        }
        il[^1] = (byte)ILOpCode.Ret;
        ImmutableArray<byte> image =
            BuildIndependentImage(il, largeTruncatedParameters: 4096);

        long before = GC.GetAllocatedBytesForCurrentThread();
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    image,
                    new(),
                    TestContext.Current.CancellationToken)).Result;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(calls, result.Coverage.OperandsUnavailable);
        Assert.Equal(
            calls,
            result.Diagnostics.Count(static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .UnresolvedOperand));
        // One decode allocates about 2 MB; one per call would be about 600 MB.
        Assert.InRange(allocated, 0, 32 * 1024 * 1024);
    }

    [Fact]
    public void ExecuteImage_BoundsStateMachineAttributeNames()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "StateMachineName.dll",
                    BuildOversizedStateMachineNameImage(kickoffs: 64),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        // The oversized shared name fails the generated body it would
        // attribute, visibly; ordinary kickoff bodies are unaffected.
        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(1, result.Coverage.BodiesUnavailable);
        Assert.Equal(64, result.Coverage.BodiesExamined);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind.MalformedBody
                && diagnostic.Detail.Contains(
                    "state-machine host",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteImage_ReportsFailedCurrentImageBinding()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Ldtoken,
                            0x02, 0x00, 0x00, 0x02,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ldtoken,
                            0x01, 0x00, 0x00, 0x01,
                            (byte)ILOpCode.Pop,
                            (byte)ILOpCode.Ret,
                        ],
                        missingCurrentModuleTypeReference: true),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(2, result.Coverage.OperandsConsidered);
        Assert.Equal(1, result.Coverage.OperandsExamined);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(0, occurrence.IlOffset);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(
                result.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind
                            .UnresolvedOperand);
        Assert.Contains(
            "current-image Type definition",
            diagnostic.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteImage_StopsBeforeUnadmittedMalformedInstruction()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [
                            (byte)ILOpCode.Nop,
                            0xFF,
                        ]),
                    new(
                        new(
                            MaximumInstructionsPerBody: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        AnalysisLibraryBodyUseDiagnostic diagnostic =
            Assert.Single(
                result.Diagnostics,
                static diagnostic =>
                    diagnostic.Kind
                        == AnalysisLibraryBodyUseDiagnosticKind.Limit);
        Assert.Equal(1, diagnostic.Limit);
        Assert.Equal(2, diagnostic.AttemptedCharge);
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind.MalformedBody);
    }

    [Fact]
    public void ExecuteImage_AcceptsExactInstructionLimit()
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentImage(
                        [(byte)ILOpCode.Ret]),
                    new(
                        new(
                            MaximumInstructionsPerBody: 1)),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Complete,
            result.Disposition);
        Assert.Equal(1, result.Coverage.BodiesExamined);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecuteImage_RejectsNestedInvalidTypeSpecAtomically(
        bool genericInstance)
    {
        AnalysisLibraryBodyUseResult result =
            Available(
                AnalysisLibraryBodyUseService.ExecuteImage(
                    "IndependentEcma335.dll",
                    BuildIndependentTypeSpecImage(
                        genericInstance),
                    new(),
                    TestContext.Current.CancellationToken)).Result;

        Assert.Equal(
            AnalysisLibraryBodyUseDisposition.Partial,
            result.Disposition);
        Assert.Equal(2, result.Coverage.OperandsConsidered);
        Assert.Equal(1, result.Coverage.OperandsExamined);
        Assert.Equal(1, result.Coverage.OperandsUnavailable);
        AnalysisLibraryBodyUseOccurrence occurrence =
            Assert.Single(result.Occurrences);
        Assert.Equal(0, occurrence.IlOffset);
        Assert.DoesNotContain(
            result.Occurrences,
            static occurrence => occurrence.IlOffset == 6);
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind
                        .UnresolvedOperand);
    }

    [Fact]
    public void Accumulator_DiscardsRowsRejectedByGlobalLimit()
    {
        var accumulator =
            new AnalysisLibraryBodyUseProducer.Accumulator(
                maximumOccurrences: 1);
        accumulator.Add(
            new(
                Body(
                    methodToken: 0x06000001,
                    ilOffset: 0)));
        accumulator.Add(
            new(
                Body(
                    methodToken: 0x06000002,
                    ilOffset: 1)));

        AnalysisLibraryBodyUseProducer.Result result =
            accumulator.Complete();

        Assert.Single(result.Occurrences);
        Assert.Equal(2, result.Bodies.Length);
        Assert.All(
            result.Bodies,
            static body =>
                Assert.IsType<BodyTypeUsePhysicalFact>(body));
        Assert.Single(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Kind
                    == AnalysisLibraryBodyUseDiagnosticKind.Limit);

        static BodyTypeUseMethodFact Body(
            int methodToken,
            int ilOffset) =>
            new(
                MetadataTokens.TypeDefinitionHandle(2),
                methodToken,
                AnalysisLibraryBodyUseFidelity.LogicalOwner,
                [new(
                    MetadataTokens.TypeDefinitionHandle(2),
                    MetadataTokens.TypeDefinitionHandle(2),
                    methodToken,
                    AnalysisLibraryBodyUseOperandKind.TypeToken,
                    MetadataTokens.GetToken(
                        MetadataTokens.TypeDefinitionHandle(2)),
                    ilOffset,
                    0)],
                [],
                OperandsConsidered: 1,
                OperandsExamined: 1,
                OperandsUnavailable: 0,
                Limited: false,
                AttemptedCharge: null,
                Limit: null);
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
        bool unreadableSecondBody = false,
        bool missingCurrentModuleTypeReference = false,
        bool methodMemberReference = false,
        bool truncatedMethodMemberReference = false,
        bool truncatedOwnSignature = false,
        int largeTruncatedParameters = 0)
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
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Independent"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        if (missingCurrentModuleTypeReference)
        {
            metadata.AddTypeReference(
                MetadataTokens.EntityHandle(0x00000001),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Missing"));
        }
        if (methodMemberReference)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature()
                .Parameters(
                    0,
                    static returnType => returnType.Void(),
                    static _ => { });
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("NotAField"),
                metadata.GetOrAddBlob(signature));
        }
        if (largeTruncatedParameters > 0)
        {
            // Declares one more parameter than it encodes.
            var truncated = new BlobBuilder();
            truncated.WriteByte(0x00);
            truncated.WriteCompressedInteger(largeTruncatedParameters + 1);
            truncated.WriteByte(0x01);
            for (int i = 0; i < largeTruncatedParameters; i++)
            {
                truncated.WriteByte(0x1D);
                truncated.WriteByte(0x08);
            }
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("LargeTruncated"),
                metadata.GetOrAddBlob(truncated));
        }
        if (truncatedMethodMemberReference)
        {
            // A method header with no parameter count or return type.
            metadata.AddMemberReference(
                owner,
                metadata.GetOrAddString("Truncated"),
                metadata.GetOrAddBlob(new byte[] { 0x00 }));
        }

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
                truncatedOwnSignature
                    ? metadata.GetOrAddBlob(new byte[] { 0x00 })
                    : metadata.GetOrAddBlob(signature),
                offset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte> BuildIndependentTypeSpecImage(
        bool genericInstance)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("IndependentEcma335.dll"),
            metadata.GetOrAddGuid(
                new Guid("c4051295-5f43-489c-b1ee-48ac76306c9e")),
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
            metadata.GetOrAddString("Independent"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Independent"),
                metadata.GetOrAddString("Target`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddGenericParameter(
            target,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            0);

        var typeSpec = new BlobBuilder();
        SignatureTypeEncoder type =
            new BlobEncoder(typeSpec)
                .TypeSpecificationSignature();
        if (genericInstance)
        {
            type.GenericInstantiation(
                    target,
                    1,
                    isValueType: false)
                .AddArgument()
                .Type(
                    MetadataTokens.TypeDefinitionHandle(ushort.MaxValue),
                    isValueType: false);
        }
        else
        {
            type.SZArray().Type(
                MetadataTokens.TypeDefinitionHandle(ushort.MaxValue),
                isValueType: false);
        }
        TypeSpecificationHandle nestedInvalid =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpec));

        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.OpCode(ILOpCode.Ldtoken);
        instructions.Token(target);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ldtoken);
        instructions.Token(nestedInvalid);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ret);
        var bodies = new BlobBuilder();
        int bodyOffset =
            new MethodBodyStreamEncoder(bodies).AddMethodBody(
                instructions,
                maxStack: 1);
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
            metadata.GetOrAddString("Use"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

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
    }

    static ImmutableArray<byte>
        BuildIndependentModuleInitializerImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                "IndependentModuleInitializer.dll"),
            metadata.GetOrAddGuid(
                new Guid("40882445-e08e-4de3-8e0b-de1a61b0b486")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "IndependentModuleInitializer"),
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
            metadata.GetOrAddString("Independent"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddMethod(
            ".cctor",
            [
                (byte)ILOpCode.Ldtoken,
                0x02, 0x00, 0x00, 0x02,
                (byte)ILOpCode.Pop,
                (byte)ILOpCode.Ret,
            ],
            MethodAttributes.Private
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName);
        AddMethod(
            "Healthy",
            [(byte)ILOpCode.Ret],
            MethodAttributes.Public | MethodAttributes.Static);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableArray.Create(image.ToArray());

        void AddMethod(
            string name,
            byte[] il,
            MethodAttributes attributes)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            int offset = encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature()
                .Parameters(
                    0,
                    static returnType => returnType.Void(),
                    static _ => { });
            metadata.AddMethodDefinition(
                attributes,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                offset,
                MetadataTokens.ParameterHandle(1));
        }
    }

    static ImmutableArray<byte> BuildIndependentGlobalMethodImage()
    {
        var metadata = CreateMetadata(
            "IndependentGlobalMethod",
            new Guid("3a65859f-6e0e-4fd1-8717-dba6012c9dfa"));
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
            metadata.GetOrAddString("Independent"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Independent"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddMethod("Global", [(byte)ILOpCode.Ret]);
        AddMethod(
            "Use",
            [
                (byte)ILOpCode.Ldtoken,
                0x03, 0x00, 0x00, 0x02,
                (byte)ILOpCode.Pop,
                (byte)ILOpCode.Call,
                0x01, 0x00, 0x00, 0x06,
                (byte)ILOpCode.Ret,
            ]);
        return Serialize(metadata, bodies);

        void AddMethod(string name, byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            int offset = encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
            AddVoidMethod(metadata, name, offset);
        }
    }

    static ImmutableArray<byte> BuildIndependentNestedModuleImage()
    {
        var metadata = CreateMetadata(
            "IndependentNestedModule",
            new Guid("48ccb239-0746-462a-a99a-4ae8e7564b5e"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Independent"),
                metadata.GetOrAddString("Owner"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Independent"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(nested, owner);

        var code = new BlobBuilder();
        code.WriteBytes(
            new byte[]
            {
                (byte)ILOpCode.Ldtoken,
                0x04, 0x00, 0x00, 0x02,
                (byte)ILOpCode.Pop,
                (byte)ILOpCode.Ret,
            });
        var bodies = new BlobBuilder();
        int offset =
            new MethodBodyStreamEncoder(bodies).AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        AddVoidMethod(metadata, "Use", offset);
        return Serialize(metadata, bodies);
    }

    static ImmutableArray<byte> BuildMethodSpecScopeImage()
    {
        MetadataBuilder metadata = CreateMetadata(
            "MethodSpecScope",
            new Guid("b0e5f7a2-61c4-4d0e-8f3a-2d8c9e4b7a15"));
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
            metadata.GetOrAddString("Scope"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);

        // 0x06000001: static void G<T>()
        MethodDefinitionHandle generic = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("G"),
            VoidSignature(genericParameters: 1),
            Body([(byte)ILOpCode.Ret]),
            MetadataTokens.ParameterHandle(1));

        // 0x2B000001: G<!!0>
        var instantiation = new BlobBuilder();
        new BlobEncoder(instantiation)
            .MethodSpecificationSignature(1)
            .AddArgument()
            .GenericMethodTypeParameter(0);
        metadata.AddMethodSpecification(
            generic,
            metadata.GetOrAddBlob(instantiation));

        byte[] call =
        [
            (byte)ILOpCode.Call,
            0x01, 0x00, 0x00, 0x2B,
            (byte)ILOpCode.Ret,
        ];
        // 0x06000002: static void Valid<U>() { G<U>(); }
        MethodDefinitionHandle valid = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Valid"),
            VoidSignature(genericParameters: 1),
            Body(call),
            MetadataTokens.ParameterHandle(1));
        // 0x06000003: static void Invalid() { G<!!0>(); }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Invalid"),
            VoidSignature(genericParameters: 0),
            Body(call),
            MetadataTokens.ParameterHandle(1));

        metadata.AddGenericParameter(
            generic,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            0);
        metadata.AddGenericParameter(
            valid,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("U"),
            0);
        return Serialize(metadata, bodies);

        int Body(byte[] il)
        {
            var code = new BlobBuilder();
            code.WriteBytes(il);
            return encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        BlobHandle VoidSignature(int genericParameters)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(genericParameterCount: genericParameters)
                .Parameters(
                    0,
                    static returnType => returnType.Void(),
                    static _ => { });
            return metadata.GetOrAddBlob(signature);
        }
    }

    static ImmutableArray<byte> BuildOversizedStateMachineNameImage(
        int kickoffs)
    {
        MetadataBuilder metadata = CreateMetadata(
            "StateMachineName",
            new Guid("3c9f0d0e-2b1a-4d61-9a55-6a9d1c1f4e21"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle stateMachine = metadata.AddTypeDefinition(
            TypeAttributes.NestedPrivate | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("<Run>d__0"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(kickoffs + 1));
        metadata.AddNestedType(stateMachine, host);

        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("AsyncStateMachineAttribute"));
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                static returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(
                    systemType,
                    isValueType: false));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        // One shared value blob naming N.Host+<Run>d__0 behind a name far
        // longer than any type-name bound.
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(
            new string('A', 64 * 1024) + "N.Host+<Run>d__0");
        value.WriteUInt16(0);
        BlobHandle sharedValue = metadata.GetOrAddBlob(value);

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        for (int i = 0; i < kickoffs; i++)
        {
            MethodDefinitionHandle kickoff = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Run{i}"),
                VoidSignature(metadata, instance: false),
                AddRet(encoder),
                MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(kickoff, constructor, sharedValue);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("MoveNext"),
            VoidSignature(metadata, instance: true),
            AddRet(encoder),
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata, bodies);

        static int AddRet(MethodBodyStreamEncoder encoder)
        {
            var code = new BlobBuilder();
            code.WriteByte((byte)ILOpCode.Ret);
            return encoder.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        }

        static BlobHandle VoidSignature(
            MetadataBuilder metadata,
            bool instance)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: instance)
                .Parameters(
                    0,
                    static returnType => returnType.Void(),
                    static _ => { });
            return metadata.GetOrAddBlob(signature);
        }
    }

    static MetadataBuilder CreateMetadata(
        string name,
        Guid mvid)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        return metadata;
    }

    static void AddVoidMethod(
        MetadataBuilder metadata,
        string name,
        int bodyOffset)
    {
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
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
    }

    static ImmutableArray<byte> Serialize(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
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
    }
}
