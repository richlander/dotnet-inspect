using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>Content-shaped request for one group-scoped Research type projection.</summary>
public sealed record AssemblyContextTypeProjectionRequest(
    string Type,
    bool PublicOnly = false,
    bool Composition = true,
    bool RelationshipGraph = true);

/// <summary>Content-shaped request for one group-scoped Research member projection.</summary>
/// <param name="MethodToken">
/// The exact <c>MethodDef</c> token to project. Supplying it addresses one overload — or one
/// property/event accessor body — without name-and-index guessing, which is what a consumer that
/// already holds a surface's body selectors should do.
/// </param>
/// <param name="AnalysisFeatures">
/// The whole-assembly Analysis features the projection's fact context is built with. The default
/// matches what the Research fact producers observe through.
/// </param>
public sealed record AssemblyContextMemberProjectionRequest(
    string Type,
    string Member,
    int OverloadIndex = 0,
    int? MethodToken = null,
    bool PublicOnly = false,
    bool AnnotatedSource = false,
    bool SourceDocument = false,
    bool FactRows = false,
    AnnotationStage AnnotatedStage = AnnotationStage.Raised,
    PrinterOptions? PrinterOptions = null,
    LibraryBodyAnalysisFeatures AnalysisFeatures = LibraryBodyAnalysisFeatures.Default);

/// <summary>Why a member projection's whole-assembly fact context is narrower than a complete one.</summary>
public enum MemberProjectionContextLimitationKind
{
    /// <summary>
    /// The whole-assembly Analysis index could not be built from the participant's image, so the
    /// projection observed a consistent absence of assembly-scoped facts rather than the facts
    /// themselves.
    /// </summary>
    AssemblyContextUnavailable,
}

/// <summary>
/// A visible narrowing of a member projection's fact context. A projection that could not build
/// the whole-assembly context carries this instead of silently omitting the facts that context
/// produces.
/// </summary>
public sealed record MemberProjectionContextLimitation(
    MemberProjectionContextLimitationKind Kind,
    string Detail);

/// <summary>One participant's member projection and any narrowing of its fact context.</summary>
public sealed record AssemblyMemberProjection(
    ResearchViews.MemberProjectionResult Projection,
    MemberProjectionContextLimitation? ContextLimitation);

