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
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(httpClient);

        using var workspace = new InspectionWorkspace();
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

            return WorkspaceImplementationComparisonQuery.Execute(
                new(
                    new(
                        beforeGroup,
                        beforeSide.Root,
                        beforeSide.Bindings),
                    new(
                        afterGroup,
                        afterSide.Root,
                        afterSide.Bindings),
                    declaringType,
                    selector,
                    ResearchProducerCatalog.Kinds),
                cancellationToken);
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
            corpusAssemblyPaths: null);
        try
        {
            TypeResolutionOutcome outcome =
                resolution.Resolve(declaringType);
            if (outcome is TypeResolutionOutcome.UnboundBinding
                {
                    Target:
                        AssemblyBindingTarget.AssemblyReference reference,
                })
            {
                string? targetPath =
                    await AcquireDeclaredTargetAsync(
                        packageDirectory,
                        rootEntry.Tfm,
                        reference.Identity,
                        httpClient,
                        sourceOptions,
                        createComposition,
                        log,
                        ownedTemporaryDirectories,
                        cancellationToken);
                if (targetPath is not null)
                {
                    resolution.Dispose();
                    resolution = CreateResolution([targetPath]);
                    outcome = resolution.Resolve(declaringType);
                }
            }
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
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
                    CreateBinding(
                        participant.Assembly,
                        participant.Assembly.Path)),
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
        finally
        {
            resolution.Dispose();
        }

        TypeDefinitionResolutionSession CreateResolution(
            IReadOnlyList<string>? corpusAssemblyPaths)
            => new(
                rootAssembly,
                isPlatformAssembly: false,
                projectAssetsPath: null,
                targetFramework: rootEntry.Tfm,
                packageDirectory: packageDirectory,
                sourceOptions: sourceOptions,
                usePackageSourcePolicy: packageDirectory is not null,
                allowPlatformAssemblyVersionRollForward: false,
                corpusAssemblyPaths: corpusAssemblyPaths);
    }

    static async Task<string?> AcquireDeclaredTargetAsync(
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
        return matches.Length == 1
            ? Path.Combine(
                extracted.ExtractPath,
                matches[0].EntryPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar))
            : null;
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
            MethodBodyInspectionSession.Open(assembly).BodyIndex);

    static bool IsPackageRoot(
        AssemblySetEntry entry,
        string packageName)
        => entry.SourceKind == AssemblySetSourceKind.Package
            && entry.Source.Equals(
                packageName,
                StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(entry.Path).Equals(
                packageName,
                StringComparison.OrdinalIgnoreCase);

    sealed record ComparisonSide(
        AssemblyContextGroup Group,
        AssemblyContextParticipant Root,
        IReadOnlyList<ImplementationComparisonBinding> Bindings);

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
