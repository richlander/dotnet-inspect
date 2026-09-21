using System.Collections.Immutable;

using QuerySpace;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class CommittedScenarioSelectorResolverTests
{
    [Fact]
    public async Task
        Resolve_PacketTypeSelectorMatchesExactEscapedMetadataName()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        ApiType literalPlus = new()
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            DefinitionName = TypeName("Outer+Inner"),
            Accessibility = "public",
            Members = [],
        };
        ApiType nested = new()
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            DefinitionName = TypeName("Outer", "Inner"),
            Accessibility = "public",
            Members = [],
        };
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    literalPlus,
                    nested));
        const string json =
            """{"f":2,"t":[["Package.A","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["Navigation.Library","1.0.0.0",null,null],"y":"Sample.Outer\\+Inner"},"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);
        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet,
                TestContext.Current.CancellationToken);

        CommittedScenarioSelectorResolutionResult result =
            WorkspaceDefinitionConsumer.ResolveSelectors(
                definitions,
                workspace.Identity,
                scope,
                [new("t0", package)]);

        CommittedScenarioSelectorResolution resolved =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    result).Resolution;
        var packageState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolved.ActiveState);
        NavigationRetainedSubjectContext context =
            Assert.IsType<NavigationRetainedSubjectContext>(
                packageState.Initialization.Context);
        Assert.Equal(literalPlus.DefinitionName, context.Type!.Identity.Type);
        Assert.NotEqual(nested.DefinitionName, context.Type.Identity.Type);
    }

    [Fact]
    public async Task
        Resolve_WorkspaceFocusRetainsExactMemberContextAndDormantRows()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        ApiMember member = NavigationSnapshotTestData.Member("Run");
        ApiType type = NavigationSnapshotTestData.Type("Target", member);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    type));
        string memberAnchor =
            ApiMemberIdentity.GetMemberAnchor(type, member).Fingerprint;
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: null,
            tabs:
            [
                PackageTab("package", "Package.A"),
                new NavigationTabDefinition(
                    "platform",
                    new DefinitionMemberCoordinate.PlatformCoordinate(
                        "runtime",
                        Framework: "net11.0")),
            ],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    facet: "workspace.overview"),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Member(
                        Library("Navigation.Library"),
                        TypeName("Target"),
                        memberAnchor: memberAnchor),
                    facet: "workspace.overview"),
                new CommittedViewStateDefinition("platform"),
            ]);

        CommittedScenarioSelectorResolutionResult result =
            WorkspaceDefinitionConsumer.ResolveSelectors(
                definitions,
                workspace.Identity,
                scope,
                [new("package", package)]);

        CommittedScenarioSelectorResolution resolved =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    result).Resolution;
        var active =
            Assert.IsType<ResolvedCommittedWorkspaceViewState>(
                resolved.ActiveState);
        Assert.Equal(
            workspace.Identity,
            Assert.IsType<StructuralSubjectIdentity.WorkspaceSubject>(
                active.Initialization.Subject).Identity);
        Assert.Same(active.Initialization, resolved.Activation);
        Assert.Equal("workspace.overview", active.Initialization.Lens!.Facet.Value);

        var packageState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolved.States[1]);
        NavigationRetainedSubjectContext context =
            Assert.IsType<NavigationRetainedSubjectContext>(
                packageState.Initialization.Context);
        Assert.Equal(
            scope.Packages[0].Occurrence,
            context.Package.Occurrence);
        Assert.Equal(
            "Navigation.Library",
            Assert.IsType<StructuralSubjectIdentity.LibrarySubject>(
                context.Library).Identity.Assembly.Name);
        Assert.Equal(TypeName("Target"), context.Type!.Identity.Type);
        Assert.Equal(memberAnchor, context.Member!.Identity.Member.Fingerprint);
        Assert.Same(package, packageState.Package);
        Assert.IsType<ResolvedCommittedDormantViewState>(resolved.States[2]);
    }

    [Fact]
    public async Task
        Resolve_PackageFocusProducesOneActivationAndInactiveExactInput()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding firstBinding =
            NavigationSnapshotTestData.Binding(
                "Package.First",
                "First.Library");
        PackageRootBinding secondBinding =
            NavigationSnapshotTestData.Binding(
                "Package.Second",
                "Second.Library");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                firstBinding,
                secondBinding);
        NavigationPackageEvaluation first = Evaluation(
            scope,
            firstBinding,
            NavigationSnapshotTestData.Surface(
                "First.Library",
                NavigationSnapshotTestData.Type("FirstType")));
        NavigationPackageEvaluation second = Evaluation(
            scope,
            secondBinding,
            NavigationSnapshotTestData.Surface(
                "Second.Library",
                NavigationSnapshotTestData.Type("SecondType")));
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "second",
            tabs:
            [
                PackageTab("first", "Package.First"),
                PackageTab("second", "Package.Second"),
            ],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("first"),
                new CommittedViewStateDefinition(
                    "second",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Type(
                        Library("Second.Library"),
                        TypeName("SecondType")),
                    facet: "package.overview"),
            ]);

        var result =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        definitions,
                        workspace.Identity,
                        scope,
                        [
                            new("first", first),
                            new("second", second),
                        ]));

        var active =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                result.Resolution.ActiveState);
        Assert.Equal("second", active.NavigationId);
        Assert.Same(second, active.Package);
        Assert.Same(
            active.Initialization.Subject,
            active.Initialization.Context!.Package);
        Assert.Equal(
            "package.overview",
            active.Initialization.Lens!.Facet.Value);
        Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
            active.Initialization.Context.Type);

        var inactive =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                result.Resolution.States[1]);
        Assert.Same(first, inactive.Package);
        Assert.Null(inactive.Initialization.Subject);
        Assert.Null(inactive.Initialization.Lens);
        Assert.Equal(
            first.Occurrence.Occurrence,
            inactive.Initialization.Context!.Package.Occurrence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        Resolve_OmittedPackageContextMaterializesExactPackage(
            bool workspaceSubject)
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package = Evaluation(
            scope,
            binding,
            NavigationSnapshotTestData.Surface("Navigation.Library"));
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "package",
            tabs: [PackageTab("package", "Package.A")],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    workspaceSubject
                        ? new PortableSubjectRequest.Workspace()
                        : null),
            ]);

        CommittedScenarioSelectorResolution resolution =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        definitions,
                        workspace.Identity,
                        scope,
                        [new("package", package)])).Resolution;

        var active =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolution.ActiveState);
        Assert.Equal(
            package.Occurrence.Occurrence,
            active.Initialization.Context!.Package.Occurrence);
        if (workspaceSubject)
        {
            Assert.Equal(
                workspace.Identity,
                Assert.IsType<StructuralSubjectIdentity.WorkspaceSubject>(
                    active.Initialization.Subject).Identity);
        }
        else
        {
            Assert.Null(active.Initialization.Subject);
        }
    }

    [Fact]
    public async Task
        Resolve_SameLibraryIdentityAcrossOccurrencesStaysOccurrenceLocal()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding firstBinding =
            NavigationSnapshotTestData.Binding(
                "Package.First",
                "Shared.Library");
        PackageRootBinding secondBinding =
            NavigationSnapshotTestData.Binding(
                "Package.Second",
                "Shared.Library");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                firstBinding,
                secondBinding);
        NavigationPackageEvaluation first = Evaluation(
            scope,
            firstBinding,
            NavigationSnapshotTestData.Surface(
                "Shared.Library",
                NavigationSnapshotTestData.Type("FirstType")));
        NavigationPackageEvaluation second = Evaluation(
            scope,
            secondBinding,
            NavigationSnapshotTestData.Surface(
                "Shared.Library",
                NavigationSnapshotTestData.Type("SecondType")));
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "second",
            tabs:
            [
                PackageTab("first", "Package.First"),
                PackageTab("second", "Package.Second"),
            ],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "first",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Type(
                        Library("Shared.Library"),
                        TypeName("FirstType"))),
                new CommittedViewStateDefinition(
                    "second",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Type(
                        Library("Shared.Library"),
                        TypeName("SecondType"))),
            ]);

        CommittedScenarioSelectorResolution resolution =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        definitions,
                        workspace.Identity,
                        scope,
                        [
                            new("first", first),
                            new("second", second),
                        ])).Resolution;

        var firstState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolution.States[1]);
        var secondState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolution.States[2]);
        Assert.Equal(
            first.Occurrence.Occurrence,
            firstState.Initialization.Context!.Package.Occurrence);
        Assert.Equal(
            second.Occurrence.Occurrence,
            secondState.Initialization.Context!.Package.Occurrence);
        Assert.Equal(
            TypeName("FirstType"),
            firstState.Initialization.Context.Type!.Identity.Type);
        Assert.Equal(
            TypeName("SecondType"),
            secondState.Initialization.Context.Type!.Identity.Type);
    }

    [Fact]
    public async Task
        Resolve_RejectsMissingAmbiguousAndForeignPackageFacts()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package = Evaluation(
            scope,
            binding,
            NavigationSnapshotTestData.Surface("Navigation.Library"));
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "package",
            tabs: [PackageTab("package", "Package.A")],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("package"),
            ]);

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                scope,
                []),
            CommittedSelectorResolutionFailureKind.PackageOccurrenceMissing);
        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                scope,
                [
                    new("package", package),
                    new("package", package),
                ]),
            CommittedSelectorResolutionFailureKind.PackageOccurrenceAmbiguous);

        CommittedScenarioDefinitionSet mismatchedDefinitions = Definitions(
            focus: "package",
            tabs: [PackageTab("package", "Package.Other")],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition("package"),
            ]);
        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                mismatchedDefinitions,
                workspace.Identity,
                scope,
                [new("package", package)]),
            CommittedSelectorResolutionFailureKind.PackageCoordinateMismatch);

        PackageRootBinding replacementBinding =
            NavigationSnapshotTestData.Binding("Package.Replacement");
        WorkspaceScopeSnapshot replacedScope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                replacementBinding);
        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                replacedScope,
                [new("package", package)]),
            CommittedSelectorResolutionFailureKind
                .PackageOccurrenceOutsideScope);

        await using var foreignWorkspace = new InspectionWorkspace();
        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                foreignWorkspace.Identity,
                scope,
                [new("package", package)]),
            CommittedSelectorResolutionFailureKind.WorkspaceMismatch);
    }

    [Theory]
    [InlineData("missing-library", CommittedSelectorResolutionFailureKind.LibraryMissing)]
    [InlineData("ambiguous-library", CommittedSelectorResolutionFailureKind.LibraryAmbiguous)]
    [InlineData("missing-type", CommittedSelectorResolutionFailureKind.TypeMissing)]
    [InlineData("ambiguous-type", CommittedSelectorResolutionFailureKind.TypeAmbiguous)]
    [InlineData("missing-member", CommittedSelectorResolutionFailureKind.MemberMissing)]
    [InlineData("ambiguous-member", CommittedSelectorResolutionFailureKind.MemberAmbiguous)]
    public async Task Resolve_SelectorCardinalityFailuresAreTyped(
        string scenario,
        CommittedSelectorResolutionFailureKind expected)
    {
        await using var workspace = new InspectionWorkspace();
        ApiType type = scenario switch
        {
            "ambiguous-type" =>
                NavigationSnapshotTestData.Type("Target"),
            "missing-member" =>
                NavigationSnapshotTestData.Type(
                    "Target",
                    NavigationSnapshotTestData.Member("Other")),
            "ambiguous-member" =>
                NavigationSnapshotTestData.Type(
                    "Target",
                    NavigationSnapshotTestData.Member("Run"),
                    NavigationSnapshotTestData.Member("Run")),
            _ => NavigationSnapshotTestData.Type(
                "Target",
                NavigationSnapshotTestData.Member("Run")),
        };
        PackageRootBinding binding = scenario == "ambiguous-library"
            ? NavigationSnapshotTestData.BindingWithAssemblyImages(
                "Package.A",
                "net11.0",
                (
                    "First",
                    File.ReadAllBytes(
                        typeof(InspectionWorkspace).Assembly.Location)),
                (
                    "Second",
                    File.ReadAllBytes(
                        typeof(ApiType).Assembly.Location)))
            : NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationSnapshotTestData.LibrarySurface[] surfaces =
            scenario switch
            {
                "ambiguous-library" =>
                [
                    NavigationSnapshotTestData.Surface(
                        "Navigation.Library",
                        type),
                    NavigationSnapshotTestData.Surface(
                        "Navigation.Library",
                        type),
                ],
                "ambiguous-type" =>
                [
                    NavigationSnapshotTestData.Surface(
                        "Navigation.Library",
                        type,
                        NavigationSnapshotTestData.Type("Target")),
                ],
                _ =>
                [
                    NavigationSnapshotTestData.Surface(
                        "Navigation.Library",
                        type),
                ],
            };
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                surfaces);
        PortableLibraryIdentity library = scenario == "missing-library"
            ? Library("Missing.Library")
            : Library("Navigation.Library");
        PortableRetainedSubjectContext context = scenario switch
        {
            "missing-library" or "ambiguous-library" =>
                new PortableRetainedSubjectContext.Library(library),
            "missing-type" or "ambiguous-type" =>
                new PortableRetainedSubjectContext.Type(
                    library,
                    TypeName(
                        scenario == "missing-type"
                            ? "MissingType"
                            : "Target")),
            "missing-member" =>
                new PortableRetainedSubjectContext.Member(
                    library,
                    TypeName("Target"),
                    memberSignature: "M:Sample.Target.Run"),
            "ambiguous-member" =>
                new PortableRetainedSubjectContext.Member(
                    library,
                    TypeName("Target"),
                    memberAnchor:
                        ApiMemberIdentity.GetMemberAnchor(
                            type,
                            type.Members[0]).Fingerprint),
            _ => throw new InvalidOperationException(),
        };
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "package",
            tabs: [PackageTab("package", "Package.A")],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Workspace(),
                    context),
            ]);

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                scope,
                [new("package", package)]),
            expected);
    }

    [Fact]
    public async Task Resolve_QueryLibraryScopeRetainsExactOccurrenceLibraries()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library"));
        CommittedScenarioDefinitionSet definitions =
            DefinitionsWithLibraryScope("Navigation.Library");

        CommittedScenarioSelectorResolution resolved =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        definitions,
                        workspace.Identity,
                        scope,
                        [new("package", package)])).Resolution;

        var packageState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolved.ActiveState);
        Assert.Same(
            package.Libraries[0],
            Assert.Single(packageState.Libraries));
    }

    [Fact]
    public async Task Resolve_QueryLibraryScopeMatchesAlternateIdentityCasing()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library"));
        CommittedScenarioDefinitionSet definitions =
            DefinitionsWithLibraryScope("navigation.library");

        CommittedScenarioSelectorResolution resolved =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        definitions,
                        workspace.Identity,
                        scope,
                        [new("package", package)])).Resolution;

        var packageState =
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolved.ActiveState);
        Assert.Same(
            package.Libraries[0],
            Assert.Single(packageState.Libraries));
    }

    [Theory]
    [InlineData(
        false,
        CommittedSelectorResolutionFailureKind.LibraryMissing)]
    [InlineData(
        true,
        CommittedSelectorResolutionFailureKind.LibraryAmbiguous)]
    public async Task Resolve_QueryLibraryScopeCardinalityFailuresAreTyped(
        bool ambiguous,
        CommittedSelectorResolutionFailureKind expected)
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding = ambiguous
            ? NavigationSnapshotTestData.BindingWithAssemblyImages(
                "Package.A",
                "net11.0",
                (
                    "First",
                    File.ReadAllBytes(
                        typeof(InspectionWorkspace).Assembly.Location)),
                (
                    "Second",
                    File.ReadAllBytes(
                        typeof(ApiType).Assembly.Location)))
            : NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationSnapshotTestData.LibrarySurface[] surfaces = ambiguous
            ?
            [
                NavigationSnapshotTestData.Surface("Navigation.Library"),
                NavigationSnapshotTestData.Surface("Navigation.Library"),
            ]
            :
            [
                NavigationSnapshotTestData.Surface("Other.Library"),
            ];
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                surfaces);
        CommittedScenarioDefinitionSet definitions =
            DefinitionsWithLibraryScope("Navigation.Library");

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                scope,
                [new("package", package)]),
            expected);
    }

    [Fact]
    public async Task Resolve_IncompleteTypeInventoryIsNotReportedAsMissing()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                new NavigationSnapshotTestData.LibrarySurface(
                    "Navigation.Library",
                    [],
                    [],
                    new InvalidOperationException("Inspection failed.")));
        CommittedScenarioDefinitionSet definitions = Definitions(
            focus: "package",
            tabs: [PackageTab("package", "Package.A")],
            states:
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Workspace(),
                    new PortableRetainedSubjectContext.Type(
                        Library("Navigation.Library"),
                        TypeName("Target"))),
            ]);

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
                workspace.Identity,
                scope,
                [new("package", package)]),
            CommittedSelectorResolutionFailureKind.TypeInventoryUnavailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        Resolve_ProjectedMemberCannotEscapeItsExactDeclaringType(
            bool useAnchor)
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Package.A");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        ApiMember projected = NavigationSnapshotTestData.Member("Extend");
        projected.Kind = "extension-method";
        projected.MetadataToken = 0x06000001;
        projected.DeclaringTypeDefinitionName = TypeName("Extensions");
        ApiType receiver =
            NavigationSnapshotTestData.Type("Receiver", projected);
        ApiMember declaration =
            NavigationSnapshotTestData.Member("Extend");
        declaration.MetadataToken = projected.MetadataToken;
        ApiType declaring =
            NavigationSnapshotTestData.Type("Extensions", declaration);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    receiver,
                    declaring));
        var anchor =
            ApiMemberIdentity.GetMemberAnchor(declaring, declaration);
        PortableRetainedSubjectContext.Member ReceiverContext() =>
            new(
                Library("Navigation.Library"),
                TypeName("Receiver"),
                memberAnchor: useAnchor ? anchor.Fingerprint : null,
                memberSignature:
                    useAnchor ? null : anchor.CanonicalSignature);
        PortableRetainedSubjectContext.Member DeclaringContext() =>
            new(
                Library("Navigation.Library"),
                TypeName("Extensions"),
                memberAnchor: useAnchor ? anchor.Fingerprint : null,
                memberSignature:
                    useAnchor ? null : anchor.CanonicalSignature);

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                Definitions(
                    focus: "package",
                    tabs: [PackageTab("package", "Package.A")],
                    states:
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace()),
                        new CommittedViewStateDefinition(
                            "package",
                            new PortableSubjectRequest.Workspace(),
                            ReceiverContext()),
                    ]),
                workspace.Identity,
                scope,
                [new("package", package)]),
            CommittedSelectorResolutionFailureKind.MemberMissing);

        CommittedScenarioSelectorResolution resolved =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        Definitions(
                            focus: "package",
                            tabs: [PackageTab("package", "Package.A")],
                            states:
                            [
                                new CommittedViewStateDefinition(
                                    null,
                                    new PortableSubjectRequest.Workspace()),
                                new CommittedViewStateDefinition(
                                    "package",
                                    new PortableSubjectRequest.Workspace(),
                                    DeclaringContext()),
                            ]),
                        workspace.Identity,
                        scope,
                        [new("package", package)])).Resolution;
        Assert.Equal(
            TypeName("Extensions"),
            Assert.IsType<ResolvedCommittedPackageViewState>(
                resolved.ActiveState).Initialization.Context!
                .Member!.DeclaringType.Identity.Type);
    }

    [Fact]
    public async Task
        Resolve_AllLibrariesRequiresOneLibraryButPackageContextDoesNot()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding emptyBinding =
            NavigationSnapshotTestData.BindingWithAssemblies(
                "Package.Empty",
                "net11.0");
        PackageRootBinding fullBinding =
            NavigationSnapshotTestData.Binding("Package.Full");
        WorkspaceScopeSnapshot fullScope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                fullBinding);
        (
            WorkspacePackageOccurrenceDescriptor emptyOccurrence,
            NavigationPackageEvaluation empty) =
                RootOnlyPackage(workspace.Identity, emptyBinding);
        WorkspaceScopeSnapshot scope = ScopeWith(
            workspace.Identity,
            fullScope.Packages[0],
            emptyOccurrence);
        NavigationPackageEvaluation full = Evaluation(
            scope,
            fullBinding,
            NavigationSnapshotTestData.Surface("Navigation.Library"));

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                Definitions(
                    focus: "empty",
                    tabs: [PackageTab("empty", "Package.Empty")],
                    states:
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace()),
                        new CommittedViewStateDefinition(
                            "empty",
                            new PortableSubjectRequest.Workspace(),
                            new PortableRetainedSubjectContext.AllLibraries()),
                    ]),
                workspace.Identity,
                scope,
                [new("empty", empty)]),
            CommittedSelectorResolutionFailureKind.LibraryMissing);

        AssertFailure(
            CommittedScenarioSelectorResolver.Resolve(
                Definitions(
                    focus: "full",
                    tabs:
                    [
                        PackageTab("full", "Package.Full"),
                        PackageTab("empty", "Package.Empty"),
                    ],
                    states:
                    [
                        new CommittedViewStateDefinition(
                            null,
                            new PortableSubjectRequest.Workspace()),
                        new CommittedViewStateDefinition("full"),
                        new CommittedViewStateDefinition(
                            "empty",
                            new PortableSubjectRequest.Workspace(),
                            new PortableRetainedSubjectContext.AllLibraries()),
                    ]),
                workspace.Identity,
                scope,
                [
                    new("full", full),
                    new("empty", empty),
                ]),
            CommittedSelectorResolutionFailureKind.LibraryMissing);

        CommittedScenarioSelectorResolution packageOnly =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Resolved>(
                    CommittedScenarioSelectorResolver.Resolve(
                        Definitions(
                            focus: "empty",
                            tabs: [PackageTab("empty", "Package.Empty")],
                            states:
                            [
                                new CommittedViewStateDefinition(
                                    null,
                                    new PortableSubjectRequest.Workspace()),
                                new CommittedViewStateDefinition(
                                    "empty",
                                    new PortableSubjectRequest.Package(),
                                    new PortableRetainedSubjectContext.Package()),
                            ]),
                        workspace.Identity,
                        scope,
                        [new("empty", empty)])).Resolution;
        Assert.Null(
            Assert.IsType<ResolvedCommittedPackageViewState>(
                packageOnly.ActiveState).Initialization.Context!.Library);
    }

    private static (
        WorkspacePackageOccurrenceDescriptor Occurrence,
        NavigationPackageEvaluation Evaluation)
        RootOnlyPackage(
            InspectionWorkspaceIdentity workspace,
            PackageRootBinding binding)
    {
        PackageArtifactRootRequest request =
            PackageArtifactRootRequest.From(binding);
        var correspondence =
            new PackageArtifactRootCorrespondence(workspace, request);
        var occurrence = new WorkspacePackageOccurrence(
            workspace,
            new WorkspacePackageDescriptor(binding),
            correspondence);
        var descriptor = new WorkspacePackageOccurrenceDescriptor(
            occurrence,
            new ArtifactRootScopeProjection(
                correspondence,
                new ArtifactRootRealizationStatus.Ready(
                    new ArtifactRootGenerationReference())));
        return (
            descriptor,
            NavigationSnapshotTestData.PackageEvaluation(
                descriptor,
                binding));
    }

    private static WorkspaceScopeSnapshot ScopeWith(
        InspectionWorkspaceIdentity workspace,
        params WorkspacePackageOccurrenceDescriptor[] packages)
    {
        var revision = new WorkspaceScopeRevision(
            workspace,
            [
                .. packages.Select(
                    static package => package.Occurrence),
            ]);
        return new WorkspaceScopeSnapshot(
            revision,
            new ArtifactRootCompositionGenerationIdentity(),
            [.. packages],
            new WorkspaceClosureObservation(revision.Identity),
            preparing: null);
    }

    private static NavigationPackageEvaluation Evaluation(
        WorkspaceScopeSnapshot scope,
        PackageRootBinding binding,
        params NavigationSnapshotTestData.LibrarySurface[] libraries) =>
        NavigationSnapshotTestData.PackageEvaluation(
            scope.Packages.Single(
                occurrence =>
                    occurrence.Occurrence.Package.PackageId.Equals(
                        binding.Root.PackageId,
                        StringComparison.Ordinal)),
            binding,
            libraries);

    private static CommittedScenarioDefinitionSet Definitions(
        string? focus,
        IReadOnlyList<NavigationTabDefinition> tabs,
        IReadOnlyList<CommittedViewStateDefinition> states)
    {
        var registry = new InspectionDefinitionRegistry();
        registry.Add(
            new WorkspaceDefinition(
                InspectionDefinitionSchema.Version2,
                "workspace",
                [
                    new WorkspaceContextDefinition(
                        "context",
                        members:
                        [
                            .. tabs.Select(
                                static tab => tab.Coordinate
                                    ?? throw new InvalidOperationException(
                                        "Test tabs require direct coordinates.")),
                        ]),
                ]));
        registry.Add(
            new CommittedNavigationDefinition(
                InspectionDefinitionSchema.Version2,
                "navigation",
                tabs,
                focus));
        registry.Add(
            new CommittedViewDefinition(
                InspectionDefinitionSchema.Version2,
                "view",
                states));
        registry.Add(
            new ScenarioDefinition(
                InspectionDefinitionSchema.Version2,
                "scenario",
                workspace: "workspace",
                context: "context",
                view: "view",
                navigation: "navigation"));
        return Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario")).Definitions;
    }

    private static CommittedScenarioDefinitionSet
        DefinitionsWithLibraryScope(string libraryName)
    {
        var descriptor = new PortableQueryDefinitionDescriptor<string>(
            "test-query/v1",
            "test-query",
            PortableQueryDefinitionInputs.StateBound(
                [PortableSubjectRequestKind.Package],
                ["package.overview"],
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required,
                PortableQueryInputRequirement.Required),
            static (_, _, _) =>
                new PortableQueryDefinitionResolution<string>.Accepted(
                    "bound"));
        var registry = new InspectionDefinitionRegistry();
        registry.AddQueryDescriptor(descriptor);
        registry.Add(
            new WorkspaceDefinition(
                InspectionDefinitionSchema.Version2,
                "workspace",
                [
                    new WorkspaceContextDefinition(
                        "context",
                        members:
                        [
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                "Package.A",
                                "1.0.0",
                                "net11.0"),
                        ]),
                ]));
        registry.Add(
            new CommittedNavigationDefinition(
                InspectionDefinitionSchema.Version2,
                "navigation",
                [PackageTab("package", "Package.A")],
                focus: "package"));
        registry.Add(
            new CommittedQueryDefinition(
                InspectionDefinitionSchema.Version2,
                "query",
                PortableQueryIdentity.Create(
                    descriptor.QueryId,
                    PortableQueryIntent.Create([], [], [], []),
                    TestContext.Current.CancellationToken)));
        registry.Add(
            new CommittedViewDefinition(
                InspectionDefinitionSchema.Version2,
                "view",
                [
                    new CommittedViewStateDefinition(
                        null,
                        new PortableSubjectRequest.Workspace()),
                    new CommittedViewStateDefinition(
                        "package",
                        new PortableSubjectRequest.Package(),
                        new PortableRetainedSubjectContext.Package(),
                        "package.overview",
                        ["query"],
                        [Library(libraryName)]),
                ]));
        registry.Add(
            new ScenarioDefinition(
                InspectionDefinitionSchema.Version2,
                "scenario",
                workspace: "workspace",
                context: "context",
                view: "view",
                navigation: "navigation"));
        return Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario")).Definitions;
    }

    private static NavigationTabDefinition PackageTab(
        string id,
        string packageId) =>
        new(
            id,
            new DefinitionMemberCoordinate.PackageCoordinate(
                packageId,
                "1.0.0",
                "net11.0"));

    private static PortableLibraryIdentity Library(string name) =>
        new(
            name,
            "1.0.0.0",
            culture: null,
            publicKeyToken: null);

    private static MetadataTypeDefinitionName TypeName(params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "Sample",
                [.. segments])).Name;

    private static void AssertFailure(
        CommittedScenarioSelectorResolutionResult result,
        CommittedSelectorResolutionFailureKind expected)
    {
        var failed =
            Assert.IsType<
                CommittedScenarioSelectorResolutionResult.Failed>(result);
        Assert.Equal(expected, failed.Failure.Kind);
    }
}
