using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Packages;
using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;
using NuGet.Versioning;

namespace DotnetInspect.Cli.Inspectors;

internal static class WorkspaceImplementationComparisonRunner
{
    internal static async Task<WorkspaceImplementationComparisonResult>
        ExecuteAsync(
        AssemblySet before,
        AssemblySet after,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions = null,
        Func<DesktopPackageSourceComposition>? createComposition = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
        => await ExecuteWithSidesAsync(
            before,
            after,
            packageName,
            declaringType,
            httpClient,
            sourceOptions,
            createComposition,
            log,
            (beforeSide, afterSide) =>
                WorkspaceImplementationComparisonQuery.Execute(
                    new(
                        beforeSide,
                        afterSide,
                        declaringType,
                        selector,
                        ResearchProducerCatalog.Kinds),
                    cancellationToken),
            cancellationToken);

    internal static async Task<WorkspaceMethodAllocationComparisonResult>
        ExecuteAllocationsAsync(
        AssemblySet before,
        AssemblySet after,
        string packageName,
        string beforeSurfaceAssetPath,
        int beforeMethodToken,
        string afterSurfaceAssetPath,
        int afterMethodToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeSurfaceAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterSurfaceAssetPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (ExactAllocationAssembly(
                before,
                beforeSurfaceAssetPath)
                is not { } beforeRoot
            || ExactAllocationAssembly(
                after,
                afterSurfaceAssetPath)
                is not { } afterRoot)
        {
            return new WorkspaceMethodAllocationComparisonResult.Fallback(
                "The exact allocation target does not identify one assembly "
                    + "at each endpoint.");
        }

        ResolvedAssemblyReference beforeAssembly =
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                beforeRoot.Path,
                AssemblySetInspectionWorkspace.ProvenanceFor(beforeRoot))
            ?? throw new InvalidOperationException(
                $"The selected assembly '{beforeRoot.Path}' does not contain managed metadata.");
        ResolvedAssemblyReference afterAssembly =
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                afterRoot.Path,
                AssemblySetInspectionWorkspace.ProvenanceFor(afterRoot))
            ?? throw new InvalidOperationException(
                $"The selected assembly '{afterRoot.Path}' does not contain managed metadata.");
        await using var workspace = new InspectionWorkspace();
        var beforeParticipant = new AssemblyContextParticipant(
            beforeAssembly,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        var afterParticipant = new AssemblyContextParticipant(
            afterAssembly,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        return WorkspaceMethodAllocationComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            beforeMethodToken,
            afterGroup,
            afterParticipant,
            afterMethodToken,
            cancellationToken);
    }

