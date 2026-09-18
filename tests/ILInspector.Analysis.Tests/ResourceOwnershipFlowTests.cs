using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace ILInspector.Analysis.Tests;

public sealed class ResourceOwnershipFlowTests
{
    static readonly ResourceEffectModelIdentity ModelIdentity =
        new("fixture.declared-resource");
    static readonly ResourceKindIdentity DeclaredKind =
        new("fixture.declared-resource.buffer");
    static readonly ResourceKindIdentity SecondaryKind =
        new("fixture.secondary-resource.buffer");
    static readonly ResourceEffectModelIdentity ValueModelIdentity =
        new("fixture.value-resource");
    static readonly ResourceKindIdentity ValueKind =
        new("fixture.value-resource.buffer");
    static readonly ResourceEffectModelIdentity TokenModelIdentity =
        new("fixture.token-resource");
    static readonly ResourceKindIdentity TokenKind =
        new("fixture.token-resource.value");

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
                candidate.Method.Name == "Release"
                && candidate.Method.DeclaringType.Name
                    == "DeclaredResourceApi");
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

    [Theory]
    [InlineData(
        "ForwardRentedArray",
        ResourceOwnershipUseKind.Forwarded)]
    [InlineData(
        "StoreRentedArray",
        ResourceOwnershipUseKind.Stored)]
    [InlineData(
        "ReturnRentedArrayToCaller",
        ResourceOwnershipUseKind.ReturnedToCaller)]
    public void ScopedArrayParameterFlowRetainsCompatibilityEvidence(
        string methodName,
        ResourceOwnershipUseKind expected)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            CallerPath,
            LibraryBodyAnalysisFeatures.OwnershipFlow,
            Resolver(CallerPath),
            bodyScope: new HashSet<int>
            {
                MethodToken(methodName),
            });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        Assert.Equal(
            expected,
            Assert.Single(
                Assert.Single(evidence.Parameters).Uses).Kind);
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

    [Fact]
    public void UnconstrainedReleaseDoesNotRequireIssuerAuthority()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(ArrayPoolCorrespondenceModel(
                    retainAcquireCorrespondence: true,
                    retainReleaseCorrespondence: false)),
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentTwiceAndReturn"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        Assert.All(
            evidence.Acquisitions,
            acquisition => Assert.Equal(
                ResourceOwnershipUseKind.Released,
                Assert.Single(acquisition.Uses).Kind));
    }

    [Fact]
    public void CorrespondingReleaseRequiresKnownAcquisitionAuthority()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(ArrayPoolCorrespondenceModel(
                    retainAcquireCorrespondence: false,
                    retainReleaseCorrespondence: true)),
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentTwiceAndReturn"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        Assert.All(
            evidence.Acquisitions,
            acquisition =>
            {
                Assert.Null(acquisition.Authority);
                Assert.Equal(
                    ResourceOwnershipUseKind.Forwarded,
                    Assert.Single(acquisition.Uses).Kind);
            });
    }

    [Fact]
    public void ValueAuthorityRequiresTheSameProducingOccurrence()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(ValueResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ReleaseAcrossValueAuthorities"),
                    MethodToken("ReleaseThroughSameValueAuthority"),
                });

        ResourceOwnershipMethodEvidence wrong =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name
                    == "ReleaseAcrossValueAuthorities");
        ResourceAcquisitionOwnership wrongAcquisition =
            Assert.Single(wrong.Acquisitions);
        Assert.DoesNotContain(
            wrongAcquisition.Uses,
            use => use.Kind == ResourceOwnershipUseKind.Released);

        ResourceOwnershipMethodEvidence same =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name
                    == "ReleaseThroughSameValueAuthority");
        ResourceAcquisitionOwnership sameAcquisition =
            Assert.Single(same.Acquisitions);
        Assert.Equal(
            ResourceOwnershipUseKind.Released,
            Assert.Single(sameAcquisition.Uses).Kind);
        Assert.Equal(
            sameAcquisition.Authority!.PhysicalInvocation,
            Assert.Single(sameAcquisition.Uses)
                .Authority!.PhysicalInvocation);
    }

    [Fact]
    public void AtomicConflictKeepsWithheldRootsIncomplete()
    {
        ResourceEffectModelDefinition normal =
            DeclaredReleaseModel(
                new("fixture.conflict.normal"),
                DeclaredKind,
                new ResourceEffectCompletion.NormalReturn());
        ResourceEffectModelDefinition entry =
            DeclaredReleaseModel(
                new("fixture.conflict.entry"),
                DeclaredKind,
                new ResourceEffectCompletion.Entry());
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(
                    ArrayPoolResourceEffectModel.Definition(),
                    normal,
                    entry),
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentAndReturnThroughHelper"),
                    MethodToken("ReleaseDeclaredParameter"),
                });

        ResourceOwnershipMethodEvidence pooled =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name
                    == "RentAndReturnThroughHelper");
        Assert.False(pooled.IsComplete);
        Assert.Empty(pooled.Acquisitions);
        Assert.Contains(
            pooled.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.ResolutionConflict
                && limit.ILOffset is null);

        ResourceOwnershipMethodEvidence conflicting =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "ReleaseDeclaredParameter");
        Assert.False(conflicting.IsComplete);
        Assert.Contains(
            conflicting.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.ResolutionConflict
                && limit.ILOffset is not null);
    }

    [Fact]
    public void ParameterRetainsEveryCompatibleKindSpecificRelease()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(
                    DeclaredResourceModel(),
                    DeclaredReleaseModel(
                        new("fixture.secondary-resource"),
                        SecondaryKind,
                        new ResourceEffectCompletion.NormalReturn())),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ReleaseDeclaredParameter"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.True(evidence.IsComplete);
        ResourceOwnershipUse[] releases =
        [
            .. Assert.Single(evidence.Parameters).Uses
                .Where(use =>
                    use.Kind == ResourceOwnershipUseKind.Released),
        ];
        Assert.Equal(2, releases.Length);
        Assert.Equal(
            new HashSet<ResourceKindIdentity>
            {
                DeclaredKind,
                SecondaryKind,
            },
            releases.Select(use =>
                Assert.Single(use.Effect!.ResourceKinds).Identity)
                .ToHashSet());
    }

    [Fact]
    public void UnsupportedReleaseOnlyLimitsApplicableResourceKinds()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(
                    DeclaredResourceModel(),
                    DeclaredReleaseModel(
                        new("fixture.secondary-resource"),
                        SecondaryKind,
                        new ResourceEffectCompletion.Entry())),
                bodyScope: new HashSet<int>
                {
                    MethodToken("HoldDistinctResources"),
                    MethodToken("ReleaseDeclaredParameter"),
                });

        ResourceOwnershipMethodEvidence acquisition =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "HoldDistinctResources");
        Assert.True(acquisition.IsComplete);
        Assert.Equal(
            ResourceOwnershipUseKind.Released,
            Assert.Single(
                acquisition.Acquisitions.Single(candidate =>
                    candidate.ResourceKind.Identity
                        == DeclaredKind).Uses).Kind);

        ResourceOwnershipMethodEvidence parameter =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "ReleaseDeclaredParameter");
        Assert.False(parameter.IsComplete);
        ResourceOwnershipUse released = Assert.Single(
            Assert.Single(parameter.Parameters).Uses,
            use => use.Kind == ResourceOwnershipUseKind.Released);
        Assert.Equal(
            DeclaredKind,
            Assert.Single(released.Effect!.ResourceKinds).Identity);
        Assert.Contains(
            parameter.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.UnsupportedEffect);
    }

    [Fact]
    public void NonArrayResourceParameterRetainsReleaseEvidence()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(TokenResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ReleaseTokenParameter"),
                });

        ResourceOwnershipMethodEvidence helper =
            index.ResourceOwnership.Single(candidate =>
                candidate.Method.Name == "ReleaseTokenParameter");
        Assert.True(
            helper.IsComplete,
            string.Join(", ", helper.Limits));
        ResourceParameterOwnership parameter =
            Assert.Single(helper.Parameters);
        Assert.Equal(TypeRefKind.Definition, parameter.ValueType.Kind);
        Assert.Equal(
            ResourceOwnershipUseKind.Released,
            Assert.Single(parameter.Uses).Kind);
    }

    [Fact]
    public void ReceiverReleaseRemainsVisibleAsUnsupported()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(TokenResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ReleaseReceiverParameter"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(Assert.Single(evidence.Parameters).Uses);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.UnsupportedEffect);
    }

    [Fact]
    public void ReceiverReleasesRetainDistinctPhysicalOffsets()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(TokenResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ReleaseTwoReceiverParameters"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Equal(2, evidence.Parameters.Length);
        Assert.All(evidence.Parameters, parameter =>
            Assert.Empty(parameter.Uses));
        ResourceOwnershipFlowLimit[] limits =
        [
            .. evidence.Limits.Where(limit =>
                limit.Kind
                    == ResourceOwnershipFlowLimitKind.UnsupportedEffect),
        ];
        Assert.Equal(2, limits.Length);
        Assert.All(limits, limit => Assert.NotNull(limit.ILOffset));
        Assert.Equal(
            2,
            limits.Select(limit => limit.ILOffset).Distinct().Count());
    }

    [Fact]
    public void ImplicitReceiverReleaseRemainsVisibleAsUnsupported()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                ApiPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(ApiPath),
                Admit(TokenResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken(ApiPath, "CloseThis"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        Assert.Empty(evidence.Parameters);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.UnsupportedEffect);
    }

    [Fact]
    public void UnsupportedMoveRemainsVisible()
    {
        LibraryBodyIndex index =
            LibraryBodyIndex.OpenWithResourceEffects(
                CallerPath,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                Resolver(CallerPath),
                Admit(TokenResourceModel()),
                bodyScope: new HashSet<int>
                {
                    MethodToken("ForwardMovedToken"),
                });

        ResourceOwnershipMethodEvidence evidence =
            Assert.Single(index.ResourceOwnership);
        Assert.False(evidence.IsComplete);
        Assert.Empty(evidence.Acquisitions);
        Assert.Empty(evidence.Parameters);
        Assert.Contains(
            evidence.Limits,
            limit => limit.Kind
                == ResourceOwnershipFlowLimitKind.UnsupportedEffect);
    }

    [Fact]
    public void OwnershipResolutionUsesTheAcquiredRootSnapshot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"OwnershipRoot-{Guid.NewGuid():N}.dll");
        File.Copy(CallerPath, path);
        var resolver = new DeleteRootOnResolveResolver(
            Resolver(CallerPath),
            path);

        try
        {
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.OwnershipFlow,
                resolver,
                bodyScope: new HashSet<int>
                {
                    MethodToken("RentAndReturnThroughHelper"),
                });

            Assert.True(resolver.DeletedRoot);
            Assert.Contains(
                index.ResourceOwnership,
                evidence => evidence.Method.Name
                    == "RentAndReturnThroughHelper");
        }
        finally
        {
            File.Delete(path);
        }
    }

    static ResourceEffectModelDefinition DeclaredResourceModel()
    {
        ResourceAssemblySelector assembly = FixtureAssembly();
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

    static ResourceEffectModelDefinition ValueResourceModel()
    {
        ResourceTypeExpression.Named pool = new(
            FixtureAssembly(),
            "DeclaredOwnership",
            [new ResourceTypeNameSegment("ValueResourcePool", 0)],
            []);
        ResourceTypeExpression array =
            new ResourceTypeExpression.SzArray(CoreType("Byte"));
        ResourceKindReference kind = new(ValueKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            ValueModelIdentity,
            [
                new ResourceKindDefinition(
                    ValueKind,
                    arity: 0,
                    [Provenance(
                        ValueModelIdentity,
                        "resource",
                        0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    Member(
                        pool,
                        "Create",
                        parameters: [],
                        pool),
                    new ResourceEffect.Authority(
                        kind,
                        new ResourceEffectLocation.Return(),
                        new ResourceAuthorityKey.Value()),
                    [Provenance(
                        ValueModelIdentity,
                        "authority",
                        1)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        pool,
                        "Acquire",
                        parameters: [CoreType("Int32")],
                        array,
                        isStatic: false),
                    new ResourceEffect.Acquire(
                        kind,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        new ResourceEffectLocation.Receiver(),
                        Lender: null),
                    [Provenance(
                        ValueModelIdentity,
                        "acquire",
                        2)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        pool,
                        "Release",
                        parameters: [array],
                        CoreType("Void"),
                        isStatic: false),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        kind,
                        new ResourceEffectLocation.Receiver(),
                        Observation: null),
                    [Provenance(
                        ValueModelIdentity,
                        "release",
                        3)]),
            ]);
    }

    static ResourceEffectModelDefinition ArrayPoolCorrespondenceModel(
        bool retainAcquireCorrespondence,
        bool retainReleaseCorrespondence)
    {
        ResourceEffectModelDefinition model =
            ArrayPoolResourceEffectModel.Definition();
        return new(
            model.Language,
            model.Identity,
            model.ResourceKinds,
            model.Declarations,
            [
                .. model.TypedDeclarations.Select(declaration =>
                    declaration.Effect switch
                    {
                        ResourceEffect.Acquire acquire =>
                            new ResourceEffectTypedDeclaration(
                                declaration.Target,
                                new ResourceEffect.Acquire(
                                    acquire.Kind,
                                    acquire.Target,
                                    acquire.When,
                                    retainAcquireCorrespondence
                                        ? acquire.Correspondence
                                        : null,
                                    acquire.Lender),
                                declaration.Provenances),
                        ResourceEffect.Release release =>
                            new ResourceEffectTypedDeclaration(
                                declaration.Target,
                                new ResourceEffect.Release(
                                    release.Source,
                                    release.When,
                                    release.Kind,
                                    retainReleaseCorrespondence
                                        ? release.Correspondence
                                        : null,
                                    release.Observation),
                                declaration.Provenances),
                        _ => declaration,
                    }),
            ]);
    }

    static ResourceEffectModelDefinition DeclaredReleaseModel(
        ResourceEffectModelIdentity modelIdentity,
        ResourceKindIdentity kindIdentity,
        ResourceEffectCompletion completion)
    {
        ResourceTypeExpression.Named api = new(
            FixtureAssembly(),
            "DeclaredOwnership",
            [new ResourceTypeNameSegment("DeclaredResourceApi", 0)],
            []);
        ResourceTypeExpression array =
            new ResourceTypeExpression.SzArray(CoreType("Byte"));
        ResourceKindReference kind = new(kindIdentity, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            modelIdentity,
            [
                new ResourceKindDefinition(
                    kindIdentity,
                    arity: 0,
                    [Provenance(
                        modelIdentity,
                        "resource",
                        0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    Member(
                        api,
                        "Release",
                        parameters: [array],
                        CoreType("Void")),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        completion,
                        kind,
                        Correspondence: null,
                        Observation: null),
                    [Provenance(
                        modelIdentity,
                        "release",
                        1)]),
            ]);
    }

    static ResourceEffectModelDefinition TokenResourceModel()
    {
        ResourceAssemblySelector assembly = FixtureAssembly();
        ResourceTypeExpression.Named api = new(
            assembly,
            "DeclaredOwnership",
            [new ResourceTypeNameSegment("TokenResourceApi", 0)],
            []);
        ResourceTypeExpression.Named token = new(
            assembly,
            "DeclaredOwnership",
            [new ResourceTypeNameSegment("DeclaredToken", 0)],
            []);
        ResourceKindReference kind = new(TokenKind, []);
        return new(
            ResourceEffectLanguageIdentity.Version1,
            TokenModelIdentity,
            [
                new ResourceKindDefinition(
                    TokenKind,
                    arity: 0,
                    [Provenance(
                        TokenModelIdentity,
                        "resource",
                        0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    Member(
                        api,
                        "Release",
                        parameters: [CoreType("Object")],
                        CoreType("Void")),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        kind,
                        Correspondence: null,
                        Observation: null),
                    [Provenance(
                        TokenModelIdentity,
                        "release",
                        1)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        token,
                        "Close",
                        parameters: [],
                        CoreType("Void"),
                        isStatic: false),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Receiver(),
                        new ResourceEffectCompletion.NormalReturn(),
                        kind,
                        Correspondence: null,
                        Observation: null),
                    [Provenance(
                        TokenModelIdentity,
                        "receiver-release",
                        2)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        api,
                        "Move",
                        parameters: [token],
                        token),
                    new ResourceEffect.Move(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        kind),
                    [Provenance(
                        TokenModelIdentity,
                        "move",
                        3)]),
            ]);
    }

    static ResourceEffectTargetSelector Member(
        ResourceTypeExpression.Named declaringType,
        string name,
        ImmutableArray<ResourceTypeExpression> parameters,
        ResourceTypeExpression returnType,
        bool isStatic = true) =>
        new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                name,
                ResourceEffectMemberKind.Method,
                isStatic,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: !isStatic,
                explicitThis: false,
                [
                    .. parameters.Select(type =>
                        new ResourceEffectParameterSelector(
                            type,
                            ResourceEffectRefKind.Value)),
                ],
                returnType));

    static ResourceAssemblySelector FixtureAssembly() =>
        new(
            "ILInspector.Analysis.ResourceOwnershipApiFixtures",
            publicKeyToken: null,
            ResourceAssemblyVersionPolicy.Any);

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
        Provenance(ModelIdentity, source, ordinal);

    static ResourceDeclarationProvenance Provenance(
        ResourceEffectModelIdentity modelIdentity,
        string source,
        int ordinal) =>
        new(
            modelIdentity,
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(
                TextPolicy.Field,
                $"{modelIdentity.Value}.{source}"),
            ordinal);

    static ResourceEffectAdmission Admit(
        params ResourceEffectModelDefinition[] definitions) =>
        Assert.IsType<ResourceEffectAdmissionOutcome.Admitted>(
            ResourceEffectAdmissionBuilder.Admit(definitions))
            .Admission;

    static AssemblyDependencyResolver Resolver(string path) =>
        new(new AssemblyDependencyResolutionOptions(path));

    static int MethodToken(string methodName) =>
        MethodToken(CallerPath, methodName);

    static int MethodToken(string path, string methodName) =>
        LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.MethodEvidence)
            .Methods.Single(method => method.Name == methodName)
            .MetadataToken;

    sealed class DeleteRootOnResolveResolver(
        IAssemblyReferenceResolver inner,
        string rootPath)
        : IAssemblyReferenceResolver
    {
        int _deletedRoot;

        internal bool DeletedRoot =>
            Volatile.Read(ref _deletedRoot) != 0;

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ResolvedAssemblyReference? resolved =
                inner.Resolve(identity, scope);
            if (Interlocked.Exchange(ref _deletedRoot, 1) == 0)
                File.Delete(rootPath);
            return resolved;
        }
    }
}
