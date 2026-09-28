using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

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
                        [MetadataFindings.TypeDescriptor, MetadataFindings.MemberDescriptor]),
                    (AnalysisReportSurfaceKind.Type, TypeAnchors,
                        [MetadataFindings.TypeDescriptor, MetadataFindings.MemberDescriptor]),
                    (AnalysisReportSurfaceKind.Member, MemberAnchors,
                        [MetadataFindings.MemberDescriptor]),
                ],
                ApiRoute,
                ProduceApi),
            Register(
                "api-attribute",
                InspectionCost.NetworkFree,
                [
                    (AnalysisReportSurfaceKind.Type, TypeAnchors,
                        [MetadataFindings.AttributeDescriptor]),
                ],
                ApiAttributeRoute,
                ProduceApiAttributes),
            RegisterBody("allocation", AnalysisFindings.AllocationDescriptor, ProduceBody<AllocationOccurrence>),
            RegisterBody("call-site", AnalysisFindings.CallSiteDescriptor, ProduceBody<DirectCall>),
            RegisterBody("unsafety", AnalysisFindings.UnsafetyDescriptor, ProduceBody<UnsafetyOccurrence>),
            Register(
                "csharp",
                InspectionCost.Moderated,
                [
                    (AnalysisReportSurfaceKind.Member, SingleMemberAnchor,
                        [CSharpFindings.LineDescriptor]),
                ],
                RetainedResearchRoute,
                context => ProduceRetained<CSharpCanonicalLine>(
                    context,
                    ResearchChangeMechanism.CSharp)),
            Register(
                "il",
                InspectionCost.Moderated,
                [
                    (AnalysisReportSurfaceKind.Member, SingleMemberAnchor,
                        [IlFindings.OperationDescriptor]),
                ],
                RetainedResearchRoute,
                context => ProduceRetained<CanonicalIlOperation>(
                    context,
                    ResearchChangeMechanism.IlBody)),
        ];

        Operation = new AnalysisOperationDefinition(
            AnalysisOperationKind.Compare,
            ["api"]);
        ProductModule = new InspectionCapabilityModule(
            "diff.analyses",
            analyses: Registrations);
    }

    /// <summary>Registrations in product order.</summary>
    public static ImmutableArray<InspectionAnalysisRegistration> Registrations { get; }

    /// <summary>Diff's Compare operation and its operation-owned default set (<c>api</c>).</summary>
    public static AnalysisOperationDefinition Operation { get; }

    /// <summary>The host-neutral capability module registering Diff's analyses.</summary>
    public static InspectionCapabilityModule ProductModule { get; }

    static InspectionAnalysisRegistration RegisterBody(
        string identity,
        FindingDescriptor descriptor,
        Func<DiffAnalysisProducerContext, FindingDescriptor, DiffAnalysisProduction> produce)
        => Register(
            identity,
            InspectionCost.Moderated,
            [(AnalysisReportSurfaceKind.Member, SingleMemberAnchor, [descriptor])],
            BodySignalRoute,
            context => produce(context, descriptor));

    static InspectionAnalysisRegistration Register(
        string identity,
        InspectionCost cost,
        (AnalysisReportSurfaceKind Surface,
            AnalysisTargetRoleDescriptor Anchor,
            FindingDescriptor[] Descriptors)[] surfaces,
        AnalysisDeclarationId route,
        Func<DiffAnalysisProducerContext, DiffAnalysisProduction> produce)
    {
        AnalysisSurfaceParticipation[] participations =
        [
            .. surfaces.Select(surface => new AnalysisSurfaceParticipation(
                surface.Surface,
                surface.Descriptors,
                route)),
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
                ApiComparisonQuery.Execute(
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
        DiffAnalysisProducerContext context,
        ResearchChangeMechanism mechanism)
        where T : notnull
    {
        FindingDescriptor descriptor = context.Participation.Descriptors.Single();
        DiffAnalysisInput input = context.Input;
        ResearchComparison research = ResearchDiff.Compare(
            ResearchDiffInput.FromAssemblies(input.FromPaths),
            ResearchDiffInput.FromAssemblies(input.ToPaths),
            new ResearchDiffOptions(
                mechanism,
                TypeFilters: input.TypeFilters,
                MemberTargetIdentities: input.MemberTargetIdentities)
            {
                RetainedComparisonDescriptorIds =
                    ImmutableHashSet.Create(StringComparer.Ordinal, descriptor.Id),
            });
        return DiffAnalysisProduction.Compared(
            KeyedFindingComparison.Of(
                new RetainedFindingComparisonSet(
                    research.RetainedComparisons.Get<T>(descriptor))));
    }
}
