using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void NestedDeclaringType_DisplayString_PreservesContainingPath()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(NestedLeft).Assembly.Location);
        var ns = typeof(NestedLeft).Namespace;

        // The two `Target` methods live in NestedLeft.Dup and NestedRight.Dup. Their
        // declaring-type display must keep the containing-type path so they do not
        // collapse to a single `<ns>.Dup` identity.
        var displays = index.CallGraph.Methods
            .Where(method => method.Name == nameof(NestedLeft.Dup.Target)
                && method.DeclaringType.Name.EndsWith("+Dup", StringComparison.Ordinal))
            .Select(method => method.DeclaringType.ToQualifiedDisplayString())
            .Distinct()
            .OrderBy(display => display, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, displays.Count);
        Assert.Equal($"{ns}.NestedLeft.Dup", displays[0]);
        Assert.Equal($"{ns}.NestedRight.Dup", displays[1]);
    }

    [Fact]
    public void FindCalls_NestedTypesWithSameSimpleName_StayDistinct()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(NestedLeft).Assembly.Location);
        var ns = typeof(NestedLeft).Namespace;

        var leftCalls = index.CallGraph.FindCalls(MemberPattern.Method($"{ns}.NestedLeft.Dup", nameof(NestedLeft.Dup.Target)));
        var rightCalls = index.CallGraph.FindCalls(MemberPattern.Method($"{ns}.NestedRight.Dup", nameof(NestedRight.Dup.Target)));

        // Each pattern resolves to exactly its own containing type's call site, not both.
        var left = Assert.Single(leftCalls);
        Assert.Equal($"{ns}.NestedLeft", left.Caller.DeclaringType.ToQualifiedDisplayString());
        var right = Assert.Single(rightCalls);
        Assert.Equal($"{ns}.NestedRight", right.Caller.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void NestedTypeUnderGenericOuter_DisplayString_PreservesPathAndStripsArity()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(GenericOuter<int>).Assembly.Location);
        var ns = typeof(GenericOuter<>).Namespace;

        var leaf = Assert.Single(index.CallGraph.Methods.Where(method =>
            method.Name == nameof(GenericOuter<int>.Inner.Leaf)
            && method.DeclaringType.Name.StartsWith("GenericOuter", StringComparison.Ordinal)));
        Assert.Equal($"{ns}.GenericOuter.Inner", leaf.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void FindCalls_DistinguishesOverloadsBySignature()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(OverloadTargets).Assembly.Location);
        var declaring = $"{typeof(OverloadTargets).Namespace}.{nameof(OverloadTargets)}";

        var noArg = index.CallGraph.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray<TypeRef>.Empty));
        var intArg = index.CallGraph.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray.Create(TypeRef.CoreLib("System", "Int32"))));
        var stringArg = index.CallGraph.FindCalls(MemberPattern.Method(declaring, nameof(OverloadTargets.M), ImmutableArray.Create(TypeRef.CoreLib("System", "String"))));

        // Each signature pattern resolves to exactly its own overload's call site.
        Assert.Empty(Assert.Single(noArg).Callee.ParameterTypes);
        Assert.Equal(TypeRef.CoreLib("System", "Int32"), Assert.Single(Assert.Single(intArg).Callee.ParameterTypes));
        Assert.Equal(TypeRef.CoreLib("System", "String"), Assert.Single(Assert.Single(stringArg).Callee.ParameterTypes));
    }

    [Fact]
    public void TopLeverage_KeepsOverloadsDistinct()
    {
        var index = BodyAnalysisTestExecution.Open(typeof(OverloadTargets).Assembly.Location);

        // Same-assembly calls resolve by MethodDef token, so this guards token-based
        // overload distinctness. The param-bearing key collapse is exercised separately
        // by BuildCallerTree_WithScope_KeepsTargetOverloadsDistinct (cross-assembly).
        var overloads = index.Leverage.Top(count: 200,
                scope: method => method.DeclaringType.Name == nameof(OverloadTargets) && method.Name == nameof(OverloadTargets.M))
            .Where(entry => entry.Method.Name == nameof(OverloadTargets.M))
            .ToList();

        // All three overloads remain separate leverage entries, and each is credited
        // with exactly its own single caller. If the keys collapsed, one entry would
        // absorb all three calls and the others would show zero.
        Assert.Equal(3, overloads.Count);
        Assert.All(overloads, entry => Assert.Equal(1, entry.DirectCallerCount));
    }

    [Fact]
    public void ImplementationProfiles_ExposeRawStructureAndOverloadEdges()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);
        var profiles = index.ImplementationProfiles.Profiles.Where(
            profile =>
                profile.Method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && profile.Method.Name
                    == "Analyze");

        var wrapper = Assert.Single(
            profiles,
            profile =>
                profile.Method.ParameterTypes.Length == 1
                && profile.Method.ParameterTypes[0]
                    .Name == "Int32");
        var implementation = Assert.Single(
            profiles,
            profile =>
                profile.Method.ParameterTypes.Length == 2);
        var independent = Assert.Single(
            profiles,
            profile =>
                profile.Method.ParameterTypes.Length == 1
                && profile.Method.ParameterTypes[0]
                    .Name == "String");

        Assert.True(
            implementation.InstructionCount
                > wrapper.InstructionCount);
        Assert.True(implementation.DistinctOpcodeCount > 1);
        Assert.True(implementation.ConditionalBranchCount > 0);
        Assert.True(implementation.LoopCount > 0);
        Assert.Equal(1, implementation.CatchCount);
        Assert.Equal(1, wrapper.OutgoingOverloadTargetCount);
        Assert.Equal(1, implementation.IncomingOverloadCallerCount);
        Assert.Equal(0, independent.IncomingOverloadCallerCount);
        Assert.Equal(0, independent.OutgoingOverloadTargetCount);
        var functionLoader = Assert.Single(
            profiles,
            profile =>
                profile != wrapper
                && profile != implementation
                && profile != independent);
        Assert.Equal(0, functionLoader.OutgoingOverloadTargetCount);
        Assert.DoesNotContain(
            index.ImplementationProfiles.OverloadRelationships,
            relationship =>
                relationship.Caller.MetadataToken
                    == functionLoader.Method.MetadataToken);
        Assert.True(wrapper.IsComplete);
        Assert.True(implementation.IsComplete);
    }

    [Fact]
    public void ImplementationProfiles_ExposeNormalFlowCyclomaticComplexity()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

        var profile = Assert.Single(
            index.ImplementationProfiles.Profiles,
            candidate => candidate.Method.Name == "SemanticSwitchCase");

        Assert.Equal(1, profile.SwitchCount);
        Assert.Equal(9, profile.SwitchTargetCount);
        Assert.Equal(10, profile.NormalFlowCyclomaticComplexity);
    }

    [Fact]
    public void ImplementationProfiles_RequireExplicitAcquisition()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.MethodEvidence);

        Assert.False(index.ImplementationProfiles.WasRequested);
        Assert.Empty(index.ImplementationProfiles.Profiles);
        Assert.Empty(
            index.ImplementationProfiles
                .OverloadRelationships);
    }

    [Fact]
    public void
        OverloadRelationships_DefaultCompatibilityAcquisitionPreservesLegacyResult()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());

        Assert.False(
            index.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .ImplementationProfiles));
        OverloadCallRelationship relationship =
            Assert.Single(
                index.OverloadRelationships(),
                relationship =>
                    relationship.Caller.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && relationship.Caller.Name
                        == "Analyze");

        Assert.Single(relationship.Caller.ParameterTypes);
        Assert.Equal(
            2,
            relationship.Callee.ParameterTypes.Length);
    }

    [Fact]
    public void OverloadRelationships_PreserveExactCallerTargetAndOffset()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

        var relationship = Assert.Single(
            index.ImplementationProfiles.OverloadRelationships,
            relationship =>
                relationship.Caller.DeclaringType.Name
                    == "ImplementationProfileSample"
                && relationship.Caller.Name
                    == "Analyze");

        Assert.Single(relationship.Caller.ParameterTypes);
        Assert.Equal(2, relationship.Callee.ParameterTypes.Length);
        Assert.Equal(
            relationship.Caller,
            relationship.EvidenceMethod);
        Assert.True(relationship.ILOffset >= 0);
    }

    [Fact]
    public void OverloadRelationships_ResolveConstructedGenericArityExactly()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

        var relationship = Assert.Single(
            index.ImplementationProfiles.OverloadRelationships,
            relationship =>
                relationship.Caller.DeclaringType.Name
                    == "GenericOverloadSample`1"
                && relationship.Caller.Name == "Route");

        Assert.Equal(0, relationship.Caller.GenericArity);
        Assert.Equal(2, relationship.Callee.GenericArity);
    }

    [Fact]
    public void
        OverloadRelationships_LocalMemberReferenceUsesExactReturnType()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "ReturnOverloads.dll",
                EmitReturnOverloadAssembly(),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        OverloadCallRelationship relationship =
            Assert.Single(index.ImplementationProfiles.OverloadRelationships);
        Assert.Equal(0x06000002, relationship.Callee.MetadataToken);
        Assert.Equal(
            "String",
            relationship.Callee.ReturnType.Name);

        MethodImplementationProfile integer =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000001);
        MethodImplementationProfile text =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000002);
        Assert.Equal(0, integer.IncomingOverloadCallerCount);
        Assert.Equal(1, text.IncomingOverloadCallerCount);
    }

    [Fact]
    public void
        OverloadRelationships_LocalVarargMemberReferenceUsesRequiredPrefix()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "VarargOverloads.dll",
                EmitVarargOverloadAssembly(),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        OverloadCallRelationship relationship =
            Assert.Single(index.ImplementationProfiles.OverloadRelationships);
        Assert.Equal(0x06000002, relationship.Caller.MetadataToken);
        Assert.Equal(0x06000001, relationship.Callee.MetadataToken);

        MethodImplementationProfile target =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000001);
        MethodImplementationProfile caller =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000002);
        Assert.Equal(1, target.IncomingOverloadCallerCount);
        Assert.Equal(1, caller.OutgoingOverloadTargetCount);
        Assert.True(target.IsComplete);
        Assert.True(caller.IsComplete);
    }

    [Fact]
    public void
        OverloadRelationships_LocalVarargMethodDefinitionParentUsesExactTarget()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "VarargMethodDefinitionParent.dll",
                EmitVarargOverloadAssembly(
                    methodDefinitionParent: true),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        Assert.Collection(
            index.CallGraph.DirectCalls,
            call => Assert.Equal(
                0x06000001,
                call.CalleeDefinitionToken),
            call => Assert.Equal(
                0x06000001,
                call.CalleeDefinitionToken));
        Assert.All(
            index.ImplementationProfiles.OverloadRelationships,
            relationship =>
            {
                Assert.Equal(
                    0x06000002,
                    relationship.Caller.MetadataToken);
                Assert.Equal(
                    0x06000001,
                    relationship.Callee.MetadataToken);
            });
        Assert.Equal(2, index.ImplementationProfiles.OverloadRelationships.Length);

        MethodImplementationProfile target =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000001);
        MethodImplementationProfile caller =
            Assert.Single(
                index.ImplementationProfiles.Profiles,
                profile =>
                    profile.Method.MetadataToken == 0x06000002);
        Assert.Equal(1, target.IncomingOverloadCallerCount);
        Assert.Equal(1, caller.OutgoingOverloadTargetCount);
        Assert.Equal(2, caller.DirectCallCount);
        Assert.Equal(1, caller.DistinctCalleeCount);
    }

    [Fact]
    public void
        OverloadRelationships_ExternalAssemblyMemberReferenceDoesNotResolveLocally()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "SelfCollision.dll",
                EmitExternalScopeCollisionAssembly(),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        DirectCall call = Assert.Single(index.CallGraph.DirectCalls);
        Assert.Equal(
            0x0A000001,
            call.CalleeDefinitionToken);
        Assert.Equal(
            "SelfCollision",
            call.Callee.DeclaringType.Assembly);
        Assert.Empty(index.ImplementationProfiles.OverloadRelationships);
    }

    [Fact]
    public void
        OverloadRelationships_ExternalSignatureTypeDoesNotResolveLocally()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "ParameterCollision.dll",
                EmitExternalSignatureTypeCollisionAssembly(),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        DirectCall call = Assert.Single(index.CallGraph.DirectCalls);
        Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
            call.Callee.ParameterTypes[0].Resolution?.Origin);
        Assert.Empty(index.ImplementationProfiles.OverloadRelationships);
    }

    [Fact]
    public void
        ImplementationProfiles_GuardRejectedLocalSignatureIsIncomplete()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                "DeepLocal.dll",
                EmitGuardRejectedLocalSignatureAssembly(),
                LibraryBodyAnalysisFeatures.ImplementationProfiles);

        MethodImplementationProfile profile =
            Assert.Single(index.ImplementationProfiles.Profiles);
        Assert.Equal(1, profile.LocalCount);
        Assert.False(profile.IsComplete);
        Assert.Contains(
            profile.IncompleteReasons,
            reason => reason.Contains(
                "local signature",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void
        ImplementationMetrics_GuardRejectedLocalSignaturePublishesLimitation()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 100,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 100);
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "DeepLocal.dll",
                EmitGuardRejectedLocalSignatureAssembly(),
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricKind.Locals,
                        limits,
                        new HashSet<int>
                        {
                            MetadataTokens.GetToken(
                                MetadataTokens
                                    .MethodDefinitionHandle(1)),
                        }));

        MethodImplementationMetricEvidence body =
            Assert.Single(
                execution.ImplementationMetrics.Bodies);
        ImplementationMetricLocalEvidence locals =
            Assert.IsType<ImplementationMetricLocalEvidence>(
                body.Locals);
        Assert.Equal(1, locals.DeclaredCount);
        Assert.False(locals.IsComplete);
        Assert.Contains(
            "local signature",
            locals.IncompleteReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            execution.ImplementationMetrics
                .Participation!.ActualStages,
            stage => stage.Stage
                == ImplementationMetricWorkStage
                    .CanonicalMethodContext);
    }

    [Fact]
    public void
        ImplementationMetrics_MalformedLocalsPreserveCompletedHeaderEvidence()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 100,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 100);
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedLocal.dll",
                EmitMalformedLocalSignatureAssembly(),
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricKind.BodySize
                            | ImplementationMetricKind
                                .Locals,
                        limits,
                        new HashSet<int>
                        {
                            MetadataTokens.GetToken(
                                MetadataTokens
                                    .MethodDefinitionHandle(1)),
                        }));

        MethodImplementationMetricEvidence body =
            Assert.Single(
                execution.ImplementationMetrics.Bodies);
        Assert.Equal(1, body.ILBytes);
        Assert.Null(body.Locals);
        Assert.Contains(
            execution.ImplementationMetrics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                nameof(BadImageFormatException),
                StringComparison.Ordinal));
        ImplementationMetricStageParticipation decode =
            Assert.Single(
                execution.ImplementationMetrics
                    .Participation!.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .LocalSignatureDecode);
        Assert.Equal(1, decode.AttemptedBodies);
        Assert.Equal(0, decode.CompletedBodies);
        Assert.Equal(1, decode.FailedBodies);
        Assert.DoesNotContain(
            execution.ImplementationMetrics
                .Participation.ActualStages,
            stage => stage.Stage
                == ImplementationMetricWorkStage
                    .CanonicalMethodContext);
    }

    [Fact]
    public void
        ImplementationMetrics_MalformedInstructionsPreserveEarlierEvidence()
    {
        var limits = new ImplementationMetricWorkLimits(
            maximumPhysicalBodies: 1,
            maximumEncodedIlBytes: 100,
            maximumAttributionProbeBodies: 1,
            maximumAttributionProbeIlBytes: 100);
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedInstruction.dll",
                EmitMalformedInstructionAssembly(),
                LibraryBodyAnalysisRequest
                    .CreateImplementationMetrics(
                        ImplementationMetricKind.BodySize
                            | ImplementationMetricKind.Locals
                            | ImplementationMetricKind
                                .InstructionShape,
                        limits,
                        new HashSet<int>
                        {
                            MetadataTokens.GetToken(
                                MetadataTokens
                                    .MethodDefinitionHandle(1)),
                        }));

        MethodImplementationMetricEvidence body =
            Assert.Single(
                execution.ImplementationMetrics.Bodies);
        Assert.Equal(1, body.ILBytes);
        ImplementationMetricLocalEvidence locals =
            Assert.IsType<ImplementationMetricLocalEvidence>(
                body.Locals);
        Assert.Equal(0, locals.DeclaredCount);
        Assert.True(locals.IsComplete);
        Assert.Null(body.InstructionShape);
        Assert.Contains(
            execution.ImplementationMetrics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                nameof(BadImageFormatException),
                StringComparison.Ordinal));
        ImplementationMetricStageParticipation context =
            Assert.Single(
                execution.ImplementationMetrics
                    .Participation!.ActualStages,
                stage => stage.Stage
                    == ImplementationMetricWorkStage
                        .CanonicalMethodContext);
        Assert.Equal(1, context.AttemptedBodies);
        Assert.Equal(0, context.CompletedBodies);
        Assert.Equal(1, context.FailedBodies);
        Assert.DoesNotContain(
            execution.ImplementationMetrics
                .Participation.ActualStages,
            stage => stage.Stage
                is ImplementationMetricWorkStage
                    .DirectCallCollection
                    or ImplementationMetricWorkStage
                        .AllocationSignalCollection
                    or ImplementationMetricWorkStage
                        .BodySignalCollection
                    or ImplementationMetricWorkStage
                        .SafetyCollection);
    }

    [Fact]
    public void ImplementationProfiles_AttributeAsyncBodiesToSourceMethods()
    {
        var index = BodyAnalysisTestExecution.Open(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

        var profiles = index.ImplementationProfiles.Profiles
            .Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "AnalyzeAsync"
                    && profile.Method.ParameterTypes.Length == 1
                    && profile.Method.ParameterTypes[0]
                        .Name == "Int32")
            .ToArray();
        Assert.Equal(2, profiles.Length);

        var stateMachineBody = Assert.Single(
            profiles,
            profile =>
                profile.Async);
        var kickoffBody = Assert.Single(
            profiles,
            profile => !profile.Async);

        Assert.Equal(
            stateMachineBody.Method,
            kickoffBody.Method);
        Assert.NotEqual(
            stateMachineBody.EvidenceMethod,
            kickoffBody.EvidenceMethod);
        Assert.Equal("MoveNext", stateMachineBody.EvidenceMethod.Name);

        var forwardingProfiles = index.ImplementationProfiles.Profiles
            .Where(
                profile =>
                    profile.Method.DeclaringType.Name
                        == "ImplementationProfileSample"
                    && profile.Method.Name == "AnalyzeAsync"
                    && profile.Method.ParameterTypes.Length == 1
                    && profile.Method.ParameterTypes[0]
                        .Name == "String")
            .ToArray();
        var forwardingBody = Assert.Single(
            forwardingProfiles,
            profile => profile.Async);
        var forwardingKickoff = Assert.Single(
            forwardingProfiles,
            profile => !profile.Async);
        Assert.Equal(
            1,
            forwardingBody.OutgoingOverloadTargetCount);
        Assert.Equal(
            0,
            forwardingKickoff.OutgoingOverloadTargetCount);
    }
}