    internal static async Task<AnnotatedSourceDiffDocumentQueryResult>
        ExecuteAnnotatedSourceDiffAsync(
        AssemblySet before,
        AssemblySet after,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        bool includeIl,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions = null,
        Func<DesktopPackageSourceComposition>? createComposition = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
        => await ExecuteWithSidesAsync(
            before,
            after,
            packageName,
            declaringType,
            httpClient,
            sourceOptions,
            createComposition,
            log,
            (beforeSide, afterSide) =>
                AnnotatedSourceDiffDocumentQuery.Execute(
                    new(
                        beforeSide,
                        afterSide,
                        declaringType,
                        selector,
                        includeIl),
                    cancellationToken),
            cancellationToken);

    /// <summary>
    /// Compares one carried member across the two package endpoint scopes of
    /// <paramref name="session"/>, over their implementation participants
    /// (docs/design/package-endpoint-scope.md, step 4 part 5).
    /// </summary>
    internal static Task<WorkspaceImplementationComparisonResult>
        ExecuteAsync(
        PackageEndpointDiffSession session,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
        => ExecuteWithParticipantsAsync(
            session,
            packageName,
            declaringType,
            logger,
            (beforeSide, afterSide) =>
                WorkspaceImplementationComparisonQuery.Execute(
                    new(
                        beforeSide,
                        afterSide,
                        declaringType,
                        selector,
                        ResearchProducerCatalog.Kinds),
                    cancellationToken),
            cancellationToken);

    internal static Task<WorkspaceMethodAllocationComparisonResult>
        ExecuteAllocationsAsync(
        PackageEndpointDiffSession session,
        string packageName,
        string beforeSurfaceAssetPath,
        int beforeMethodToken,
        string afterSurfaceAssetPath,
        int afterMethodToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeSurfaceAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterSurfaceAssetPath);

        return ExecuteAsync();

        async Task<WorkspaceMethodAllocationComparisonResult> ExecuteAsync()
        {
            Task<PackageEndpointMethodAllocationResult> beforeTask =
                PackageEndpointMethodAllocationQuery.ExecuteAsync(
                    session.From,
                    beforeSurfaceAssetPath,
                    beforeMethodToken,
                    cancellationToken);
            Task<PackageEndpointMethodAllocationResult> afterTask =
                PackageEndpointMethodAllocationQuery.ExecuteAsync(
                    session.To,
                    afterSurfaceAssetPath,
                    afterMethodToken,
                    cancellationToken);
            await Task.WhenAll(beforeTask, afterTask);
            if (beforeTask.Result is not
                    PackageEndpointMethodAllocationResult.Analyzed before
                || afterTask.Result is not
                    PackageEndpointMethodAllocationResult.Analyzed after)
            {
                string reason =
                    (beforeTask.Result as
                        PackageEndpointMethodAllocationResult.Fallback)?.Reason
                    ?? (afterTask.Result as
                        PackageEndpointMethodAllocationResult.Fallback)?.Reason
                    ?? "The package MethodDef pair was unavailable.";
                return new WorkspaceMethodAllocationComparisonResult.Fallback(
                    reason);
            }

            return WorkspaceMethodAllocationComparisonQuery.Compare(
                before.Entry,
                after.Entry,
                packageName,
                afterMethodToken,
                cancellationToken);
        }
    }

    static AssemblySetEntry? ExactAllocationAssembly(
        AssemblySet assemblySet,
        string surfaceAssetPath)
    {
        string selectedPath = Path.GetFullPath(surfaceAssetPath);
        StringComparer comparer =
            LibraryMetadataService.ReferenceTreePathComparer(
                OperatingSystem.IsWindows());
        AssemblySetEntry[] matches =
        [
            .. assemblySet.Assemblies.Where(entry =>
                comparer.Equals(
                    Path.GetFullPath(entry.Path),
                    selectedPath)),
        ];
        return matches.Length == 1
            ? matches[0]
            : null;
    }

    /// <summary>
    /// Publishes the Annotated Source Diff document for one carried member
    /// across the two package endpoint scopes of <paramref name="session"/>
    /// (docs/design/package-endpoint-scope.md, step 4 part 5).
    /// </summary>
    internal static Task<AnnotatedSourceDiffDocumentQueryResult>
        ExecuteAnnotatedSourceDiffAsync(
        PackageEndpointDiffSession session,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        MemberTargetSelector selector,
        bool includeIl,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
        => ExecuteWithParticipantsAsync(
            session,
            packageName,
            declaringType,
            logger,
            (beforeSide, afterSide) =>
                AnnotatedSourceDiffDocumentQuery.Execute(
                    new(
                        beforeSide,
                        afterSide,
                        declaringType,
                        selector,
                        includeIl),
                    cancellationToken),
            cancellationToken);

    /// <summary>
    /// Whether the package endpoint has exactly one implementation
    /// participant named for the package, its Root assembly.
    /// </summary>
    internal static bool HasSinglePackageRoot(
        PackageEndpointScope scope,
        string packageName)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return scope.ImplementationParticipants.Count(participant =>
            IsPackageRoot(participant.Asset.Path, packageName)) == 1;
    }

    /// <summary>
    /// Forms each side from the endpoint's implementation participants while
    /// the body query holds their groups. A forwarded Type's target opens
    /// through the session's House, and its scope stays open until the
    /// comparison returns.
    /// </summary>
    static Task<TResult> ExecuteWithParticipantsAsync<TResult>(
        PackageEndpointDiffSession session,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        VerboseLogger logger,
        Func<
            WorkspaceImplementationComparisonSide,
            WorkspaceImplementationComparisonSide,
            TResult> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(execute);

        return PackageEndpointBodyQuery.ExecuteAsync(
            session.From,
            session.To,
            async (before, after, token) =>
            {
                ParticipantRoot beforeRoot = ResolveRoot(before, packageName, declaringType);
                ParticipantRoot afterRoot = ResolveRoot(after, packageName, declaringType);
                Task<PackageEndpointScope?> beforeOpening =
                    OpenForwardedTargetAsync(session, session.From, beforeRoot, logger, token);
                Task<PackageEndpointScope?> afterOpening =
                    OpenForwardedTargetAsync(session, session.To, afterRoot, logger, token);
                try
                {
                    await Task.WhenAll(beforeOpening, afterOpening);
                }
                catch
                {
                    // A target that opened while its sibling failed is closed
                    // here; the failure itself propagates.
                    foreach (Task<PackageEndpointScope?> opening in new[] { beforeOpening, afterOpening })
                    {
                        if (opening.IsCompletedSuccessfully && opening.Result is { } opened)
                            await opened.DisposeAsync();
                    }
                    throw;
                }
                await using PackageEndpointScope? beforeTarget = beforeOpening.Result;
                await using PackageEndpointScope? afterTarget = afterOpening.Result;
                return await WithForwardedTargetAsync(
                    beforeTarget,
                    beforeRoot,
                    (beforeTargetParticipant, beforeToken) => WithForwardedTargetAsync(
                        afterTarget,
                        afterRoot,
                        async (afterTargetParticipant, afterToken) =>
                        {
                            await using var workspace = new InspectionWorkspace();
                            ComparisonSide beforeSide = CreateSide(
                                workspace,
                                beforeRoot,
                                beforeTargetParticipant,
                                declaringType);
                            using AssemblyContextGroup beforeGroup = beforeSide.Group;
                            ComparisonSide afterSide = CreateSide(
                                workspace,
                                afterRoot,
                                afterTargetParticipant,
                                declaringType);
                            using AssemblyContextGroup afterGroup = afterSide.Group;
                            afterToken.ThrowIfCancellationRequested();
                            return execute(
                                new(beforeGroup, beforeSide.Root, beforeSide.Bindings),
                                new(afterGroup, afterSide.Root, afterSide.Bindings));
                        },
                        beforeToken),
                    token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Finds the endpoint's Root participant and resolves the declaring Type
    /// in the Root alone, so a forward is never bound through installed
    /// runtimes or local caches.
    /// </summary>
    static ParticipantRoot ResolveRoot(
        ImmutableArray<PackageEndpointBodyParticipant> participants,
        string packageName,
        MetadataTypeDefinitionName declaringType)
    {
        PackageEndpointBodyParticipant[] roots =
        [
            .. participants.Where(participant =>
                IsPackageRoot(participant.Descriptor.Asset.Path, packageName)),
        ];
        if (roots.Length != 1)
        {
            throw new InvalidOperationException(
                roots.Length == 0
                    ? $"Package '{packageName}' did not resolve its package-root assembly."
                    : $"Package '{packageName}' resolved more than one package-root assembly.");
        }

        PackageEndpointBodyParticipant root = roots[0];
        var request = TypeResolutionRequest.FromAssembly(
            root.Assembly,
            AssemblyResolutionScope.Any,
            declaringType);
        TypeResolutionOutcome outcome;
        using (TypeResolutionContext context = new TypeResolutionCatalog().CreateContext(
                   NoResolverAssemblyBindingPolicy.Instance,
                   [root.Assembly],
                   [request]))
        {
            outcome = context.Resolve(request);
        }

        return new(root, outcome, ForwardedTarget(outcome));
    }

    static async Task<PackageEndpointScope?> OpenForwardedTargetAsync(
        PackageEndpointDiffSession session,
        PackageEndpointScope endpoint,
        ParticipantRoot root,
        VerboseLogger logger,
        CancellationToken cancellationToken)
        => root.Forward is { } forward
            ? await session.OpenForwardedTargetAsync(
                endpoint,
                root.Root.Descriptor.Provenance.TargetFramework,
                forward.Identity.Name,
                logger,
                cancellationToken)
            : null;

    /// <summary>
    /// Runs <paramref name="next"/> with the forwarded target's
    /// implementation participant while its scope's body query holds it, or
    /// with <see langword="null"/> when the side forwards nothing or its
    /// target did not open.
    /// </summary>
    static async ValueTask<TResult> WithForwardedTargetAsync<TResult>(
        PackageEndpointScope? target,
        ParticipantRoot root,
        Func<PackageEndpointBodyParticipant?, CancellationToken, ValueTask<TResult>> next,
        CancellationToken cancellationToken)
    {
        if (target is null || root.Forward is not { } forward)
            return await next(null, cancellationToken);

        return await PackageEndpointBodyQuery.ExecuteAsync(
            target,
            (participants, token) =>
            {
                PackageEndpointBodyParticipant[] matches =
                [
                    .. participants.Where(participant =>
                        participant.Descriptor.Asset.AssemblyName.Equals(
                            forward.Identity.Name + ".dll",
                            StringComparison.OrdinalIgnoreCase)),
                ];
                return next(matches.Length == 1 ? matches[0] : null, token);
            },
            cancellationToken);
    }

    static ComparisonSide CreateSide(
        InspectionWorkspace workspace,
        ParticipantRoot root,
        PackageEndpointBodyParticipant? target,
        MetadataTypeDefinitionName declaringType)
    {
        ResolvedAssemblyReference rootAssembly = root.Root.Assembly;
        TypeResolutionOutcome outcome = root.Outcome;
        if (root.Forward is { } forward)
        {
            if (target is null)
                return UnavailableSide();
            outcome = ResolveDeclaredTarget(
                rootAssembly,
                target.Assembly,
                forward.Identity,
                forward.Scope,
                declaringType);
        }
        if (outcome is not TypeResolutionOutcome.Resolved resolved)
            return UnavailableSide();

        PackageEndpointBodyParticipant[] bodies = target is null
            ? [root.Root]
            : [root.Root, target];
        return CreateResolvedSide(
            workspace,
            rootAssembly,
            resolved,
            assembly => CreateBinding(
                bodies.SingleOrDefault(body => ReferenceEquals(
                        body.Assembly.Registration,
                        assembly.Registration))
                    ?? throw new InvalidOperationException(
                        $"The route assembly '{assembly.Identity.Name}' is not an endpoint participant."),
                assembly));

        ComparisonSide UnavailableSide()
        {
            var participant = new AssemblyContextParticipant(
                rootAssembly,
                NoResolverAssemblyBindingPolicy.Instance);
            return new(
                workspace.CreateAssemblyContextGroup([participant]),
                participant,
                [CreateBinding(root.Root, rootAssembly)]);
        }
    }

    /// <summary>
    /// Binds a participant's method bodies through its endpoint's
    /// implementation group, as the body views do.
    /// </summary>
    static ImplementationComparisonBinding CreateBinding(
        PackageEndpointBodyParticipant body,
        ResolvedAssemblyReference assembly)
        => new(
            assembly,
            body.Resolver,
            MethodBodyInspectionSession.OpenWithPrefetchedImage(
                    body.Descriptor.Asset.Path,
                    body.Image,
                    LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.Allocations
                        | LibraryBodyAnalysisFeatures.OptimizationOpportunities,
                    body.Resolver,
                    assembly)
                .CallGraphAnalysis);

    static (AssemblyReferenceIdentity Identity, AssemblyResolutionScope Scope)?
        ForwardedTarget(TypeResolutionOutcome outcome)
        => outcome switch
        {
            TypeResolutionOutcome.UnboundBinding
            {
                Target: AssemblyBindingTarget.AssemblyReference reference,
            } unbound => (reference.Identity, unbound.Scope),
            TypeResolutionOutcome.Resolved
            {
                Hops.Length: > 0,
            } resolved => (resolved.Hops[0].TargetReference, resolved.Hops[0].Scope),
            _ => null,
        };

    static async Task<TResult> ExecuteWithSidesAsync<TResult>(
        AssemblySet before,
        AssemblySet after,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions,
        Func<DesktopPackageSourceComposition>? createComposition,
        Action<string>? log,
        Func<
            WorkspaceImplementationComparisonSide,
            WorkspaceImplementationComparisonSide,
            TResult> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(execute);

        await using var workspace = new InspectionWorkspace();
        var ownedTemporaryDirectories = new List<string>();
        try
        {
            ComparisonSide beforeSide = await CreateSideAsync(
                workspace,
                before,
                packageName,
                declaringType,
                httpClient,
                sourceOptions,
                createComposition,
                log,
                ownedTemporaryDirectories,
                cancellationToken);
            using AssemblyContextGroup beforeGroup = beforeSide.Group;
            ComparisonSide afterSide = await CreateSideAsync(
                workspace,
                after,
                packageName,
                declaringType,
                httpClient,
                sourceOptions,
                createComposition,
                log,
                ownedTemporaryDirectories,
                cancellationToken);
            using AssemblyContextGroup afterGroup = afterSide.Group;

            return execute(
                new(
                    beforeGroup,
                    beforeSide.Root,
                    beforeSide.Bindings),
                new(
                    afterGroup,
                    afterSide.Root,
                    afterSide.Bindings));
        }
        finally
        {
            foreach (string directory in
                ownedTemporaryDirectories
                    .Distinct(StringComparer.Ordinal)
                    .Reverse())
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static bool HasSinglePackageRoot(
        AssemblySet assemblySet,
        string packageName)
    {
        ArgumentNullException.ThrowIfNull(assemblySet);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return assemblySet.Assemblies.Count(entry =>
            IsPackageRoot(entry, packageName)) == 1;
    }

    static async Task<ComparisonSide> CreateSideAsync(
        InspectionWorkspace workspace,
        AssemblySet assemblySet,
        string packageName,
        MetadataTypeDefinitionName declaringType,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions,
        Func<DesktopPackageSourceComposition>? createComposition,
        Action<string>? log,
        List<string> ownedTemporaryDirectories,
        CancellationToken cancellationToken)
    {
        AssemblySetEntry[] roots =
        [
            .. assemblySet.Assemblies.Where(entry =>
                IsPackageRoot(entry, packageName)),
        ];
        if (roots.Length != 1)
        {
            throw new InvalidOperationException(
                roots.Length == 0
                    ? $"Package '{packageName}' did not resolve its package-root assembly."
                    : $"Package '{packageName}' resolved more than one package-root assembly.");
        }

        AssemblySetEntry rootEntry = roots[0];
        ResolvedAssemblyReference rootAssembly =
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                rootEntry.Path,
                AssemblySetInspectionWorkspace.ProvenanceFor(rootEntry))
            ?? throw new InvalidOperationException(
                $"The package-root assembly '{rootEntry.Path}' does not contain managed metadata.");
        string? packageDirectory = FindPackageDirectory(
            rootEntry.Path);
        TypeDefinitionResolutionSession resolution = CreateResolution(
            usePackageSourcePolicy:
                packageDirectory is not null);
        try
        {
            TypeResolutionOutcome outcome =
                resolution.Resolve(declaringType);
            (AssemblyReferenceIdentity Identity,
                AssemblyResolutionScope Scope)? forwardedTarget =
                outcome switch
                {
                    TypeResolutionOutcome.UnboundBinding
                    {
                        Target:
                            AssemblyBindingTarget.AssemblyReference reference,
                    } unbound => (
                        reference.Identity,
                        unbound.Scope),
                    TypeResolutionOutcome.Resolved
                    {
                        Hops.Length: > 0,
                    } initialResolved =>
                        (
                            initialResolved.Hops[0].TargetReference,
                            initialResolved.Hops[0].Scope),
                    _ => null,
                };
            if (forwardedTarget is not null)
            {
                DeclaredTarget? target =
                    await AcquireDeclaredTargetAsync(
                        packageDirectory,
                        rootEntry.Tfm,
                        forwardedTarget.Value.Identity,
                        httpClient,
                        sourceOptions,
                        createComposition,
                        log,
                        ownedTemporaryDirectories,
                        cancellationToken);
                if (target is null
                    || ResolvedAssemblyReference
                        .CreateFromPathIfManaged(
                            target.Path,
                            AssemblyResolutionProvenance.Package(
                                target.PackageId,
                                target.PackageVersion,
                                target.TargetFramework,
                                rid: null))
                        is not { } targetAssembly)
                {
                    return UnavailableSide();
                }
                outcome = ResolveDeclaredTarget(
                    rootAssembly,
                    targetAssembly,
                    forwardedTarget.Value.Identity,
                    forwardedTarget.Value.Scope,
                    declaringType);
            }
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
            {
                return UnavailableSide();
            }

            return CreateResolvedSide(
                workspace,
                rootAssembly,
                resolved,
                assembly => CreateBinding(
                    assembly,
                    assembly.Path));
        }
        finally
        {
            resolution.Dispose();
        }

        TypeDefinitionResolutionSession CreateResolution(
            bool usePackageSourcePolicy)
            => new(
                rootAssembly,
                isPlatformAssembly: false,
                projectAssetsPath: null,
                targetFramework: rootEntry.Tfm,
                packageDirectory: packageDirectory,
                sourceOptions: sourceOptions,
                usePackageSourcePolicy:
                    usePackageSourcePolicy,
                allowPlatformAssemblyVersionRollForward: false);

        ComparisonSide UnavailableSide()
        {
            var policy =
                NoResolverAssemblyBindingPolicy.Instance;
            var unavailableRootParticipant =
                new AssemblyContextParticipant(
                    rootAssembly,
                    policy);
            return new(
                workspace.CreateAssemblyContextGroup(
                    [unavailableRootParticipant]),
                unavailableRootParticipant,
                [CreateBinding(
                    rootAssembly,
                    rootEntry.Path)]);
        }
    }

    /// <summary>
    /// The closed route of one resolved side: the Root, each forwarding hop,
    /// and the definition, in one group whose policy binds only that route.
    /// </summary>
    static ComparisonSide CreateResolvedSide(
        InspectionWorkspace workspace,
        ResolvedAssemblyReference rootAssembly,
        TypeResolutionOutcome.Resolved resolved,
        Func<ResolvedAssemblyReference, ImplementationComparisonBinding> bind)
    {
        var occurrences = new Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyBindingOccurrence>(
                ReferenceEqualityComparer.Instance)
        {
            [rootAssembly.Registration] =
                AssemblyBindingOccurrence.Seed(rootAssembly),
        };
        foreach (TypeForwardingHop hop in resolved.Hops)
        {
            occurrences.TryAdd(
                hop.SourceAssembly.Assembly.Registration,
                hop.SourceOccurrence);
        }
        occurrences.TryAdd(
            resolved.Definition.Assembly.Assembly.Registration,
            resolved.Definition.Occurrence);

        var routePolicy = new ClosedRouteBindingPolicy(
            resolved.Hops.Select((hop, index) =>
                new ClosedRoute(
                    hop.SourceAssembly.Assembly.Registration,
                    hop.TargetReference,
                    hop.Scope,
                    index + 1 < resolved.Hops.Length
                        ? resolved.Hops[index + 1].SourceAssembly.Assembly
                        : resolved.Definition.Assembly.Assembly)));
        IAcquisitionFreeAssemblyBindingPolicy groupPolicy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                occurrences.Values.Select(occurrence => (
                    occurrence.Assembly,
                    Policy: (IAcquisitionFreeAssemblyBindingPolicy)
                        routePolicy)));
        AssemblyContextParticipant[] participants =
        [
            .. occurrences.Values.Select(occurrence =>
                new AssemblyContextParticipant(
                    occurrence.Assembly,
                    groupPolicy)),
        ];
        IReadOnlyList<ImplementationComparisonBinding> bindings =
        [
            .. participants.Select(participant =>
                bind(participant.Assembly)),
        ];
        AssemblyContextParticipant rootParticipant =
            participants.Single(participant =>
                ReferenceEquals(
                    participant.Assembly.Registration,
                    rootAssembly.Registration));
        return new(
            workspace.CreateAssemblyContextGroup(participants),
            rootParticipant,
            bindings);
    }

    static TypeResolutionOutcome ResolveDeclaredTarget(
        ResolvedAssemblyReference root,
        ResolvedAssemblyReference target,
        AssemblyReferenceIdentity targetIdentity,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName declaringType)
    {
        var policy = new ClosedRouteBindingPolicy(
            [
                new(
                    root.Registration,
                    targetIdentity,
                    scope,
                    target),
            ]);
        var catalog = new TypeResolutionCatalog();
        TypeResolutionRequest request =
            TypeResolutionRequest.FromAssembly(
                root,
                AssemblyResolutionScope.Any,
                declaringType);
        using TypeResolutionContext context =
            catalog.CreateContext(
                policy,
                [root, target],
                [request]);
        return context.Resolve(request);
    }

    static async Task<DeclaredTarget?> AcquireDeclaredTargetAsync(
        string? packageDirectory,
        string? targetFramework,
        AssemblyReferenceIdentity target,
        HttpClient httpClient,
        NuGetSourceOptions? sourceOptions,
        Func<DesktopPackageSourceComposition>? createComposition,
        Action<string>? log,
        List<string> ownedTemporaryDirectories,
        CancellationToken cancellationToken)
    {
        if (packageDirectory is null
            || targetFramework is null
            || NuspecParser.FindAndParse(packageDirectory)
                is not { DependencyGroups: { } groups })
        {
            return null;
        }

        DependencyGroup[] applicable =
        [
            .. groups.Where(group =>
                group.IsImplicitManifestGroup
                || TfmSelector.NormalizeTfm(
                    group.TargetFramework).Equals(
                    TfmSelector.NormalizeTfm(
                        targetFramework),
                    StringComparison.OrdinalIgnoreCase)),
        ];
        string[] versions =
        [
            .. applicable
                .SelectMany(group => group.Dependencies)
                .Where(dependency => dependency.Id.Equals(
                    target.Name,
                    StringComparison.OrdinalIgnoreCase))
                .Select(dependency => VersionRange.TryParse(
                        dependency.Version,
                        out VersionRange? range)
                    ? range.MinVersion?.ToNormalizedString()
                    : null)
                .Where(static version => version is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (versions.Length != 1)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var outcome = await PackageExtractor.ExtractPinnedPackageAsync(
            httpClient,
            target.Name,
            versions[0],
            log,
            "inspect-diff-target",
            sourceOptions,
            createComposition);
        if (!outcome.IsSuccess)
        {
            log?.Invoke(
                outcome.ErrorMessage
                    ?? $"Package '{target.Name}@{versions[0]}' could not be resolved.");
            return null;
        }

        PackageExtractionResult extracted = outcome.Result!;
        if (extracted.TempDir is { } temporary)
            ownedTemporaryDirectories.Add(temporary);
        var content = new FileSystemPackageContent(
            extracted.ExtractPath,
            extracted.NupkgPath,
            extracted.FromCache,
            extracted.ProducerKey
                ?? $"package:{target.Name}@{versions[0]}");
        if (PackageAssetSelector.Select(
                content,
                targetFramework)
            is not PackageAssetSelection.Selected selected)
        {
            return null;
        }

        PackageAssetEntry[] matches =
        [
            .. selected.Universe.Assets.Where(asset =>
                asset.FileName.Equals(
                    target.Name + ".dll",
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (matches.Length != 1)
            return null;
        return new(
            Path.Combine(
                extracted.ExtractPath,
                matches[0].EntryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)),
            target.Name,
            versions[0],
            selected.Universe.TargetFramework);
    }

    static string? FindPackageDirectory(string assemblyPath)
    {
        DirectoryInfo? directory =
            Directory.GetParent(
                Path.GetFullPath(assemblyPath));
        for (int depth = 0;
            directory is not null && depth < 8;
            depth++, directory = directory.Parent)
        {
            if (NuspecParser.FindNuspec(directory.FullName)
                is not null)
            {
                return directory.FullName;
            }
        }
        return null;
    }

    static ImplementationComparisonBinding CreateBinding(
        ResolvedAssemblyReference assembly,
        string? path,
        IAssemblyReferenceResolver? resolver = null)
        => new(
            assembly,
            resolver
                ?? MetadataSource.DefaultAssemblyReferenceResolver(
                    path
                    ?? throw new InvalidOperationException(
                        $"The realized assembly '{assembly.Identity.Name}' has no local image path.")),
            MethodBodyInspectionSession.Open(assembly).CallGraphAnalysis);

    static bool IsPackageRoot(
        AssemblySetEntry entry,
        string packageName)
        => entry.SourceKind == AssemblySetSourceKind.Package
            && entry.Source.Equals(
                packageName,
                StringComparison.OrdinalIgnoreCase)
            && IsPackageRoot(entry.Path, packageName);

    static bool IsPackageRoot(
        string assetPath,
        string packageName)
        => Path.GetFileNameWithoutExtension(assetPath).Equals(
            packageName,
            StringComparison.OrdinalIgnoreCase);

    sealed record ComparisonSide(
        AssemblyContextGroup Group,
        AssemblyContextParticipant Root,
        IReadOnlyList<ImplementationComparisonBinding> Bindings);

    sealed record ParticipantRoot(
        PackageEndpointBodyParticipant Root,
        TypeResolutionOutcome Outcome,
        (AssemblyReferenceIdentity Identity, AssemblyResolutionScope Scope)? Forward);

    sealed record DeclaredTarget(
        string Path,
        string PackageId,
        string PackageVersion,
        string? TargetFramework);

    sealed record ClosedRoute(
        AssemblyAcquisitionRegistration Source,
        AssemblyReferenceIdentity Target,
        AssemblyResolutionScope Scope,
        ResolvedAssemblyReference Selected);

    sealed class ClosedRouteBindingPolicy(
        IEnumerable<ClosedRoute> routes)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        readonly ClosedRoute[] _routes = [.. routes];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Target is AssemblyBindingTarget.AssemblyReference
                    reference
                && request.Origin is
                    AssemblyBindingOrigin.RequestingAssembly requesting)
            {
                ClosedRoute? route = _routes.SingleOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Source,
                        requesting.Registration)
                    && candidate.Target.Equals(reference.Identity)
                    && candidate.Scope == request.Scope);
                if (route is not null)
                {
                    return new(
                        Version,
                        AssemblyBindingSelection.Found(
                            route.Selected));
                }
            }

            return new(
                Version,
                request.Target switch
                {
                    AssemblyBindingTarget.AssemblyReference =>
                        AssemblyBindingSelection.NameNotOwned(),
                    AssemblyBindingTarget.IntrinsicCoreLibrary =>
                        AssemblyBindingSelection.CannotSelect(
                            new(
                                AssemblyBindingFailureKind
                                    .UnsupportedScope)),
                    _ => AssemblyBindingSelection.Invalid(
                        new(
                            AssemblyBindingFailureKind
                                .InvalidPolicyResult)),
                });
        }
    }
}
