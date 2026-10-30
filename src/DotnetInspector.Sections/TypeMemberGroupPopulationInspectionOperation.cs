using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class TypeMemberGroupPopulationInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Execute(
            TypeMemberGroupPopulationInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            TypeMemberGroupPopulationExecutionPlan query =
                request.Plan.Query;
            TypeMemberGroupRowsRequest? rows =
                query.Terminal is QuerySpaceTerminalRequirement.Rows
                    ? request.Plan.Members.Rows
                    : null;
            AssemblyReferenceIdentity requestedAssembly =
                request.Library.ApiAssembly.AssemblyIdentity
                    ?.Identity
                ?? throw new InvalidOperationException(
                    "A Type Member-group population requires a managed API assembly identity.");
            if (rows?.Continuation is { } continuation
                && !IsCompatible(
                    continuation,
                    requestedAssembly,
                    request.Plan.Type,
                    query))
            {
                return Rejected(
                    TypeMemberGroupPopulationInspectionRejection
                        .IncompatibleContinuation);
            }

            int startOrdinal =
                rows?.Continuation?.NextOrdinal ?? 0;
            MetadataTypeMemberGroupPopulationRequest metadataRequest =
                CreateMetadataRequest(request.Plan, startOrdinal);
            LibraryTypeMemberGroupPopulationInspectionOutcome source =
                LibraryTypeMemberGroupPopulationInspection.Execute(
                    new(
                        request.Library,
                        metadataRequest,
                        request.Plan.Bounds,
                        rows?.Continuation?.Binding
                            .ModuleVersionId),
                    lease,
                    cancellationToken);
            return Project(
                source,
                request,
                startOrdinal);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Project(
        LibraryTypeMemberGroupPopulationInspectionOutcome outcome,
        TypeMemberGroupPopulationInspectionRequest request,
        int startOrdinal) =>
        outcome switch
        {
            LibraryTypeMemberGroupPopulationInspectionOutcome.Completed
                completed =>
                    Project(
                        completed.Correspondence,
                        request,
                        startOrdinal),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Rejected
                rejected =>
                    Rejected(
                        rejected.Reason switch
                        {
                            LibraryTypeMemberGroupPopulationInspectionRejection
                                    .LeaseReferenceMismatch =>
                                TypeMemberGroupPopulationInspectionRejection
                                    .LeaseReferenceMismatch,
                            LibraryTypeMemberGroupPopulationInspectionRejection
                                    .AssemblyIdentityMismatch =>
                                TypeMemberGroupPopulationInspectionRejection
                                    .AssemblyIdentityMismatch,
                            LibraryTypeMemberGroupPopulationInspectionRejection
                                    .StaleContinuation =>
                                TypeMemberGroupPopulationInspectionRejection
                                    .StaleContinuation,
                            _ => throw new InvalidOperationException(
                                "Unknown Library Type Member-group rejection."),
                        }),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Incomplete
                incomplete =>
                    Incomplete(
                        TypeMemberGroupPopulationBound.MetadataRows,
                        request.Plan.Bounds.MaxMetadataRows,
                        incomplete.Measured),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Failed
                failed =>
                    Failed(
                        failed.Reason switch
                        {
                            LibraryTypeMemberGroupPopulationInspectionFailure
                                    .NotManagedAssembly =>
                                TypeMemberGroupPopulationInspectionFailure
                                    .NotManagedAssembly,
                            LibraryTypeMemberGroupPopulationInspectionFailure
                                    .ManagedModule =>
                                TypeMemberGroupPopulationInspectionFailure
                                    .ManagedModule,
                            LibraryTypeMemberGroupPopulationInspectionFailure
                                    .UnsupportedWindowsMetadata =>
                                TypeMemberGroupPopulationInspectionFailure
                                    .UnsupportedWindowsMetadata,
                            LibraryTypeMemberGroupPopulationInspectionFailure
                                    .MalformedMetadata =>
                                TypeMemberGroupPopulationInspectionFailure
                                    .MalformedMetadata,
                            LibraryTypeMemberGroupPopulationInspectionFailure
                                    .EmptyModuleVersionId =>
                                TypeMemberGroupPopulationInspectionFailure
                                    .EmptyModuleVersionId,
                            _ => throw new InvalidOperationException(
                                "Unknown Library Type Member-group failure."),
                        }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type Member-group outcome."),
        };

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Project(
        LibraryTypeMemberGroupPopulationCorrespondence correspondence,
        TypeMemberGroupPopulationInspectionRequest request,
        int startOrdinal) =>
        Envelope(
            ProjectPopulation(
                correspondence.AssemblyIdentity,
                correspondence.ModuleVersionId,
                correspondence.AssemblyBytes,
                correspondence.Population,
                request.Plan,
                startOrdinal));

    internal static TypeMemberGroupPopulationInspectionOutcome
        ProjectPopulation(
            AssemblyReferenceIdentity assemblyIdentity,
            Guid moduleVersionId,
            int assemblyBytes,
            MetadataTypeMemberGroupPopulationOutcome population,
            TypeMemberGroupPopulationInspectionPlan plan,
            int startOrdinal) =>
        population switch
        {
            MetadataTypeMemberGroupPopulationOutcome.Available available =>
                AvailableOutcome(
                    assemblyIdentity,
                    moduleVersionId,
                    assemblyBytes,
                    plan,
                    available.Population,
                    startOrdinal),
            MetadataTypeMemberGroupPopulationOutcome.TypeNotFound =>
                new TypeMemberGroupPopulationInspectionOutcome.Rejected(
                    TypeMemberGroupPopulationInspectionRejection
                        .TypeNotFound),
            MetadataTypeMemberGroupPopulationOutcome.TypeAmbiguous =>
                new TypeMemberGroupPopulationInspectionOutcome.Rejected(
                    TypeMemberGroupPopulationInspectionRejection
                        .TypeAmbiguous),
            MetadataTypeMemberGroupPopulationOutcome.Incomplete incomplete =>
                new TypeMemberGroupPopulationInspectionOutcome.Incomplete(
                    incomplete.Bound switch
                    {
                        MetadataTypeMemberGroupPopulationBound
                                .MetadataRows =>
                            TypeMemberGroupPopulationBound.MetadataRows,
                        MetadataTypeMemberGroupPopulationBound.Members =>
                            TypeMemberGroupPopulationBound.Members,
                        _ => throw new InvalidOperationException(
                            "Unknown Metadata Type Member-group bound."),
                    },
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeMemberGroupPopulationOutcome.Failed =>
                new TypeMemberGroupPopulationInspectionOutcome.Failed(
                    TypeMemberGroupPopulationInspectionFailure
                        .MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type Member-group outcome."),
        };

    private static TypeMemberGroupPopulationInspectionOutcome
        AvailableOutcome(
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        int assemblyBytes,
        TypeMemberGroupPopulationInspectionPlan plan,
        MetadataTypeMemberGroupPopulation population,
        int startOrdinal)
    {
        TypeMemberGroupPopulationExecutionPlan query =
            plan.Query;
        LibraryAssemblyIdentity assembly =
            PortableIdentity(assemblyIdentity);
        var binding = new TypeMemberGroupPopulationBinding(
            assembly,
            moduleVersionId,
            population.Binding.Type,
            population.Binding.TypeDefinitionToken,
            query.Spelling,
            query.IncludeHidden,
            query.Accessibility,
            query.Receiver,
            query.Ordering);
        if (plan.Members.Rows?.Continuation is { } continuation
            && continuation.Binding.TypeDefinitionToken
                != binding.TypeDefinitionToken)
        {
            return new TypeMemberGroupPopulationInspectionOutcome.Rejected(
                TypeMemberGroupPopulationInspectionRejection
                    .StaleContinuation);
        }

        TypeMemberGroupCountOutcome? count =
            query.Terminal is QuerySpaceTerminalRequirement.Count
                ? new TypeMemberGroupCountOutcome.Counted(
                    population.Count
                    ?? throw new InvalidOperationException(
                        "The Metadata Type Member-group Count was not returned."))
                : null;
        TypeMemberGroupRowsOutcome? rows =
            query.Terminal is QuerySpaceTerminalRequirement.Rows
                ? Rows(
                    population.Rows
                    ?? throw new InvalidOperationException(
                        "The Metadata Type Member-group Rows were not returned."),
                    binding,
                    plan.Members.Rows!,
                    startOrdinal,
                    plan.Bounds.MaxRetainedTextCharacters)
                : null;
        TypeMemberCompositionCount? composition =
            !query.IncludesComposition
                ? null
                : Composition(
                    population.Composition
                    ?? throw new InvalidOperationException(
                        "The Metadata Type Member composition was not returned."));
        TypeMemberSelectorCounts? selectorCounts =
            !query.IncludesSelectorCounts
                ? null
                : SelectorCounts(
                    population.SelectorCounts
                    ?? throw new InvalidOperationException(
                        "The Metadata Type Member selector Counts were not returned."));

        return new TypeMemberGroupPopulationInspectionOutcome.Available(
            new(
                assembly,
                plan.Type,
                new(
                    binding,
                    count,
                    rows,
                    composition,
                    selectorCounts),
                assemblyBytes));
    }

    internal static MetadataTypeMemberGroupPopulationRequest
        CreateMetadataRequest(
            TypeMemberGroupPopulationInspectionPlan plan,
            int startOrdinal)
    {
        TypeMemberGroupPopulationExecutionPlan query = plan.Query;
        TypeMemberGroupRowsRequest? rows =
            query.Terminal is QuerySpaceTerminalRequirement.Rows
                ? plan.Members.Rows
                : null;
        return new(
            plan.Type,
            Spelling(query.Spelling),
            query.IncludeHidden,
            Accessibility(query.Accessibility),
            Receiver(query.Receiver),
            query.Terminal is QuerySpaceTerminalRequirement.Count
                ? new MetadataTypeMemberGroupCountRequest()
                : null,
            rows is null
                ? null
                : new(
                    rows.MaximumRows,
                    startOrdinal,
                    query.IncludesExactMemberCount),
            query.IncludesComposition,
            query.IncludesSelectorCounts);
    }

    private static TypeMemberGroupRowsOutcome Rows(
        MetadataTypeMemberGroupRows source,
        TypeMemberGroupPopulationBinding binding,
        TypeMemberGroupRowsRequest request,
        int startOrdinal,
        long maximumRetainedTextCharacters)
    {
        if (source.ContinuationOutOfRange)
        {
            return new TypeMemberGroupRowsOutcome.Rejected(
                TypeMemberGroupRowsRejection.ContinuationOutOfRange);
        }
        if (source.IncompleteRetainedTextCharacters is { } measured)
        {
            return new TypeMemberGroupRowsOutcome.Incomplete(
                TypeMemberGroupPopulationBound.RetainedTextCharacters,
                maximumRetainedTextCharacters,
                measured);
        }

        ImmutableArray<TypeMemberGroupShape> items =
            [
                .. source.Items.Select(
                    (row, index) =>
                    {
                        var rowBinding = new TypeMemberGroupRowBinding(
                            binding,
                            Field(row.Name),
                            Category(row.Category),
                            MemberGroupRole.Declared);
                        return new TypeMemberGroupShape(
                            rowBinding,
                            checked(startOrdinal + index + 1),
                            Receivers(row.Receivers),
                            row.ExactMemberCount,
                            row.SharedGenericParameters is { } parameters
                                ? [.. parameters.Select(Field)]
                                : null,
                            row.Traits is { } traits
                                ? TraitCounts(traits)
                                : null);
                    }),
            ];
        return new TypeMemberGroupRowsOutcome.Read(
            request.Ordering,
            items,
            source.NextOrdinal is { } next
                ? new TypeMemberGroupContinuation(
                    binding,
                    next,
                    request.IncludeExactMemberCount)
                : null);
    }

    private static TypeMemberCompositionCount Composition(
        MetadataTypeMemberComposition composition) =>
        new(
            composition.Public,
            composition.Protected,
            composition.Internal,
            composition.Private,
            composition.Static,
            composition.This,
            composition.Extension);

    private static TypeMemberSelectorCounts SelectorCounts(
        MetadataTypeMemberSelectorCounts counts) =>
        new(
            [
                .. counts.Kinds.Select(count =>
                    new TypeMemberKindCount(
                        Category(count.Value),
                        count.Count)),
            ],
            new(
                counts.Traits.All,
                counts.Traits.BodyBacked,
                counts.Traits.Static,
                counts.Traits.Instance,
                counts.Traits.Virtual,
                counts.Traits.Interface,
                counts.Traits.Extensions));

    private static TypeMemberTraitCounts TraitCounts(
        MetadataTypeMemberTraitCounts counts) =>
        new(
            counts.All,
            counts.BodyBacked,
            counts.Static,
            counts.Instance,
            counts.Virtual,
            counts.Interface,
            counts.Extensions);

    internal static bool IsCompatible(
        TypeMemberGroupContinuation continuation,
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName type,
        TypeMemberGroupPopulationExecutionPlan query)
    {
        TypeMemberGroupPopulationBinding binding =
            continuation.Binding;
        return assembly.IsEquivalentTo(
            new(
                binding.Assembly.Name.ToString(),
                binding.Assembly.Version,
                binding.Assembly.Culture?.ToString(),
                binding.Assembly.PublicKeyToken?.ToString()))
        && binding.Type == type
        && binding.Spelling == query.Spelling
        && binding.IncludeHidden == query.IncludeHidden
        && binding.Accessibility == query.Accessibility
        && binding.Receiver == query.Receiver
        && binding.Ordering == query.Ordering
        && continuation.IncludeExactMemberCount
            == query.IncludesExactMemberCount;
    }

    internal static MetadataMemberSpelling Spelling(
        TypeMemberGroupSpelling spelling) =>
        spelling switch
        {
            TypeMemberGroupSpelling.CSharp =>
                MetadataMemberSpelling.CSharp,
            TypeMemberGroupSpelling.Metadata =>
                MetadataMemberSpelling.Metadata,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group spelling."),
        };

    internal static MetadataMethodAccessibilityFilter Accessibility(
        TypeMemberGroupAccessibilityFilter accessibility) =>
        accessibility switch
        {
            TypeMemberGroupAccessibilityFilter.Public =>
                MetadataMethodAccessibilityFilter.Public,
            TypeMemberGroupAccessibilityFilter.Protected =>
                MetadataMethodAccessibilityFilter.Protected,
            TypeMemberGroupAccessibilityFilter.Internal =>
                MetadataMethodAccessibilityFilter.Internal,
            TypeMemberGroupAccessibilityFilter.Private =>
                MetadataMethodAccessibilityFilter.Private,
            TypeMemberGroupAccessibilityFilter.All =>
                MetadataMethodAccessibilityFilter.All,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group accessibility."),
        };

    internal static MetadataTypeMemberGroupReceiverFilter Receiver(
        TypeMemberGroupReceiverFilter receiver) =>
        receiver switch
        {
            TypeMemberGroupReceiverFilter.All =>
                MetadataTypeMemberGroupReceiverFilter.All,
            TypeMemberGroupReceiverFilter.This =>
                MetadataTypeMemberGroupReceiverFilter.This,
            TypeMemberGroupReceiverFilter.Static =>
                MetadataTypeMemberGroupReceiverFilter.Static,
            TypeMemberGroupReceiverFilter.Extension =>
                MetadataTypeMemberGroupReceiverFilter.Extension,
            TypeMemberGroupReceiverFilter.NonExtension =>
                MetadataTypeMemberGroupReceiverFilter.NonExtension,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group receiver."),
        };

    internal static MemberGroupCategory Category(
        MetadataTypeMemberGroupCategory category) =>
        category switch
        {
            MetadataTypeMemberGroupCategory.Method =>
                MemberGroupCategory.Method,
            MetadataTypeMemberGroupCategory.Constructor =>
                MemberGroupCategory.Constructor,
            MetadataTypeMemberGroupCategory.Operator =>
                MemberGroupCategory.Operator,
            MetadataTypeMemberGroupCategory.Finalizer =>
                MemberGroupCategory.Finalizer,
            MetadataTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation =>
                MemberGroupCategory.ExplicitInterfaceImplementation,
            MetadataTypeMemberGroupCategory.Property =>
                MemberGroupCategory.Property,
            MetadataTypeMemberGroupCategory.Field =>
                MemberGroupCategory.Field,
            MetadataTypeMemberGroupCategory.Event =>
                MemberGroupCategory.Event,
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type Member-group category."),
        };

    private static MemberGroupCategory Category(string category) =>
        category switch
        {
            "method" => MemberGroupCategory.Method,
            "constructor" => MemberGroupCategory.Constructor,
            "operator" => MemberGroupCategory.Operator,
            "finalizer" => MemberGroupCategory.Finalizer,
            "explicit-interface-implementation" =>
                MemberGroupCategory.ExplicitInterfaceImplementation,
            "property" => MemberGroupCategory.Property,
            "field" => MemberGroupCategory.Field,
            "event" => MemberGroupCategory.Event,
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type Member selector kind."),
        };

    private static MemberGroupReceiverForms Receivers(
        MetadataTypeMemberGroupReceiverForms receivers)
    {
        MemberGroupReceiverForms result =
            MemberGroupReceiverForms.None;
        if ((receivers
            & MetadataTypeMemberGroupReceiverForms.Static) != 0)
        {
            result |= MemberGroupReceiverForms.Static;
        }
        if ((receivers
            & MetadataTypeMemberGroupReceiverForms.This) != 0)
        {
            result |= MemberGroupReceiverForms.This;
        }
        if ((receivers
            & MetadataTypeMemberGroupReceiverForms.Extension) != 0)
        {
            result |= MemberGroupReceiverForms.Extension;
        }
        return result;
    }

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Rejected(
            TypeMemberGroupPopulationInspectionRejection reason) =>
        Envelope(
            new TypeMemberGroupPopulationInspectionOutcome.Rejected(
                reason));

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Incomplete(
            TypeMemberGroupPopulationBound bound,
            long limit,
            long measured) =>
        Envelope(
            new TypeMemberGroupPopulationInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Failed(
            TypeMemberGroupPopulationInspectionFailure reason) =>
        Envelope(
            new TypeMemberGroupPopulationInspectionOutcome.Failed(
                reason));

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Envelope(
            TypeMemberGroupPopulationInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));

    internal static LibraryAssemblyIdentity PortableIdentity(
        AssemblyReferenceIdentity identity) =>
        new(
            Field(identity.Name),
            identity.Version
                ?? throw new InvalidOperationException(
                    "The assembly identity has no version."),
            identity.Culture is null
                ? null
                : Field(identity.Culture),
            identity.PublicKeyToken is null
                ? null
                : Field(identity.PublicKeyToken));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
