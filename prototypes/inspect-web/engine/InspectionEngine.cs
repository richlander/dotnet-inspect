using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;
using Analysis = ILInspector.Analysis;
using Pipeline = ILInspector.Decompiler.Pipeline;

// The generated wwwroot/inspect-web-engine.js module binds exports.InspectionEngine.*, so this
// type stays in the global namespace. Its helpers live in InspectWeb.Engine.
using InspectWeb.Engine;

/// <summary>
/// The browser's exported inspection surface.
/// </summary>
/// <remarks>
/// <para>
/// An export that inspects an assembly resolves an exact package/version/framework identity,
/// opens a <see cref="BrowserInspectionScope"/> over it, and hands that scope's
/// <see cref="AssemblyContextGroup"/> to a public product query that owns the session. No export
/// — and no helper the engine owns — opens an <see cref="AssemblyInspectionSession"/>, a
/// <c>MetadataSource</c>, an Analysis index, or a retained image descriptor. An operation with no
/// such query is exported as explicitly unsupported; see
/// <c>BrowserUnsupportedOperations.cs</c>.
/// </para>
/// <para>
/// Two other categories exist and say so in place: exports that read package content without
/// inspecting an assembly (the document and XML-documentation reads), and exports that touch no
/// artifact at all (type-name ranking, cache statistics, and the style catalog).
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    /// <summary>
    /// A deterministic awaited operation used by the paired deployment smoke.
    /// </summary>
    [JSExport]
    public static async Task<string> AsyncLoweringCanary()
    {
        await Task.Yield();
        return "inspect-web-async-lowering-ok";
    }

    /// <summary>
    /// The package type surface for one exact package/version/framework workspace, produced by
    /// <see cref="AssemblyContextApiSurfaceQuery"/> over the workspace's own group. The query owns
    /// every session and every accessibility bucket; this method adapts its typed models and
    /// composes no evidence, no classification, and no ordering of its own.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackage(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageSurface surface =
            ProjectPackageSurface(scope, scope.Coordinates[0]);
        return JsonSerializer.Serialize(
            surface,
            BrowserJsonContext.Default.BrowserPackageSurface);
    }

    internal static BrowserPackageSurface ProjectPackageSurface(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate) =>
        ProjectPackage(scope, coordinate).Surface;

    static BrowserPackageProjection ProjectPackage(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(coordinate);
        BrowserCompileLibraryAvailability compileLibrary =
            CompileLibrary(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageProjection(
                new BrowserPackageSurface(
                    coordinate.PackageId,
                    coordinate.Version,
                    BrowserFrameworks(coordinate.Selection),
                    BrowserFramework(coordinate),
                    DefaultAssemblyId: null,
                    compileLibrary,
                    Assemblies: [],
                    Types: [],
                    Accessibility: [],
                    TotalMembers: 0,
                    BrowserPackageWireProjection.Project(coordinate.Package.Documents()),
                    InspectionErrors: [],
                    InspectionError: null),
                ApiSurfaces: null);
        }

        PackageCompileAsset defaultAsset = coordinate.DefaultAsset
            ?? throw new InvalidOperationException(
                "A selected compile-library outcome did not identify its default asset.");
        // Only this coordinate's assemblies are projected. A composite workspace may hold several
        // packages, and projecting all of them here materialized every other package's surface
        // only to discard it.
        BrowserWorkspaceParticipant[] requested =
        [
            .. scope.SurfaceParticipants.Where(candidate =>
                ReferenceEquals(
                    candidate.Coordinate.Root.Identity,
                    coordinate.Root.Identity)),
        ];

        // The site's default path shows public types by default and reaches non-public ones
        // through the accessibility filter, so it asks for the composed scope: a public type
        // keeps its public member list even though non-public types are present. The projection
        // runs under the browser's explicit bounds; an early stop is reported, never silent.
        AssemblyContextApiSurfaceResult surfaces = scope.UseSurface(group =>
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                BrowserApiSurfacePolicy.Limits,
                [.. requested.Select(participant => participant.Participant)]));
        BrowserSurfaceProjection.Surface projected =
            BrowserSurfaceProjection.Project(
                surfaces,
                [
                    .. requested.Select(participant =>
                        new BrowserSurfaceProjection.Participant(
                            participant.Participant,
                            participant.Asset.AssemblyName,
                            participant.Asset.Id,
                            participant.Asset.Path)),
                ]);
        if (projected.Assemblies.Length == 0
            && !projected.IsTruncated)
        {
            throw new InvalidOperationException(
                $"No assembly of {coordinate.PackageId} {coordinate.Version} "
                + "produced an API surface. "
                + (projected.InspectionError
                    ?? "The workspace reported no failure."));
        }

        string defaultAssemblyId = projected.Assemblies.FirstOrDefault(
                assembly => assembly.Id.Equals(
                    defaultAsset.Id,
                    StringComparison.Ordinal))
            ?.Id
            ?? projected.Assemblies.FirstOrDefault()?.Id
            ?? defaultAsset.Id;

        return new BrowserPackageProjection(
            new BrowserPackageSurface(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFrameworks(coordinate.Selection),
                BrowserFramework(coordinate),
                defaultAssemblyId,
                compileLibrary,
                projected.Assemblies,
                projected.Types,
                projected.Accessibility,
                projected.TotalMembers,
                BrowserPackageWireProjection.Project(coordinate.Package.Documents()),
                projected.InspectionErrors,
                projected.InspectionError),
            surfaces);
    }

    sealed record BrowserPackageProjection(
        BrowserPackageSurface Surface,
        AssemblyContextApiSurfaceResult? ApiSurfaces);

    static BrowserCompileLibraryAvailability CompileLibrary(
        PackageCompileAssetSelection selection)
    {
        string? framework = BrowserFramework(selection.TargetFramework);
        if (selection.IsSelected && framework is null)
        {
            throw new InvalidOperationException(
                "The selected compile-library framework cannot be represented safely.");
        }

        return new(
            selection.Status switch
            {
                PackageCompileAssetSelectionStatus.Selected =>
                    BrowserCompileLibraryStatus.Selected,
                PackageCompileAssetSelectionStatus.NoCompileAssets =>
                    BrowserCompileLibraryStatus.NoCompileAssets,
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                    BrowserCompileLibraryStatus.NoMatchingTargetFramework,
                PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                    BrowserCompileLibraryStatus.EmptyCompileGroup,
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                    BrowserCompileLibraryStatus.InvalidImplementationAssets,
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            },
            framework,
            selection.Status switch
            {
                PackageCompileAssetSelectionStatus.Selected => null,
                PackageCompileAssetSelectionStatus.NoCompileAssets =>
                    "The package contains no compile assets.",
                PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                    "No compatible target framework was selected.",
                PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                    "The selected target framework declares an empty compile group.",
                PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                    "The package has an invalid implementation-asset layout.",
                _ => throw new InvalidOperationException(
                    "Package compile-asset selection returned an unknown outcome."),
            });
    }

    static BrowserCompileLibraryAvailability SelectedCompileLibrary(
        string framework)
    {
        string projectedFramework = RequiredBrowserFramework(framework);
        return new(
            BrowserCompileLibraryStatus.Selected,
            projectedFramework,
            Message: null);
    }

    static string[] BrowserFrameworks(PackageCompileAssetSelection selection) =>
    [
        .. selection.AvailableTargetFrameworks
            .Select(BrowserFramework)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    static string BrowserFramework(BrowserPackageCoordinate coordinate) =>
        BrowserFramework(coordinate.Framework) ?? "";

    static string RequiredBrowserFramework(string framework) =>
        BrowserFramework(framework)
        ?? throw new InvalidOperationException(
            "A framework identifier cannot be represented safely.");

    static string BrowserDependencyGroupFramework(string framework) =>
        string.IsNullOrWhiteSpace(framework)
            ? "any"
            : RequiredBrowserFramework(framework);

    static string? BrowserFramework(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework)
            || framework.Length > 128)
        {
            return null;
        }

        foreach (char character in framework)
        {
            if (!(character is >= 'a' and <= 'z')
                && !(character is >= 'A' and <= 'Z')
                && !(character is >= '0' and <= '9')
                && character is not
                    ('.' or '-' or '+' or '_' or ',' or '=' or ' '))
            {
                return null;
            }
        }

        return framework;
    }

    /// <summary>
    /// One type's metadata projection, produced by
    /// <see cref="AssemblyContextTypeProjectionQuery"/> over the participant that owns the type.
    /// The query owns the metadata source and resolves references through the group's binding
    /// policy; nothing here opens a source or reads an image.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryTypeProjection(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId)
    {
        (BrowserInspectionScope scope, BrowserWorkspaceParticipant participant) =
            await SurfaceParticipantAsync(packageId, version, targetFramework, assemblyName);

        ResearchViews.TypeProjectionResult projection = BrowserSurfaceProjection.Require(
            scope.UseSurfaceParticipant(
                participant,
                (group, member) => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextTypeProjectionRequest(typeId))),
            $"Type projection for '{typeId}'");

        return JsonSerializer.Serialize(
            new BrowserTypeMetadata(
                projection.Identity.FullName,
                projection.Identity.Namespace,
                projection.Identity.Name,
                projection.Identity.Kind,
                [.. projection.Identity.Modifiers],
                projection.Identity.Accessibility,
                projection.Identity.Assembly,
                projection.BaseType,
                [.. projection.Interfaces],
                [.. projection.DerivedTypes],
                [
                    .. projection.TypeParameters.Select(parameter => new BrowserTypeParameter(
                        parameter.Name,
                        parameter.Variance,
                        [.. parameter.Constraints])),
                ],
                [.. projection.Attributes],
                projection.EnumUnderlyingType,
                projection.Composition is { } composition
                    ? new BrowserTypeComposition(
                        composition.Methods,
                        composition.Properties,
                        composition.Fields,
                        composition.Events,
                        composition.Constructors,
                        composition.Operators,
                        composition.ExplicitInterfaceImplementations,
                        composition.ExtensionMethods,
                        composition.Static,
                        composition.Unsafe,
                        composition.Async,
                        composition.Virtual,
                        composition.Abstract,
                        composition.Override,
                        composition.Extension,
                        composition.Obsolete,
                        composition.Total)
                    : null,
                [
                    .. projection.Graph?.Nodes.Select(node => new BrowserTypeGraphNode(
                        node.Id,
                        node.DisplayName,
                        node.Role.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.Graph?.Edges.Select(edge => new BrowserTypeGraphEdge(
                        edge.FromId,
                        edge.ToId,
                        edge.Kind.ToString().ToLowerInvariant())) ?? [],
                ],
                [
                    .. projection.InspectionFailures.Select(
                        failure => $"{failure.Operation}: {failure.Detail}"),
                ]),
            BrowserJsonContext.Default.BrowserTypeMetadata);
    }

    /// <summary>
    /// One member's portable <c>AnnotatedSourceDocument</c>, produced by
    /// <see cref="AssemblyContextMemberProjectionQuery"/> over the participant that owns the
    /// member's implementation. The document is serialized by its owning product context, so the
    /// payload is the same artifact the CLI emits and the viewer validates.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberAnnotatedSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        _ = memberSignature;
        (
            BrowserInspectionScope scope,
            _,
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution resolution
        ) = await ImplementationMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);

        AssemblyMemberProjection projection = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextMemberProjectionRequest(
                        typeQueryId,
                        memberName,
                        MethodToken: resolution.BodyToken,
                        SourceDocument: true,
                        InvocationDestinations: true,
                        PrinterOptions: BrowserStyleOptions.Resolve(styleOptionsJson)))),
            $"Annotated source for '{typeQueryId}.{memberName}'");

        if (projection.Projection.SourceDocument is not { } document)
        {
            IReadOnlyList<DecompilerDiagnostic> diagnostics =
                projection.Projection.SourceDocumentFailure?.Diagnostics ?? [];
            throw new InvalidOperationException(
                diagnostics.Count > 0
                    ? $"Annotated source projection failed: "
                        + string.Join("; ", diagnostics.Select(item => item.ToString()))
                    : "Annotated source projection produced no document.");
        }

        BrowserAnnotatedSourceInvocationDestination[]? destinations =
            projection.ContextLimitation is null
                ?
                [
                    .. projection.InvocationDestinations.Select(destination =>
                        new BrowserAnnotatedSourceInvocationDestination(
                            destination.NodeId,
                            Target(
                                destination.Target,
                                [participant.Assembly.Identity],
                                null,
                                scope.SurfaceParticipants))),
                ]
                : null;

        return JsonSerializer.Serialize(
            BrowserAnnotatedSource.Create(
                document,
                $"Annotated by dotnet-inspect from {participant.Coordinate.PackageId} "
                    + $"{participant.Coordinate.Version} {participant.Asset.Path}",
                projection.ContextLimitation is { } limitation
                    ? $"{limitation.Kind}: {limitation.Detail}"
                    : null,
                destinations,
                destinations is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected),
            BrowserJsonContext.Default.BrowserAnnotatedSource);
    }

    /// <summary>
    /// Exact method-body Analysis and metadata evidence for one implementation
    /// participant. The product query owns the retained snapshot and Analysis
    /// index; this adapter only resolves ref/lib identity and formats the wire
    /// model.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberFacts(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        bool implementationBodySelected)
    {
        _ = memberSignature;
        (
            BrowserInspectionScope scope,
            _,
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution resolution
        ) = await ImplementationMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            implementationBodySelected ? metadataToken : 0);

        AssemblyMethodAnalysis analysis = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) =>
                    AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                        group,
                        member,
                        resolution.BodyToken)),
            $"Facts for '{typeIdentity}.{memberName}'");

        var result = new BrowserMemberFacts(
            analysis.Method.MetadataToken,
            new BrowserMethodSignals(
                analysis.Signals.Allocations,
                analysis.Signals.Copies,
                analysis.Signals.Unsafe,
                analysis.Signals.Reflection,
                analysis.Signals.Throws,
                analysis.Signals.Catches,
                analysis.Signals.Finallys,
                analysis.Signals.AllocInLoop,
                [.. analysis.Signals.Evidence.Select(FormatOffset)],
                [.. analysis.Signals.ExceptionTypes]),
            [
                .. analysis.Allocations.Select(
                    allocation => new BrowserAllocationFact(
                        allocation.Kind.ToString(),
                        allocation.AllocatedType?.ToDisplayString()
                            ?? allocation.RuntimeAllocationType,
                        FormatOffset(allocation.ILOffset),
                        allocation.CountsAsHeapAllocation,
                        allocation.Frequency.ToString(),
                        allocation.Multiplicity.ToString(),
                        allocation.PathContext.ToString(),
                        allocation.EscapeKind
                            != Analysis.AllocationEscapeKind.None
                                ? allocation.EscapeKind.ToString()
                                : allocation.Escape.ToString(),
                        allocation.InLoop,
                        allocation.EstimatedSizeBytes,
                        allocation.Detail)),
            ],
            [
                .. analysis.DirectCalls.Select(
                    call =>
                    {
                        string typeArguments =
                            call.Callee.TypeArguments.Length == 0
                                ? ""
                                : $"<{string.Join(
                                    ", ",
                                    call.Callee.TypeArguments.Select(
                                        argument =>
                                            argument
                                                .ToQualifiedDisplayString()))}>";
                        return new BrowserCallFact(
                            $"{call.Callee.DeclaringType.ToQualifiedDisplayString()}."
                            + $"{call.Callee.Name}{typeArguments}("
                            + string.Join(
                                ", ",
                                call.Callee.ParameterTypes.Select(
                                    parameter =>
                                        parameter
                                            .ToQualifiedDisplayString()))
                            + ")",
                            FormatOffset(call.ILOffset),
                            string.IsNullOrEmpty(call.Opcode)
                                ? FormatCallKind(call.Kind)
                                : call.Opcode,
                            call.Kind.ToString(),
                            call.Multiplicity.ToString(),
                            call.InLoop);
                    }),
            ],
            [
                .. Analysis.SemanticFactProjection.SafetyFacts(
                    analysis.UnsafeEvidence,
                    analysis.UnsafetyOccurrences)
                    .Select(
                        fact => new BrowserSafetyFact(
                            fact.SafetyKind,
                            fact.ILOffset is int offset
                                ? FormatOffset(offset)
                                : null,
                            fact.Operation,
                            fact.Requirement,
                            fact.Evidence)),
            ],
            [
                .. analysis.ExceptionRegions.Select(
                    region => new BrowserExceptionRegion(
                        region.Region,
                        region.Clause,
                        FormatRange(region.TryStart, region.TryEnd),
                        FormatRange(
                            region.HandlerStart,
                            region.HandlerEnd),
                        region.FilterStart is int filterStart
                            && region.FilterEnd is int filterEnd
                                ? FormatRange(filterStart, filterEnd)
                                : null,
                        region.CaughtType)),
            ],
            [
                .. analysis.OptimizationOpportunities.Select(
                    opportunity =>
                        new BrowserPerformanceOpportunity(
                            opportunity.Shape,
                            opportunity.Evidence,
                            opportunity.SafeFixDirection,
                            opportunity.Confidence,
                            opportunity.ILOffset is int offset
                                ? FormatOffset(offset)
                                : null,
                            opportunity.InLoop,
                            opportunity.Caveat,
                            opportunity.SourceFinding,
                            opportunity.Provenance.ToString()
                                .ToLowerInvariant())),
            ],
            [
                .. analysis.Diagnostics.Select(
                    diagnostic =>
                        $"{diagnostic.Method}: {diagnostic.Message}"),
            ]);

        return JsonSerializer.Serialize(
            result,
            BrowserJsonContext.Default.BrowserMemberFacts);
    }

    static string FormatOffset(int offset) => $"IL_{offset:X4}";

    static string FormatRange(int start, int end) =>
        $"{FormatOffset(start)}..{FormatOffset(end)}";

    static string FormatCallKind(Analysis.CallKind kind) =>
        kind switch
        {
            Analysis.CallKind.Call => "call",
            Analysis.CallKind.CallVirtual => "callvirt",
            Analysis.CallKind.NewObject => "newobj",
            Analysis.CallKind.LoadFunction => "ldftn",
            Analysis.CallKind.LoadVirtualFunction => "ldvirtftn",
            Analysis.CallKind.CallIndirect => "calli",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown direct-call kind."),
        };

    /// <summary>
    /// Declared NuGet dependency groups plus the selected compile assembly's direct references.
    /// Package parsing and exact-framework selection belong to
    /// <see cref="PackageDependencyGroupsQuery"/>; the assembly-context query owns the metadata
    /// session. This method only adapts their typed results for the browser.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageDependencies(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            CompileLibrary(coordinate.Selection);

        PackageDependencyGroupsResult dependencyResult =
            await PackageDependencyGroupsQuery.ExecuteAsync(
        coordinate.Package.Content,
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework);
        PackageDependencyGroups dependencies = dependencyResult switch
        {
            PackageDependencyGroupsResult.Available available => available.Value,
            PackageDependencyGroupsResult.NoManifest =>
                throw new InvalidDataException(
                    "The package contains no root manifest."),
            PackageDependencyGroupsResult.Failed failed =>
                throw new InvalidOperationException(
                    failed.Error.Message,
                    failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown package dependency-group query result."),
        };

        string? assembly = null;
        BrowserAssemblyReference[] assemblyReferences = [];
        string? assemblyReferenceError = null;
        if (coordinate.Selection.IsSelected)
        {
            PackageCompileAsset asset = coordinate.CompileAsset(assemblyId);
            assembly = asset.AssemblyName;
            BrowserWorkspaceParticipant participant =
                scope.SurfaceParticipant(coordinate, asset);
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> referenceResult =
                scope.UseSurfaceParticipant(
                    participant,
                    AssemblyContextReferencesQuery.ExecuteParticipant);

            switch (referenceResult)
            {
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Available available:
                    assemblyReferences =
                    [
                        .. available.Value
                            .Select(reference => reference.ToReference())
                            .OrderBy(
                                reference => reference.Name,
                                StringComparer.OrdinalIgnoreCase)
                            .ThenBy(
                                reference => reference.Version,
                                StringComparer.Ordinal)
                            .ThenBy(
                                reference => reference.Culture,
                                StringComparer.OrdinalIgnoreCase)
                            .ThenBy(
                                reference => reference.PublicKeyToken,
                                StringComparer.OrdinalIgnoreCase)
                            .Select(reference => new BrowserAssemblyReference(
                                reference.Name,
                                reference.Version,
                                reference.Culture,
                                reference.PublicKeyToken)),
                    ];
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected:
                    assemblyReferenceError =
                        $"{rejected.Failure.Kind} ({rejected.Failure.Detail})";
                    break;
                case AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceIdentity>>.Failed failed:
                    assemblyReferenceError = failed.Error.Message;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly-context reference query result.");
            }
        }
        else
        {
            assemblyReferenceError = compileLibrary.Message;
        }

        string? dependencyGroupError =
            dependencies.SelectionStatus
                == PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework
                    ? "The manifest declares no dependency group for the active target framework."
                    : null;
        return JsonSerializer.Serialize(
            new BrowserPackageDependencies(
                coordinate.PackageId,
                coordinate.Version,
                BrowserFramework(coordinate),
                assembly,
                [
                    .. dependencies.Groups.Select((group, index) =>
                        new BrowserPackageDependencyGroup(
                            index,
                            BrowserDependencyGroupFramework(group.TargetFramework),
                            index == dependencies.SelectedGroupIndex,
                            [
                                .. group.Dependencies.Select(dependency =>
                                    new BrowserPackageDependency(
                                        dependency.Id,
                                        dependency.VersionRange)),
                            ])),
                ],
                assemblyReferences,
                dependencyGroupError,
                assemblyReferenceError,
                compileLibrary),
            BrowserJsonContext.Default.BrowserPackageDependencies);
    }

    /// <summary>
    /// Ecosystem integration evidence for one package/version/framework workspace, produced by
    /// <see cref="PackageWorkspaceIntegrationsQuery"/> over the product-selected package roles.
    /// Its group query owns every session; this method groups signals for display and composes no
    /// evidence. Role selection is gated by
    /// <c>PackageWorkspaceIntegrationsQuery_UsesImplementationRoleAndReferenceFallback</c> and
    /// <c>PackageWorkspaceIntegrationsQuery_SharedRoleDoesNotDuplicateLibraries</c>.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageIntegrations(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            CompileLibrary(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return JsonSerializer.Serialize(
                new BrowserPackageIntegrations(
                    coordinate.PackageId,
                    coordinate.Version,
                    BrowserFramework(coordinate),
                    Categories: [],
                    TotalSignals: 0,
                    IsComplete: false,
                    InspectionError: null,
                    compileLibrary),
                BrowserJsonContext.Default.BrowserPackageIntegrations);
        }

        PackageWorkspaceIntegrationsResult result =
            scope.QueryIntegrations();

        return JsonSerializer.Serialize(
            CreateIntegrations(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                result.Libraries.Select(entry => entry.Integrations),
                compileLibrary),
            BrowserJsonContext.Default.BrowserPackageIntegrations);
    }

    /// <summary>
    /// Missing ecosystem integration opportunities for one package/version/framework workspace.
    /// The product query composes them from its typed Integrations prerequisite; the browser only
    /// groups and deduplicates the returned evidence for presentation.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageOpportunities(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            CompileLibrary(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return JsonSerializer.Serialize(
                new BrowserPackageOpportunities(
                    coordinate.PackageId,
                    coordinate.Version,
                    BrowserFramework(coordinate),
                    Categories: [],
                    TotalOpportunities: 0,
                    IsComplete: false,
                    InspectionError: null,
                    compileLibrary),
                BrowserJsonContext.Default.BrowserPackageOpportunities);
        }

        var registry = new InspectionQueryRegistry<AssemblyContextGroup>()
            .Add(
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationsQuery.Execute)
            .Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Execute,
                AssemblyContextIntegrationsQuery.Definition);
        AssemblyContextIntegrationOpportunitiesResult result =
            scope.UseSurface(group =>
                registry.Run(
                        [AssemblyContextIntegrationOpportunitiesQuery.Definition],
                        group)
                    .Get(AssemblyContextIntegrationOpportunitiesQuery.Definition));

        return JsonSerializer.Serialize(
            CreateOpportunities(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                result.Assemblies,
                compileLibrary),
            BrowserJsonContext.Default.BrowserPackageOpportunities);
    }

    internal static BrowserPackageIntegrations CreateIntegrations(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationsEntry> entries,
        BrowserCompileLibraryAvailability? compileLibrary = null)
    {
        AssemblyIntegrationsEntry[] materialized = [.. entries];
        var failures = new List<string>();
        var signals = new List<EcosystemIntegrationSignalInfo>();
        foreach (AssemblyIntegrationsEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationsEntry.Available available:
                    signals.AddRange(available.EcosystemSignals);
                    break;
                case AssemblyIntegrationsEntry.Rejected rejected:
                    failures.Add(
                        BrowserSurfaceProjection.RejectedAssembly(
                            rejected.Failure));
                    break;
                case AssemblyIntegrationsEntry.Failed failed:
                    failures.Add(
                        BrowserSurfaceProjection.FailedAssembly(failed.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown assembly integrations entry '{entry.GetType().Name}'.");
            }
        }

        return new BrowserPackageIntegrations(
                package,
                version,
                RequiredBrowserFramework(framework),
                [
                    .. signals
                        .GroupBy(
                            signal => signal.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserIntegrationCategory(
                            group.Key,
                            [
                                .. group
                                    .Select(signal =>
                                        new BrowserIntegrationSignal(
                                            signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .DistinctBy(signal =>
                                        (signal.Kind,
                                            signal.Name,
                                            signal.Shape))
                                    .OrderBy(
                                        signal => signal.Name,
                                        StringComparer.OrdinalIgnoreCase),
                            ])),
                ],
                signals.Count,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationsEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures),
                compileLibrary ?? SelectedCompileLibrary(framework));
    }

    internal static BrowserPackageOpportunities CreateOpportunities(
        string package,
        string version,
        string framework,
        IEnumerable<AssemblyIntegrationOpportunitiesEntry> entries,
        BrowserCompileLibraryAvailability? compileLibrary = null)
    {
        AssemblyIntegrationOpportunitiesEntry[] materialized =
            [.. entries];
        var failures = new List<string>();
        var opportunities =
            new List<(
                AssemblyReferenceIdentity Source,
                IntegrationOpportunityInfo Opportunity)>();
        foreach (AssemblyIntegrationOpportunitiesEntry entry in materialized)
        {
            switch (entry)
            {
                case AssemblyIntegrationOpportunitiesEntry.Available available:
                    opportunities.AddRange(
                        available.Opportunities.Select(
                            opportunity =>
                                (available.Subject.Identity, opportunity)));
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Rejected rejected:
                    failures.Add(
                        BrowserSurfaceProjection.RejectedAssembly(
                            rejected.Failure));
                    break;
                case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                    failures.Add(
                        BrowserSurfaceProjection.FailedAssembly(failed.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly integration-opportunities entry "
                        + $"'{entry.GetType().Name}'.");
            }
        }

        (AssemblyReferenceIdentity Source, IntegrationOpportunityInfo Opportunity)[]
            distinctOpportunities =
        [
            .. opportunities.DistinctBy(item =>
                (item.Source,
                    item.Opportunity.Integration,
                    item.Opportunity.Api,
                    item.Opportunity.IntegrationType)),
        ];
        return new BrowserPackageOpportunities(
                package,
                version,
                RequiredBrowserFramework(framework),
                [
                    .. distinctOpportunities
                        .GroupBy(
                            item => item.Opportunity.Integration,
                            StringComparer.Ordinal)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new BrowserOpportunityCategory(
                            group.Key,
                            [
                                .. group
                                    .OrderBy(
                                        item => item.Opportunity.Api,
                                        StringComparer.OrdinalIgnoreCase)
                                    .Select(item =>
                                        new BrowserOpportunityItem(
                                            item.Opportunity.Api,
                                            item.Opportunity.IntegrationType,
                                            item.Opportunity.LookFor,
                                            item.Opportunity
                                                .GetSourceTypeDefinition()?
                                                .ToEscapedFullName(),
                                            item.Source.Name,
                                            item.Source.Version?.ToString()
                                                ?? "",
                                            item.Source.Culture,
                                            item.Source.PublicKeyToken)),
                            ])),
                ],
                distinctOpportunities.Length,
                materialized.All(
                    entry => entry
                        is AssemblyIntegrationOpportunitiesEntry.Available),
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures),
                compileLibrary ?? SelectedCompileLibrary(framework));
    }

    /// <summary>
    /// Product-ranked optimization-opportunity members for one package workspace. Analysis owns
    /// opportunity and member order; the query owns index lifetime and public-API attribution;
    /// this host only maps the typed rows to the existing browser wire contract.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackagePerformance(
        string packageId,
        string version,
        string targetFramework)
    {
        BrowserInspectionScope scope =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            CompileLibrary(coordinate.Selection);
        if (!coordinate.Selection.IsSelected)
        {
            return JsonSerializer.Serialize(
                new BrowserPackagePerformance(
                    Members: [],
                    InspectionError: null,
                    NonPublicOpportunities: 0,
                    TotalOpportunities: 0,
                    compileLibrary),
                BrowserJsonContext.Default.BrowserPackagePerformance);
        }

        ImmutableArray<BrowserWorkspaceParticipant> participants =
            scope.ImplementationParticipants.Length > 0
                ? scope.ImplementationParticipants
                : scope.SurfaceParticipants;

        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextOptimizationOpportunitiesQuery.Definition,
                    AssemblyContextOptimizationOpportunitiesQuery.Execute);
        AssemblyContextOptimizationOpportunitiesResult result =
            scope.UseImplementationOrSurface(
                group =>
                    registry.Run(
                            [
                                AssemblyContextOptimizationOpportunitiesQuery
                                    .Definition,
                            ],
                            group)
                        .Get(
                            AssemblyContextOptimizationOpportunitiesQuery
                                .Definition));
        BrowserPackageSurface surface =
            ProjectPackageSurface(scope, coordinate);
        HashSet<(
            string Assembly,
            string Type,
            string Selector)> navigableMembers =
        [
            .. surface.Types.SelectMany(type =>
                type.Api.Select(member => (
                    type.Assembly,
                    type.DefinitionId,
                    member.StableSelector))),
        ];

        var failures = new List<string>();
        if (!string.IsNullOrWhiteSpace(surface.InspectionError))
            failures.Add($"API surface: {surface.InspectionError}");
        foreach (AssemblyContextEntry<
            AssemblyOptimizationOpportunityRanking> entry
            in result.Assemblies.Assemblies)
        {
            switch (entry)
            {
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Rejected
                    rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: "
                        + $"{rejected.Failure.Kind} "
                        + $"({rejected.Failure.Detail})");
                    break;
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: "
                        + failed.Error.Message);
                    break;
                case AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Available
                    available:
                    failures.AddRange(
                        available.Value.Diagnostics.Select(
                            diagnostic =>
                                $"{available.Subject.Identity.Name}: "
                                + $"performance analysis incomplete for "
                                + $"{diagnostic.Method}: "
                                + diagnostic.Message));
                    failures.AddRange(
                        available.Value.ApiSurfaceInspectionFailures
                            .Select(
                                failure =>
                                    $"{available.Subject.Identity.Name}: "
                                    + $"{failure.Operation}: "
                                    + failure.Detail));
                    break;
            }
        }

        BrowserPerformanceMember[] members =
            ApplyPerformanceMemberLimit(
                PerformanceMembers(
                    result,
                    participants,
                    scope,
                    navigableMembers),
                failures);

        return JsonSerializer.Serialize(
            new BrowserPackagePerformance(
                members,
                failures.Count == 0
                    ? null
                    : string.Join("; ", failures),
                result.NonPublicOpportunities,
                result.TotalOpportunities,
                compileLibrary),
            BrowserJsonContext.Default.BrowserPackagePerformance);
    }

    static IEnumerable<BrowserPerformanceMember> PerformanceMembers(
        AssemblyContextOptimizationOpportunitiesResult result,
        ImmutableArray<BrowserWorkspaceParticipant> participants,
        BrowserInspectionScope scope,
        HashSet<(
            string Assembly,
            string Type,
            string Selector)> navigableMembers)
    {
        foreach (AssemblyContextOptimizationOpportunityMember member
            in result.RankedMembers)
        {
            if (member.Member.PublicMember is not { } publicMember)
                continue;

            BrowserWorkspaceParticipant analysisParticipant =
                participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        member.Subject.Registration));
            BrowserWorkspaceParticipant? surfaceParticipant =
                scope.TryGetSurfaceParticipant(analysisParticipant);
            if (surfaceParticipant is null
                || !navigableMembers.Contains((
                    surfaceParticipant.Asset.AssemblyName,
                    publicMember.Type,
                    publicMember.StableSelector)))
            {
                continue;
            }

            yield return new BrowserPerformanceMember(
                surfaceParticipant.Asset.AssemblyName,
                publicMember.Type,
                publicMember.Member,
                publicMember.StableSelector,
                [.. publicMember.BodyTokens],
                member.Member.Ranking.Opportunities.Length,
                member.Member.Ranking.InLoopCount,
                [.. member.Member.Ranking.Shapes],
                member.Member.Ranking.Confidence);
        }
    }

    internal static BrowserPerformanceMember[] ApplyPerformanceMemberLimit(
        IEnumerable<BrowserPerformanceMember> candidates,
        ICollection<string> failures)
    {
        const int MemberLimit = 200;
        var members = new List<BrowserPerformanceMember>(MemberLimit);
        foreach (BrowserPerformanceMember candidate in candidates)
        {
            if (members.Count == MemberLimit)
            {
                failures.Add(
                    $"Performance ranking truncated after the top "
                    + $"{MemberLimit} navigable public members.");
                break;
            }

            members.Add(candidate);
        }

        return [.. members];
    }

    /// <summary>
    /// A progressively acquired member call graph, produced by <see cref="MemberCallGraphSession"/>
    /// over one workspace spanning every package the site currently has open. Callers in a sibling
    /// package are only visible when that package is a participant of the same binding-consistent
    /// group, so the workspace is opened over the whole set rather than one package at a time.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberCallGraph(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string workspaceJson)
    {
        _ = memberSignature;
        _ = typeQueryId;

        (BrowserPackageRequest[] requests, int rootIndex) =
            MemberCallGraphRequests(
                packageId,
                version,
                targetFramework,
                workspaceJson);

        BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(requests);
        if (resolution.RequestedCoordinates.Length != requests.Length)
        {
            throw new InvalidOperationException(
                "The selected Call Graph context did not preserve its "
                + "distinct package coordinates.");
        }
        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageCoordinate rootCoordinate =
            scope.Coordinate(resolution.RequestedCoordinates[rootIndex]);
        (
            _,
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution memberResolution
        ) = ResolveImplementationMember(
            scope,
            rootCoordinate,
            assemblyName,
            typeIdentity,
            memberName,
            selectorKey,
            metadataToken);

        MemberCallGraphView view = scope.UseImplementation(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                participant.Assembly,
                memberResolution.BodyToken);
            return session.HasCrossLibraryScope ? session.CrossLibrary() : session.Callers();
        });

        BrowserCallGraph graph =
            ProjectCallGraph(scope, view);
        return JsonSerializer.Serialize(
            graph,
            BrowserJsonContext.Default.BrowserCallGraph);
    }

    internal static (BrowserPackageRequest[] Requests, int RootIndex)
        MemberCallGraphRequests(
            string packageId,
            string version,
            string targetFramework,
            string workspaceJson)
    {
        BrowserWorkspacePackage[] workspace =
            JsonSerializer.Deserialize(
                workspaceJson,
                BrowserJsonContext.Default.BrowserWorkspacePackageArray) ?? [];
        if (workspace.Length == 0)
        {
            return (
                [new BrowserPackageRequest(packageId, version, targetFramework)],
                0);
        }

        BrowserPackageRequest[] requests =
        [
            .. workspace.Select(entry => new BrowserPackageRequest(
                entry.Package,
                entry.Version,
                string.IsNullOrWhiteSpace(entry.Framework)
                    ? null
                    : entry.Framework)),
        ];
        int[] rootIndexes =
        [
            .. requests.Select((request, index) => (request, index))
                .Where(entry =>
                    string.Equals(
                        entry.request.PackageId,
                        packageId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        entry.request.Version,
                        version,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        entry.request.TargetFramework ?? "",
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index),
        ];
        if (rootIndexes.Length != 1)
        {
            throw new InvalidOperationException(
                "The selected Call Graph context must contain the active "
                + "package coordinate exactly once.");
        }
        return (requests, rootIndexes[0]);
    }

    internal static BrowserCallGraph ProjectCallGraph(
        BrowserInspectionScope scope,
        MemberCallGraphView view)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(view);
        CallGraphProjection projection = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        int callerAssemblies = scope.ImplementationParticipants
            .Select(candidate => candidate.Assembly.Identity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new BrowserCallGraph(
            Mermaid(projection),
            Tree(view.CallerRoot),
            Tree(view.CalleeRoot),
            new BrowserCallGraphScope(
                scope.Coordinates.Length,
                scope.ImplementationParticipants.Length,
                callerAssemblies,
                view.Tier.ToString()),
            Targets(
                projection.Nodes,
                scope.ImplementationParticipants.Select(
                    participant => participant.Assembly.Identity),
                surfaceParticipants: scope.SurfaceParticipants),
            Diagnostics(
                view.Diagnostics,
                projection.HasUnexploredTraversalBoundary,
                projection.HasAnalysisFailureBoundary),
            NoBody: view.CalleeRoot is null && view.CallerRoot is null);
    }

    /// <summary>
    /// The exact API member selected by a call-graph target. Package surfaces keep public types
    /// lean by omitting their non-public members; a graph click is the explicit gesture that
    /// projects one such member from the already bounded implementation surface.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryGraphMemberSurface(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int metadataToken)
    {
        (_,
            BrowserWorkspaceParticipant surfaceParticipant,
            _,
            Analysis.CallGraphMemberResolution resolution) =
            await ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        var textBudget = new BrowserSurfaceProjection.BrowserSurfaceTextBudget(
            BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
        textBudget.BeginParticipant();
        BrowserTypeSurface type =
            BrowserSurfaceProjection.Type(
                resolution.Type,
                surfaceParticipant.Asset.AssemblyName,
                surfaceParticipant.Asset.Id,
                surfaceParticipant.Assembly.Identity.Name,
                textBudget,
                qualifyId: true,
                selectedMembers: [resolution.Member]);
        BrowserMemberSurface member = type.Api.Single();
        BrowserMemberBodySelector selectedBody =
            member.BodySelectors.SingleOrDefault(
                body => body.Token == resolution.BodyToken)
            ?? throw new InvalidOperationException(
                $"The projected member '{member.Name}' does not retain "
                + $"body 0x{resolution.BodyToken:X8}.");
        textBudget.CommitParticipant();
        return JsonSerializer.Serialize(
            new BrowserGraphMemberSurface(type, selectedBody),
            BrowserJsonContext.Default.BrowserGraphMemberSurface);
    }

    /// <summary>
    /// The UTF-8 text of one package-shipped Markdown document, identified by its exact package
    /// entry path. Only paths the package's own document manifest lists are served. This reads
    /// package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> GetPackageDocument(string packageId, string version, string path)
    {
        BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(packageId, version);
        BrowserPackageDocumentContent document =
            BrowserPackageWireProjection.Project(package.ReadDocument(path));
        return JsonSerializer.Serialize(
            document,
            BrowserJsonContext.Default.BrowserPackageDocumentContent);
    }

    /// <summary>
    /// One member's entry from the XML documentation shipped beside the product-selected compile
    /// asset. This reads package content and inspects no assembly, so it opens no group.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberDocumentation(
        string packageId,
        string version,
        string framework,
        string assemblyName,
        string documentationId)
    {
        BrowserPackageCoordinate coordinate = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            version,
            framework);
        PackageCompileAsset asset = coordinate.CompileAsset(assemblyName);
        BrowserMemberDocumentation documentation = coordinate.Package.TryReadText(
            Path.ChangeExtension(asset.Path, ".xml"),
            out byte[] xml)
                ? BrowserXmlDocumentation.Read(xml, documentationId)
                : BrowserXmlDocumentation.Empty;
        return JsonSerializer.Serialize(
            documentation,
            BrowserJsonContext.Default.BrowserMemberDocumentation);
    }

    /// <summary>
    /// Ranks loaded type candidates against an incremental query through the product's
    /// <see cref="TypeMatcher"/>: exact and namespace-suffix matches, then prefix and substring
    /// globs, then a Levenshtein "did you mean" fallback. This inspects no artifact — the
    /// candidates are names the client already holds — so it opens no workspace.
    /// </summary>
    [JSExport]
    public static string SearchTypes(string query, string candidatesJson)
    {
        BrowserTypeCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserJsonContext.Default.BrowserTypeCandidateArray) ?? [];
        query = query?.Trim() ?? "";

        if (query.Length == 0)
        {
            return JsonSerializer.Serialize(
                candidates
                    .OrderBy(candidate => candidate.Name.Length)
                    .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .Select(candidate => new BrowserTypeSearchHit(candidate.Key, "all"))
                    .ToArray(),
                BrowserJsonContext.Default.BrowserTypeSearchHitArray);
        }

        var hits = new List<BrowserTypeSearchHit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(string kind, Func<BrowserTypeCandidate, bool> predicate)
        {
            foreach (BrowserTypeCandidate candidate in candidates
                .Where(candidate => !used.Contains(candidate.Key) && predicate(candidate))
                .OrderBy(candidate => candidate.Name.Length)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (used.Add(candidate.Key))
                    hits.Add(new BrowserTypeSearchHit(candidate.Key, kind));
            }
        }

        AddTier("exact", candidate => TypeMatcher.Matches(candidate.Full, query));
        AddTier("prefix", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, query + "*"));
        AddTier("substring", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, "*" + query + "*"));
        AddTier("path", candidate => TypeMatcher.MatchesTypeFilter(candidate.Full, "*" + query + "*"));

        var remaining = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (BrowserTypeCandidate candidate in candidates.Where(candidate => !used.Contains(candidate.Key)))
        {
            if (!remaining.TryGetValue(candidate.Full, out List<string>? keys))
                remaining[candidate.Full] = keys = [];
            keys.Add(candidate.Key);
        }

        if (remaining.Count > 0)
        {
            foreach ((string name, _) in TypeMatcher.FindClosest(
                remaining.Keys,
                query,
                minSimilarity: 0.5,
                maxResults: 8))
            {
                if (!remaining.TryGetValue(name, out List<string>? keys))
                    continue;
                foreach (string key in keys)
                {
                    if (used.Add(key))
                        hits.Add(new BrowserTypeSearchHit(key, "fuzzy"));
                }
            }
        }

        return JsonSerializer.Serialize(
            hits.Take(40).ToArray(),
            BrowserJsonContext.Default.BrowserTypeSearchHitArray);
    }

    /// <summary>Session acquisition statistics. Inspects no artifact and opens no workspace.</summary>
    [JSExport]
    public static string PackageCacheStats()
    {
        BrowserPackageCacheStats stats =
            BrowserPackageWireProjection.Project(BrowserPackageWorkspace.Stats());
        return JsonSerializer.Serialize(
            stats,
            BrowserJsonContext.Default.BrowserPackageCacheStats);
    }

    /// <summary>Version, source revision, and build time embedded in this browser engine.</summary>
    [JSExport]
    public static string BuildIdentity() => JsonSerializer.Serialize(
        BrowserBuildIdentityReader.Read(typeof(InspectionEngine).Assembly),
        BrowserJsonContext.Default.BrowserBuildIdentity);

    /// <summary>
    /// Published package versions from the browser acquisition owner's bounded version-index
    /// reader. The JavaScript host does not fetch or parse the untrusted index independently.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageVersions(string packageId) =>
        JsonSerializer.Serialize(
            await BrowserPackageWorkspace.GetVersionsAsync(packageId),
            BrowserJsonContext.Default.StringArray);

    [JSExport]
    public static Task<string> ResolvePackageDependencyVersion(
        string packageId,
        string? declaredRange) =>
        BrowserPackageWorkspace.ResolveDependencyVersionAsync(
            packageId,
            declaredRange);

    [JSExport]
    public static string MatchPackageDependencyCoordinate(
        string packageId,
        string? declaredRange,
        string candidatesJson)
    {
        BrowserDependencyCoordinateCandidate[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            BrowserJsonContext.Default.BrowserDependencyCoordinateCandidateArray) ?? [];
        PackageDependencyCoordinateMatch result = PackageDependencyCoordinateMatchQuery.Execute(
            candidates.Select(candidate => new PackageDependencyCoordinateCandidate(
                candidate.Key,
                candidate.Provenance switch
                {
                    BrowserDependencyCoordinateProvenance.NuGetPackage =>
                        PackageDependencyCoordinateKind.NuGetPackage,
                    BrowserDependencyCoordinateProvenance.PlatformRuntime =>
                        PackageDependencyCoordinateKind.PlatformRuntime,
                    _ => throw new InvalidOperationException(
                        "The dependency-coordinate provenance is invalid."),
                },
                candidate.PackageId,
                candidate.Version,
                candidate.TargetFramework)),
            packageId,
            declaredRange);
        var browserResult = new BrowserDependencyCoordinateMatch(
            result.Status switch
            {
                PackageDependencyCoordinateMatchStatus.NoMatch =>
                    BrowserDependencyCoordinateMatchOutcome.NoMatch,
                PackageDependencyCoordinateMatchStatus.Unique =>
                    BrowserDependencyCoordinateMatchOutcome.Unique,
                PackageDependencyCoordinateMatchStatus.Ambiguous =>
                    BrowserDependencyCoordinateMatchOutcome.Ambiguous,
                _ => throw new InvalidOperationException(
                    "The dependency-coordinate match outcome is invalid."),
            },
            result.CandidateKey);
        return JsonSerializer.Serialize(
            browserResult,
            BrowserJsonContext.Default.BrowserDependencyCoordinateMatch);
    }

    // Vocabulary is product-owned static data. The browser receives the same section/field/value
    // document as the CLI and retains no separate labels, ordering, defaults, or query semantics.
    [JSExport]
    public static string ListVocabulary() =>
        JsonSerializer.Serialize(
            BrowserVocabulary.ToBrowserDocument(
                DotnetInspector.Vocabulary.VocabularyJson.ToWireDocument(
                    DotnetInspector.Vocabulary.VocabularyCatalog.Document)),
            BrowserJsonContext.Default.BrowserVocabularyDocument);

    // Home demos are product-owned closed presets. Catalog listing is metadata-only; resolve
    // allocates one demo's definition graph. The browser builds share links / runners from the
    // projected coordinates rather than a hand-maintained TypeScript twin.
    [JSExport]
    public static string ListHomeDemos() =>
        JsonSerializer.Serialize(
            BrowserProductHomeDemos.ToCatalog(ProductInspectionDemos.Entries),
            BrowserJsonContext.Default.BrowserHomeDemoCatalog);

    /// <summary>
    /// Resolves one product home demo. <c>found</c> is false when the id is unknown.
    /// </summary>
    [JSExport]
    public static string ResolveHomeDemo(string scenarioId)
    {
        if (!ProductInspectionDemos.TryResolveHomeScenario(scenarioId, out var resolved))
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoResolveResult(false, null),
                BrowserJsonContext.Default.BrowserHomeDemoResolveResult);
        }

        return JsonSerializer.Serialize(
            new BrowserHomeDemoResolveResult(true, BrowserProductHomeDemos.ToResolved(resolved)),
            BrowserJsonContext.Default.BrowserHomeDemoResolveResult);
    }

    /// <summary>
    /// Runs one supported home demo from its product definition.
    /// The browser supplies only the scenario id: workspace coordinates,
    /// navigation focus, section selection, optional member selection, and
    /// query execution remain on the engine side.
    /// </summary>
    [JSExport]
    public static async Task<string> RunHomeDemo(string scenarioId)
    {
        if (!ProductInspectionDemos.TryResolveHomeScenario(scenarioId, out var resolved))
        {
            return JsonSerializer.Serialize(
                new BrowserHomeDemoRunResult(false, [], null, null),
                BrowserJsonContext.Default.BrowserHomeDemoRunResult);
        }

        BrowserHomeDemoRunPlan plan =
            BrowserProductHomeDemos.ToRunPlan(resolved);
        BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    plan.Requests,
                    deadline.Token),
                BrowserPackageWorkspace.PackageOperationTimeout);
        BrowserHomeDemoRunResult result =
            RunHomeDemoCore(plan, resolution);
        return JsonSerializer.Serialize(
            result,
            BrowserJsonContext.Default.BrowserHomeDemoRunResult);
    }

    internal static BrowserHomeDemoRunResult RunHomeDemoCore(
        BrowserHomeDemoRunPlan plan,
        BrowserScopeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolution);

        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageProjection[] projections =
        [
            .. resolution.RequestedCoordinates.Select(requested =>
                ProjectPackage(scope, scope.Coordinate(requested))),
        ];
        if (resolution.RequestedCoordinates.Length != plan.Requests.Length)
        {
            throw new InvalidOperationException(
                "The product home demo workspace did not preserve its distinct request ordering.");
        }
        if ((uint)plan.FocusRequestIndex >= (uint)resolution.RequestedCoordinates.Length)
        {
            throw new InvalidOperationException(
                "The product home demo focus is outside its resolved browser workspace.");
        }

        BrowserPackageCoordinate focusCoordinate =
            scope.Coordinate(
                resolution.RequestedCoordinates[plan.FocusRequestIndex]);
        BrowserPackageProjection focusProjection =
            projections[plan.FocusRequestIndex];
        BrowserPackageSurface focusPackage = focusProjection.Surface;
        BrowserTypeSurface[] types =
        [
            .. focusPackage.Types.Where(type =>
                string.Equals(
                    type.Id,
                    plan.TypeId,
                    StringComparison.Ordinal)),
        ];
        if (types.Length != 1)
        {
            throw new InvalidOperationException(
                $"The product home demo type '{plan.TypeId}' resolved to "
                + $"{types.Length} browser surface rows.");
        }

        BrowserTypeSurface type = types[0];
        BrowserHomeDemoRunMember? memberPlan = plan.Member;
        if (memberPlan is null)
        {
            return new BrowserHomeDemoRunResult(
                true,
                [.. projections.Select(projection => projection.Surface)],
                new BrowserHomeDemoRunActivation(
                    focusCoordinate.PackageId,
                    focusCoordinate.Version,
                    focusCoordinate.Framework,
                    type.Id,
                    plan.Section,
                    MemberName: null,
                    MemberKind: null,
                    MemberAnchorDigest: null,
                    MemberSection: null),
                null);
        }

        (ApiType Type, AssemblyContextSubject Subject)[] apiTypes =
        [
            .. (focusProjection.ApiSurfaces
                ?? throw new InvalidOperationException(
                    "The product home demo focus has no compile-library API surface."))
                .Assemblies.Assemblies
                .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                .SelectMany(entry => entry.Value.Surface.Types
                    .Where(candidate => string.Equals(
                        AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(
                            candidate),
                        type.DefinitionId,
                        StringComparison.Ordinal))
                    .Select(candidate => (candidate, entry.Subject))),
        ];
        if (apiTypes.Length != 1)
        {
            throw new InvalidOperationException(
                $"The product home demo type '{plan.TypeId}' resolved to "
                + $"{apiTypes.Length} product API rows.");
        }

        (ApiType apiType, AssemblyContextSubject subject) = apiTypes[0];
        var selector = new MemberTargetSelector(
            $"{memberPlan.Name}~{memberPlan.AnchorDigest}",
            memberPlan.Name,
            DigestPrefix: memberPlan.AnchorDigest,
            Kind: memberPlan.MemberKind);
        MemberTargetResolution target =
            MemberTargetResolver.Resolve(apiType, selector);
        if (target.Diagnostic is { } diagnostic)
        {
            throw new InvalidOperationException(
                $"The product home demo member could not be selected: {diagnostic.Message}");
        }

        BrowserMemberSurface projectedMember =
            BrowserSurfaceProjection.Member(
                apiType,
                target.Target!.ApiMember.Member);
        BrowserMemberSurface[] transportedMembers =
        [
            .. type.Api.Where(member =>
                string.Equals(
                    member.AnchorDigest,
                    projectedMember.AnchorDigest,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (transportedMembers.Length != 1)
        {
            throw new InvalidOperationException(
                $"The selected product home demo member projected to "
                + $"{transportedMembers.Length} browser surface rows.");
        }

        BrowserMemberSurface member = transportedMembers[0];
        if (!string.Equals(
                type.AssemblyName,
                subject.Identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The product home demo type projection lost its owning assembly identity.");
        }
        (
            _,
            BrowserWorkspaceParticipant participant,
            Analysis.CallGraphMemberResolution memberResolution
        ) = ResolveImplementationMember(
            scope,
            focusCoordinate,
            type.Assembly,
            type.DefinitionId,
            member.Name,
            member.GraphSelectorKey,
            member.MetadataToken ?? 0);
        MemberCallGraphView view = scope.UseImplementation(group =>
        {
            using var session = new MemberCallGraphSession(
                group,
                participant.Assembly,
                memberResolution.BodyToken);
            return session.HasCrossLibraryScope
                ? session.CrossLibrary()
                : session.Callers();
        });

        return new BrowserHomeDemoRunResult(
            true,
            [.. projections.Select(projection => projection.Surface)],
            new BrowserHomeDemoRunActivation(
                focusCoordinate.PackageId,
                focusCoordinate.Version,
                focusCoordinate.Framework,
                type.Id,
                plan.Section,
                member.Name,
                member.Kind,
                member.AnchorDigest,
                memberPlan.MemberSection),
            ProjectCallGraph(scope, view));
    }

    /// <summary>
    /// Resolves one exact package/version/framework coordinate, reuses its workspace, and returns
    /// the reference-preferred participant for one product-selected compile asset.
    /// </summary>
    static async Task<(BrowserInspectionScope Scope, BrowserWorkspaceParticipant Participant)>
        SurfaceParticipantAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];

        return (scope, scope.SurfaceParticipant(
            coordinate,
            coordinate.CompileAsset(assemblyName)));
    }

    /// <summary>
    /// Resolves a reference-surface body selector onto its implementation participant. Metadata
    /// tokens are image-local, so the product resolver validates the token and falls back to the
    /// opaque structural selector when <c>ref/</c> and <c>lib/</c> row numbers differ.
    /// </summary>
    static async Task<(
        BrowserInspectionScope Scope,
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        Analysis.CallGraphMemberResolution Resolution)> ImplementationMemberAsync(
            string packageId,
            string version,
            string targetFramework,
            string assemblyName,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken,
            CancellationToken cancellationToken = default)
    {
        BrowserInspectionScope scope = await BrowserPackageWorkspace.OpenScopeAsync(
            packageId,
            version,
            targetFramework,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        (
            BrowserWorkspaceParticipant surfaceParticipant,
            BrowserWorkspaceParticipant implementationParticipant,
            Analysis.CallGraphMemberResolution resolution
        ) = ResolveImplementationMember(
            scope,
            coordinate,
            assemblyName,
            typeId,
            memberName,
            selectorKey,
            metadataToken);
        return (
            scope,
            surfaceParticipant,
            implementationParticipant,
            resolution);
    }

    static (
        BrowserWorkspaceParticipant SurfaceParticipant,
        BrowserWorkspaceParticipant ImplementationParticipant,
        Analysis.CallGraphMemberResolution Resolution)
        ResolveImplementationMember(
            BrowserInspectionScope scope,
            BrowserPackageCoordinate coordinate,
            string assemblyName,
            string typeId,
            string memberName,
            string selectorKey,
            int metadataToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);
        PackageCompileAsset surfaceAsset = coordinate.CompileAsset(assemblyName);
        BrowserWorkspaceParticipant surfaceParticipant =
            scope.SurfaceParticipant(coordinate, surfaceAsset);
        BrowserWorkspaceParticipant participant =
            scope.ImplementationParticipant(surfaceParticipant);
        // One participant, under the same browser bounds as the package load: a body selector is
        // resolved against the implementation surface, and an over-budget implementation must
        // fail visibly rather than resolve against a silently shortened surface.
        AssemblyContextApiSurfaceResult implementationSurfaces =
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits,
                    [member]));
        if (implementationSurfaces.Truncation is { } truncation)
        {
            throw new InvalidOperationException(
                $"The implementation surface for '{typeId}' exceeds the browser projection "
                + $"bounds, so the selected body cannot be resolved. "
                + BrowserApiSurfacePolicy.TruncationNotice(truncation));
        }

        AssemblyApiSurface implementation = BrowserSurfaceProjection.Require(
            implementationSurfaces.Assemblies.Assemblies.Single(),
            $"Implementation surface for '{typeId}'");
        Analysis.CallGraphMemberResolution resolution =
            Analysis.CallGraphMemberResolver.ResolveDefinitionIdentity(
                implementation.Surface,
                typeId,
                memberName,
                selectorKey,
                metadataToken == 0 ? null : metadataToken)
            ?? throw new InvalidOperationException(
                $"The implementation of '{typeId}.{memberName}' does not contain the selected "
                + "API body.");
        return (surfaceParticipant, participant, resolution);
    }

    // Presentation over the neutral projection. docs/design/call-graph-projection.md makes
    // rendering host-owned on purpose: the projection carries identity, direction, cycles, and
    // boundaries, and every front end spells them for itself.
    internal static string Mermaid(CallGraphProjection projection)
    {
        var builder = new StringBuilder("graph LR\n");
        foreach (CallGraphNode node in projection.Nodes)
        {
            builder.Append("  n").Append(node.Id).Append("[\"")
                .Append(MermaidLabel(node.Label))
                .Append("\"]:::")
                .Append(node.Kind.ToString().ToLowerInvariant())
                .Append('\n');
        }

        foreach (CallGraphEdge edge in projection.Edges)
        {
            builder.Append("  n").Append(edge.From)
                .Append(edge.AnyCallInLoop ? " -- loop --> " : " --> ")
                .Append('n').Append(edge.To).Append('\n');
        }

        return builder.ToString();
    }

    internal static string MermaidLabel(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                char lowSurrogate = value[++index];
                var scalar = new Rune(character, lowSurrogate);
                if (Rune.GetUnicodeCategory(scalar) == UnicodeCategory.Format)
                {
                    AppendUnicodeEscape(builder, character);
                    AppendUnicodeEscape(builder, lowSurrogate);
                }
                else
                {
                    builder.Append(character).Append(lowSurrogate);
                }
                continue;
            }

            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\\':
                    builder.Append("&#92;");
                    break;
                case '\u2028':
                case '\u2029':
                    AppendUnicodeEscape(builder, character);
                    break;
                default:
                    if (char.IsControl(character)
                        || char.IsSurrogate(character)
                        || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
                    {
                        AppendUnicodeEscape(builder, character);
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    static void AppendUnicodeEscape(StringBuilder builder, char character) =>
        builder.Append("&#92;u")
            .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));

    static BrowserCallGraphTarget Target(
        CallGraphNode node,
        IReadOnlyList<AssemblyReferenceIdentity> loadedIdentities,
        Func<string, string?>? platformPackForAssembly,
        IReadOnlyList<BrowserWorkspaceParticipant>? surfaceParticipants = null)
    {
        Analysis.TypeRef? definition = DeclaringTypeDefinition(node.Member.DeclaringType);
        // The metadata origin may be a facade; the resolved definition identifies the browsable
        // assembly and must win when the catalog established it.
        AssemblyReferenceIdentity? identity =
            node.DefinitionAssemblyIdentity
            ?? node.OccurrenceAssemblyIdentity
            ?? (definition?.Resolution?.Origin
                    as Analysis.TypeReferenceOrigin.AssemblyReference)
                ?.Assembly;
        if (identity is null && definition is not null)
        {
            AssemblyReferenceIdentity[] matches =
            [
                .. loadedIdentities.Where(candidate =>
                    candidate.Name.Equals(definition.Assembly, StringComparison.OrdinalIgnoreCase)),
            ];
            if (matches.Length > 0
                && matches.All(candidate => candidate.IsEquivalentTo(matches[0])))
            {
                identity = matches[0];
            }
        }
        string assembly =
            identity?.Name
            ?? definition?.Assembly
            ?? node.Member.DeclaringType.Assembly
            ?? "";
        string? surfaceAssemblyId = null;
        if (identity is not null && surfaceParticipants is not null)
        {
            BrowserWorkspaceParticipant[] matches =
            [
                .. surfaceParticipants.Where(participant =>
                    participant.Assembly.Identity.IsEquivalentTo(identity)),
            ];
            if (matches.Length == 1)
                surfaceAssemblyId = matches[0].Asset.Id;
        }
        return new BrowserCallGraphTarget(
            $"n{node.Id}",
            assembly,
            identity?.Version?.ToString(),
            identity?.Culture,
            identity?.PublicKeyToken,
            node.Member.DeclaringType.ToQualifiedDisplayString(),
            definition is null ? null : LegacyMetadataTypeId(definition),
            DefinitionTypeId(definition),
            node.Member.Name,
            [.. node.Member.OpenSignatureParameters.Select(type => type.ToQualifiedDisplayString())],
            node.Member.OpenSignatureReturn.ToQualifiedDisplayString(),
            node.Member.GenericArity,
            null,
            Analysis.CallGraphMemberResolver.CreateSelector(node.Member).Key,
            node.Kind.ToString().ToLowerInvariant(),
            platformPackForAssembly?.Invoke(assembly),
            surfaceAssemblyId);
    }

    /// <summary>
    /// The exact escaped structured identity of a call-graph target's declaring type — the same
    /// identity the browsable type surface carries and the same one the product's resolver
    /// matches. The product owns both projections; the host only carries them.
    /// </summary>
    static string? DefinitionTypeId(Analysis.TypeRef? type) =>
        type is null ? null : Analysis.CallGraphMemberResolver.DefinitionIdentity(type);

    /// <summary>
    /// The legacy flattened metadata identity, published only where the product reports that it
    /// names exactly one type. A nested <c>Outer+Inner</c> and a type whose own metadata name
    /// contains a literal <c>+</c> share that spelling, so a consumer matching on it would
    /// navigate to the wrong type.
    /// </summary>
    static string? LegacyMetadataTypeId(Analysis.TypeRef type) =>
        Analysis.CallGraphMemberResolver.UnambiguousMetadataIdentity(type);

    static Analysis.TypeRef? DeclaringTypeDefinition(Analysis.TypeRef type)
    {
        while (type.Kind == Analysis.TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        return type.Kind == Analysis.TypeRefKind.Definition ? type : null;
    }

    internal static BrowserCallGraphTarget[] Targets(
        IEnumerable<CallGraphNode> nodes,
        IEnumerable<AssemblyReferenceIdentity>? loadedIdentities = null,
        Func<string, string?>? platformPackForAssembly = null,
        IReadOnlyList<BrowserWorkspaceParticipant>? surfaceParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        AssemblyReferenceIdentity[] identities = [.. loadedIdentities ?? []];
        return
        [
            .. nodes.Select(node =>
                Target(
                    node,
                    identities,
                    platformPackForAssembly,
                    surfaceParticipants)),
        ];
    }

    internal static BrowserCallGraphDiagnostics Diagnostics(
        Analysis.CatalogCallGraphDiagnostics diagnostics,
        bool hasUnexploredTraversalBoundary = false,
        bool hasAnalysisFailureBoundary = false) =>
        new(
            diagnostics.IncompleteNodeCount,
            diagnostics.IncompleteEdgeCount,
            diagnostics.BindingIdentityConflictCount,
            hasUnexploredTraversalBoundary,
            hasAnalysisFailureBoundary);

    static BrowserCallGraphNode Tree(Analysis.CallTreeNode? node) => node is null
        ? new BrowserCallGraphNode("", "None", false, null, [], "", "", "")
        : new BrowserCallGraphNode(
            $"{node.Member.DeclaringType.ToQualifiedDisplayString()}.{node.Member.Name}",
            node.Status.ToString(),
            node.Perf?.InLoop ?? false,
            node.Kind?.ToString(),
            [.. node.Children.Select(Tree)],
            node.Member.DeclaringType.Assembly ?? "",
            node.Member.DeclaringType.ToQualifiedDisplayString(),
            node.Member.Name);
}
