using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Metadata;
using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/unsafe-member-findings.md.
public sealed class UnsafeMemberFindingsTests
{
    static readonly FindingSubject Subject = new("library:unsafe-fixture", "unsafe fixture");

    static UnsafeMemberCensus OpenCensus(bool legacy) =>
        BodyAnalysisTestExecution.Open(UnsafeFixturePath(legacy)).Safety.MemberCensus;

    static string UnsafeFixturePath(bool legacy) =>
        (legacy
            ? FixtureCatalog.DecompilerUnsafeLegacy
            : FixtureCatalog.DecompilerUnsafeNew).AssemblyPath();

    static UnsafeMemberFinding Single(UnsafeMemberCensus census, string type, string name) =>
        Assert.Single(
            census.Members,
            finding => finding.Member.DeclaringType.Name == type
                && finding.Member.Name == name);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedBodiesFoldIntoTheirDeclaredMember(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        Assert.DoesNotContain(census.Members, finding => finding.Member.Name.StartsWith('<'));

        UnsafeMemberFinding lambdaOwner = Single(census, "PointerVariableUpdateSamples", "CaptureLocal");
        Assert.Contains(
            lambdaOwner.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.PointerDereference
                && evidence.Body.Name == "<CaptureLocal>b__0"
                && evidence.Body.MetadataToken != lambdaOwner.Member.MetadataToken);

        UnsafeMemberFinding localFunctionOwner =
            Single(census, "UnsafeFindingAttributionSamples", "LocalFunctionDereference");
        Assert.Contains(
            localFunctionOwner.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.PointerDereference
                && evidence.Body.Name.Contains("g__Read", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassicAsyncMoveNextFoldsIntoTheAsyncMethod()
    {
        UnsafeMemberCensus census = BodyAnalysisTestExecution
            .Open(typeof(ClassicAsyncUnsafeFixtures).Assembly.Location)
            .Safety.MemberCensus;

        UnsafeMemberFinding finding = Single(
            census,
            nameof(ClassicAsyncUnsafeFixtures),
            nameof(ClassicAsyncUnsafeFixtures.AsyncStackAllocation));
        Assert.Contains(
            finding.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.StackAllocation
                && evidence.Body.Name == "MoveNext");
        Assert.False(finding.HasPartialEvidence);
        Assert.DoesNotContain(census.Members, member => member.Member.Name == "MoveNext");
    }

    // The attribution owner never associates synchronous iterators, so their
    // evidence is an unattributed limitation rather than a guessed owner.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronousIteratorEvidenceIsAnUnattributedLimitation(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        Assert.False(census.IsComplete);
        UnsafeMemberLimitation limitation = Assert.Single(
            census.Limitations,
            limitation => limitation.Body?.DeclaringType.Name.Contains(
                "IteratorDereference",
                StringComparison.Ordinal) == true);
        Assert.Equal(UnsafeMemberLimitationReason.UnattributedGeneratedBody, limitation.Reason);
        Assert.Null(limitation.DeclaredMember);
        Assert.Contains(
            limitation.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.StackAllocation);
        Assert.DoesNotContain(
            census.Members,
            finding => finding.Member.Name == "IteratorDereference");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonPublicMembersAreFindingsWithDeclarationExposure(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        Assert.Equal(
            UnsafeMemberExposure.NonPublic,
            Single(census, "UnsafeFindingAttributionSamples", "PrivateDereference").Exposure);
        Assert.Equal(
            UnsafeMemberExposure.NonPublic,
            Single(census, "UnsafeFindingAttributionSamples", "InternalDereference").Exposure);
        Assert.Equal(
            UnsafeMemberExposure.Public,
            Single(census, "UnsafeFindingAttributionSamples", "LocalFunctionDereference").Exposure);
    }

    [Fact]
    public void ExplicitContractBelongsOnlyToTheDeclaringMember()
    {
        UnsafeMemberCensus census = OpenCensus(legacy: false);

        UnsafeMemberFinding owner =
            Single(census, "UnsafeFindingAttributionSamples", "LocalFunctionDereference");
        Assert.False(owner.HasExplicitUnsafeContract);
        Assert.False(owner.PropagatesUnsafe);
        Assert.DoesNotContain(
            owner.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.ExplicitContract);

        UnsafeMemberFinding contracted =
            Single(census, "UnsafeFindingAttributionSamples", "PrivateDereference");
        Assert.True(contracted.HasExplicitUnsafeContract);
        Assert.True(contracted.PropagatesUnsafe);
    }

    [Fact]
    public void LegacyEvidenceNeverPropagates()
    {
        Assert.All(
            OpenCensus(legacy: true).Members,
            finding => Assert.False(finding.PropagatesUnsafe));
    }

    [Fact]
    public void ScopedReceiptIsIncompleteRatherThanEmpty()
    {
        string path = typeof(ClassicAsyncUnsafeFixtures).Assembly.Location;
        LibraryBodyAnalysisExecution execution = BodyAnalysisTestExecution.Open(
            path,
            bodyScope: new HashSet<int>());

        UnsafeMemberCensus census = execution.Safety.MemberCensus;
        Assert.False(census.IsComplete);
        Assert.Contains(
            census.Limitations,
            limitation => limitation.Reason == UnsafeMemberLimitationReason.ScopedReceipt
                && limitation.Body is null);
    }

    [Fact]
    public void InspectionProjectsOneFindingPerMemberAndKeepsIncompleteness()
    {
        LibraryBodyAnalysisExecution execution =
            BodyAnalysisTestExecution.Open(UnsafeFixturePath(legacy: false));

        var incomplete = Assert.IsType<UnsafeMemberFindingInspection.Incomplete>(
            AnalysisFindings.InspectUnsafeMembers(execution.Safety, Subject));
        Assert.Equal(execution.Safety.MemberCensus.Members.Length, incomplete.Inspection.Findings.Length);
        Assert.All(
            incomplete.Inspection.Findings,
            finding => Assert.Equal(AnalysisFindings.UnsafeMemberDescriptor, finding.Descriptor));
        Assert.Equal(
            incomplete.Inspection.Findings.Length,
            incomplete.Inspection.Findings
                .Select(finding => finding.Key.IdentityKey)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(execution.Safety.MemberCensus.Limitations, incomplete.Limitations);

        var complete = Assert.IsType<UnsafeMemberFindingInspection.Complete>(
            AnalysisFindings.InspectUnsafeMembers(
                BodyAnalysisTestExecution
                    .Open(typeof(ClassicAsyncUnsafeFixtures).Assembly.Location)
                    .Safety,
                Subject));
        Assert.NotEmpty(complete.Inspection.Findings);
    }

    [Fact]
    public void InspectionFailsWhenMethodEvidenceWasNotRequested()
    {
        LibraryBodyAnalysisExecution execution = LibraryBodyAnalysisService.ExecutePath(
            typeof(ClassicAsyncUnsafeFixtures).Assembly.Location,
            LibraryBodyAnalysisRequest.Create(LibraryBodyAnalysisFeatures.None));

        Assert.IsType<UnsafeMemberFindingInspection.Failed>(
            AnalysisFindings.InspectUnsafeMembers(execution.Safety, Subject));
        Assert.Throws<InvalidOperationException>(() => execution.Safety.MemberCensus);
    }

    [Fact]
    public void IdentityIgnoresModuleTokenAndContractButSeparatesOverloads()
    {
        UnsafeMemberCensus census = OpenCensus(legacy: false);
        MethodIdentity member = Single(census, "UnsafeFindingAttributionSamples", "PrivateDereference").Member;

        Assert.Equal(
            AnalysisFindings.GetUnsafeMemberIdentityKey(member),
            AnalysisFindings.GetUnsafeMemberIdentityKey(member with
            {
                ModuleVersionId = Guid.NewGuid(),
                MetadataToken = member.MetadataToken + 1,
                CallerUnsafeMode = CallerUnsafeMode.None,
            }));

        string[] overloadKeys =
        [
            .. census.Members
                .Where(finding => finding.Member.Name == "Examine")
                .Select(finding => AnalysisFindings.GetUnsafeMemberIdentityKey(finding.Member)),
        ];
        Assert.True(overloadKeys.Length > 1);
        Assert.Equal(overloadKeys.Length, overloadKeys.Distinct(StringComparer.Ordinal).Count());
    }

    // Census rules over synthetic per-body facts, for states Roslyn input does
    // not readily produce.
    public sealed class CensusRules
    {
        static readonly MethodIdentity Template = OpenCensus(legacy: false)
            .Members[0].Member with { CallerUnsafeMode = CallerUnsafeMode.None };

        static MethodIdentity Method(int token, string name, CallerUnsafeMode mode = CallerUnsafeMode.None) =>
            Template with { MetadataToken = token, Name = name, CallerUnsafeMode = mode };

        static readonly UnsafeMemberRootSet NoRoots =
            new(new HashSet<int>(), IsComplete: true, Failed: false);

        static UnsafeMemberBodyFacts Own(
            MethodIdentity body,
            MethodBodyAvailability availability = MethodBodyAvailability.Present,
            bool failed = false,
            params UnsafeMemberUseKind[] roles) =>
            new(
                body.MetadataToken,
                body,
                availability,
                failed,
                failed ? "decode failed" : null,
                RequiresDeclaredOwner: false,
                DeclaredOwnerResolution.None,
                OwnerResolutionFailed: false,
                DeclaredOwner: null,
                [.. roles.Select(role => new UnsafeMemberUseEvidence(role, 1, role.ToString()))]);

        static UnsafeMemberBodyFacts Generated(
            MethodIdentity body,
            MethodIdentity? owner,
            bool failed = false,
            params UnsafeMemberUseKind[] roles) =>
            Own(body, failed: failed, roles: roles) with
            {
                RequiresDeclaredOwner = true,
                OwnerResolution = owner is null
                    ? DeclaredOwnerResolution.Rejected
                    : DeclaredOwnerResolution.Resolved,
                DeclaredOwner = owner,
            };

        static UnsafeMemberCensus Build(
            UnsafeMemberRootSet? roots = null,
            bool fullScope = true,
            ReferenceAssemblyState reference = ReferenceAssemblyState.Implementation,
            params UnsafeMemberBodyFacts[] bodies) =>
            UnsafeMemberCensusBuilder.Build(bodies, fullScope, reference, roots ?? NoRoots);

        [Fact]
        public void BodylessDeclarationKeepsItsContractAndTheCensusComplete()
        {
            MethodIdentity bodyless = Method(0x06000001, "Extern", CallerUnsafeMode.Explicit);

            UnsafeMemberCensus census = Build(bodies:
                Own(bodyless, MethodBodyAvailability.NoApplicableInput, roles: UnsafeMemberUseKind.ExplicitContract));

            Assert.True(census.IsComplete);
            UnsafeMemberFinding finding = Assert.Single(census.Members);
            Assert.True(finding.HasExplicitUnsafeContract);
            Assert.False(finding.HasPartialEvidence);
        }

        [Fact]
        public void FailedAttributedBodyMarksItsOwnerPartial()
        {
            MethodIdentity owner = Method(0x06000001, "Owner");
            MethodIdentity lambda = Method(0x06000002, "<Owner>b__0");

            UnsafeMemberCensus census = Build(bodies:
            [
                Own(owner, roles: UnsafeMemberUseKind.PointerDereference),
                Generated(lambda, owner, failed: true),
            ]);

            UnsafeMemberFinding finding = Assert.Single(census.Members);
            Assert.True(finding.HasPartialEvidence);
            UnsafeMemberLimitation gap = Assert.Single(finding.UninspectedBodies);
            Assert.Equal(UnsafeMemberLimitationReason.BodyAnalysisFailed, gap.Reason);
            Assert.Equal(lambda, gap.Body);
            Assert.Equal(owner, gap.DeclaredMember);
            Assert.Equal(gap, Assert.Single(census.Limitations));
        }

        [Fact]
        public void FailedBodyOfAMemberWithoutAFindingNamesThatMember()
        {
            MethodIdentity owner = Method(0x06000001, "Owner");
            MethodIdentity lambda = Method(0x06000002, "<Owner>b__0");

            UnsafeMemberCensus census = Build(bodies:
            [
                Own(owner),
                Generated(lambda, owner, failed: true),
            ]);

            Assert.Empty(census.Members);
            UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
            Assert.Equal(owner, limitation.DeclaredMember);
            Assert.Equal(lambda, limitation.Body);
        }

        [Fact]
        public void UnattributedBodyIsALimitationOnlyWhenItCarriesOrMayHideEvidence()
        {
            MethodIdentity silent = Method(0x06000001, "<Silent>b__0");
            MethodIdentity loud = Method(0x06000002, "<Loud>b__0");

            UnsafeMemberCensus census = Build(bodies:
            [
                Generated(silent, owner: null),
                Generated(loud, owner: null, roles: UnsafeMemberUseKind.PointerDereference),
            ]);

            Assert.Empty(census.Members);
            UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
            Assert.Equal(UnsafeMemberLimitationReason.UnattributedGeneratedBody, limitation.Reason);
            Assert.Equal(loud, limitation.Body);
            Assert.Single(limitation.Evidence);
        }

        [Fact]
        public void GeneratedBodyContractIsNotConferred()
        {
            MethodIdentity owner = Method(0x06000001, "Owner");
            MethodIdentity localFunction = Method(0x06000002, "<Owner>g__Inner|0_0", CallerUnsafeMode.Explicit);

            UnsafeMemberCensus census = Build(bodies:
            [
                Own(owner),
                Generated(
                    localFunction,
                    owner,
                    roles: [UnsafeMemberUseKind.ExplicitContract, UnsafeMemberUseKind.PointerDereference]),
            ]);

            UnsafeMemberFinding finding = Assert.Single(census.Members);
            Assert.Equal(owner, finding.Member);
            Assert.False(finding.HasExplicitUnsafeContract);
            UnsafeMemberFindingEvidence evidence = Assert.Single(finding.Evidence);
            Assert.Equal(UnsafeMemberUseKind.PointerDereference, evidence.Kind);
            Assert.Equal(localFunction, evidence.Body);
        }

        [Fact]
        public void OwnerWithoutItsOwnResultStillReceivesFoldedEvidence()
        {
            MethodIdentity owner = Method(0x06000001, "Owner");
            MethodIdentity lambda = Method(0x06000002, "<Owner>b__0");

            UnsafeMemberCensus census = Build(bodies:
                Generated(lambda, owner, roles: UnsafeMemberUseKind.PointerDereference));

            UnsafeMemberFinding finding = Assert.Single(census.Members);
            Assert.Equal(owner, finding.Member);
            Assert.Equal(lambda, Assert.Single(finding.Evidence).Body);
        }

        [Fact]
        public void MissingBodyAndTokenOnlyFailureAreLimitations()
        {
            MethodIdentity missing = Method(0x06000001, "Missing");

            UnsafeMemberCensus census = Build(bodies:
            [
                Own(missing, MethodBodyAvailability.Missing),
                new(0x06000002, null, MethodBodyAvailability.Present, true, "identity failed",
                    false, DeclaredOwnerResolution.None, false, null, []),
            ]);

            Assert.Equal(
                [UnsafeMemberLimitationReason.BodyMissing, UnsafeMemberLimitationReason.BodyAnalysisFailed],
                census.Limitations.Select(limitation => limitation.Reason));
            Assert.Equal(0x06000002, census.Limitations[1].BodyToken);
            Assert.Null(census.Limitations[1].Body);
        }

        [Theory]
        [InlineData(ReferenceAssemblyState.Reference, UnsafeMemberLimitationReason.ReferenceAssembly)]
        [InlineData(ReferenceAssemblyState.Undecidable, UnsafeMemberLimitationReason.ReferenceAssemblyUndecidable)]
        public void ReferenceAssemblyIsOneImageLimitation(
            ReferenceAssemblyState state,
            UnsafeMemberLimitationReason expected)
        {
            MethodIdentity contracted = Method(0x06000001, "Contracted", CallerUnsafeMode.Explicit);

            UnsafeMemberCensus census = Build(
                reference: state,
                bodies: Own(contracted, roles: UnsafeMemberUseKind.ExplicitContract));

            Assert.Single(census.Members);
            UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
            Assert.Equal(expected, limitation.Reason);
            Assert.Null(limitation.BodyToken);
        }

        [Fact]
        public void BoundedRootsProvePublicButNeverNonPublic()
        {
            MethodIdentity retained = Method(0x06000001, "Retained");
            MethodIdentity unretained = Method(0x06000002, "Unretained");

            UnsafeMemberCensus census = Build(
                roots: new UnsafeMemberRootSet(
                    new HashSet<int> { retained.MetadataToken },
                    IsComplete: false,
                    Failed: false),
                bodies:
                [
                    Own(retained, roles: UnsafeMemberUseKind.PointerDereference),
                    Own(unretained, roles: UnsafeMemberUseKind.PointerDereference),
                ]);

            Assert.Equal(
                [UnsafeMemberExposure.Public, UnsafeMemberExposure.Unknown],
                census.Members.Select(finding => finding.Exposure));
            Assert.Equal(
                UnsafeMemberLimitationReason.PublicRootInventoryBounded,
                Assert.Single(census.Limitations).Reason);
        }

        [Fact]
        public void FailedRootInventoryLeavesExposureUnknown()
        {
            MethodIdentity member = Method(0x06000001, "Member");

            UnsafeMemberCensus census = Build(
                roots: UnsafeMemberRootSet.Unavailable,
                bodies: Own(member, roles: UnsafeMemberUseKind.PointerDereference));

            Assert.Equal(UnsafeMemberExposure.Unknown, Assert.Single(census.Members).Exposure);
            Assert.Equal(
                UnsafeMemberLimitationReason.PublicRootInventoryFailed,
                Assert.Single(census.Limitations).Reason);
        }
    }
}