/// <summary>
/// Projects the Research type view from participants of one binding-consistent assembly context
/// group, without a filesystem path.
/// </summary>
/// <remarks>
/// The query owns the <see cref="MetadataSource"/> it opens over the group's immutable image
/// snapshot, and resolves that source's assembly references through the participant's own binding
/// policy rather than by guessing names. Consumers receive a typed per-participant outcome.
/// Pathless projection and binding-consistent resolution are gated by
/// <c>AssemblyContextResearchProjectionQueryTests</c>.
/// </remarks>
public static class AssemblyContextTypeProjectionQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ResearchViews.TypeProjectionResult>> Definition { get; } =
        new("Assembly context type projection", InspectionCost.Unbounded);

    public static AssemblyContextResult<ResearchViews.TypeProjectionResult> Execute(
        AssemblyContextGroup group,
        AssemblyContextTypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        return AssemblyContextQueryExecutor.ExecuteOverSnapshots(
            group,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    public static AssemblyContextEntry<ResearchViews.TypeProjectionResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextTypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    static ResearchViews.TypeProjectionResult Project(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextTypeProjectionRequest request)
    {
        using MetadataSource source =
            AssemblyContextResearchSource.Open(group, subject, snapshot);
        return ResearchViews.ProjectType(
            new ResearchViews.TypeProjectionRequest(
                source,
                request.Type,
                request.PublicOnly,
                request.Composition,
                request.RelationshipGraph));
    }
}

/// <summary>
/// Projects the Research member view — including the portable
/// <see cref="AnnotatedSourceDocument"/> — from participants of one binding-consistent assembly
/// context group, without a filesystem path.
/// </summary>
/// <remarks>
/// The query owns both lifetimes the projection needs: the <see cref="MetadataSource"/> over the
/// group's immutable image snapshot, and the whole-assembly <see cref="LibraryBodyIndex"/> the
/// Research fact producers observe through. Path-keyed Analysis resolution cannot reach a
/// snapshot, so the context is supplied explicitly; when it cannot be built the result carries a
/// <see cref="MemberProjectionContextLimitation"/> rather than a fact-free projection that reads
/// as complete. Gated by <c>AssemblyContextResearchProjectionQueryTests</c>.
/// </remarks>
public static class AssemblyContextMemberProjectionQuery
{
    public static InspectionQuery<
        AssemblyContextResult<AssemblyMemberProjection>> Definition { get; } =
        new("Assembly context member projection", InspectionCost.Unbounded);

    public static AssemblyContextResult<AssemblyMemberProjection> Execute(
        AssemblyContextGroup group,
        AssemblyContextMemberProjectionRequest request)
    {
        Validate(request);
        return AssemblyContextQueryExecutor.ExecuteOverSnapshots(
            group,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    public static AssemblyContextEntry<AssemblyMemberProjection> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextMemberProjectionRequest request)
    {
        Validate(request);
        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    static void Validate(AssemblyContextMemberProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Member);
        ArgumentOutOfRangeException.ThrowIfNegative(request.OverloadIndex);
    }

    static AssemblyMemberProjection Project(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextMemberProjectionRequest request)
    {
        LibraryBodyIndex? index = null;
        MemberProjectionContextLimitation? limitation = null;
        try
        {
            index = LibraryBodyIndex.OpenFromPrefetchedImage(
                AssemblyContextResearchSource.Name(subject),
                snapshot.Content,
                request.AnalysisFeatures,
                AssemblyContextResearchSource.Resolver(group, subject));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException
                or NotSupportedException)
        {
            limitation = new MemberProjectionContextLimitation(
                MemberProjectionContextLimitationKind.AssemblyContextUnavailable,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            using MetadataSource source =
                AssemblyContextResearchSource.Open(group, subject, snapshot);
            ResearchViews.MemberProjectionResult projection =
                ResearchViews.ProjectMember(
                    new ResearchViews.MemberProjectionRequest(
                        source,
                        request.Type,
                        request.Member,
                        request.OverloadIndex,
                        request.PublicOnly,
                        request.AnnotatedSource,
                        CostOverlay: false,
                        SemanticsOverlay: false,
                        request.FactRows,
                        request.AnnotatedStage,
                        Registry: null,
                        request.MethodToken,
                        request.PrinterOptions,
                        CaretFocus: null,
                        request.SourceDocument,
                        index is null ? null : ResearchAssemblyContext.Create(index)));
            return new AssemblyMemberProjection(projection, limitation);
        }
        finally
        {
            // The index holds derived call-graph maps for the whole assembly. It is not
            // disposable, so hand that memory back explicitly before the query returns rather
            // than leaving it to a browser's collector.
            index?.ReleaseCallGraphCaches();
        }
    }
}

/// <summary>
/// Opens the Research inputs for one group participant from workspace-owned content: a
/// <see cref="MetadataSource"/> over the retained immutable image, and reference resolution that
/// answers from the participant's own binding policy.
/// </summary>
internal static class AssemblyContextResearchSource
{
    internal static MetadataSource Open(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot)
        => MetadataSource.OpenWithoutSymbols(
            snapshot.RetainAssemblyReference(Participant(group, subject).Assembly),
            Resolver(group, subject));

    /// <summary>
    /// The name Analysis and the decompiler label this assembly by. It is a label, not a file:
    /// a participant acquired from content has no path.
    /// </summary>
    internal static string Name(AssemblyContextSubject subject)
        => subject.Identity.Name;

    internal static IAssemblyReferenceResolver Resolver(
        AssemblyContextGroup group,
        AssemblyContextSubject subject)
        => new BindingPolicyResolver(group, Participant(group, subject));

    static AssemblyContextParticipant Participant(
        AssemblyContextGroup group,
        AssemblyContextSubject subject)
        => group.Participants.Single(
            candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                subject.Registration));

    /// <summary>
    /// Answers reference resolution from the participant's binding policy — the same
    /// source-relative snapshot the group is consistent with — rather than by matching simple
    /// names. Only a selected sibling participant is returned, as a snapshot-backed descriptor,
    /// so resolution cannot acquire content outside the group's ownership and byte budget.
    /// </summary>
    sealed class BindingPolicyResolver(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
        : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(identity);
            AssemblyBindingSelection selection = participant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.FromAssembly(participant.Assembly),
                    scope));
            if (selection is not AssemblyBindingSelection.Selected selected)
                return null;

            ImmutableArray<AssemblyContextParticipant> participants = group.Participants;
            bool isParticipant = participants.Any(
                candidate => ReferenceEquals(
                    candidate.Assembly.Registration,
                    selected.Assembly.Registration));
            if (!isParticipant)
                return null;

            return group.RetainAssemblyReference(selected.Assembly)
                is AssemblyImageAccessResult<ResolvedAssemblyReference>.Available retained
                ? retained.Value
                : null;
        }
    }
}
