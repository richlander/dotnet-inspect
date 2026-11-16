using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal sealed record ResolvedAssemblyApiCatalogTypeTarget(
    ApiType Type,
    ResolvedAssemblyReference Assembly,
    AssemblyBindingOccurrence Occurrence);

internal sealed class ResolvedAssemblyApiCatalogExecution
{
    readonly InspectionWorkspace _workspace;
    readonly IAssemblyBindingPolicy _bindingPolicy;
    readonly ApiSurfaceScope _scope;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        AssemblyContextEntry<AssemblyApiSurface>> _projections;

    internal ResolvedAssemblyApiCatalogExecution(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceScope scope,
        AssemblyContextGroup rootGroup,
        AssemblyContextParticipant rootParticipant,
        Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyContextEntry<AssemblyApiSurface>> projections,
        ApiSurface? catalog,
        IReadOnlyList<ResolvedAssemblyApiCatalogTypeTarget> typeTargets,
        string? unavailableDetail)
    {
        _workspace = workspace;
        _bindingPolicy = bindingPolicy;
        _scope = scope;
        _projections = projections;
        RootGroup = rootGroup;
        RootParticipant = rootParticipant;
        Catalog = catalog;
        TypeTargets = typeTargets;
        UnavailableDetail = unavailableDetail;
    }

    internal AssemblyContextGroup RootGroup { get; }

    internal AssemblyContextParticipant RootParticipant { get; }

    internal ApiSurface? Catalog { get; }

    internal IReadOnlyList<ResolvedAssemblyApiCatalogTypeTarget> TypeTargets
    {
        get;
    }

    internal string? UnavailableDetail { get; }

    internal AssemblyContextEntry<AssemblyApiSurface> ProjectSupplier(
        ResolvedAssemblyReference supplier)
    {
        if (_projections.TryGetValue(
                supplier.Registration,
                out AssemblyContextEntry<AssemblyApiSurface>? existing))
        {
            return existing;
        }

        var participant =
            new AssemblyContextParticipant(supplier, _bindingPolicy);
        AssemblyContextGroup group =
            _workspace.CreateAssemblyContextGroup([participant]);
        AssemblyContextEntry<AssemblyApiSurface> entry =
            AssemblyContextApiSurfaceQuery.ExecuteParticipantResolved(
                group,
                participant,
                _scope);
        if (entry
            is AssemblyContextEntry<AssemblyApiSurface>.Available available
            && supplier.Path is { } path)
        {
            available.Value.Surface
                .SetInspectionSourceAssemblyPath(path);
        }
        _projections.Add(supplier.Registration, entry);
        return entry;
    }
}

internal static class ResolvedAssemblyApiCatalogQuery
{
    internal static ResolvedAssemblyApiCatalogExecution Execute(
        InspectionWorkspace workspace,
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceScope scope)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        var root = new AssemblyContextParticipant(assembly, bindingPolicy);
        AssemblyContextGroup rootGroup =
            workspace.CreateAssemblyContextGroup([root]);
        AssemblyContextEntry<AssemblyApiSurface> rootEntry =
            AssemblyContextApiSurfaceQuery.ExecuteParticipantResolved(
                rootGroup,
                root,
                scope);
        var projections = new Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyContextEntry<AssemblyApiSurface>>
        {
            [assembly.Registration] = rootEntry,
        };
        if (rootEntry
            is not AssemblyContextEntry<AssemblyApiSurface>.Available
                rootSurface)
        {
            return new(
                workspace,
                bindingPolicy,
                scope,
                rootGroup,
                root,
                projections,
                catalog: null,
                typeTargets: [],
                UnavailableDetail(rootEntry));
        }

        ApiSurface catalog = rootSurface.Value.Surface;
        if (assembly.Path is { } rootPath)
            catalog.SetInspectionSourceAssemblyPath(rootPath);
        var targets = new Dictionary<
            ApiType,
            ResolvedAssemblyApiCatalogTypeTarget>(
            ReferenceEqualityComparer.Instance);
        AssemblyBindingOccurrence rootOccurrence =
            AssemblyBindingOccurrence.Seed(assembly);
        foreach (ApiType type in catalog.Types)
        {
            targets.Add(
                type,
                new(type, assembly, rootOccurrence));
        }

