using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record ResolvedAssemblyExactTypeInspectionExecution(
    ExactTypeInspectionExecution Execution,
    ApiSurface? Catalog);

internal static partial class ExactTypeInspectionQuery
{
    internal static ResolvedAssemblyExactTypeInspectionExecution ExecuteResolvedAssembly(
        InspectionWorkspace workspace,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        string requestedType,
        ApiSurfaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedType);
        var root = new AssemblyContextParticipant(assembly, bindingPolicy);
        AssemblyContextGroup rootGroup =
            workspace.CreateAssemblyContextGroup([root]);
        AssemblyContextEntry<AssemblyApiSurface> rootEntry =
            AssemblyContextApiSurfaceQuery.ExecuteParticipantResolved(
                rootGroup, root, scope);
        if (rootEntry is not AssemblyContextEntry<AssemblyApiSurface>.Available rootSurface)
            return Unavailable("The selected Library API could not be read.");

        ApiSurface catalogSurface = rootSurface.Value.Surface;
        var projections = new Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyContextEntry<AssemblyApiSurface>>
        {
            [assembly.Registration] = rootEntry,
        };
        TypeResolutionRequest[] forwardedRequests =
        [
            .. catalogSurface.TypeForwarders
                .Where(forwarder => forwarder.DefinitionName is not null)
                .Select(forwarder => TypeResolutionRequest.FromAssembly(
                    assembly,
                    AssemblyResolutionScope.Any,
                    forwarder.DefinitionName!)),
        ];
        if (forwardedRequests.Length > 0)
        {
            using var forwardingCatalog = new TypeResolutionCatalog();
            using TypeResolutionContext forwarding =
                forwardingCatalog.CreateContext(bindingPolicy, [assembly], forwardedRequests);
            foreach (TypeResolutionRequest forwardedRequest in forwardedRequests)
            {
                TypeResolutionOutcome outcome = forwarding.Resolve(forwardedRequest);
                if (outcome is not TypeResolutionOutcome.Resolved available)
                {
                    catalogSurface.InspectionFailures.Add(new(
                        "resolve forwarded type",
                        0,
                        MetadataTypeNameFailureMechanism.Metadata,
                        outcome.GetType().Name,
                        "A forwarded Type could not be resolved.",
                        assembly.Identity,
                        outcome.TerminalAssemblyIdentity)
                    {
                        SourceAssemblyPath = assembly.Path,
                    });
                    continue;
                }
                AssemblyContextEntry<AssemblyApiSurface> entry =
                    ProjectSupplier(available.Definition.Assembly.Assembly);
                if (entry is not AssemblyContextEntry<AssemblyApiSurface>.Available projected)
                    return Unavailable("The forwarded Library API could not be read.");
                ApiType? forwardedType = projected.Value.Surface.Types.FirstOrDefault(
                    type => type.DefinitionName?.Equals(available.Definition.Type) == true);
                if (forwardedType is null)
                    continue;
                forwardedType.IsForwarded = true;
                if (!catalogSurface.Types.Contains(forwardedType))
                    catalogSurface.Types.Add(forwardedType);
            }
            catalogSurface.Types = [.. catalogSurface.Types.OrderBy(type => type.FullName)];
            catalogSurface.PublicTypeCount = catalogSurface.Types.Count;
        }
        string[] names = [.. catalogSurface.Types.Select(type => type.FullName)];
        LookupResult lookup = TypeMatcher.Lookup(names, requestedType);
        if (lookup.Match is null)
        {
            ExactTypeInspectionOutcome outcome =
                TypeMatcher.IsTypeGlobPattern(requestedType)
                    && lookup.Suggestions.Count > 0
                || names.Count(name => TypeMatcher.MatchesExactTypeName(name, requestedType)) > 1
                    ? ExactTypeInspectionOutcome.Ambiguous
                    : ExactTypeInspectionOutcome.NotFound;
            return new(
                new(
                    new(outcome, requestedType, null, null, null, null,
                        [], [.. lookup.Suggestions], [], []),
                    [],
                    null),
                catalogSurface);
        }

        ApiType selectedType = catalogSurface.Types.First(
            type => type.FullName == lookup.Match);
        if (selectedType.DefinitionName is not { } selectedDefinition)
            return Unavailable("The selected Type has no Metadata definition identity.");
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly, AssemblyResolutionScope.Any, selectedDefinition);
        using var resolutionCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            resolutionCatalog.CreateContext(bindingPolicy, [assembly], [request]);
        TypeResolutionOutcome resolution = context.Resolve(request);
        if (resolution is TypeResolutionOutcome.Ambiguous)
        {
            return new(
                new(
                    new(ExactTypeInspectionOutcome.Ambiguous,
                        requestedType, lookup.Match, null, null, null,
                        [], [.. lookup.Suggestions], [], []),
                    [],
                    null),
                null);
        }
        if (resolution is not TypeResolutionOutcome.Resolved resolved)
            return Unavailable($"The exact Type could not be resolved ({resolution.GetType().Name}).");

        ResolvedTypeDefinition definition = resolved.Definition;
        ResolvedAssemblyReference supplier = definition.Assembly.Assembly;
        AssemblyContextEntry<AssemblyApiSurface> surface = ProjectSupplier(supplier);

        // Acquisition is settled before the exact core consumes the selected definition.
        var participant = new AssemblyContextParticipant(
            supplier, NoResolverAssemblyBindingPolicy.Instance);
        AssemblyContextGroup selectedGroup =
            workspace.CreateAssemblyContextGroup([participant]);
        var loaded = new WorkspaceContextLoadOutcome.Loaded(
            workspace.Identity, selectedGroup, [], [], null, null);
        ExactTypeInspectionTarget? target = null;
        ExactTypeInspectionResult result = ExecuteCore(
            workspace.Identity,
            new ExactTypeInspectionContext(loaded),
            requestedType,
            ExactTypeSelectionKind.Query,
            [participant],
            definingSource: null,
            definingSources: null,
            value => target = value,
            scope,
            projectionLimits: null,
            selectedDefinition: definition.Type,
            preparedContext: new(
                new([new(0, participant, surface)], null),
                null));
        if (target is not null)
        {
            target.Type.IsForwarded = !resolved.Hops.IsDefaultOrEmpty;
            if (supplier.Path is { } path)
                target.Surface.Surface.SetInspectionSourceAssemblyPath(path);
            target = target with
            {
                Occurrence = definition.Occurrence,
                BindingPolicy = bindingPolicy,
            };
            result = result with { Type = ExactTypeApi.From(target.Type) };
        }
        return new(
            new(
                result with
                {
                    MatchedType = lookup.Match,
                    RequestedAssembly = AssemblyIdentity(rootGroup, root),
                    ForwardingHops = ForwardingHops(resolved),
                },
                [],
                target),
            null);

        ResolvedAssemblyExactTypeInspectionExecution Unavailable(string detail) =>
            new(
                new(ExactTypeInspectionResult.RuntimeUnavailable(requestedType, detail), [], null),
                null);

        AssemblyContextEntry<AssemblyApiSurface> ProjectSupplier(
            ResolvedAssemblyReference supplier)
        {
            if (projections.TryGetValue(supplier.Registration, out var existing))
                return existing;
            var participant = new AssemblyContextParticipant(supplier, bindingPolicy);
            AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
            var entry = AssemblyContextApiSurfaceQuery.ExecuteParticipantResolved(
                group, participant, scope);
            projections.Add(supplier.Registration, entry);
            return entry;
        }
    }
}
