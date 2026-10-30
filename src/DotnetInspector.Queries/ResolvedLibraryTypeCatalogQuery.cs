using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>Source authority borrowed while a resolved catalog consumer runs.</summary>
public sealed record LibraryTypeCatalogSource(
    ResolvedAssemblyReference Assembly,
    AssemblyBindingOccurrence Occurrence,
    IAssemblyBindingPolicy BindingPolicy);

internal sealed record ResolvedLibraryTypeCatalogExecution(
    AssemblyContextApiSurfaceResult Result,
    IReadOnlyDictionary<ApiType, LibraryTypeCatalogSource> Sources);

internal static class ResolvedLibraryTypeCatalogQuery
{
    internal static ResolvedLibraryTypeCatalogExecution Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        ApiSurfaceScope scope)
    {
        if (scope is not (ApiSurfaceScope.Public or ApiSurfaceScope.IncludeAll))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var sources = new Dictionary<ApiType, LibraryTypeCatalogSource>(
            ReferenceEqualityComparer.Instance);
        AssemblyContextEntry<AssemblyApiSurface> entry =
            AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
                group,
                participant,
                (_, snapshot) =>
                {
                    using var catalog = new TypeResolutionCatalog();
                    catalog.RegisterRetainedSnapshot(participant.Assembly, snapshot);
                    using AssemblyInspectionSession session =
                        AssemblyInspectionSession.Open(snapshot);
                    ApiSurface surface = session.CompatibilityApiSurface(
                        participant.Assembly,
                        catalog,
                        participant.BindingPolicy,
                        AssemblyContextApiSurfaceQuery.ExtractionScope(scope));
                    if (participant.Assembly.Path is { } path)
                        surface.SetInspectionSourceAssemblyPath(path);
                    var rootSource = new LibraryTypeCatalogSource(
                        participant.Assembly,
                        AssemblyBindingOccurrence.Seed(participant.Assembly),
                        participant.BindingPolicy);
                    foreach (ApiType type in surface.Types)
                        sources.Add(type, rootSource);

                    ResolveForwarders(surface, catalog, participant, scope, sources);
                    return new AssemblyApiSurface(surface, [.. surface.InspectionFailures]);
                });
        var assemblies = new AssemblyContextResult<AssemblyApiSurface>([entry]);
        return new(
            new(
                assemblies,
                ApiAccessibility.Buckets(
                    assemblies.Assemblies
                        .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                        .SelectMany(item => item.Value.Surface.Types)
                        .Select(type => type.Accessibility))),
            sources);
    }

    static void ResolveForwarders(
        ApiSurface surface,
        TypeResolutionCatalog catalog,
        AssemblyContextParticipant participant,
        ApiSurfaceScope scope,
        Dictionary<ApiType, LibraryTypeCatalogSource> sources)
    {
        TypeResolutionRequest[] requests =
        [
            .. surface.TypeForwarders
                .Where(forwarder => forwarder.DefinitionName is not null)
                .Select(forwarder => TypeResolutionRequest.FromAssembly(
                    participant.Assembly, AssemblyResolutionScope.Any,
                    forwarder.DefinitionName!)),
        ];
        if (requests.Length == 0)
            return;

        using TypeResolutionContext context = catalog.CreateContext(
            participant.BindingPolicy, [participant.Assembly], requests);
        var suppliers = new Dictionary<
            AssemblyAcquisitionRegistration,
            List<ResolvedTypeDefinition>>();
        for (int index = 0; index < requests.Length; index++)
        {
            TypeResolutionRequest request = requests[index];
            TypeResolutionOutcome outcome = context.Resolve(request);
            if (outcome is not TypeResolutionOutcome.Resolved resolved
                || resolved.Hops.IsDefaultOrEmpty)
            {
                string kind = outcome switch
                {
                    TypeResolutionOutcome.Rejected rejected => rejected.Failure.GetType().Name,
                    TypeResolutionOutcome.Unavailable unavailable => unavailable.Failure.Kind.ToString(),
                    TypeResolutionOutcome.Resolved => "MissingForwardingEvidence",
                    _ => outcome.GetType().Name,
                };
                surface.InspectionFailures.Add(new(
                    "resolve forwarded type", 0,
                    MetadataTypeNameFailureMechanism.Metadata, kind,
                    $"Forwarded type '{request.Type.ToEscapedFullName()}' could not be resolved: {kind}.",
                    outcome.Hops.IsDefaultOrEmpty
                        ? null
                        : outcome.Hops[0].SourceAssembly.Assembly.Identity,
                    outcome.TerminalAssemblyIdentity)
                {
                    SourceAssemblyPath = participant.Assembly.Path,
                    AffectedTypeDefinitions = [request.Type],
                });
                continue;
            }

            AssemblyAcquisitionRegistration registration =
                resolved.Definition.Assembly.Assembly.Registration;
            if (!suppliers.TryGetValue(registration, out var definitions))
                suppliers.Add(registration, definitions = []);
            definitions.Add(resolved.Definition);
        }

        int copied = 0;
        foreach (List<ResolvedTypeDefinition> definitions in suppliers.Values)
        {
            ResolvedAssemblyReference supplier = definitions[0].Assembly.Assembly;
            HashSet<MetadataTypeDefinitionName> selected =
                [.. definitions.Select(definition => definition.Type)];
            ApiSurface? projected;
            try
            {
                ResolutionAwareApiSurfaceOutcome outcome = catalog.ExtractApiSurface(
                    supplier, participant.BindingPolicy,
                    includeAll: scope == ApiSurfaceScope.IncludeAll);
                if (outcome is ResolutionAwareApiSurfaceOutcome.Rejected rejected)
                {
                    TargetFailure(rejected.Failure.Kind.ToString(), rejected.Failure.Detail);
                    continue;
                }
                projected = ((ResolutionAwareApiSurfaceOutcome.Read)outcome).Surface;
                if (supplier.Path is { } path)
                    projected.SetInspectionSourceAssemblyPath(path);
            }
            catch (Exception failure) when (failure is IOException
                or UnauthorizedAccessException or BadImageFormatException
                or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                TargetFailure(failure.GetType().Name, failure.Message);
                continue;
            }

            ApiType[] types =
                [.. projected.Types.Where(type =>
                    type.DefinitionName is not null && selected.Contains(type.DefinitionName))];
            HashSet<int> tokens =
                [.. definitions.Select(definition => definition.Address.Definition.Value)];
            foreach (ApiType type in types)
            {
                Add(type.MetadataToken);
                foreach (ApiMember member in type.Members)
                {
                    Add(member.MetadataToken);
                    Add(member.GetterToken);
                    Add(member.SetterToken);
                    Add(member.AdderToken);
                    Add(member.RemoverToken);
                }
            }
            surface.MergeInspectionFailuresFrom(
                projected, subject => tokens.Contains(subject.SubjectToken),
                includeNonConstraintFailures: false);
            foreach (ApiSurfaceInspectionFailure failure in projected.InspectionFailures)
            {
                if (failure.Operation != ApiSurface.ConstraintResolutionOperation
                    && (failure.SubjectToken == 0
                        || tokens.Contains(failure.OwningTypeToken ?? failure.SubjectToken)))
                {
                    surface.InspectionFailures.Add(
                        failure.SubjectToken == 0
                            ? failure with { AffectedTypeDefinitions = [.. selected] }
                            : failure);
                }
            }
            foreach (ApiType type in types)
            {
                type.IsForwarded = true;
                type.SourceAssemblyPath = supplier.Path;
                ResolvedTypeDefinition definition = definitions.First(
                    item => item.Type.Equals(type.DefinitionName));
                sources.Add(type, new(supplier, definition.Occurrence, participant.BindingPolicy));
                surface.Types.Add(type);
                surface.PublicMethodCount += type.Members.Count(member =>
                    member.Kind is "method" or "constructor" or "finalizer" or "operator"
                        or "explicit-interface-implementation" or "extension-method");
                surface.PublicPropertyCount += type.Members.Count(member => member.Kind == "property");
                surface.PublicEventCount += type.Members.Count(member => member.Kind == "event");
                surface.PublicFieldCount += type.Members.Count(member => member.Kind == "field");
                copied++;
            }

            void Add(int? token)
            {
                if (token is int value)
                    tokens.Add(value);
            }
            void TargetFailure(string kind, string detail) =>
                surface.InspectionFailures.Add(new(
                    "extract forwarded API surface", 0,
                    MetadataTypeNameFailureMechanism.Metadata,
                    kind, detail, supplier.Identity)
                {
                    SourceAssemblyPath = supplier.Path,
                    AffectedTypeDefinitions = [.. selected],
                });
        }
        if (copied == 0)
            return;

        surface.SurfaceClassification = AssemblySurfaceClassifier.Classify(participant.Assembly);
        surface.SurfaceClassificationInspection = MetadataFindings.InspectAssemblySurface(
            surface.SurfaceClassification,
            new FindingSubject(participant.Assembly.Path ?? participant.Assembly.Identity.Name,
                participant.Assembly.Path is { } sourcePath
                    ? Path.GetFileName(sourcePath)
                    : participant.Assembly.Identity.Name));
        surface.IsTypeForwardingAssembly =
            surface.SurfaceClassification is AssemblySurfaceClassificationOutcome.Classified classified
            && classified.Classification.Kind == AssemblySurfaceKind.Facade;
        surface.PublicTypeCount = surface.Types.Count;
        surface.Types = [.. surface.Types.OrderBy(type => type.FullName)];
    }
}
