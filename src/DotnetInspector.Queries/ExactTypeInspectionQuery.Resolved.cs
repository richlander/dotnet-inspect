using System.Collections.Immutable;

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
        ApiSurfaceScope scope,
        MetadataTypeDefinitionName? selectedDefinition = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedType);
        ExactTypeSelectionKind selection = selectedDefinition is null
            ? ExactTypeSelectionKind.Query
            : ExactTypeSelectionKind.DefinitionIdentity;
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
                        AffectedTypeDefinitions = [forwardedRequest.Type],
                    });
                    continue;
                }
                AssemblyContextEntry<AssemblyApiSurface> entry =
                    ProjectSupplier(available.Definition.Assembly.Assembly);
                if (entry is not AssemblyContextEntry<AssemblyApiSurface>.Available projected)
                {
                    catalogSurface.InspectionFailures.Add(new(
                        "resolve forwarded type",
                        0,
                        MetadataTypeNameFailureMechanism.Metadata,
                        "Unavailable",
                        "The forwarded Library API could not be read.",
                        assembly.Identity,
                        available.Definition.Assembly.Assembly.Identity)
                    {
                        SourceAssemblyPath = assembly.Path,
                        AffectedTypeDefinitions = [forwardedRequest.Type],
                    });
                    continue;
                }
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
        if (catalogSurface.InspectionFailures.Any(failure =>
            failure.Operation != ApiSurface.ConstraintResolutionOperation
                && MayAffectTypeLookup(
                    failure, requestedType, selection)))
        {
            ImmutableArray<ApiSurfaceInspectionFailure> lookupFailures =
            [
                .. catalogSurface.InspectionFailures.Where(static failure =>
                    failure.Operation != ApiSurface.ConstraintResolutionOperation),
            ];
            return new(
                new(
                    new(
                        ExactTypeInspectionOutcome.Unavailable,
                        requestedType, null, null, null, null,
                        [], [],
                        [.. lookupFailures.Select(ExactTypeApiInspectionFailure.From)],
                        Incompleteness(lookupFailures)),
                    [],
                    null),
                null);
        }
        ApiType? selectedType;
        string? matchedName;
        string[] suggestions;
        bool ambiguous;
        if (selectedDefinition is not null)
        {
            MetadataTypeLookupResult<ApiType> lookup = MetadataTypeLookup.Definition(
                catalogSurface.Types.Where(type => type.DefinitionName is not null),
                static type => type.DefinitionName!,
                selectedDefinition);
            selectedType = lookup.Matches.Length == 1 ? lookup.Matches[0] : null;
            matchedName = lookup.MatchedName;
            ambiguous = lookup.Matches.Length > 1;
            suggestions = [];
        }
        else if (TypeMatcher.IsTypeGlobPattern(requestedType))
        {
            string[] names = [.. catalogSurface.Types.Select(type => type.FullName)];
            LookupResult pattern = TypeMatcher.Lookup(names, requestedType);
            selectedType = pattern.Match is null
                ? null
                : catalogSurface.Types.First(type => type.FullName == pattern.Match);
            matchedName = pattern.Match;
            suggestions = [.. pattern.Suggestions];
            ambiguous = pattern.Match is null && pattern.Suggestions.Count > 0;
        }
        else
        {
            MetadataTypeLookupResult<ApiType> lookup = MetadataTypeLookup.Query(
                catalogSurface.Types.Where(type => type.DefinitionName is not null),
                static type => type.DefinitionName!,
                requestedType);
            selectedType = lookup.Matches.Length == 1 ? lookup.Matches[0] : null;
            matchedName = lookup.MatchedName;
            ambiguous = lookup.Matches.Length > 1;
            suggestions = ambiguous
                ? [.. lookup.Matches.Select(type => type.FullName).Distinct(StringComparer.Ordinal)]
                : selectedType is null
                    ? [.. TypeMatcher.Lookup(
                        [.. catalogSurface.Types.Select(type => type.FullName)],
                        requestedType).Suggestions]
                    : [];
        }
        if (selectedType is null)
        {
            if (!ambiguous
                && (selectedDefinition is not null || !TypeMatcher.IsTypeGlobPattern(requestedType)))
            {
                IEnumerable<TypeForwarder> forwarders =
                    catalogSurface.TypeForwarders.Where(item => item.DefinitionName is not null);
                MetadataTypeLookupResult<TypeForwarder> forwarded = selectedDefinition is null
                    ? MetadataTypeLookup.Query(
                        forwarders, static item => item.DefinitionName!, requestedType)
                    : MetadataTypeLookup.Definition(
                        forwarders, static item => item.DefinitionName!, selectedDefinition);
                if (forwarded.Matches.Length == 1)
                    return Unavailable("The selected forwarded Type API could not be read.");
                if (forwarded.Matches.Length > 1)
                {
                    ambiguous = true;
                    suggestions = [.. forwarded.Matches.Select(item => item.TypeName)];
                }
            }
            return new(
                new(
                    new(
                        ambiguous
                            ? ExactTypeInspectionOutcome.Ambiguous
                            : ExactTypeInspectionOutcome.NotFound,
                        requestedType, null, null, null, null,
                        [], [.. suggestions], [], []),
                    [],
                    null),
                catalogSurface);
        }

        if (selectedType.DefinitionName is not { } definitionName)
            return Unavailable("The selected Type has no Metadata definition identity.");
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly, AssemblyResolutionScope.Any, definitionName);
        using var resolutionCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            resolutionCatalog.CreateContext(bindingPolicy, [assembly], [request]);
        TypeResolutionOutcome resolution = context.Resolve(request);
        if (resolution is TypeResolutionOutcome.Ambiguous)
        {
            return new(
                new(
                    new(ExactTypeInspectionOutcome.Ambiguous,
                        requestedType, matchedName, null, null, null,
                        [], [.. suggestions], [], []),
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
            selection,
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
                    MatchedType = matchedName,
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
