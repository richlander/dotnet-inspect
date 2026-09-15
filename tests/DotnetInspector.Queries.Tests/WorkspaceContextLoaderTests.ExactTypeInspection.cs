using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    static ApiSurfaceProjectionLimits ExactTypeLimits { get; } =
        new(1, 10_000, 100_000, 10_000, 10_000, 1_000_000, 10_000_000);

    [Fact]
    public async Task ExactTypeInspection_ReturnsDetachedIdentityMembersAndShare()
    {
        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, LibraryPackage()),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "Target.Api");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal(
            LocatorName("Target", "Api"),
            available.Candidate.Definition);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(TargetPath),
            available.Candidate.SupplierAssembly.Name);
        Assert.Empty(available.Candidate.ForwardingHops);
        Assert.Equal("Target.Api", available.Type.FullName);
        Assert.Equal(
            ["Ping", "Ping", "Ping", "Forward", "Leaf"],
            available.Members.Members.Select(member => member.Name));
        Assert.Equal(available.Type.Members.Count, available.Members.Members.Count);
        Assert.Null(available.Type.SourceAssemblyPath);
        Assert.Null(available.Type.SourceFilePath);
        Assert.All(available.Type.Members, member => Assert.Null(member.SourceFilePath));
        Assert.All(available.InspectionFailures, failure => Assert.Null(failure.SourceAssemblyPath));

        var share =
            Assert.IsType<InspectionShare.Available>(envelope.Share);
        WorkspaceSharePacket packet =
            WorkspaceSharePacketCodec.Decode(
                share.Packet,
                TestContext.Current.CancellationToken);
        Assert.Equal("Target.Api", packet.Type);
        Assert.Equal(Version, packet.Tabs[0].Version);

        // The helper closes the realization before returning. These facts must
        // remain usable without a lease, Workspace, reader, or acquired payload.
        Assert.Equal("Target.Api", available.Type.FullName);
        Assert.Equal(5, available.Type.Members.Count);
        AssertDetached(envelope);
    }

    [Fact]
    public async Task ExactTypeInspection_PrefersExactShortNameOverGenericFallback()
    {
        byte[] image = ApiAssembly(
            "Exact.Name",
            ("N.Widget", []),
            ("N.Widget`1", []));

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"ref/{Framework}/Exact.Name.dll", image))),
                PackageContext(Version),
                "Widget");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal("N.Widget", available.Type.FullName);
        Assert.Equal(
            "N.Widget",
            available.Candidate.Definition.ToMetadataFullName());
    }

    [Fact]
    public async Task ExactTypeInspection_PrefersFullIdentityOverNamespaceSuffix()
    {
        byte[] image = ApiAssembly(
            "Full.Identity",
            ("N.Widget", []),
            ("Other.N.Widget", []));

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"ref/{Framework}/Full.Identity.dll", image))),
                PackageContext(Version),
                "N.Widget");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal("N.Widget", available.Type.FullName);
        Assert.Equal(
            "N.Widget",
            available.Candidate.Definition.ToMetadataFullName());
    }

    [Fact]
    public async Task ExactTypeInspection_ShareUsesEscapedNestedDefinitionIdentity()
    {
        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"ref/{Framework}/Nested.dll",
                            NestedApiAssembly()))),
                PackageContext(Version),
                "N.Outer.Inner");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal(
            "N.Outer+Inner",
            available.Candidate.Definition.ToEscapedFullName());
        WorkspaceSharePacket packet =
            WorkspaceSharePacketCodec.Decode(
                Assert.IsType<InspectionShare.Available>(
                    envelope.Share).Packet,
                TestContext.Current.CancellationToken);
        Assert.Equal("N.Outer+Inner", packet.Type);
    }

    [Fact]
    public async Task ExactTypeInspection_PublicShareRequiresAllVisibilityUniqueness()
    {
        byte[] publicImage = ApiAssembly(
            "Public.Collision",
            TypeAttributes.Public,
            ("N.Collision", []));
        byte[] internalImage = ApiAssembly(
            "Internal.Collision",
            TypeAttributes.NotPublic,
            ("N.Collision", []));
        IPackageStore store =
            await CachedStoreAsync(
                Version,
                Archive(
                    ($"ref/{Framework}/Public.Collision.dll", publicImage),
                    ($"ref/{Framework}/Internal.Collision.dll",
                        internalImage)));

        InspectionEnvelope<ExactTypeInspectionResult> publicEnvelope =
            await ExecuteExactAsync(
                store,
                PackageContext(Version),
                "N.Collision");
        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                publicEnvelope.Content);
        Assert.False(available.IsContextUnique);
        Assert.IsType<InspectionShare.NonProjectable>(
            publicEnvelope.Share);

        InspectionEnvelope<ExactTypeInspectionResult> allEnvelope =
            await ExecuteExactAsync(
                store,
                PackageContext(Version),
                "N.Collision",
                scope: ApiSurfaceScope.IncludeAll);
        Assert.IsType<ExactTypeInspectionResult.Ambiguous>(
            allEnvelope.Content);
    }

    [Fact]
    public async Task ExactTypeInspection_NormalizesFrameworkAssociation()
    {
        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, LibraryPackage()),
                new WorkspaceContextInput
                {
                    Framework = Framework.ToUpperInvariant(),
                    Members = [PackageMember(Version)],
                },
                "Target.Api");

        Assert.IsType<ExactTypeInspectionResult.Available>(
            envelope.Content);
    }

    [Fact]
    public async Task ExactTypeInspection_ResolvesForwarderToSupplierIdentity()
    {
        AssemblyName targetName = AssemblyName.GetAssemblyName(TargetPath);
        byte[] facade = LocatorImage(
            "A.Facade",
            metadata =>
            {
                AssemblyReferenceHandle target =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString(targetName.Name!),
                        targetName.Version!,
                        default,
                        default,
                        0,
                        default);
                metadata.AddExportedType(
                    (TypeAttributes)0x00200000,
                    metadata.GetOrAddString("Target"),
                    metadata.GetOrAddString("Api"),
                    target,
                    0);
            });
        byte[] archive = Archive(
            ($"ref/{Framework}/A.Facade.dll", facade),
            ($"ref/{Framework}/{Path.GetFileName(TargetPath)}",
                File.ReadAllBytes(TargetPath)));

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, archive),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "Target.Api");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal("A.Facade", available.Candidate.Declaration.LibraryIdentity.Identity.Name);
        Assert.Equal(
            targetName.Name,
            available.Candidate.SupplierAssembly.Name);
        Assert.Single(available.Candidate.ForwardingHops);
        Assert.Equal("Target.Api", available.Type.FullName);
        Assert.True(available.Type.IsForwarded);
        Assert.Equal(targetName.Name, available.Candidate.Supplier.LibraryIdentity.Identity.Name);
        AssertDetached(envelope);
    }

    [Fact]
    public async Task ExactTypeInspection_ReturnsTypedMissAmbiguityIncompleteAndFailure()
    {
        InspectionEnvelope<ExactTypeInspectionResult> miss =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, LibraryPackage()),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "Target.Missing");
        Assert.IsType<ExactTypeInspectionResult.NotFound>(miss.Content);
        Assert.IsType<InspectionShare.NonProjectable>(miss.Share);

        byte[] first = LocatorImage(
            "First",
            metadata => LocatorDefinition(metadata, "N", "Widget"));
        byte[] second = LocatorImage(
            "Second",
            metadata => LocatorDefinition(metadata, "N", "Widget"));
        InspectionEnvelope<ExactTypeInspectionResult> ambiguity =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"lib/{Framework}/First.dll", first),
                        ($"lib/{Framework}/Second.dll", second))),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "N.Widget");
        Assert.Equal(
            2,
            Assert.IsType<ExactTypeInspectionResult.Ambiguous>(
                ambiguity.Content).Candidates.Length);
        Assert.IsType<InspectionShare.NonProjectable>(ambiguity.Share);

        InspectionEnvelope<ExactTypeInspectionResult> selected =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"lib/{Framework}/First.dll", first),
                        ($"lib/{Framework}/Second.dll", second))),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "N.Widget",
                assemblyName: "Second");
        Assert.Equal(
            "Second",
            Assert.IsType<ExactTypeInspectionResult.Available>(
                selected.Content).Candidate.SupplierAssembly.Name);
        Assert.IsType<InspectionShare.NonProjectable>(selected.Share);

        byte[] broken = LocatorImage(
            "Broken",
            metadata =>
            {
                LocatorDefinition(metadata, "N", "Duplicate");
                LocatorDefinition(metadata, "N", "Duplicate");
            });
        InspectionEnvelope<ExactTypeInspectionResult> incomplete =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"lib/{Framework}/{Path.GetFileName(TargetPath)}",
                            File.ReadAllBytes(TargetPath)),
                        ($"lib/{Framework}/Broken.dll", broken))),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "Target.Api");
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Incomplete>(
                incomplete.Content).Failures,
            failure =>
                failure.Kind
                    == ExactTypeInspectionFailureKind
                        .DeclarationInventoryIncomplete);
        Assert.IsType<InspectionShare.NonProjectable>(incomplete.Share);
        Assert.NotEmpty(incomplete.Diagnostics);

        InspectionEnvelope<ExactTypeInspectionResult> failed =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"lib/{Framework}/Broken.dll",
                            CreateMalformedMetadataRootImage()))),
                new WorkspaceContextInput
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                "Target.Api");
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Incomplete>(
                failed.Content).Failures,
            failure =>
                failure.Kind
                    == ExactTypeInspectionFailureKind
                        .DeclarationInventoryIncomplete);
        Assert.IsType<InspectionShare.NonProjectable>(failed.Share);
        Assert.NotEmpty(failed.Diagnostics);
        foreach (var envelope in new[] { miss, ambiguity, incomplete, failed })
            AssertDetached(envelope);
    }

    [Fact]
    public async Task ExactTypeInspection_SelectsReferenceSurfaceOverMatchingLibraryIdentity()
    {
        const string assemblyName = "Role.Split";
        byte[] reference = ApiAssembly(
            assemblyName,
            ("Role.Subject", ["ReferenceOnly"]),
            ("Role.ReferenceDeclaration", []));
        byte[] library = ApiAssembly(
            assemblyName,
            ("Role.Subject", ["LibraryOnly"]),
            ("Role.LibraryDeclaration", []));
        IPackageStore store =
            await CachedStoreAsync(
                Version,
                Archive(
                    ($"ref/{Framework}/{assemblyName}.dll", reference),
                    ($"lib/{Framework}/{assemblyName}.dll", library)));

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                store,
                PackageContext(Version),
                "Role.Subject");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal(assemblyName, available.Candidate.SupplierAssembly.Name);
        Assert.Contains(
            available.Type.Members,
            member => member.Name == "ReferenceOnly");
        Assert.DoesNotContain(
            available.Type.Members,
            member => member.Name == "LibraryOnly");
        InspectionEnvelope<ExactTypeInspectionResult> libraryDeclaration =
            await ExecuteExactAsync(
                store,
                PackageContext(Version),
                "Role.LibraryDeclaration");
        Assert.IsType<ExactTypeInspectionResult.NotFound>(
            libraryDeclaration.Content);
        AssertDetached(envelope);
        AssertDetached(libraryDeclaration);
    }

    [Fact]
    public async Task ExactTypeInspection_ReferenceOnlyPackageIsInspectable()
    {
        const string assemblyName = "Reference.Only";
        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"ref/{Framework}/{assemblyName}.dll",
                            ApiAssembly(
                                assemblyName,
                                ("Reference.Subject", ["Visible"]))))),
                PackageContext(Version),
                "Reference.Subject");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        Assert.Equal(assemblyName, available.Candidate.SupplierAssembly.Name);
        Assert.Contains(
            available.Type.Members,
            member => member.Name == "Visible");
        AssertDetached(envelope);
    }

    [Fact]
    public async Task ExactTypeInspection_CompileAssetIdNarrowsOnlySurfaceParticipants()
    {
        const string firstAssembly = "Asset.First";
        const string secondAssembly = "Asset.Second";
        byte[] archive = Archive(
            ($"ref/{Framework}/{firstAssembly}.dll",
                ApiAssembly(
                    firstAssembly,
                    ("Asset.Subject", ["FirstReference"]))),
            ($"ref/{Framework}/{secondAssembly}.dll",
                ApiAssembly(
                    secondAssembly,
                    ("Asset.Subject", ["SecondReference"]))),
            ($"lib/{Framework}/{firstAssembly}.dll",
                ApiAssembly(
                    firstAssembly,
                    ("Asset.Subject", ["FirstLibrary"]))),
            ($"lib/{Framework}/{secondAssembly}.dll",
                ApiAssembly(
                    secondAssembly,
                    ("Asset.Subject", ["SecondLibrary"]))));
        IPackageStore store = await CachedStoreAsync(Version, archive);
        WorkspaceContextInput context = PackageContext(Version);

        InspectionEnvelope<ExactTypeInspectionResult> ambiguous =
            await ExecuteExactAsync(store, context, "Asset.Subject");
        Assert.Equal(
            2,
            Assert.IsType<ExactTypeInspectionResult.Ambiguous>(
                ambiguous.Content).Candidates.Length);

        InspectionEnvelope<ExactTypeInspectionResult> selected =
            await ExecuteExactAsync(
                store,
                context,
                "Asset.Subject",
                compileAssetId:
                    $"compile:ref/{Framework}/{secondAssembly}.dll");
        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                selected.Content);
        Assert.Equal(
            secondAssembly,
            available.Candidate.SupplierAssembly.Name);
        Assert.Equal(
            $"compile:ref/{Framework}/{secondAssembly}.dll",
            available.Request.CompileAssetId);
        Assert.Contains(
            available.Type.Members,
            member => member.Name == "SecondReference");
        Assert.DoesNotContain(
            available.Type.Members,
            member => member.Name == "SecondLibrary");

        InspectionEnvelope<ExactTypeInspectionResult> unselectedLibrary =
            await ExecuteExactAsync(
                store,
                context,
                "Asset.Subject",
                compileAssetId:
                    $"compile:lib/{Framework}/{firstAssembly}.dll");
        Assert.IsType<ExactTypeInspectionResult.NotFound>(
            unselectedLibrary.Content);
        AssertDetached(ambiguous);
        AssertDetached(selected);
        AssertDetached(unselectedLibrary);
    }

    [Fact]
    public async Task ExactTypeInspection_CompileAssetSelectionCannotHideUnavailableBindingParticipant()
    {
        const string assemblyName = "Selected.Healthy";
        IPackageStore store =
            await CachedStoreAsync(
                Version,
                Archive(
                    ($"ref/{Framework}/{assemblyName}.dll",
                        ApiAssembly(
                            assemblyName,
                            ("Selected.Subject", ["Healthy"]))),
                    ($"ref/{Framework}/Unselected.Broken.dll",
                        CreateMalformedMetadataRootImage())));
        WorkspaceContextInput context = PackageContext(Version);

        InspectionEnvelope<ExactTypeInspectionResult> wholeContext =
            await ExecuteExactAsync(
                store,
                context,
                "Selected.Subject");
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Incomplete>(
                wholeContext.Content).Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind
                    .DeclarationInventoryIncomplete);

        InspectionEnvelope<ExactTypeInspectionResult> selected =
            await ExecuteExactAsync(
                store,
                context,
                "Selected.Subject",
                compileAssetId:
                    $"compile:ref/{Framework}/{assemblyName}.dll");
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Incomplete>(selected.Content).Failures,
            failure => failure.Kind == ExactTypeInspectionFailureKind.TypeResolutionRejected);
        Assert.IsType<InspectionShare.NonProjectable>(selected.Share);
        AssertDetached(wholeContext);
        AssertDetached(selected);
    }

    [Fact]
    public async Task ExactTypeInspection_SelectedConstraintFailuresKeepTypeAndMemberSubjects()
    {
        ConstraintFailureFixture fixture =
            ConstraintFailureImage();
        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExecuteExactAsync(
                await CachedStoreAsync(
                    Version,
                    Archive(
                        ($"ref/{Framework}/Constraint.Subjects.dll",
                            fixture.Image))),
                PackageContext(Version),
                "N.Selected`1");

        var available =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                envelope.Content);
        ApiMember member = Assert.Single(
            available.Type.Members,
            member => member.Name == "SelectedMethod");
        int typeToken = Assert.IsType<int>(available.Type.MetadataToken);
        int memberToken = Assert.IsType<int>(member.MetadataToken);
        Assert.Equal(fixture.SelectedTypeToken, typeToken);
        Assert.Equal(fixture.SelectedMethodToken, memberToken);
        Assert.Equal(
            [typeToken, memberToken],
            available.InspectionFailures
                .Select(failure => failure.SubjectToken)
                .Order()
                .ToArray());
        Assert.All(
            available.InspectionFailures,
            failure =>
            {
                Assert.Equal(
                    ApiSurface.ConstraintResolutionOperation,
                    failure.Operation);
                Assert.Equal(
                    "Missing.Constraint.Dependency",
                    failure.DependencyAssembly?.Name);
                Assert.Null(failure.SourceAssemblyPath);
            });
        Assert.DoesNotContain(
            available.InspectionFailures,
            failure =>
                failure.SubjectToken == fixture.EarlierTypeToken
                || failure.SubjectToken == fixture.EarlierMethodToken);
        AssertDetached(envelope);
    }

    [Fact]
    public async Task ExactTypeInspection_PredecessorOperationKeepsItsDefinition()
    {
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            "1.0.0",
            Producer(NuGetOrg),
            new MemoryStream(TargetPackage()),
            TestContext.Current.CancellationToken);
        await store.CommitAsync(
            PackageId,
            "2.0.0",
            Producer(NuGetOrg),
            new MemoryStream(CallerPackage()),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        await using var coordinator = new WorkspaceRealizationCoordinator();

        WorkspaceContextInput predecessorContext = new()
        {
            Framework = Framework,
            Members = [PackageMember("1.0.0")],
        };
        var predecessor =
            await ActivateAsync(
                coordinator,
                predecessorContext,
                Options(
                    client,
                    store,
                    includePackageRootBindings: true));

        WorkspaceContextInput successorContext = new()
        {
            Framework = Framework,
            Members = [PackageMember("2.0.0")],
        };
        var successor =
            await ActivateAsync(
                coordinator,
                successorContext,
                Options(
                    client,
                    store,
                    includePackageRootBindings: true));
        using (predecessor.Realization)
        using (predecessor.Operation)
        using (successor.Realization)
        using (successor.Operation)
        {
            InspectionEnvelope<ExactTypeInspectionResult> predecessorEnvelope =
                await ExactTypeInspection.ExecuteAsync(
                    Request("Target.Api"),
                    predecessor.Operation,
                    predecessor.Binding,
                    predecessor.Realization,
                    TestContext.Current.CancellationToken);
            var predecessorResult =
                Assert.IsType<ExactTypeInspectionResult.Available>(
                    predecessorEnvelope.Content);
            InspectionEnvelope<ExactTypeInspectionResult> successorEnvelope =
                await ExactTypeInspection.ExecuteAsync(
                    Request("Shared.Entry"),
                    successor.Operation,
                    successor.Binding,
                    successor.Realization,
                    TestContext.Current.CancellationToken);
            var successorResult =
                Assert.IsType<ExactTypeInspectionResult.Available>(
                    successorEnvelope.Content);

            Assert.NotEqual(
                predecessorResult.Definition,
                successorResult.Definition);
            Assert.Same(
                predecessor.Operation.Definition.Identity,
                predecessorResult.Definition);
            Assert.Same(
                successor.Operation.Definition.Identity,
                successorResult.Definition);
            var mismatched = await ExactTypeInspection.ExecuteAsync(
                Request("Target.Api"),
                successor.Operation,
                predecessor.Binding,
                predecessor.Realization,
                TestContext.Current.CancellationToken);
            var reverseMismatched =
                await ExactTypeInspection.ExecuteAsync(
                    Request("Shared.Entry"),
                    predecessor.Operation,
                    successor.Binding,
                    successor.Realization,
                    TestContext.Current.CancellationToken);
            Assert.Contains(
                Assert.IsType<ExactTypeInspectionResult.Rejected>(mismatched.Content).Failures,
                failure => failure.Kind == ExactTypeInspectionFailureKind.DefinitionMismatch);
            Assert.Contains(
                Assert.IsType<ExactTypeInspectionResult.Rejected>(
                    reverseMismatched.Content).Failures,
                failure => failure.Kind
                    == ExactTypeInspectionFailureKind.DefinitionMismatch);
            Assert.IsType<InspectionShare.NonProjectable>(mismatched.Share);
            Assert.IsType<InspectionShare.NonProjectable>(
                reverseMismatched.Share);
            Assert.Equal("Target.Api", predecessorResult.Type.FullName);
            Assert.Equal(
                "Shared.Entry",
                successorResult.Type.FullName);
            AssertDetached(predecessorEnvelope);
            AssertDetached(successorEnvelope);
            AssertDetached(mismatched);
            AssertDetached(reverseMismatched);
        }
    }

    [Fact]
    public async Task ExactTypeInspection_EquivalentColdRealizationsReturnSameEnvelopeSemantics()
    {
        WorkspaceContextInput context = new()
        {
            Framework = Framework,
            Members = [PackageMember(Version)],
        };
        InspectionEnvelope<ExactTypeInspectionResult> first =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, TargetPackage()),
                context,
                "Target.Api");
        InspectionEnvelope<ExactTypeInspectionResult> second =
            await ExecuteExactAsync(
                await CachedStoreAsync(Version, TargetPackage()),
                context,
                "Target.Api");

        var firstAvailable =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                first.Content);
        var secondAvailable =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                second.Content);
        Assert.Equal(firstAvailable.Candidate.Definition, secondAvailable.Candidate.Definition);
        Assert.Equal(firstAvailable.Candidate.Address, secondAvailable.Candidate.Address);
        Assert.Equal(firstAvailable.Candidate.Supplier, secondAvailable.Candidate.Supplier);
        Assert.Equal(
            firstAvailable.Type.FullName,
            secondAvailable.Type.FullName);
        Assert.Equal(
            firstAvailable.Members.Members.Select(MemberIdentity),
            secondAvailable.Members.Members.Select(MemberIdentity));
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(
            Assert.IsType<InspectionShare.Available>(first.Share).Packet,
            Assert.IsType<InspectionShare.Available>(second.Share).Packet);

        static string MemberIdentity(ApiMember member) =>
            $"{member.Kind}|{member.Name}|{member.Signature}";
    }

    [Fact]
    public async Task ExactTypeInspection_DirectSelectionLeavesResidentLocatorInactiveAndFactsIndependent()
    {
        var context = new WorkspaceContextInput
        {
            Framework = Framework,
            Members = [PackageMember(Version)],
        };
        using var client = new HttpClient(new FailingHandler());
        await using var coordinator = new WorkspaceRealizationCoordinator();
        var activated = await ActivateAsync(
            coordinator,
            context,
            Options(client, await CachedStoreAsync(Version, TargetPackage()),
                includePackageRootBindings: true));
        InspectionEnvelope<ExactTypeInspectionResult> firstEnvelope;
        InspectionEnvelope<ExactTypeInspectionResult> secondEnvelope;
        using (activated.Realization)
        using (activated.Operation)
        {
            WorkspaceDeclarationLocator locator =
                activated.Operation.Workspace.GetDeclarationLocator();
            Assert.False(locator.IsActive);
            firstEnvelope = await ExactTypeInspection.ExecuteAsync(
                Request("Api"),
                activated.Operation,
                activated.Binding,
                activated.Realization,
                TestContext.Current.CancellationToken);
            secondEnvelope = await ExactTypeInspection.ExecuteAsync(
                Request("Target.Api"),
                activated.Operation,
                activated.Binding,
                activated.Realization,
                TestContext.Current.CancellationToken);
            Assert.False(locator.IsActive);
            Assert.Equal(0, locator.InventoryReadCount);
        }

        var first =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                firstEnvelope.Content);
        var second =
            Assert.IsType<ExactTypeInspectionResult.Available>(
                secondEnvelope.Content);
        Assert.Same(first.Definition, second.Definition);
        Assert.Equal(first.Candidate.Address, second.Candidate.Address);
        Assert.NotSame(first.Type, second.Type);
        Assert.Equal(
            first.Type.Members.Select(member =>
                (member.Name, member.Signature, member.IsAsync)),
            second.Type.Members.Select(member =>
                (member.Name, member.Signature, member.IsAsync)));
        first.Type.Members.Clear();
        first.Type.DerivedTypes.Add("Mutated.Type");
        first.Type.Attributes.Add("MutatedAttribute");
        Assert.NotEmpty(second.Type.Members);
        Assert.DoesNotContain("Mutated.Type", second.Type.DerivedTypes);
        Assert.DoesNotContain(
            "MutatedAttribute",
            second.Type.Attributes);
        var packet = WorkspaceSharePacketCodec.Decode(
            Assert.IsType<InspectionShare.Available>(
                firstEnvelope.Share).Packet,
            TestContext.Current.CancellationToken);
        Assert.Equal("Target.Api", packet.Type);
        AssertDetached(firstEnvelope);
        AssertDetached(secondEnvelope);
    }

    [Fact]
    public async Task ExactTypeInspection_TruncationAndInvalidSelectionCannotShare()
    {
        var context = new WorkspaceContextInput
        {
            Framework = Framework,
            Members = [PackageMember(Version)],
        };
        var store = await CachedStoreAsync(Version, TargetPackage());
        var truncated = await ExecuteExactAsync(
            store, context, "Target.Api",
            limits: new(1, 10_000, 1, 10_000, 10_000, 1_000_000, 10_000_000));
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Incomplete>(truncated.Content).Failures,
            failure => failure.Kind == ExactTypeInspectionFailureKind.ApiSurfaceIncomplete
                && failure.SurfaceLimit is not null);
        Assert.IsType<InspectionShare.NonProjectable>(truncated.Share);
        Assert.NotEmpty(truncated.Diagnostics);

        var rejected = await ExecuteExactAsync(store, context, "*");
        Assert.Contains(
            Assert.IsType<ExactTypeInspectionResult.Rejected>(rejected.Content).Failures,
            failure => failure.Kind == ExactTypeInspectionFailureKind.InvalidRequest);
        Assert.IsType<InspectionShare.NonProjectable>(rejected.Share);
        Assert.NotEmpty(rejected.Diagnostics);
        AssertDetached(truncated);
        AssertDetached(rejected);
    }

    [Theory]
    [InlineData("cache/assembly.dll")]
    [InlineData("/tmp/assembly.dll")]
    [InlineData("C:\\cache\\assembly.dll")]
    public void ExactTypeInspection_DetachmentGuardDetectsStringPaths(string path)
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertDetached(new ApiType { SourceAssemblyPath = path }));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertDetached(new { Value = path }));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertDetached(new { SupplierSurface = new ApiSurface() }));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertDetached(new { Callback = (Action)(() => { }) }));
    }

    static async Task<InspectionEnvelope<ExactTypeInspectionResult>>
        ExecuteExactAsync(
            IPackageStore store,
            WorkspaceContextInput context,
            string selector,
            string? assemblyName = null,
            string? compileAssetId = null,
            ApiSurfaceProjectionLimits? limits = null,
            ApiSurfaceScope scope = ApiSurfaceScope.Public)
    {
        using var client = new HttpClient(new FailingHandler());
        await using var coordinator = new WorkspaceRealizationCoordinator();
        var activated = await ActivateAsync(
            coordinator,
            context,
            Options(
                client,
                store,
                includePackageRootBindings: true));
        using (activated.Realization)
        using (activated.Operation)
        {
            return await ExactTypeInspection.ExecuteAsync(
                Request(selector) with
                {
                    Scope = scope,
                    AssemblyName = assemblyName,
                    CompileAssetId = compileAssetId,
                    SurfaceLimits = limits ?? ExactTypeLimits,
                },
                activated.Operation,
                activated.Binding,
                activated.Realization,
                TestContext.Current.CancellationToken);
        }
    }

    static async Task<(
        PackageRootBinding Binding,
        PackageAssemblyContextRealization Realization,
        WorkspaceRealizationOperationLease Operation)> ActivateAsync(
        WorkspaceRealizationCoordinator coordinator,
        WorkspaceContextInput context,
        WorkspaceContextLoadOptions options)
    {
        var plan = new WorkspacePlan([], [context]);
        WorkspaceRealizationCandidateStartResult.Prepared prepared =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    plan,
                    TestContext.Current.CancellationToken));
        PackageRootBinding binding;
        PackageAssemblyContextRealization realization;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            WorkspacePackageRootAcquisitionOutcome.Acquired acquired =
                Assert.IsType<
                    WorkspacePackageRootAcquisitionOutcome.Acquired>(
                    await WorkspaceContextLoader.AcquirePackageRootAsync(
                        context,
                        options,
                        TestContext.Current.CancellationToken));
            binding = acquired.Root;
            realization =
                construction.Workspace.RealizePackageAssemblyContextRoles(
                    [binding.Root],
                    cancellationToken:
                        TestContext.Current.CancellationToken);
        }
        try
        {
            Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
                await coordinator.CompleteCandidateAsync(
                    prepared.Candidate,
                    TestContext.Current.CancellationToken));
            Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(prepared.Candidate));
            WorkspaceRealizationOperationAdmission.Admitted admitted =
                Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                    await coordinator.EnterOperationAsync(
                        TestContext.Current.CancellationToken));
            return (binding, realization, admitted.Lease);
        }
        catch
        {
            realization.Dispose();
            throw;
        }
    }

    static WorkspaceContextInput PackageContext(string version) =>
        new()
        {
            Framework = Framework,
            Members = [PackageMember(version)],
        };

    static ExactTypeInspectionRequest Request(string selector) =>
        new(
            ContextIndex: 0,
            TypeSelector: selector,
            Scope: ApiSurfaceScope.Public,
            SurfaceLimits: ExactTypeLimits);

    static byte[] ApiAssembly(
        string assemblyName,
        params (string TypeName, string[] Members)[] declarations)
        => ApiAssembly(
            assemblyName,
            TypeAttributes.Public,
            declarations);

    static byte[] ApiAssembly(
        string assemblyName,
        TypeAttributes visibility,
        params (string TypeName, string[] Members)[] declarations)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule(assemblyName);
        foreach ((string typeName, string[] members) in declarations)
        {
            TypeBuilder type = module.DefineType(
                typeName,
                visibility | TypeAttributes.Class);
            int aritySeparator = typeName.LastIndexOf('`');
            if (aritySeparator >= 0
                && int.TryParse(
                    typeName.AsSpan(aritySeparator + 1),
                    out int arity)
                && arity > 0)
            {
                type.DefineGenericParameters(
                    [
                        .. Enumerable.Range(0, arity)
                            .Select(index => "T" + index),
                    ]);
            }
            type.DefineDefaultConstructor(MethodAttributes.Public);
            foreach (string name in members)
            {
                MethodBuilder method = type.DefineMethod(
                    name,
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
            }
            type.CreateType();
        }

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    static byte[] NestedApiAssembly()
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName("Nested"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule("Nested");
        TypeBuilder outer = module.DefineType(
            "N.Outer",
            TypeAttributes.Public | TypeAttributes.Class);
        TypeBuilder nested = outer.DefineNestedType(
            "Inner",
            TypeAttributes.NestedPublic | TypeAttributes.Class);
        nested.DefineDefaultConstructor(MethodAttributes.Public);
        outer.DefineDefaultConstructor(MethodAttributes.Public);
        nested.CreateType();
        outer.CreateType();

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    static ConstraintFailureFixture ConstraintFailureImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Constraint.Subjects.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Constraint.Subjects"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle missing =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(
                    "Missing.Constraint.Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                missing,
                metadata.GetOrAddString("Missing"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle earlierType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Earlier`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle selectedType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Selected`1"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 1,
            isInstanceMethod: false).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        BlobHandle methodSignature =
            metadata.GetOrAddBlob(signature);
        var instructions = new BlobBuilder();
        var encoder = new InstructionEncoder(
            instructions,
            new ControlFlowBuilder());
        encoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset =
            new MethodBodyStreamEncoder(methodBodies)
                .AddMethodBody(encoder, maxStack: 0);
        MethodDefinitionHandle earlierMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("EarlierMethod"),
                methodSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle selectedMethod =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("SelectedMethod"),
                methodSignature,
                bodyOffset,
                MetadataTokens.ParameterHandle(1));

        GenericParameterHandle earlierMethodParameter =
            metadata.AddGenericParameter(
                earlierMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        GenericParameterHandle earlierTypeParameter =
            metadata.AddGenericParameter(
                earlierType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        GenericParameterHandle selectedMethodParameter =
            metadata.AddGenericParameter(
                selectedMethod,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        GenericParameterHandle selectedTypeParameter =
            metadata.AddGenericParameter(
                selectedType,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
        metadata.AddGenericParameterConstraint(
            earlierMethodParameter,
            constraint);
        metadata.AddGenericParameterConstraint(
            earlierTypeParameter,
            constraint);
        metadata.AddGenericParameterConstraint(
            selectedMethodParameter,
            constraint);
        metadata.AddGenericParameterConstraint(
            selectedTypeParameter,
            constraint);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return new(
            image.ToArray(),
            MetadataTokens.GetToken(earlierType),
            MetadataTokens.GetToken(earlierMethod),
            MetadataTokens.GetToken(selectedType),
            MetadataTokens.GetToken(selectedMethod));
    }

    sealed record ConstraintFailureFixture(
        byte[] Image,
        int EarlierTypeToken,
        int EarlierMethodToken,
        int SelectedTypeToken,
        int SelectedMethodToken);

    static Type[] ResourceBearingTypes { get; } =
    [
        typeof(InspectionWorkspace),
        typeof(WorkspaceRealizationOperationLease),
        typeof(WorkspaceRealizationConstructionLease),
        typeof(PackageAssemblyContextRealization),
        typeof(PackageAssemblyRoleParticipant),
        typeof(PackageRootBinding),
        typeof(PackageRootRealization),
        typeof(IPackageContent),
        typeof(AssemblyContextGroup),
        typeof(AssemblyContextParticipant),
        typeof(ResolvedAssemblyReference),
        typeof(MetadataReader),
        typeof(Stream),
        typeof(Delegate),
        typeof(IDisposable),
        typeof(IAsyncDisposable),
        typeof(ApiSurface),
        typeof(WorkspaceDeclarationLocator),
        typeof(WorkspaceDeclarationContext),
        typeof(AssemblyImageSnapshot),
    ];

    static void AssertDetached(object root)
    {
        var pending = new Queue<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(root);
        while (pending.TryDequeue(out object? current))
        {
            if (!visited.Add(current))
                continue;
            Type type = current.GetType();
            Assert.DoesNotContain(ResourceBearingTypes, prohibited => prohibited.IsAssignableFrom(type));
            if (current is string text)
            {
                Assert.False(
                    Regex.IsMatch(text, @"^(?:/|[A-Za-z]:[\\/]|\\\\)")
                        || Regex.IsMatch(text, @"(?:^|[/\\])[^/\\]+\.(?:dll|exe|pdb|cs)$",
                            RegexOptions.IgnoreCase) && text.IndexOfAny(['/', '\\']) >= 0,
                    $"Path-bearing string escaped: {text}");
                continue;
            }
            if (type.IsPrimitive || type.IsEnum || current is Guid or Version)
                continue;
            if (current is IEnumerable sequence)
            {
                foreach (object? item in sequence)
                    if (item is not null)
                        pending.Enqueue(item);
                continue;
            }

            foreach (PropertyInfo property
                in type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                object? value = property.GetValue(current);
                if (property.Name is "CompileAssetId" or "DeclarationAssetId" or "SupplierAssetId"
                    && value is string assetId)
                {
                    // Package-issued compile keys are logical archive identities,
                    // not paths a host can reopen.
                    Assert.StartsWith("compile:", assetId, StringComparison.Ordinal);
                    continue;
                }
                if (property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                    && current is not InspectionShare.NonProjectable)
                    Assert.True(value is null or "", $"Path property escaped: {type.Name}.{property.Name}");
                if (value is not null)
                    pending.Enqueue(value);
            }
        }
    }
}
