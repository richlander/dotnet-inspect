using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

using ILInspector.ILDiff;

namespace DotnetInspector.ResearchSections;

/// <summary>
/// Diff's Compare-participating analyses. Each registration holds the
/// owner-issued analysis descriptor and its typed producer per report
/// surface; explanation, <c>-D</c>, help, and dispatch all read these
/// registrations through Inspection Capability Composition.
/// </summary>
public static class DiffAnalysisCatalog
{
    public static AnalysisDeclarationId ApiRoute { get; } =
        new("producer.diff.api-comparison");

    public static AnalysisDeclarationId ApiAttributeRoute { get; } =
        new("producer.diff.api-attribute-comparison");

    public static AnalysisDeclarationId StringLiteralRoute { get; } =
        new("producer.diff.string-literal-comparison");

    /// <summary>The unsafe member census comparison (<c>UnsafeMemberComparisonQuery</c>).</summary>
    public static AnalysisDeclarationId UnsafeMemberRoute { get; } =
        new("producer.diff.unsafe-member-comparison");

    /// <summary>The typed body-signal comparison (<c>BodySignalComparisonQuery</c>).</summary>
    public static AnalysisDeclarationId BodySignalRoute { get; } =
        new("producer.diff.body-signal-comparison");

    /// <summary>The retained Research diff comparison (<c>ResearchDiff</c>).</summary>
    public static AnalysisDeclarationId RetainedResearchRoute { get; } =
        new("producer.diff.research-retained-comparison");

    static readonly AnalysisTargetRoleDescriptor LibraryAnchor = new(
        new("target.diff.library-pair"),
        AnalysisTargetFunction.PrivilegedAnchor,
        minimumCount: 1,
        maximumCount: 1);

    static readonly AnalysisTargetRoleDescriptor TypeAnchors = new(
        new("target.diff.type"),
        AnalysisTargetFunction.PrivilegedAnchor,
        minimumCount: 1,
        maximumCount: int.MaxValue);

    static readonly AnalysisTargetRoleDescriptor MemberAnchors = new(
        new("target.diff.member"),
        AnalysisTargetFunction.PrivilegedAnchor,
        minimumCount: 1,
        maximumCount: int.MaxValue);

    static readonly AnalysisTargetRoleDescriptor SingleMemberAnchor = new(
        new("target.diff.single-member"),
        AnalysisTargetFunction.PrivilegedAnchor,
        minimumCount: 1,
        maximumCount: 1);

    /// <summary>The per-Finding transition projection every Compare analysis supports.</summary>
    public static AnalysisProjectionDescriptor TransitionsProjection { get; } =
        new(new("projection.diff.finding-transitions"));

