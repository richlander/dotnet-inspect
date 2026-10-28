using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

// Gates for comparing the census in Diff (docs/design/unsafe-members-section.md#diff).
public sealed partial class UnsafeMemberFindingsTests
{
    public sealed class Comparison
    {
        static readonly MethodIdentity Template = OpenCensus(legacy: false)
            .Members[0].Member with { CallerUnsafeMode = CallerUnsafeMode.None };

        static MethodIdentity Method(string name, int token = 0x06000001) =>
            Template with { Name = name, MetadataToken = token };

        static UnsafeMemberFindingEvidence Evidence(
            UnsafeMemberUseKind kind,
            MethodIdentity body,
            int offset,
            string detail = "") =>
            new(kind, body, offset, detail);

        static UnsafeMemberFinding Finding(
            MethodIdentity member,
            params UnsafeMemberFindingEvidence[] evidence) =>
            new(member, HasExplicitUnsafeContract: false, [.. evidence], [], UnsafeMemberExposure.Public);

        static UnsafeMemberFindingInspection Complete(params UnsafeMemberFinding[] members) =>
            AnalysisFindings.InspectUnsafeMembers(new UnsafeMemberCensus([.. members], []), Subject);

        static ImmutableArray<PairFinding<UnsafeMemberFinding>> Pairs(
            UnsafeMemberFindingInspection before,
            UnsafeMemberFindingInspection after) =>
            Assert.IsType<FindingComparison<UnsafeMemberFinding>.Complete>(
                    AnalysisFindings.CompareUnsafeMembers(before, after).Value)
                .Pairs;

        static PairFinding<UnsafeMemberFinding> PairFor(
            ImmutableArray<PairFinding<UnsafeMemberFinding>> pairs,
            string name) =>
            Assert.Single(
                pairs,
                pair => (pair.Value switch
                {
                    PairFinding<UnsafeMemberFinding>.Present present => present.New,
                    PairFinding<UnsafeMemberFinding>.Changed changed => changed.New,
                    PairFinding<UnsafeMemberFinding>.Added added => added.New,
                    PairFinding<UnsafeMemberFinding>.Removed removed => removed.Old,
                    _ => throw new InvalidOperationException(),
                }).Payload.Member.Name == name);

        [Fact]
        public void MembersAreAddedRemovedChangedOrPresentByIdentity()
        {
            MethodIdentity stable = Method("Stable");
            MethodIdentity changed = Method("Changed", 0x06000002);
            MethodIdentity removed = Method("Removed", 0x06000003);
            MethodIdentity added = Method("Added", 0x06000004);

            var pairs = Pairs(
                Complete(
                    Finding(stable, Evidence(UnsafeMemberUseKind.PointerDereference, stable, 2)),
                    Finding(changed, Evidence(UnsafeMemberUseKind.StackAllocation, changed, 2)),
                    Finding(removed, Evidence(UnsafeMemberUseKind.PointerDereference, removed, 2))),
                Complete(
                    Finding(stable, Evidence(UnsafeMemberUseKind.PointerDereference, stable, 2)),
                    Finding(
                        changed,
                        Evidence(UnsafeMemberUseKind.StackAllocation, changed, 2),
                        Evidence(UnsafeMemberUseKind.PointerDereference, changed, 6)),
                    Finding(added, Evidence(UnsafeMemberUseKind.IndirectCall, added, 2))));

            Assert.Equal(PairKind.Present, PairFor(pairs, "Stable").Kind);
            var change = Assert.IsType<PairFinding<UnsafeMemberFinding>.Changed>(
                PairFor(pairs, "Changed").Value);
            Assert.Equal(
                "evidence: [stack allocation] -> [pointer dereference; stack allocation]",
                change.Detail);
            Assert.Equal(PairKind.Removed, PairFor(pairs, "Removed").Kind);
            Assert.Equal(PairKind.Added, PairFor(pairs, "Added").Kind);
        }

