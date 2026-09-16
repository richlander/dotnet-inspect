using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using DotnetInspector.Fixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class ApiDeclarationCorrespondenceTests
{
    static readonly FixturePair Pair =
        FixtureCatalog.MetadataApiCorrespondencePair;

    [Fact]
    public void ApiCorrespondence_StrictDeclarationProfile()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        ResolvedAssemblyReference destination =
            Reference(Pair.NewAssemblyPath());
        Assert.Equal(source.Identity.Name, destination.Identity.Name);
        Assert.Equal(
            "ILInspector.Metadata.ApiDeclarationCorrespondence",
            source.Identity.Name);
        Assert.Equal(new Version(1, 0, 0, 0), source.Identity.Version);
        Assert.Equal(new Version(2, 0, 0, 0), destination.Identity.Version);
        MetadataTypeDefinitionName container =
            Name("MetadataCorrespondenceFixture", "Container`1");

        AssertExact(source, destination, container);
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "StableMethod");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "BodyOnly");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "Array");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "FunctionPointer");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Property,
            "StableProperty");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Property,
            "AccessorChanged");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Event,
            "StableEvent");
        AssertExact(
            source,
            destination,
            container,
            ApiDeclarationKind.Field,
            "StableField");

        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "ReturnChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "StaticMethodChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "MethodConstraintChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "ParameterFlagsChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "ArrayChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Method,
            "FunctionPointerChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Property,
            "ChangedProperty");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Property,
            "StaticPropertyChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Event,
            "ChangedEvent");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Event,
            "StaticEventChanged");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Field,
            "ChangedField");
        AssertAbsent(
            source,
            destination,
            container,
            ApiDeclarationKind.Field,
            "StaticFieldChanged");

        MetadataTypeDefinitionName nested = Name(
            "MetadataCorrespondenceFixture",
            "Outer`1",
            "Inner`1");
        AssertExact(source, destination, nested);
        AssertExact(
            source,
            destination,
            nested,
            ApiDeclarationKind.Method,
            "Nested");

        MetadataTypeDefinitionName changedConstraints = Name(
            "MetadataCorrespondenceFixture",
            "ConstraintChanged`1");
        ApiDeclarationReference changedType =
            BindType(source, changedConstraints);
        ApiDeclarationCorrespondenceResult changedTypeResult =
            ApiDeclarationCorrespondence.Match(
                source,
                changedType,
                destination,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            changedTypeResult.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
            changedTypeResult.Reason);

        MetadataTypeDefinitionName varArgContainer = Name(
            "MetadataCorrespondenceFixture",
            "VarArgContainer");
        AssertExact(
            source,
            destination,
            varArgContainer,
            ApiDeclarationKind.Method,
            "VarArg");
    }

    [Fact]
    public void ApiCorrespondence_BindsNullableProductionMethodAnchor()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        ResolvedAssemblyReference destination =
            Reference(Pair.NewAssemblyPath());
        MetadataTypeDefinitionName container =
            Name("MetadataCorrespondenceFixture", "Container`1");

        ApiDeclarationReference declaration = BindMember(
            source,
            container,
            ApiDeclarationKind.Method,
            "NullableMethod");

        Assert.Contains(
            "MetadataCorrespondenceFixture.Container<T>?",
            declaration.Member!.CanonicalSignature,
            StringComparison.Ordinal);
        ApiDeclarationCorrespondenceResult result =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                destination,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            result.Status);
    }

    [Fact]
    public void ApiCorrespondence_BindsProductionMethodKinds()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        ResolvedAssemblyReference destination =
            Reference(Pair.NewAssemblyPath());
        MetadataTypeDefinitionName typeName =
            Name("MetadataCorrespondenceFixture", "SpecialMethods");
        ApiType type;
        using (AssemblyInspectionSession session =
               AssemblyInspectionSession.Open(source))
        {
            type = session.ApiSurface(includeAll: true)
                .Types
                .Single(candidate => candidate.DefinitionName == typeName);
        }

        foreach ((string kind, string selectorPrefix) in new[]
        {
            ("operator", "operator:op_Addition~"),
            (
                "explicit-interface-implementation",
                "explicit:MetadataCorrespondenceFixture.IExplicit.M~"),
            ("method", "Normal~"),
        })
        {
            ApiMember member = type.Members.Single(
                candidate => candidate.Kind == kind);
            MemberAnchor anchor =
                ApiMemberIdentity.GetMemberAnchor(type, member);
            Assert.StartsWith(
                selectorPrefix,
                anchor.StableSelector,
                StringComparison.Ordinal);

            ApiDeclarationBindingResult binding =
                ApiDeclarationCorrespondence.BindSource(
                    source,
                    typeName,
                    new ApiDeclarationMemberSelection(
                        ApiDeclarationKind.Method,
                        anchor),
                    TestContext.Current.CancellationToken);
            Assert.True(
                binding.IsExact,
                $"{binding.Status}/{binding.Reason}/{binding.Stage}: "
                + binding.Detail);
            ApiDeclarationReference declaration =
                Assert.IsType<ApiDeclarationReference>(
                    binding.Declaration);

            ApiDeclarationCorrespondenceResult identical =
                ApiDeclarationCorrespondence.Match(
                    source,
                    declaration,
                    source,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                ApiDeclarationCorrespondenceStatus.Exact,
                identical.Status);
            Assert.Equal(
                declaration.Member,
                identical.Target?.Member);

            ApiDeclarationCorrespondenceResult paired =
                ApiDeclarationCorrespondence.Match(
                    source,
                    declaration,
                    destination,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                ApiDeclarationCorrespondenceStatus.Exact,
                paired.Status);
        }
    }

    [Fact]
    public void ApiCorrespondence_MethodAnchorProjection_IsRelevantAndCumulative()
    {
        foreach (int irrelevantNeighbors in new[] { 0, 1024 })
        {
            byte[] image = BuildMethodProjectionBudgetImage(
                irrelevantNeighbors,
                sameName: false);
            ApiDeclarationBindingResult binding =
                ApiDeclarationCorrespondence.BindSource(
                    Reference(image),
                    Name("N", "C"),
                    new ApiDeclarationMemberSelection(
                        ApiDeclarationKind.Method,
                        CreateFirstMethodAnchor(image)),
                    TestContext.Current.CancellationToken);

            Assert.True(
                binding.IsExact,
                $"{irrelevantNeighbors}: "
                + $"{binding.Status}/{binding.Reason}/{binding.Stage}");
        }

        byte[] relevantImage = BuildMethodProjectionBudgetImage(
            neighborCount: 768,
            sameName: true);
        ApiDeclarationBindingResult exhausted =
            ApiDeclarationCorrespondence.BindSource(
                Reference(relevantImage),
                Name("N", "C"),
                new ApiDeclarationMemberSelection(
                    ApiDeclarationKind.Method,
                    CreateFirstMethodAnchor(relevantImage)),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Failed,
            exhausted.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
            exhausted.Reason);
        Assert.Equal(
            ApiDeclarationCorrespondenceStage.SourceProjection,
            exhausted.Stage);
    }

    [Fact]
    public void ApiCorrespondence_ExactEndpointAssociation()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        ResolvedAssemblyReference equalImageDifferentRegistration =
            Reference(Pair.OldAssemblyPath());
        ResolvedAssemblyReference destination =
            Reference(Pair.NewAssemblyPath());
        MetadataTypeDefinitionName type =
            Name("MetadataCorrespondenceFixture", "Container`1");
        ApiDeclarationReference declaration = BindMember(
            source,
            type,
            ApiDeclarationKind.Method,
            "StableMethod");
        Assert.True(
            declaration.Endpoint.Registration.Matches(
                source.Registration));
        Assert.False(
            declaration.Endpoint.Registration.Matches(
                equalImageDifferentRegistration.Registration));

        ApiDeclarationCorrespondenceResult wrongSource =
            ApiDeclarationCorrespondence.Match(
                equalImageDifferentRegistration,
                declaration,
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Failed,
            wrongSource.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.InvalidEndpointAssociation,
            wrongSource.Reason);
        Assert.Null(wrongSource.Destination);

        ApiDeclarationCorrespondenceResult exact =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                destination,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            exact.Status);
        ApiDeclarationReference target =
            Assert.IsType<ApiDeclarationReference>(exact.Target);
        Assert.True(
            target.Endpoint.Registration.Matches(
                destination.Registration));
        Assert.Equal(
            target.Endpoint.ModuleVersionId,
            target.Location.ModuleVersionId);
        Assert.Equal(type, target.DeclaringType);
        Assert.Equal(declaration.Member, target.Member);
        Assert.NotEqual(
            declaration.Location.MetadataToken,
            target.Location.MetadataToken);
        Assert.NotNull(target.Location.MethodAddress);
    }

    [Fact]
    public void ApiCorrespondence_CompleteCandidates_BudgetCancellationAndRelationships()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        MetadataTypeDefinitionName type =
            Name("MetadataCorrespondenceFixture", "Container`1");

        ApiDeclarationBindingResult exhausted =
            ApiDeclarationCorrespondence.BindSource(
                source,
                type,
                member: null,
                limits: new ApiDeclarationCorrespondence.Limits(
                    MaxProjectionRows: 0,
                    MaxMemberRows: 1,
                    MaxCandidates: 1),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Failed,
            exhausted.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.WorkLimitExceeded,
            exhausted.Reason);

        ApiDeclarationBindingResult incomplete =
            ApiDeclarationCorrespondence.BindSource(
                Reference(BuildIncompleteRelationshipImage()),
                Name("N", "C"),
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Failed,
            incomplete.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.IncompleteCandidateSet,
            incomplete.Reason);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => ApiDeclarationCorrespondence.BindSource(
                source,
                type,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ApiCorrespondence_AddressableDeclarations_ExposeLocations()
    {
        ResolvedAssemblyReference source = Reference(Pair.OldAssemblyPath());
        MetadataTypeDefinitionName type =
            Name("MetadataCorrespondenceFixture", "Container`1");
        ApiDeclarationReference declaration = BindMember(
            source,
            type,
            ApiDeclarationKind.Property,
            "StableProperty");

        Assert.Equal(
            ApiDeclarationMetadataTable.PropertyDefinition,
            declaration.Location.Table);
        Assert.Null(declaration.Location.TypeAddress);
        Assert.Null(declaration.Location.MethodAddress);
        Assert.NotNull(declaration.Member);
        Assert.True(
            declaration.Endpoint.Registration.Matches(
                source.Registration));
    }

    [Fact]
    public void ApiCorrespondence_DetachedEndpointsDoNotRetainRegistration()
    {
        (
            ApiDeclarationRegistrationIdentity identity,
            WeakReference registration) = CreateDetachedEndpointSpecimen();

        Collect();

        Assert.False(registration.IsAlive);
        GC.KeepAlive(identity);
    }

    [Fact]
    public void ApiCorrespondence_AddressableDeclarations()
    {
        ResolvedAssemblyReference duplicateTypes =
            Reference(BuildDuplicateTypeImage());
        ApiDeclarationBindingResult duplicateBinding =
            ApiDeclarationCorrespondence.BindSource(
                duplicateTypes,
                Name("N", "C"),
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Ambiguous,
            duplicateBinding.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.DuplicateTypeDeclarations,
            duplicateBinding.Reason);
        Assert.Equal(2, duplicateBinding.Candidates.Length);

        ResolvedAssemblyReference source =
            Reference(BuildFieldImage(PrimitiveTypeCode.Int32));
        ResolvedAssemblyReference destination =
            Reference(BuildFieldImage(
                PrimitiveTypeCode.Int32,
                PrimitiveTypeCode.Int64));
        MetadataTypeDefinitionName type = Name("N", "C");
        ApiDeclarationReference field = BindMember(
            source,
            type,
            ApiDeclarationKind.Field,
            "F");
        ApiDeclarationBindingResult sourceCollision =
            ApiDeclarationCorrespondence.BindSource(
                destination,
                type,
                new ApiDeclarationMemberSelection(
                    ApiDeclarationKind.Field,
                    field.Member!),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Refused,
            sourceCollision.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.UnaddressableApiIdentity,
            sourceCollision.Reason);
        Assert.Equal(
            ApiDeclarationCorrespondenceStage.SourceAddressability,
            sourceCollision.Stage);

        ApiDeclarationCorrespondenceResult collision =
            ApiDeclarationCorrespondence.Match(
                source,
                field,
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Refused,
            collision.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.UnaddressableApiIdentity,
            collision.Reason);
        Assert.Equal(
            ApiDeclarationCorrespondenceStage.DestinationAddressability,
            collision.Stage);
        Assert.Equal(2, collision.Candidates.Length);
    }

    [Fact]
    public void ApiCorrespondence_CompleteCandidates_ReferenceScopesAndUnreadableCandidates()
    {
        ResolvedAssemblyReference versionOne =
            Reference(BuildExternalReferenceMethodImage(
                new Version(1, 0, 0, 0)));
        ResolvedAssemblyReference versionTwo =
            Reference(BuildExternalReferenceMethodImage(
                new Version(2, 0, 0, 0)));
        MetadataTypeDefinitionName type = Name("N", "C");
        ApiDeclarationReference method = BindMember(
            versionOne,
            type,
            ApiDeclarationKind.Method,
            "M");

        ApiDeclarationCorrespondenceResult scopeDifference =
            ApiDeclarationCorrespondence.Match(
                versionOne,
                method,
                versionTwo,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            scopeDifference.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
            scopeDifference.Reason);

        ResolvedAssemblyReference unreadable =
            Reference(BuildUnreadableMethodCandidateImage());
        ApiDeclarationCorrespondenceResult incomplete =
            ApiDeclarationCorrespondence.Match(
                versionOne,
                method,
                unreadable,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Failed,
            incomplete.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.MalformedMetadata,
            incomplete.Reason);
        Assert.Equal(
            ApiDeclarationCorrespondenceStage.DestinationCandidateScan,
            incomplete.Stage);
    }

    [Fact]
    public void ApiCorrespondence_RequiresExplicitForwardedOrModuleImage()
    {
        ResolvedAssemblyReference source =
            Reference(BuildFieldImage(PrimitiveTypeCode.Int32));
        MetadataTypeDefinitionName type = Name("N", "C");
        ApiDeclarationReference declaration = BindType(source, type);

        ApiDeclarationCorrespondenceResult forwarded =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                Reference(BuildExportedTypeImage(forwarded: true)),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Refused,
            forwarded.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason
                .ForwardedTypeRequiresExplicitImage,
            forwarded.Reason);
        Assert.Equal(
            ApiDeclarationCorrespondenceStage.DestinationTypeLookup,
            forwarded.Stage);
        ApiDeclarationCandidateEvidence forwardedCandidate =
            Assert.Single(forwarded.Candidates);
        Assert.Equal(
            "ForwardTarget",
            forwardedCandidate.ForwardedTarget?.Name);
        Assert.Equal(
            ApiDeclarationMetadataTable.ExportedType,
            forwardedCandidate.Location?.Table);

        ApiDeclarationCorrespondenceResult module =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                Reference(BuildExportedTypeImage(forwarded: false)),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Refused,
            module.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason
                .ModuleExportRequiresExplicitImage,
            module.Reason);
        Assert.Equal(
            "ForwardTarget.netmodule",
            Assert.Single(module.Candidates).Module?.Name);
        Assert.Equal(
            ApiDeclarationMetadataTable.ExportedType,
            Assert.Single(module.Candidates).Location?.Table);
    }

    static void AssertExact(
        ResolvedAssemblyReference source,
        ResolvedAssemblyReference destination,
        MetadataTypeDefinitionName type)
    {
        ApiDeclarationReference declaration = BindType(source, type);
        ApiDeclarationCorrespondenceResult result =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            result.Status);
        Assert.Equal(ApiDeclarationCorrespondenceReason.None, result.Reason);
        Assert.Equal(type, result.Target?.DeclaringType);
        Assert.Equal(ApiDeclarationKind.Type, result.Target?.Kind);
        Assert.NotNull(result.Target?.Location.TypeAddress);
    }

    static void AssertExact(
        ResolvedAssemblyReference source,
        ResolvedAssemblyReference destination,
        MetadataTypeDefinitionName type,
        ApiDeclarationKind kind,
        string memberName)
    {
        ApiDeclarationReference declaration =
            BindMember(source, type, kind, memberName);
        ApiDeclarationCorrespondenceResult result =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            result.Status);
        Assert.Equal(ApiDeclarationCorrespondenceReason.None, result.Reason);
        Assert.Equal(kind, result.Target?.Kind);
        Assert.Equal(declaration.Member, result.Target?.Member);
        Assert.Single(result.Candidates);
    }

    static void AssertAbsent(
        ResolvedAssemblyReference source,
        ResolvedAssemblyReference destination,
        MetadataTypeDefinitionName type,
        ApiDeclarationKind kind,
        string memberName)
    {
        ApiDeclarationReference declaration =
            BindMember(source, type, kind, memberName);
        ApiDeclarationCorrespondenceResult result =
            ApiDeclarationCorrespondence.Match(
                source,
                declaration,
                destination,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            result.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
            result.Reason);
        Assert.Null(result.Target);
        Assert.NotEmpty(result.Candidates);
    }

    static ApiDeclarationReference BindType(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName type)
    {
        ApiDeclarationBindingResult binding =
            ApiDeclarationCorrespondence.BindSource(
                source,
                type,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            binding.IsExact,
            $"{binding.Status}/{binding.Reason}/{binding.Stage}: "
            + binding.Detail);
        return Assert.IsType<ApiDeclarationReference>(
            binding.Declaration);
    }

    static ApiDeclarationReference BindMember(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName typeName,
        ApiDeclarationKind kind,
        string memberName)
    {
        MemberAnchor anchor;
        using (AssemblyInspectionSession session =
               AssemblyInspectionSession.Open(source))
        {
            ApiType type = session.ApiSurface(includeAll: true)
                .Types
                .Single(candidate => candidate.DefinitionName == typeName);
            ApiMember member = type.Members.Single(
                candidate =>
                    candidate.Name == memberName
                    && Kind(candidate) == kind);
            anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
        }

        ApiDeclarationBindingResult binding =
            ApiDeclarationCorrespondence.BindSource(
                source,
                typeName,
                new ApiDeclarationMemberSelection(kind, anchor),
                TestContext.Current.CancellationToken);
        Assert.True(
            binding.IsExact,
            $"{binding.Status}/{binding.Reason}/{binding.Stage}: "
            + $"{binding.Detail}; anchor={anchor.CanonicalSignature}");
        return Assert.IsType<ApiDeclarationReference>(
            binding.Declaration);
    }

    static ApiDeclarationKind Kind(ApiMember member)
        => member.Kind switch
        {
            "method"
                or "constructor"
                or "finalizer"
                or "operator"
                or "explicit-interface-implementation"
                or "extension-method" =>
                ApiDeclarationKind.Method,
            "property" => ApiDeclarationKind.Property,
            "event" => ApiDeclarationKind.Event,
            "field" => ApiDeclarationKind.Field,
            _ => throw new InvalidOperationException(
                $"Unsupported fixture member kind '{member.Kind}'."),
        };

    static ResolvedAssemblyReference Reference(string path)
        => ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Designated(
                "api-declaration-correspondence-test"));

    static ResolvedAssemblyReference Reference(byte[] image)
        => Assert.IsType<AssemblyDescriptorSelectionResult.Ready>(
            ResolvedAssemblyReference.SelectFromStream(
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Designated(
                    "api-declaration-correspondence-test")))
            .Reference;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (
        ApiDeclarationRegistrationIdentity Identity,
        WeakReference Registration) CreateDetachedEndpointSpecimen()
    {
        ResolvedAssemblyReference source =
            Reference(Pair.OldAssemblyPath());
        ApiDeclarationReference declaration = BindType(
            source,
            Name("MetadataCorrespondenceFixture", "Container`1"));
        return (
            declaration.Endpoint.Registration,
            new WeakReference(source.Registration));
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments]))
            .Name;

    static byte[] BuildDuplicateTypeImage()
    {
        MetadataBuilder metadata = CreateMetadata("DuplicateTypes");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildFieldImage(params PrimitiveTypeCode[] fieldTypes)
    {
        MetadataBuilder metadata = CreateMetadata("Fields");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        foreach (PrimitiveTypeCode type in fieldTypes)
        {
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("F"),
                metadata.GetOrAddBlob(
                    new byte[]
                    {
                        (byte)SignatureKind.Field,
                        (byte)type,
                    }));
        }
        return Serialize(metadata);
    }

    static byte[] BuildExternalReferenceMethodImage(Version version)
    {
        MetadataBuilder metadata = CreateMetadata("ReferenceScope");
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                version,
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            dependency,
            metadata.GetOrAddString("Dependency"),
            metadata.GetOrAddString("Value"));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[]
                {
                    0x00,
                    0x01,
                    0x01,
                    0x12,
                    (1 << 2) | 1,
                }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildMethodProjectionBudgetImage(
        int neighborCount,
        bool sameName)
    {
        MetadataBuilder metadata =
            CreateMetadata("MethodProjectionBudget");
        AssemblyReferenceHandle dependency =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            dependency,
            default,
            metadata.GetOrAddString(new string('A', 3000)));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Normal"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        BlobHandle neighborSignature = metadata.GetOrAddBlob(
            new byte[] { 0x00, 0x01, 0x01, 0x12, 0x05 });
        for (int index = 0; index < neighborCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(
                    sameName ? "Normal" : $"M{index}"),
                neighborSignature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static MemberAnchor CreateFirstMethodAnchor(byte[] image)
    {
        using var peReader =
            new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Single(
                handle => reader.StringComparer.Equals(
                    reader.GetTypeDefinition(handle).Name,
                    "C"));
        MethodDefinitionHandle methodHandle =
            reader.GetTypeDefinition(typeHandle).GetMethods().First();
        return ApiMemberIdentity.CreateMethodAnchor(
            reader,
            typeHandle,
            reader.GetMethodDefinition(methodHandle));
    }

    static byte[] BuildUnreadableMethodCandidateImage()
    {
        MetadataBuilder metadata = CreateMetadata("UnreadableCandidate");
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00, 0x08 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(
                new byte[] { 0x00, 0x00 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildIncompleteRelationshipImage()
    {
        MetadataBuilder metadata = CreateMetadata("IncompleteRelationships");
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_P"),
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }),
            bodyOffset: 0,
            MetadataTokens.ParameterHandle(1));
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("P"),
            metadata.GetOrAddBlob(new byte[] { 0x28, 0x00, 0x08 }));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        return Serialize(metadata);
    }

    static byte[] BuildExportedTypeImage(bool forwarded)
    {
        MetadataBuilder metadata = CreateMetadata("ExportedType");
        EntityHandle implementation;
        TypeAttributes attributes = TypeAttributes.Public;
        if (forwarded)
        {
            implementation = metadata.AddAssemblyReference(
                metadata.GetOrAddString("ForwardTarget"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
            attributes |= (TypeAttributes)0x00200000;
        }
        else
        {
            implementation = metadata.AddAssemblyFile(
                metadata.GetOrAddString("ForwardTarget.netmodule"),
                metadata.GetOrAddBlob(new byte[] { 1, 2, 3 }),
                containsMetadata: true);
        }

        metadata.AddExportedType(
            attributes,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            implementation,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll
                    | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