        TypeResolutionRequest[] forwardedRequests =
        [
            .. catalog.TypeForwarders
                .Where(forwarder => forwarder.DefinitionName is not null)
                .Select(forwarder => TypeResolutionRequest.FromAssembly(
                    assembly,
                    AssemblyResolutionScope.Any,
                    forwarder.DefinitionName!)),
        ];
        var execution = new ResolvedAssemblyApiCatalogExecution(
            workspace,
            bindingPolicy,
            scope,
            rootGroup,
            root,
            projections,
            catalog,
            [],
            unavailableDetail: null);
        if (forwardedRequests.Length > 0)
        {
            using var forwardingCatalog = new TypeResolutionCatalog();
            using TypeResolutionContext forwarding =
                forwardingCatalog.CreateContext(
                    bindingPolicy,
                    [assembly],
                    forwardedRequests);
            foreach (TypeResolutionRequest forwardedRequest
                     in forwardedRequests)
            {
                TypeResolutionOutcome outcome =
                    forwarding.Resolve(forwardedRequest);
                if (outcome is not TypeResolutionOutcome.Resolved available)
                {
                    catalog.InspectionFailures.Add(new(
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

                ResolvedAssemblyReference supplier =
                    available.Definition.Assembly.Assembly;
                AssemblyContextEntry<AssemblyApiSurface> entry =
                    execution.ProjectSupplier(supplier);
                if (entry
                    is not AssemblyContextEntry<AssemblyApiSurface>.Available
                        projected)
                {
                    catalog.InspectionFailures.Add(new(
                        "resolve forwarded type",
                        0,
                        MetadataTypeNameFailureMechanism.Metadata,
                        "Unavailable",
                        "The forwarded Library API could not be read.",
                        assembly.Identity,
                        supplier.Identity)
                    {
                        SourceAssemblyPath = assembly.Path,
                        AffectedTypeDefinitions = [forwardedRequest.Type],
                    });
                    continue;
                }

                ApiType? forwardedType =
                    projected.Value.Surface.Types.FirstOrDefault(
                        type =>
                            type.DefinitionName?.Equals(
                                available.Definition.Type)
                            == true);
                MergeSelectedInspectionFailures(
                    catalog,
                    projected.Value.Surface,
                    forwardedType,
                    forwardedRequest.Type,
                    available.Definition.Address
                        .Definition.Value);
                if (forwardedType is null)
                    continue;

                forwardedType.IsForwarded = true;
                if (!targets.TryAdd(
                    forwardedType,
                    new(
                        forwardedType,
                        supplier,
                        available.Definition.Occurrence)))
                {
                    continue;
                }

                if (!catalog.Types.Contains(forwardedType))
                {
                    catalog.Types.Add(forwardedType);
                    AddPublicMemberCounts(
                        catalog,
                        forwardedType);
                }
            }

            catalog.Types =
            [
                .. catalog.Types.OrderBy(type => type.FullName),
            ];
            catalog.PublicTypeCount = catalog.Types.Count;
        }

        return new(
            workspace,
            bindingPolicy,
            scope,
            rootGroup,
            root,
            projections,
            catalog,
            [.. targets.Values],
            unavailableDetail: null);
    }

    static void MergeSelectedInspectionFailures(
        ApiSurface destination,
        ApiSurface source,
        ApiType? selectedType,
        MetadataTypeDefinitionName selectedDefinition,
        int selectedTypeToken)
    {
        var selectedTokens = new HashSet<int>
        {
            selectedTypeToken,
        };
        if (selectedType is not null)
        {
            Add(selectedType.MetadataToken);
            foreach (ApiMember member in selectedType.Members)
            {
                Add(member.MetadataToken);
                Add(member.GetterToken);
                Add(member.SetterToken);
                Add(member.AdderToken);
                Add(member.RemoverToken);
            }
        }

        destination.MergeInspectionFailuresFrom(
            source,
            subject =>
                selectedTokens.Contains(subject.SubjectToken),
            includeNonConstraintFailures: false);
        foreach (ApiSurfaceInspectionFailure failure
                 in source.InspectionFailures)
        {
            if (failure.Operation
                    == ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                || !IncludesFailure(failure))
            {
                continue;
            }

            destination.InspectionFailures.Add(
                failure.SubjectToken == 0
                    ? failure with
                    {
                        AffectedTypeDefinitions =
                            [selectedDefinition],
                    }
                    : failure);
        }

        void Add(int? token)
        {
            if (token is int value)
                selectedTokens.Add(value);
        }

        bool IncludesFailure(
            ApiSurfaceInspectionFailure failure) =>
            failure.SubjectToken == 0
            || selectedTokens.Contains(
                failure.OwningTypeToken
                ?? failure.SubjectToken);
    }

    static void AddPublicMemberCounts(
        ApiSurface catalog,
        ApiType type)
    {
        catalog.PublicMethodCount += type.Members.Count(
            static member =>
                member.Kind is "method"
                    or "constructor"
                    or "finalizer"
                    or "operator"
                    or "explicit-interface-implementation"
                    or "extension-method");
        catalog.PublicPropertyCount += type.Members.Count(
            static member => member.Kind == "property");
        catalog.PublicEventCount += type.Members.Count(
            static member => member.Kind == "event");
        catalog.PublicFieldCount += type.Members.Count(
            static member => member.Kind == "field");
    }

    static string UnavailableDetail(
        AssemblyContextEntry<AssemblyApiSurface> entry) =>
        entry switch
        {
            AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected =>
                rejected.Failure.Detail,
            AssemblyContextEntry<AssemblyApiSurface>.Failed failed =>
                failed.Error.Message,
            _ => "The selected Library API could not be read.",
        };
}