        // Physical bodies, generated names, IL offsets, and metadata tokens
        // are provenance: a renumbered closure or shifted body is no change.
        [Fact]
        public void ProvenanceIsNeverAChange()
        {
            MethodIdentity member = Method("Read");
            MethodIdentity oldClosure = Method("<Read>b__0_0", 0x06000010);
            MethodIdentity newClosure = Method("<Read>b__1_0", 0x06000020);

            var pairs = Pairs(
                Complete(Finding(
                    member,
                    Evidence(UnsafeMemberUseKind.PointerDereference, oldClosure, 4),
                    Evidence(UnsafeMemberUseKind.ExplicitContractCall, member, 8, "Unsafe::As"))),
                Complete(Finding(
                    member with { MetadataToken = 0x06000030 },
                    Evidence(UnsafeMemberUseKind.ExplicitContractCall, member, 20, "Unsafe::As"),
                    Evidence(UnsafeMemberUseKind.PointerDereference, newClosure, 12))));

            Assert.Equal(PairKind.Present, Assert.Single(pairs).Kind);
        }

        [Fact]
        public void ExposureAndContractChangesAreChanges()
        {
            MethodIdentity member = Method("Read");
            UnsafeMemberFinding before = Finding(
                member,
                Evidence(UnsafeMemberUseKind.PointerDereference, member, 2));

            var pairs = Pairs(
                Complete(before),
                Complete(before with
                {
                    Exposure = UnsafeMemberExposure.NonPublic,
                    HasExplicitUnsafeContract = true,
                }));

            var change = Assert.IsType<PairFinding<UnsafeMemberFinding>.Changed>(
                Assert.Single(pairs).Value);
            Assert.Equal(
                "exposure: Public -> Non-public; explicit contract: False -> True",
                change.Detail);
        }

        // An incomplete census is never compared as complete: the comparison
        // fails and keeps both sides' observed findings.
        [Fact]
        public void AnIncompleteSideFailsTheComparison()
        {
            MethodIdentity member = Method("Read");
            UnsafeMemberFinding finding = Finding(
                member,
                Evidence(UnsafeMemberUseKind.PointerDereference, member, 2));
            UnsafeMemberFindingInspection incomplete =
                AnalysisFindings.InspectUnsafeMembers(
                    new UnsafeMemberCensus(
                        [],
                        [new UnsafeMemberLimitation(
                            UnsafeMemberLimitationReason.BodyAnalysisFailed,
                            0x06000001,
                            member,
                            member,
                            [])]),
                    Subject);

            var failed = Assert.IsType<FindingComparison<UnsafeMemberFinding>.Failed>(
                AnalysisFindings.CompareUnsafeMembers(Complete(finding), incomplete).Value);

            Assert.Equal(
                "The unsafe member census is incomplete: before is complete, after has 1 limitation.",
                failed.ComparisonFailure);
            Assert.IsType<FindingInspection<UnsafeMemberFinding>.Complete>(failed.OldInspection.Value);
            Assert.IsType<FindingInspection<UnsafeMemberFinding>.Complete>(failed.NewInspection.Value);
        }

        [Fact]
        public void AFailedSideFailsTheComparison()
        {
            var failed = new UnsafeMemberFindingInspection.Failed(
                new InspectionError(Subject, AnalysisFindings.UnsafeMemberDescriptor, "unreadable"));

            var comparison = Assert.IsType<FindingComparison<UnsafeMemberFinding>.Failed>(
                AnalysisFindings.CompareUnsafeMembers(failed, Complete()).Value);

            Assert.IsType<FindingInspection<UnsafeMemberFinding>.Failed>(comparison.OldInspection.Value);
        }

        // The summary is the Unsafe Members row's value: roles in inventory
        // order, a count for a repeated role, and distinct sorted callees.
        [Fact]
        public void SummaryIsTheRowValue()
        {
            MethodIdentity member = Method("Read");
            var summary = UnsafeMemberFindingSummary.Of(Finding(
                member,
                Evidence(UnsafeMemberUseKind.ExplicitContractCall, member, 2, "Unsafe::Add"),
                Evidence(UnsafeMemberUseKind.PointerDereference, member, 4),
                Evidence(UnsafeMemberUseKind.ExplicitContractCall, member, 8, "Unsafe::As"),
                Evidence(UnsafeMemberUseKind.ExplicitContractCall, member, 12, "Unsafe::Add")));

            Assert.Equal(
                "Public: pointer dereference; explicit-contract call ×3: Unsafe::Add, Unsafe::As",
                summary.ToString());
        }
    }
}
