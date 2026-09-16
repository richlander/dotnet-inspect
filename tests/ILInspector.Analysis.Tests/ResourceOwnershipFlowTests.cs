using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed class ResourceOwnershipFlowTests
{
    static readonly ResourceEffectModelIdentity ModelIdentity =
        new("fixture.declared-resource");
    static readonly ResourceKindIdentity DeclaredKind =
        new("fixture.declared-resource.buffer");

    static string CallerPath =>
        FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
    static string ApiPath =>
        FixtureCatalog.AnalysisResourceOwnershipApi.AssemblyPath();

    [Fact]
    public void DistinctDeclaredAndArrayPoolResourcesRemainIndependent()
    {
        ResourceEffectAdmission admission = Admit(
            ArrayPoolResourceEffectModel.Definition(),
            DeclaredResourceModel());
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                admission);

        ResourceOwnershipMethodEvidence evidence =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "HoldDistinctResources");
        Assert.True(evidence.IsComplete);
        Assert.Empty(evidence.Limits);
        Assert.Equal(2, evidence.Acquisitions.Length);

        ResourceAcquisitionOwnership declared =
            evidence.Acquisitions.Single(acquisition =>
                acquisition.ResourceKind.Identity == DeclaredKind);
        ResourceAcquisitionOwnership pooled =
            evidence.Acquisitions.Single(acquisition =>
                acquisition.ResourceKind.Identity
                    == ArrayPoolResourceEffectModel.BufferKind);
        Assert.Equal(
            DeclaredKind,
            Assert.Single(
                Assert.Single(declared.Uses).Effect!.ResourceKinds)
                .Identity);
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            Assert.Single(
                Assert.Single(pooled.Uses).Effect!.ResourceKinds)
                .Identity);
        Assert.All(
            evidence.Acquisitions,
            acquisition => Assert.Equal(
                ResourceOwnershipUseKind.Released,
                Assert.Single(acquisition.Uses).Kind));
        Assert.Single(
            index.ArrayPoolOwnership.Single(candidate =>
                candidate.Method.Name == "HoldDistinctResources").Rents);
    }

    [Fact]
    public void DeclaredResourceImplementationRetainsInnerArrayPoolFlow()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            ApiPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            Resolver(ApiPath));

        ResourceOwnershipMethodEvidence acquire =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "Acquire");
        ResourceAcquisitionOwnership pooled =
            Assert.Single(acquire.Acquisitions);
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            pooled.ResourceKind.Identity);
        Assert.Equal(
            ResourceOwnershipUseKind.ReturnedToCaller,
            Assert.Single(pooled.Uses).Kind);

        ResourceOwnershipMethodEvidence release =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "Release");
        ResourceOwnershipUse use =
            Assert.Single(
                Assert.Single(release.Parameters).Uses);
        Assert.Equal(ResourceOwnershipUseKind.Released, use.Kind);
        Assert.Equal(
            ArrayPoolResourceEffectModel.BufferKind,
            Assert.Single(use.Effect!.ResourceKinds).Identity);
    }

    [Fact]
    public void MissingRequiredAuthorityRemainsIncomplete()
    {
        ResourceEffectModelDefinition arrayPool =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectModelDefinition withoutAuthority = new(
            arrayPool.Language,
            arrayPool.Identity,
            arrayPool.ResourceKinds,
            arrayPool.Declarations,
            [
                .. arrayPool.TypedDeclarations.Where(declaration =>
                    declaration.Effect
                        is not ResourceEffect.Authority),
            ]);
        ResourceEffectAdmission admission = Admit(withoutAuthority);
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                admission,
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentAndReturnThroughHelper"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.AuthorityUnproven);
    }

    [Fact]
    public void MissingResolverDoesNotRecognizeArrayPoolByName()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            bodyScope: new HashSet<int>
            {
                MethodToken("RentAndReturnThroughHelper"),
            });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.ResolutionIncomplete);
        Assert.Empty(
            Assert.Single(index.ArrayPoolOwnership).Rents);
    }

    [Fact]
    public void UnsupportedAcquisitionCompletionRemainsVisible()
    {
        ResourceEffectModelDefinition arrayPool =
            ArrayPoolResourceEffectModel.Definition();
        ResourceEffectModelDefinition exceptionalAcquire = new(
            arrayPool.Language,
            arrayPool.Identity,
            arrayPool.ResourceKinds,
            arrayPool.Declarations,
            [
                .. arrayPool.TypedDeclarations.Select(declaration =>
                    declaration.Effect is ResourceEffect.Acquire acquire
                        ? new ResourceEffectTypedDeclaration(
                            declaration.Target,
                            new ResourceEffect.Acquire(
                                acquire.Kind,
                                acquire.Target,
                                new ResourceEffectCompletion
                                    .ExceptionalExit(),
                                acquire.Correspondence,
                                acquire.Lender),
                            declaration.Provenances)
                        : declaration),
            ]);
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(exceptionalAcquire),
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentAndReturnThroughHelper"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.UnsupportedEffect);
    }

    [Fact]
    public void DifferentAuthorityDoesNotReleaseAcquiredResource()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            Resolver(CallerPath),
            bodyScope: new HashSet<int>
            {
                MethodToken("RentAndReturnToDifferentPool"),
            });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        ResourceAcquisitionOwnership acquisition =
            Assert.Single(evidence.Acquisitions);
        Assert.False(evidence.IsComplete);
        Assert.False(acquisition.IsComplete);
        Assert.DoesNotContain(
            acquisition.Uses,
            use => use.Kind == ResourceOwnershipUseKind.Released);
    }

    [Fact]
    public void ParameterOnlyScopeDoesNotRequireAnAcquisitionRoot()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            Resolver(CallerPath),
            bodyScope: new HashSet<int>
            {
                MethodToken("ReturnRentedArray"),
            });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        ResourceOwnershipUse use =
            Assert.Single(Assert.Single(evidence.Parameters).Uses);
        Assert.Equal(ResourceOwnershipUseKind.Released, use.Kind);
    }

    [Fact]
    public void SameKindAcquisitionsRetainPhysicalIdentity()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            Resolver(CallerPath),
            bodyScope: new HashSet<int>
            {
                MethodToken("RentTwiceAndReturn"),
            });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        Assert.Equal(2, evidence.Acquisitions.Length);
        Assert.Single(
            evidence.Acquisitions.Select(
                acquisition => acquisition.ResourceKind).Distinct());
        Assert.Equal(
            2,
            evidence.Acquisitions.Select(
                acquisition => acquisition.AcquisitionOffset).Distinct()
                .Count());
        Assert.All(
            evidence.Acquisitions,
            acquisition => Assert.Equal(
                ResourceOwnershipUseKind.Released,
                Assert.Single(acquisition.Uses).Kind));
    }

    static ResourceEffectModelDefinition DeclaredResourceModel()
    {
        var assembly = new ResourceAssemblySelector(
            "ILInspector.Analysis.ResourceOwnershipApiFixtures",
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);
        ResourceTypeExpression.Named api = new(
            assembly,
            "DeclaredOwnership",
            [new ResourceTypeNameSegment("DeclaredResourceApi", 0)],
            []);
        ResourceTypeExpression.Named byteType = CoreType("Byte");
        ResourceTypeExpression array =
            new ResourceTypeExpression.SzArray(byteType);
        ResourceTypeExpression voidType = CoreType("Void");
        ResourceKindReference kind = new(DeclaredKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            ModelIdentity,
            [
                new ResourceKindDefinition(
                    DeclaredKind,
                    arity: 0,
                    [Provenance("resource", 0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    Member(
                        api,
                        "Acquire",
                        parameters: [],
                        array),
                    new ResourceEffect.Acquire(
                        kind,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        Correspondence: null,
                        Lender: null),
                    [Provenance("acquire", 1)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        api,
                        "Release",
                        parameters: [array],
                        voidType),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        kind,
                        Correspondence: null,
                        Observation: null),
                    [Provenance("release", 2)]),
            ]);
    }

    static ResourceEffectTargetSelector Member(
        ResourceTypeExpression.Named declaringType,
        string name,
        ImmutableArray<ResourceTypeExpression> parameters,
        ResourceTypeExpression returnType) =>
        new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                name,
                ResourceEffectMemberKind.Method,
                isStatic: true,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                [
                    .. parameters.Select(type =>
                        new ResourceEffectParameterSelector(
                            type,
                            ResourceEffectRefKind.Value)),
                ],
                returnType));

    static ResourceTypeExpression.Named CoreType(string name) =>
        new(
            new ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            "System",
            [new ResourceTypeNameSegment(name, 0)],
            []);

    static ResourceDeclarationProvenance Provenance(
        string source,
        int ordinal) =>
        new(
            ModelIdentity,
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(
                TextPolicy.Field,
                $"fixture.declared-resource.{source}"),
            ordinal);

    static ResourceEffectAdmission Admit(
        params ResourceEffectModelDefinition[] definitions) =>
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(definitions))
            .Admission;

    static AssemblyDependencyResolver Resolver(string path) =>
        new(new AssemblyDependencyResolutionOptions(path));

    static int MethodToken(string methodName) =>
        LibraryBodyIndex.Open(
                CallerPath,
                LibraryBodyAnalysisFeatures.MethodEvidence)
            .Methods.Single(method => method.Name == methodName)
            .MetadataToken;
}