    static DiffAnalysisCatalog()
    {
        Registrations =
        [
            Register(
                "api",
                InspectionCost.NetworkFree,
                [
                    (AnalysisReportSurfaceKind.Library, LibraryAnchor,
                        [MetadataFindings.TypeDescriptor, MetadataFindings.MemberDescriptor],
                        false),
                    (AnalysisReportSurfaceKind.Type, TypeAnchors,
                        [MetadataFindings.TypeDescriptor, MetadataFindings.MemberDescriptor],
                        true),
                    (AnalysisReportSurfaceKind.Member, MemberAnchors,
                        [MetadataFindings.MemberDescriptor],
                        true),
                ],
                ApiRoute,
                ProduceApi),
            Register(
                "api-attribute",
                InspectionCost.NetworkFree,
                [
                    (AnalysisReportSurfaceKind.Type, TypeAnchors,
                        [MetadataFindings.AttributeDescriptor],
                        true),
                ],
                ApiAttributeRoute,
                ProduceApiAttributes),
            RegisterBody("allocation", AnalysisFindings.AllocationDescriptor, ProduceBody<AllocationOccurrence>),
            RegisterBody("call-site", AnalysisFindings.CallSiteDescriptor, ProduceBody<DirectCall>),
            RegisterBody("unsafety", AnalysisFindings.UnsafetyDescriptor, ProduceBody<UnsafetyOccurrence>),
            Register(
                "string-literals",
                InspectionCost.Moderated,
                [
                    (
                        AnalysisReportSurfaceKind.Library,
                        LibraryAnchor,
                        new[] { StringLiteralUseFindings.Descriptor },
                        true),
                ],
                StringLiteralRoute,
                ProduceStringLiterals),
            Register(
                "unsafe-member",
                InspectionCost.Moderated,
                [
                    (
                        AnalysisReportSurfaceKind.Library,
                        LibraryAnchor,
                        new[] { AnalysisFindings.UnsafeMemberDescriptor },
                        true),
                    (
                        AnalysisReportSurfaceKind.Type,
                        TypeAnchors,
                        new[] { AnalysisFindings.UnsafeMemberDescriptor },
                        true),
                ],
                UnsafeMemberRoute,
                ProduceUnsafeMembers),
            Register(
                "csharp",
                InspectionCost.Moderated,
                [
                    (AnalysisReportSurfaceKind.Member, SingleMemberAnchor,
                        [CSharpFindings.LineDescriptor],
                        true),
                ],
                RetainedResearchRoute,
                ProduceRetained<CSharpCanonicalLine>),
            Register(
                "il",
                InspectionCost.Moderated,
                [
                    (AnalysisReportSurfaceKind.Member, SingleMemberAnchor,
                        [IlFindings.OperationDescriptor],
                        true),
                ],
                RetainedResearchRoute,
                ProduceRetained<CanonicalIlOperation>),
        ];

        Operation = new AnalysisOperationDefinition(
            AnalysisOperationKind.Compare,
            AnalysisQuestionMode.Targeted,
            ["api"]);
        Document = new(
            new(
                DocumentIdentity,
                "Diff analysis",
                "The registered analyses' comparison of two versions of one "
                    + "library, type, or member, with the selected Changes, "
                    + "Summary, and Transitions views.",
                DiffAnalysisInspection.ResultContractIdentity));
        Route = new(
            new(
                RouteIdentity,
                "Diff analysis",
                "Compares two endpoints through one validated analysis set "
                    + "and returns the Diff analysis document."),
            Document,
            static (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    DiffAnalysisInspection.Execute(request));
            },
            Operation);
        ProductModule = new InspectionCapabilityModule(
            "diff.analyses",
            documents: [Document],
            routes: [Route],
            analyses: Registrations);
    }

    public const string DocumentIdentity = "diff-analysis/document/v1";

    public const string RouteIdentity = "diff-analysis/route/compare";

    /// <summary>The Diff analysis document Compare produces.</summary>
    public static InspectionDocumentRegistration<DiffAnalysisDocument>
        Document { get; }

    /// <summary>
    /// Diff's Compare operation as an inspection route: its typed entry
    /// point runs one validated analysis set without a Query Space.
    /// </summary>
    public static InspectionRouteRegistration<
        DiffAnalysisInspectionRequest,
        DiffAnalysisDocument> Route { get; }

    /// <summary>Registrations in product order.</summary>
    public static ImmutableArray<InspectionAnalysisRegistration> Registrations { get; }

    /// <summary>Diff's Compare operation and its operation-owned default set (<c>api</c>).</summary>
    public static AnalysisOperationDefinition Operation { get; }

    /// <summary>The host-neutral capability module registering Diff's analyses.</summary>
    public static InspectionCapabilityModule ProductModule { get; }

    /// <summary>
    /// Whether the selected Compare set dispatches the retained implementation
    /// producer route.
    /// </summary>
    public static bool RequiresImplementation(
        AnalysisSetValidationResult.Accepted selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Analyses.Any(analysis =>
            Registrations
                .Single(registration =>
                    ReferenceEquals(registration.Analysis, analysis))
                .ProducerFor(selection.Operation, selection.Surface)?
                .Participation.ProducerRoute == RetainedResearchRoute);
    }

    /// <summary>
    /// Whether every selected analysis compares API metadata only, through
    /// the API or API-attribute route, and reads no method body.
    /// </summary>
    public static bool ReadsOnlyApiSurface(
        AnalysisSetValidationResult.Accepted selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Analyses.All(analysis =>
            analysis.ParticipationFor(selection.Operation)
                ?.For(selection.Surface)
                ?.ProducerRoute is { } route
            && (route == ApiRoute || route == ApiAttributeRoute));
    }

    public static bool RequiresStringLiteralQuery(
        AnalysisSetValidationResult.Accepted selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Analyses.Any(analysis =>
            analysis.ParticipationFor(selection.Operation)
                ?.For(selection.Surface)
                ?.ProducerRoute == StringLiteralRoute);
    }

    /// <summary>
    /// The identities of the analyses that declare the Transitions view at a
    /// report surface, in product order.
    /// </summary>
    public static ImmutableArray<string> TransitionAnalysesAt(
        AnalysisReportSurfaceKind surface) =>
        [
            .. Registrations
                .Where(registration => registration.Analysis
                    .ParticipationFor(AnalysisOperationKind.Compare)
                    ?.For(surface)
                    ?.Projections.Any(projection =>
                        projection.Id == TransitionsProjection.Id)
                    == true)
                .Select(static registration => registration.Analysis.Id.Value),
        ];

    public static bool RequiresUnsafeMemberComparison(
        AnalysisSetValidationResult.Accepted selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Analyses.Any(analysis =>
            analysis.ParticipationFor(selection.Operation)
                ?.For(selection.Surface)
                ?.ProducerRoute == UnsafeMemberRoute);
    }

    static InspectionAnalysisRegistration RegisterBody(
        string identity,
        FindingDescriptor descriptor,
        Func<DiffAnalysisProducerContext, FindingDescriptor, DiffAnalysisProduction> produce)
        => Register(
            identity,
            InspectionCost.Moderated,
            [(
                AnalysisReportSurfaceKind.Member,
                SingleMemberAnchor,
                new[] { descriptor },
                true)],
            BodySignalRoute,
            context => produce(context, descriptor));

    static InspectionAnalysisRegistration Register(
        string identity,
        InspectionCost cost,
        (AnalysisReportSurfaceKind Surface,
            AnalysisTargetRoleDescriptor Anchor,
            FindingDescriptor[] Descriptors,
            bool ExposesTransitions)[] surfaces,
        AnalysisDeclarationId route,
        Func<DiffAnalysisProducerContext, DiffAnalysisProduction> produce)
    {
        AnalysisSurfaceParticipation[] participations =
        [
            .. surfaces.Select(surface => new AnalysisSurfaceParticipation(
                surface.Surface,
                surface.Descriptors,
                route,
                surface.ExposesTransitions ? [TransitionsProjection] : [])),
        ];
        var descriptor = new AnalysisDescriptor(
            new AnalysisDeclarationId(identity),
            revision: 1,
            cost,
            [AnalysisQuestionMode.Targeted],
            [
                .. surfaces.Select(surface => new AnalysisReportSurfaceSupport(
                    surface.Surface,
                    AnalysisQuestionMode.Targeted,
                    [surface.Anchor])),
            ],
            universeRequirements: [],
            structuralPrerequisites: [],
            hostRequirements: [],
            [new AnalysisProjectionSupport(TransitionsProjection, [AnalysisQuestionMode.Targeted])],
            [new AnalysisOperationParticipation(AnalysisOperationKind.Compare, participations)]);
        return new InspectionAnalysisRegistration(
            descriptor,
            [
                .. participations.Select(participation =>
                    new InspectionAnalysisProducerBinding<
                        DiffAnalysisProducerContext,
                        DiffAnalysisProduction>(
                        AnalysisOperationKind.Compare,
                        participation,
                        produce)),
            ]);
    }

    static DiffAnalysisProduction ProduceApi(DiffAnalysisProducerContext context)
        => DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                context.Input.PrecomputedApiComparison
                    ?? ApiComparisonQuery.Execute(
                        context.Input.FromSurface,
                        context.Input.ToSurface)));

    static DiffAnalysisProduction ProduceApiAttributes(DiffAnalysisProducerContext context)
    {
        var subject = new FindingSubject("api", "API surface");
        return DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                new RetainedFindingComparisonSet(
                    context.Input.TypeNames.Select(typeName =>
                        new RetainedFindingComparison<ApiAttributeHandle>(
                            new ResearchSubjectKey(
                                ResearchSubjectKind.Type,
                                typeName,
                                typeName,
                                typeName),
                            MetadataFindings.AttributeDescriptor,
                            MetadataFindings.CompareApiAttributes(
                                context.Input.FromSurface,
                                context.Input.ToSurface,
                                subject,
                                typeName))))));
    }

    static DiffAnalysisProduction ProduceStringLiterals(
        DiffAnalysisProducerContext context)
        => DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                (context.Input.PrepareStringLiterals
                    ?? throw new InvalidOperationException(
                        "The string-literal comparison was not prepared."))()));

    static DiffAnalysisProduction ProduceUnsafeMembers(
        DiffAnalysisProducerContext context)
        => DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                (context.Input.PrepareUnsafeMembers
                    ?? throw new InvalidOperationException(
                        "The unsafe member comparison was not prepared."))()));

    static DiffAnalysisProduction ProduceBody<T>(
        DiffAnalysisProducerContext context,
        FindingDescriptor descriptor)
        where T : notnull
    {
        ResearchComparison bodySignals = context.BodySignals
            ?? throw new InvalidOperationException(
                "The body-signal comparison was not prepared.");
        return DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                new RetainedFindingComparisonSet(
                    bodySignals.RetainedComparisons.Get<T>(descriptor))));
    }

    static DiffAnalysisProduction ProduceRetained<T>(
        DiffAnalysisProducerContext context)
        where T : notnull
    {
        FindingDescriptor descriptor = context.Participation.Descriptors.Single();
        DiffAnalysisInput input = context.Input;
        ImplementationDiffResult implementation =
            input.PrepareImplementation?.Invoke()
            ?? throw new InvalidOperationException(
                "The implementation comparison was not prepared.");
        return DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                new RetainedFindingComparisonSet(
                    implementation.Research.RetainedComparisons.Get<T>(
                        descriptor))));
    }
}
