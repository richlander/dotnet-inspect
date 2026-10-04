using System.Collections.Immutable;
using System.Reflection.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class CatalogMethodDefinitionCorrespondencePlanTests
{
    [Fact]
    public void ReorderedMethodDefs_SelectExactTargetInsteadOfReusingSourceRid()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity expected = Method(pair.TargetCallGraph, "Transform");
        MethodIdentity rawRidTarget = pair.TargetCallGraph.DeclaredMethods.Single(
            method => method.MetadataToken == source.MetadataToken);
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                pair.TargetCallGraph.DeclaredMethods);

        CatalogMethodDefinitionCorrespondenceOutcome outcome =
            Project(pair, plan);
        if (outcome
            is CatalogMethodDefinitionCorrespondenceOutcome.Unavailable
                unavailable)
        {
            Assert.Fail(string.Join(
                ", ",
                unavailable.Failures.Select(Describe)));
        }
        CatalogMethodDefinitionCorrespondenceOutcome.Exact exact =
            Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Exact>(
                outcome);

        Assert.NotEqual(source.MetadataToken, expected.MetadataToken);
        Assert.NotEqual(source.Name, rawRidTarget.Name);
        Assert.Same(pair.TargetAssembly, exact.Assembly);
        Assert.Equal(expected.MetadataToken, exact.Method.Token);
        Assert.Equal(expected.ModuleVersionId, exact.Method.ModuleVersionId);
    }

    [Theory]
    [InlineData("Neighbor")]
    [InlineData("Invoke")]
    public void NeighboringSignatures_SelectTheirOwnTarget(string methodName)
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, methodName);
        MethodIdentity expected = Method(pair.TargetCallGraph, methodName);
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                pair.TargetCallGraph.DeclaredMethods);

        CatalogMethodDefinitionCorrespondenceOutcome.Exact exact =
            Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Exact>(
                Project(pair, plan));

        Assert.Equal(expected.MetadataToken, exact.Method.Token);
    }

    [Fact]
    public void MissingSameNameTarget_DoesNotFallBackToTokenOrOrdinal()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                pair.TargetCallGraph.DeclaredMethods.Where(
                    method => method.Name != source.Name));

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void SameNameSignatureNearMiss_DoesNotCorrespond()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = Method(pair.TargetCallGraph, "Transform") with
        {
            ParameterTypes =
                Method(pair.TargetCallGraph, "Neighbor").ParameterTypes,
        };
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void FunctionPointerCallingConvention_IsIdentityBearing()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Invoke");
        MethodIdentity target = Method(pair.TargetCallGraph, "Invoke");
        MethodSignature<TypeRef> signature =
            target.ParameterTypes[0].FunctionPointerSignature!.Value;
        var changedSignature = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.StdCall,
                signature.Header.Attributes),
            signature.ReturnType,
            signature.RequiredParameterCount,
            signature.GenericParameterCount,
            signature.ParameterTypes);
        MethodIdentity changed = target with
        {
            ParameterTypes =
            [
                TypeRef.UnsupportedFunctionPointer(changedSignature),
                target.ParameterTypes[1],
            ],
        };
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [changed]);

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void TypeDefAndTypeRefAddressing_ResolveThroughSelectedRoots()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = WithReferencedDeclaringType(
            pair,
            Method(pair.TargetCallGraph, "Transform"));
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        var exact = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Exact>(
                Project(pair, plan));

        Assert.Equal(target.MetadataToken, exact.Method.Token);
    }

    [Fact]
    public void RecursiveSameNameDefinitions_AreNotSelectedRootCorrespondence()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "UseHelper");
        MethodIdentity target = Method(pair.TargetCallGraph, "UseHelper");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void SelectedRootClassAndValueType_DoNotCorrespond()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(
            pair.SourceCallGraph,
            "TransformKind",
            declaringType: "KindShape");
        MethodIdentity target = Method(
            pair.TargetCallGraph,
            "TransformKind",
            declaringType: "KindShape");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void DerivedValueTypeKind_RejectsExplicitClassEncoding()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = WithParameterRawType(
            Method(pair.SourceCallGraph, "Transform"),
            rawTypeKind: 0);
        MethodIdentity target = WithParameterRawType(
            Method(pair.TargetCallGraph, "Transform"),
            rawTypeKind: 0x12);
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        Assert.IsType<CatalogMethodDefinitionCorrespondenceOutcome.Missing>(
            Project(pair, plan));
    }

    [Fact]
    public void MalformedArrayBounds_AreUnavailable()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = Method(pair.TargetCallGraph, "Transform");
        source = source with
        {
            ParameterTypes =
            [
                TypeRef.MdArray(
                    source.ParameterTypes[0],
                    new ArrayShape(1, [2, 3], [])),
            ],
        };
        target = target with
        {
            ParameterTypes =
            [
                TypeRef.MdArray(
                    target.ParameterTypes[0],
                    new ArrayShape(1, [2, 3], [])),
            ],
        };
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));

        Assert.Contains(
            unavailable.Failures,
            failure => failure
                is CatalogMethodDefinitionCorrespondenceFailure
                    .IncompleteProjection incomplete
                && incomplete.Failures.Any(
                    nested => nested
                        is MemberCorrespondenceFailure
                            .MalformedTypeShape));
    }

    [Fact]
    public void DuplicateExactTargetCandidates_ReportAmbiguous()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = Method(pair.TargetCallGraph, "Transform");
        MethodIdentity other = Method(pair.TargetCallGraph, "Other");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [
                    target,
                    target with { MetadataToken = other.MetadataToken },
                ]);

        var ambiguous = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Ambiguous>(
                Project(pair, plan));

        Assert.Equal(
            [target.MetadataToken, other.MetadataToken],
            ambiguous.Candidates.Select(candidate => candidate.Token));
    }

    [Fact]
    public void TargetGenerationMismatch_IsUnavailable()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = Method(pair.TargetCallGraph, "Transform") with
        {
            ModuleVersionId = source.ModuleVersionId,
        };
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));
        var mismatch = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceFailure.GenerationMismatch>(
                Assert.Single(unavailable.Failures));

        Assert.Equal(
            CatalogMethodDefinitionCorrespondenceSide.Target,
            mismatch.Side);
        Assert.Same(pair.TargetAssembly, mismatch.Assembly);
        Assert.Equal(
            pair.TargetSnapshot.ModuleVersionId,
            mismatch.ExpectedModuleVersionId);
        Assert.Equal(source.ModuleVersionId, mismatch.ActualModuleVersionId);
    }

    [Fact]
    public void SnapshotFromAnotherRegistration_IsUnavailable()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.TargetSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [Method(pair.TargetCallGraph, "Transform")]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));

        Assert.Contains(
            unavailable.Failures,
            failure => failure
                is CatalogMethodDefinitionCorrespondenceFailure
                    .ImageOwnerMismatch
            {
                Side:
                        CatalogMethodDefinitionCorrespondenceSide.Source,
            });
    }

    [Fact]
    public void InvalidSourceMethodDefToken_IsUnavailable()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform") with
        {
            MetadataToken = 0x0600FFFF,
        };
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [Method(pair.TargetCallGraph, "Transform")]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));

        Assert.Contains(
            unavailable.Failures,
            failure => failure
                is CatalogMethodDefinitionCorrespondenceFailure
                    .InvalidMethodToken
            {
                Side:
                        CatalogMethodDefinitionCorrespondenceSide.Source,
            });
    }

    [Fact]
    public void ContextWithDifferentTargetGeneration_IsUnavailable()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = WithReferencedDeclaringType(
            pair,
            Method(pair.TargetCallGraph, "Transform"));
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(
                    pair,
                    plan,
                    bindFixtureReferencesToTarget: false));
        var mismatch = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceFailure
                .ResolutionContextMismatch>(
                    Assert.Single(unavailable.Failures));

        Assert.Equal(
            CatalogMethodDefinitionCorrespondenceSide.Target,
            mismatch.Side);
        Assert.Equal(
            pair.TargetSnapshot.ModuleVersionId,
            mismatch.ExpectedModuleVersionId);
        Assert.Equal(
            pair.SourceSnapshot.ModuleVersionId,
            mismatch.ActualModuleVersionId);
    }

    [Fact]
    public void SameNameCandidateLimit_FailsClosed()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = Method(pair.SourceCallGraph, "Transform");
        MethodIdentity target = Method(pair.TargetCallGraph, "Transform");
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                Enumerable.Repeat(
                    target,
                    MetadataSafetyPolicy.MaxCorrespondenceCandidates + 1));

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));

        Assert.Contains(
            unavailable.Failures,
            failure => failure
                is CatalogMethodDefinitionCorrespondenceFailure
                    .ResourceLimitExceeded
            {
                Limit:
                        CatalogMethodDefinitionCorrespondenceLimit
                            .SameNameCandidates,
            });
    }

    [Fact]
    public void UnresolvedNominalTypes_AreUnavailableRatherThanUnique()
    {
        FixturePair pair = OpenFixtures();
        MethodIdentity source = WithMissingParameter(
            Method(pair.SourceCallGraph, "Transform"));
        MethodIdentity target = WithMissingParameter(
            Method(pair.TargetCallGraph, "Transform"));
        CatalogMethodDefinitionCorrespondencePlan plan =
            CatalogMethodDefinitionCorrespondencePlan.Create(
                pair.SourceAssembly,
                pair.SourceSnapshot,
                source,
                pair.TargetAssembly,
                pair.TargetSnapshot,
                [target]);

        var unavailable = Assert.IsType<
            CatalogMethodDefinitionCorrespondenceOutcome.Unavailable>(
                Project(pair, plan));

        Assert.IsType<
            CatalogMethodDefinitionCorrespondenceFailure
                .IndeterminateProjection>(
                    Assert.Single(unavailable.Failures));
    }

    static CatalogMethodDefinitionCorrespondenceOutcome Project(
        FixturePair pair,
        CatalogMethodDefinitionCorrespondencePlan plan,
        bool bindFixtureReferencesToTarget = true)
    {
        using var catalog = new TypeResolutionCatalog();
        catalog.RegisterRetainedSnapshot(
            pair.SourceAssembly,
            pair.SourceSnapshot);
        catalog.RegisterRetainedSnapshot(
            pair.TargetAssembly,
            pair.TargetSnapshot);
        var policy = new AssemblyDependencyResolver(
            new(pair.SourceAssembly.Path!));
        using TypeResolutionContext context = catalog.CreateContext(
            new FixtureBindingPolicy(
                bindFixtureReferencesToTarget
                    ? pair.TargetAssembly
                    : pair.SourceAssembly,
                policy),
            [pair.SourceAssembly, pair.TargetAssembly],
            plan.Requests);
        return plan.Project(context);
    }

    static FixturePair OpenFixtures()
    {
        (ResolvedAssemblyReference sourceAssembly,
            AssemblyImageSnapshot sourceSnapshot,
            LibraryCallGraphAnalysisResult sourceCallGraph) = Open(
                FixtureCatalog.AnalysisMethodCorrespondenceSurface
                    .AssemblyPath());
        (ResolvedAssemblyReference targetAssembly,
            AssemblyImageSnapshot targetSnapshot,
            LibraryCallGraphAnalysisResult targetCallGraph) = Open(
                FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                    .AssemblyPath());
        return new(
            sourceAssembly,
            sourceSnapshot,
            sourceCallGraph,
            targetAssembly,
            targetSnapshot,
            targetCallGraph);
    }

    static (
        ResolvedAssemblyReference Assembly,
        AssemblyImageSnapshot Snapshot,
        LibraryCallGraphAnalysisResult CallGraph) Open(string path)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "method correspondence test"));
        AssemblyImageSnapshot snapshot = Assert.IsType<
            AssemblyImageSnapshotResult.Ready>(
                AssemblyImageSnapshot.Open(
                    assembly,
                    static _ => true,
                    static _ => { })).Snapshot;
        LibraryBodyAnalysisExecution analysis =
            BodyAnalysisTestExecution.OpenFromPrefetchedImage(
                path,
                snapshot.Content,
                LibraryBodyAnalysisFeatures.MethodEvidence);
        return (assembly, snapshot, analysis.CallGraph);
    }

    static MethodIdentity Method(
        LibraryCallGraphAnalysisResult callGraph,
        string name,
        string declaringType = "Widget") =>
        callGraph.DeclaredMethods.Single(
            method => method.DeclaringType.Name == declaringType
                && method.Name == name);

    static MethodIdentity WithMissingParameter(MethodIdentity method)
    {
        var missingAssembly = new AssemblyReferenceIdentity(
            "Missing",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        MetadataTypeDefinitionName typeName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Missing",
                    ["Argument"])).Name;
        TypeRef missing = TypeRef.Definition(
            "Missing",
            "Missing",
            "Argument",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(
                    missingAssembly),
                typeName));
        return method with { ParameterTypes = [missing] };
    }

    static MethodIdentity WithReferencedDeclaringType(
        FixturePair pair,
        MethodIdentity method)
    {
        MetadataTypeDefinitionName typeName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    method.DeclaringType.Namespace,
                    [method.DeclaringType.Name])).Name;
        return method with
        {
            DeclaringType = TypeRef.Definition(
                method.DeclaringType.Assembly,
                method.DeclaringType.Namespace,
                method.DeclaringType.Name,
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.AssemblyReference(
                        pair.TargetAssembly.Identity),
                    typeName)),
        };
    }

    static MethodIdentity WithParameterRawType(
        MethodIdentity method,
        byte rawTypeKind)
    {
        TypeRef parameter = Assert.Single(method.ParameterTypes);
        return method with
        {
            ParameterTypes =
            [
                TypeRef.Definition(
                    parameter.Assembly,
                    parameter.Namespace,
                    parameter.Name,
                    parameter.Resolution,
                    rawTypeKind: rawTypeKind),
            ],
        };
    }

    static string Describe(
        CatalogMethodDefinitionCorrespondenceFailure failure) =>
        failure switch
        {
            CatalogMethodDefinitionCorrespondenceFailure
                .IncompleteProjection incomplete =>
                    $"{incomplete.Side}:Incomplete("
                    + string.Join(
                        ",",
                        incomplete.Failures.Select(
                            nested => nested.GetType().Name))
                    + ")",
            CatalogMethodDefinitionCorrespondenceFailure
                .IndeterminateProjection indeterminate =>
                    $"{indeterminate.Side}:Indeterminate("
                    + string.Join(
                        ",",
                        indeterminate.Evidence.Select(
                            nested => nested.GetType().Name))
                    + ")",
            _ => $"{failure.Side}:{failure.GetType().Name}",
        };

    sealed record FixturePair(
        ResolvedAssemblyReference SourceAssembly,
        AssemblyImageSnapshot SourceSnapshot,
        LibraryCallGraphAnalysisResult SourceCallGraph,
        ResolvedAssemblyReference TargetAssembly,
        AssemblyImageSnapshot TargetSnapshot,
        LibraryCallGraphAnalysisResult TargetCallGraph);

    sealed class FixtureBindingPolicy(
        ResolvedAssemblyReference target,
        IAssemblyBindingPolicy fallback) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && target.Identity.IsEquivalentTo(reference.Identity)
                ? AssemblyBindingSelection.Found(target)
                : fallback.Select(request).Selection;
        }
    }

}
