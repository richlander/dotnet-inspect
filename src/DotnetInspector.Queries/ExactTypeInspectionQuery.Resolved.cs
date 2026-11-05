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
        ApiSurfaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedType);
        ResolvedAssemblyApiCatalogExecution catalogExecution =
            ResolvedAssemblyApiCatalogQuery.Execute(
                workspace,
                assembly,
                bindingPolicy,
                scope);
        if (catalogExecution.Catalog is not { } catalogSurface)
        {
            return Unavailable(
                catalogExecution.UnavailableDetail
                ?? "The selected Library API could not be read.");
        }
        if (catalogSurface.InspectionFailures.Any(failure =>
            failure.Operation != ApiSurface.ConstraintResolutionOperation
                && MayAffectTypeLookup(
                    failure, requestedType, ExactTypeSelectionKind.Query)))
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
        if (TypeMatcher.IsTypeGlobPattern(requestedType))
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
            if (!ambiguous && !TypeMatcher.IsTypeGlobPattern(requestedType))
            {
                MetadataTypeLookupResult<TypeForwarder> forwarded = MetadataTypeLookup.Query(
                    catalogSurface.TypeForwarders.Where(item => item.DefinitionName is not null),
                    static item => item.DefinitionName!,
                    requestedType);
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
        AssemblyContextEntry<AssemblyApiSurface> surface =
            catalogExecution.ProjectSupplier(supplier);

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
                    MatchedType = matchedName,
                    RequestedAssembly = AssemblyIdentity(
                        catalogExecution.RootGroup,
                        catalogExecution.RootParticipant),
                    ForwardingHops = ForwardingHops(resolved),
                },
                [],
                target),
            null);

        ResolvedAssemblyExactTypeInspectionExecution Unavailable(string detail) =>
            new(
                new(ExactTypeInspectionResult.RuntimeUnavailable(requestedType, detail), [], null),
                null);
    }
}
