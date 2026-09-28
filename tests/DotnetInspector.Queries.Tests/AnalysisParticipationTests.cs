using System.Collections.Immutable;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class AnalysisParticipationTests
{
    static readonly FindingDescriptor TypeFinding = new("sample.type", "Sample type");
    static readonly FindingDescriptor MemberFinding = new("sample.member", "Sample member");
    static readonly FindingDescriptor BodyFinding = new("sample.body", "Sample body");
    static readonly AnalysisDeclarationId Route = new("producer.sample");
    static readonly AnalysisProjectionDescriptor Projection =
        new(new("projection.sample"));

    static readonly AnalysisOperationDefinition Compare =
        new(AnalysisOperationKind.Compare, ["surface"]);

    [Theory]
    [InlineData("api", true)]
    [InlineData("call-site", true)]
    [InlineData("api-attribute", true)]
    [InlineData("API", false)]
    [InlineData("call_site", false)]
    [InlineData("call-", false)]
    [InlineData("-call", false)]
    [InlineData("call--site", false)]
    [InlineData("il2", false)]
    [InlineData("allocation-analysis", false)]
    [InlineData("", false)]
    public void AnalysisIdentity_GrammarIsLowercaseKebabWithoutTheWordAnalysis(
        string identity,
        bool valid)
        => Assert.Equal(valid, AnalysisIdentity.IsValid(identity));

    [Fact]
    public void AnalysisCatalog_RejectsInvalidParticipatingIdentityButNotLegacyDescriptor()
    {
        Assert.Throws<ArgumentException>(() =>
            new AnalysisCapabilityCatalog([Participating("Surface")]));

        // analysis.integrations declares no participation and enters the
        // grammar only when Graph adopts it.
        var catalog = new AnalysisCapabilityCatalog(
            [IntegrationAnalysisCatalog.Analysis, Participating("surface")]);
        Assert.Equal(2, catalog.Analyses.Length);
    }

    [Fact]
    public void AnalysisCatalog_RejectsOneDescriptorIssuedByTwoAnalysesAtOneSurface()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new AnalysisCapabilityCatalog(
            [
                Participating("surface"),
                Participating("other-surface"),
            ]));

        Assert.Contains("sample.type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisDescriptor_RejectsParticipationOutsideDeclaredReportSurfaces()
        => Assert.Throws<ArgumentException>(() => Descriptor(
            "surface",
            [(AnalysisReportSurfaceKind.Type, 1, int.MaxValue)],
            [
                new AnalysisOperationParticipation(
                    AnalysisOperationKind.Compare,
                    [
                        new AnalysisSurfaceParticipation(
                            AnalysisReportSurfaceKind.Member,
                            [MemberFinding],
                            Route),
                    ]),
            ]));

    [Fact]
    public void AnalysisSet_RejectsUnknownNonParticipatingAndDuplicateEntriesBeforeProducerExecution()
    {
        AnalysisCapabilityCatalog catalog = Catalog();

        var rejected = Assert.IsType<AnalysisSetValidationResult.Rejected>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Type,
                targetCount: 1,
                ["surface", "nope", "legacy", "surface"]));

        Assert.Collection(
            rejected.Rejections,
            rejection =>
            {
                Assert.Equal(1, rejection.Position);
                Assert.Equal("nope", rejection.RequestedIdentity);
                Assert.Equal(AnalysisSetRejectionReason.Unknown, rejection.SetReason);
                Assert.Null(rejection.RequestReason);
            },
            rejection =>
            {
                Assert.Equal(2, rejection.Position);
                Assert.Equal(AnalysisSetRejectionReason.NotParticipating, rejection.SetReason);
                Assert.Equal("legacy", rejection.Analysis!.Id.Value);
            },
            rejection =>
            {
                Assert.Equal(3, rejection.Position);
                Assert.Equal(AnalysisSetRejectionReason.Duplicate, rejection.SetReason);
            });
    }

    [Fact]
    public void AnalysisSet_RejectionReportsEveryOffendingEntryWithoutNarrowing()
    {
        AnalysisCapabilityCatalog catalog = Catalog();

        var rejected = Assert.IsType<AnalysisSetValidationResult.Rejected>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Member,
                targetCount: 2,
                ["surface", "", "body", "SURFACE", "surface"]));

        // "surface" is supported at Member and stays accepted, but the whole
        // set is rejected: no entry is dropped, substituted, or narrowed.
        Assert.Equal(
            [
                (1, (AnalysisSetRejectionReason?)AnalysisSetRejectionReason.Empty, (AnalysisRequestRejectionReason?)null),
                (2, null, AnalysisRequestRejectionReason.UnsupportedTargetRole),
                (3, AnalysisSetRejectionReason.Unknown, null),
                (4, AnalysisSetRejectionReason.Duplicate, null),
            ],
            rejected.Rejections.Select(rejection => (
                rejection.Position!.Value,
                rejection.SetReason,
                rejection.RequestReason)));
        AnalysisSetEntryRejection cardinality = rejected.Rejections[1];
        AnalysisTargetRoleDescriptor role = Assert.Single(cardinality.TargetRoles);
        Assert.Equal(1, role.MaximumCount);

        var surfaceRejected = Assert.IsType<AnalysisSetValidationResult.Rejected>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Library,
                targetCount: 1,
                ["body"]));
        AnalysisSetEntryRejection unsupported = Assert.Single(surfaceRejected.Rejections);
        Assert.Equal(AnalysisRequestRejectionReason.UnsupportedSurface, unsupported.RequestReason);
    }

    [Fact]
    public void AnalysisSet_OmissionSelectsOperationDefaultNotEmptySet()
    {
        AnalysisCapabilityCatalog catalog = Catalog();

        var omitted = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Type,
                targetCount: 1,
                requested: null));
        Assert.True(omitted.IsOperationDefault);
        Assert.Equal(["surface"], omitted.Analyses.Select(analysis => analysis.Id.Value));

        var empty = Assert.IsType<AnalysisSetValidationResult.Rejected>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Type,
                targetCount: 1,
                []));
        AnalysisSetEntryRejection rejection = Assert.Single(empty.Rejections);
        Assert.Null(rejection.Position);
        Assert.Equal(AnalysisSetRejectionReason.Empty, rejection.SetReason);

        var explicitSet = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.ValidateSet(
                Compare,
                AnalysisReportSurfaceKind.Member,
                targetCount: 1,
                ["body", "surface"]));
        Assert.False(explicitSet.IsOperationDefault);
        Assert.Equal(
            ["body", "surface"],
            explicitSet.Analyses.Select(analysis => analysis.Id.Value));
    }

    static AnalysisCapabilityCatalog Catalog()
        => new(
        [
            Descriptor(
                "surface",
                [
                    (AnalysisReportSurfaceKind.Library, 1, 1),
                    (AnalysisReportSurfaceKind.Type, 1, int.MaxValue),
                    (AnalysisReportSurfaceKind.Member, 1, int.MaxValue),
                ],
                [
                    new AnalysisOperationParticipation(
                        AnalysisOperationKind.Compare,
                        [
                            new AnalysisSurfaceParticipation(
                                AnalysisReportSurfaceKind.Library,
                                [TypeFinding, MemberFinding],
                                Route),
                            new AnalysisSurfaceParticipation(
                                AnalysisReportSurfaceKind.Type,
                                [TypeFinding, MemberFinding],
                                Route),
                            new AnalysisSurfaceParticipation(
                                AnalysisReportSurfaceKind.Member,
                                [MemberFinding],
                                Route),
                        ]),
                ]),
            Descriptor(
                "body",
                [(AnalysisReportSurfaceKind.Member, 1, 1)],
                [
                    new AnalysisOperationParticipation(
                        AnalysisOperationKind.Compare,
                        [
                            new AnalysisSurfaceParticipation(
                                AnalysisReportSurfaceKind.Member,
                                [BodyFinding],
                                Route),
                        ]),
                ]),
            Descriptor(
                "legacy",
                [(AnalysisReportSurfaceKind.Type, 1, 1)],
                participations: null),
        ]);

    static AnalysisDescriptor Participating(string identity)
        => Descriptor(
            identity,
            [(AnalysisReportSurfaceKind.Type, 1, int.MaxValue)],
            [
                new AnalysisOperationParticipation(
                    AnalysisOperationKind.Compare,
                    [
                        new AnalysisSurfaceParticipation(
                            AnalysisReportSurfaceKind.Type,
                            [TypeFinding],
                            Route),
                    ]),
            ]);

    static AnalysisDescriptor Descriptor(
        string identity,
        (AnalysisReportSurfaceKind Kind, int Minimum, int Maximum)[] surfaces,
        ImmutableArray<AnalysisOperationParticipation>? participations)
        => new(
            new AnalysisDeclarationId(identity),
            revision: 1,
            InspectionCost.NetworkFree,
            [AnalysisQuestionMode.Targeted],
            [
                .. surfaces.Select(surface => new AnalysisReportSurfaceSupport(
                    surface.Kind,
                    AnalysisQuestionMode.Targeted,
                    [
                        new AnalysisTargetRoleDescriptor(
                            new($"target.{identity}.{surface.Kind}"),
                            AnalysisTargetFunction.PrivilegedAnchor,
                            surface.Minimum,
                            surface.Maximum),
                    ])),
            ],
            universeRequirements: [],
            structuralPrerequisites: [],
            hostRequirements: [],
            [new AnalysisProjectionSupport(Projection, [AnalysisQuestionMode.Targeted])],
            participations);
}
