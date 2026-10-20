using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Metadata;
using Inspector.Findings;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/unsafe-member-findings.md.
public sealed partial class UnsafeMemberFindingsTests
{
    static readonly FindingSubject Subject = new("library:unsafe-fixture", "unsafe fixture");

    static UnsafeMemberCensus OpenCensus(bool legacy) =>
        LibraryCensus(UnsafeFixturePath(legacy));

    // The Library-scope census on Assembly Analysis Operation.
    internal static UnsafeMemberCensus LibraryCensus(string path) =>
        Assert.IsType<UnsafeMemberCensusResult.Available>(
                LibraryScopeCensus(path))
            .Census;

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

        Assert.DoesNotContain(
            census.Members,
            finding => finding.Member.Name.Contains('<')
                || finding.Member.DeclaringType.Name.Contains('<'));

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

    // The grouping-type declaration copy duplicates the implementation's
    // contract and has a throwing body, so it is ignored.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtensionMemberIsOneFindingOnItsImplementation(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        UnsafeMemberFinding dereference =
            Single(census, "UnsafeExtensionMembers", "ExtensionDereference");
        Assert.Contains(
            dereference.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.PointerDereference);
        Assert.DoesNotContain(
            census.Members,
            finding => finding.Member.Name == "ExtensionDereference"
                && finding.Member.DeclaringType.Name != "UnsafeExtensionMembers");
        Assert.DoesNotContain(
            census.Limitations,
            limitation => limitation.Body?.Name.StartsWith("Extension", StringComparison.Ordinal) == true);

        if (!legacy)
        {
            UnsafeMemberFinding contract =
                Single(census, "UnsafeExtensionMembers", "ExtensionContract");
            Assert.True(contract.HasExplicitUnsafeContract);
            Assert.Equal(
                2,
                census.Members.Count(finding => finding.Member.Name.StartsWith("Extension", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ClassicAsyncMoveNextFoldsIntoTheAsyncMethod()
    {
        UnsafeMemberCensus census = LibraryCensus(AnalysisFixturePath);

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

    // The declared-source relation authenticates a synchronous iterator's
    // state machine through IteratorStateMachineAttribute, so its MoveNext
    // evidence folds into the iterator method with physical provenance.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronousIteratorEvidenceFoldsIntoTheIteratorMethod(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        UnsafeMemberFinding iterator =
            Single(census, "UnsafeFindingAttributionSamples", "IteratorDereference");
        Assert.Contains(
            iterator.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.StackAllocation
                && evidence.Body.Name == "MoveNext"
                && evidence.Body.DeclaringType.Name.Contains(
                    "<IteratorDereference>d__",
                    StringComparison.Ordinal));
        Assert.False(iterator.HasPartialEvidence);
        Assert.DoesNotContain(census.Members, member => member.Member.Name == "MoveNext");
    }

    // A lifted iterator helper is neither MoveNext nor a lambda; it is a
    // method of the authenticated state-machine type, so it folds too.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiftedIteratorHelperFoldsIntoTheIteratorMethod(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        UnsafeMemberFinding iterator =
            Single(census, "UnsafeFindingAttributionSamples", "IteratorFinallyCall");
        Assert.Contains(
            iterator.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.IndirectCall
                && evidence.Body.Name == "<>m__Finally1");
        Assert.DoesNotContain(
            census.Members,
            member => member.Member.Name == "<>m__Finally1");
    }

    // Abstract, extern UnsafeAccessor, and other bodiless declarations are no
    // applicable input, so nothing limits these fixtures' census.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodilessDeclarationsDoNotLimitTheCensus(bool legacy)
    {
        UnsafeMemberCensus census = OpenCensus(legacy);

        Assert.Empty(census.Limitations);
        Assert.True(census.IsComplete);
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
    public void InspectionProjectsOneFindingPerMemberAndKeepsIncompleteness()
    {
        // The iterator fixture keeps one generated body no source member
        // claims, so its census is incomplete.
        UnsafeMemberCensusResult result = LibraryScopeCensus(IteratorFixturePath);
        UnsafeMemberCensus census =
            Assert.IsType<UnsafeMemberCensusResult.Available>(result).Census;

        var incomplete = Assert.IsType<UnsafeMemberFindingInspection.Incomplete>(
            AnalysisFindings.InspectUnsafeMembers(result, Subject));
        Assert.Equal(census.Members.Length, incomplete.Inspection.Findings.Length);
        Assert.All(
            incomplete.Inspection.Findings,
            finding => Assert.Equal(AnalysisFindings.UnsafeMemberDescriptor, finding.Descriptor));
        Assert.Equal(
            incomplete.Inspection.Findings.Length,
            incomplete.Inspection.Findings
                .Select(finding => finding.Key.IdentityKey)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(census.Limitations, incomplete.Limitations);

        var complete = Assert.IsType<UnsafeMemberFindingInspection.Complete>(
            AnalysisFindings.InspectUnsafeMembers(
                LibraryScopeCensus(UnsafeFixturePath(legacy: false)),
                Subject));
        Assert.NotEmpty(complete.Inspection.Findings);
    }

    // A failed census operation is an inspection failure, not an observation.
    [Fact]
    public void InspectionFailsWhenTheCensusFailed()
    {
        var available = Assert.IsType<UnsafeMemberCensusResult.Available>(
            LibraryScopeCensus(AnalysisFixturePath));
        var failed = new UnsafeMemberCensusResult.Failed(
            new InvalidDataException("The unsafe member census failed."),
            ProducerOutcome.Failed,
            available.SourceReceipt,
            available.Receipt);

        UnsafeMemberFindingInspection.Failed inspection =
            Assert.IsType<UnsafeMemberFindingInspection.Failed>(
                AnalysisFindings.InspectUnsafeMembers(failed, Subject));
        Assert.Equal("The unsafe member census failed.", inspection.Error.Reason);
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
                InScope: true,
                IsExtensionDeclarationSkeleton: false,
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
        public void TokenOnlyFailureIsALimitation()
        {
            UnsafeMemberCensus census = Build(bodies:
                new UnsafeMemberBodyFacts(0x06000002, null, InScope: false, false, MethodBodyAvailability.Present,
                    true, "identity failed", false, DeclaredOwnerResolution.None, false, null, []));

            UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
            Assert.Equal(UnsafeMemberLimitationReason.BodyAnalysisFailed, limitation.Reason);
            Assert.Equal(0x06000002, limitation.BodyToken);
            Assert.Null(limitation.Body);
        }

        [Fact]
        public void ScopedCensusSkipsOutOfScopeBodies()
        {
            MethodIdentity inScope = Method(0x06000001, "InScope");
            MethodIdentity outOfScope = Method(0x06000002, "<Owner>g__Local|0_0", CallerUnsafeMode.Explicit);

            UnsafeMemberCensus census = Build(
                fullScope: false,
                bodies:
                [
                    Own(inScope, roles: UnsafeMemberUseKind.PointerDereference),
                    Own(outOfScope, roles: UnsafeMemberUseKind.ExplicitContract) with { InScope = false },
                ]);

            Assert.Equal(inScope, Assert.Single(census.Members).Member);
            Assert.Equal(
                UnsafeMemberLimitationReason.ScopedReceipt,
                Assert.Single(census.Limitations).Reason);
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
