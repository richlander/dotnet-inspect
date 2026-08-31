
using ILInspector.DecompilerHarness;
using ILInspector.Findings;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates on the source-oracle candidate ledger's classification, ranking, baseline
/// intake, and durable shape.
///
/// <para>Everything here except the PDB canary is pure: <see
/// cref="SourceOracleCandidateLedger.Build"/> takes the census and the per-target
/// outcomes as arguments, so the properties that decide whether a file may be enrolled —
/// an unmeasured target cannot leave a file qualified, a rejection family is not a
/// transient outage, a ranking is deterministic — are checked without a network.</para>
/// </summary>
[Trait("Area", "Corpus")]
public class SourceOracleCandidateLedgerTests
{
    const string FileA = "https://raw.githubusercontent.com/owner/repo/aaaa/src/A.cs";
    const string FileB = "https://raw.githubusercontent.com/owner/repo/aaaa/src/B.cs";
    const string FileC = "https://raw.githubusercontent.com/owner/repo/aaaa/src/C.cs";

    /// <summary>
    /// The defect the ledger exists for: a file whose eligible member was never measured
    /// must not read as complete.
    ///
    /// <para>Deriving membership from captured rows would qualify this file. The second
    /// member's source never arrives, so the file must be Unevaluable — neither qualified
    /// nor rejected, and never ranked.</para>
    /// </summary>
    [Fact]
    public void AcquisitionFailure_CannotLeaveAFileQualified()
    {
        var first = Member(0x06000001);
        var second = Member(0x06000002);
        var report = Build(
            [Mapped(first, FileA, eligible: true), Mapped(second, FileA, eligible: true)],
            [
                Qualified(first, FileA, "statement.return"),
                Rejected(second, FileA, SourceOracleCandidateLedger.CandidateReason.SourceUnavailable, evaluated: false),
            ]);

        var file = Assert.Single(report.Files);
        Assert.Equal("Unevaluable", file.Status);
        Assert.Equal("Acquisition", file.RejectionFamily);
        Assert.Contains("source-unavailable", file.Reasons);
        Assert.Null(file.Rank);
        Assert.Equal(2, file.EligibleTargets);
        Assert.Equal(1, file.EvaluatedTargets);
        Assert.Equal(1, file.UnevaluatedMappedMembers);
        // A file that was not fully measured publishes no feature set, so nothing can
        // rank it on coverage it has not demonstrated.
        Assert.Empty(file.Features);
        Assert.Equal(0, report.FilesQualified);
        Assert.Equal(1, report.FilesUnevaluable);
    }

    /// <summary>
    /// A mapped eligible target that produced no outcome at all is Unevaluable too. The
    /// alternative — dropping it — would shorten the denominator invisibly, which is the
    /// same failure with a different cause.
    /// </summary>
    [Fact]
    public void MissingEvaluation_IsUnevaluableRatherThanAShorterDenominator()
    {
        var first = Member(0x06000001);
        var second = Member(0x06000002);
        var report = Build(
            [Mapped(first, FileA, eligible: true), Mapped(second, FileA, eligible: true)],
            [Qualified(first, FileA, "statement.return")]);

        var file = Assert.Single(report.Files);
        Assert.Equal("Unevaluable", file.Status);
        Assert.Contains("evaluation-missing", file.Reasons);
        Assert.Equal(2, file.EligibleTargets);
        Assert.Equal(2, report.EligibleTargets);
        Assert.Equal(1, report.EvaluatedTargets);
    }

    /// <summary>
    /// A target the PDB does not map is an explicit row, and inventing file membership
    /// for it is exactly the inference the ledger refuses to make.
    /// </summary>
    [Fact]
    public void UnmappedEligibleTarget_StaysExplicitAndInventsNoFileMembership()
    {
        var mapped = Member(0x06000001);
        var unmapped = Member(0x06000002);
        var report = Build(
            [
                Mapped(mapped, FileA, eligible: true),
                new SourceOracleCandidateLedger.CensusMember(
                    unmapped,
                    null,
                    SourceOracleCandidateLedger.CandidateReason.NoPdbSourceMapping,
                    Eligible: true),
            ],
            [Qualified(mapped, FileA, "statement.return")]);

        Assert.Equal(1, report.FilesObserved);
        Assert.Equal(1, report.MappedMembers);
        Assert.Equal(2, report.EligibleTargets);
        Assert.Equal(1, report.UnmappedEligibleTargets);
        Assert.Equal(0, report.UnidentifiedSourceEligibleTargets);
        var row = Assert.Single(report.UnattributedTargets);
        Assert.Equal(unmapped.ToString(), row.Member);
        Assert.Equal("no-pdb-source-mapping", row.Reason);

        var file = Assert.Single(report.Files);
        Assert.Equal("Qualified", file.Status);
        Assert.Equal(1, file.EligibleTargets);
    }

    /// <summary>
    /// A member whose mapped document has no immutable identity is its own row, not an
    /// unmapped one: the PDB mapped it, there is simply nothing to pin its content to.
    /// </summary>
    [Fact]
    public void MemberWithoutImmutableSourceIdentity_IsItsOwnExplicitRow()
    {
        var member = Member(0x06000001);
        var report = Build(
            [
                new SourceOracleCandidateLedger.CensusMember(
                    member,
                    null,
                    SourceOracleCandidateLedger.CandidateReason.NoImmutableSourceIdentity,
                    Eligible: true),
                Mapped(Member(0x06000002), FileA, eligible: true),
            ],
            [Qualified(Member(0x06000002), FileA, "statement.return")]);

        Assert.Equal(0, report.UnmappedEligibleTargets);
        Assert.Equal(1, report.UnidentifiedSourceEligibleTargets);
        Assert.Contains(
            report.UnattributedTargets,
            row => row.Reason == "no-immutable-source-identity");
    }

    /// <summary>
    /// The three ways a file fails to be a candidate are not the same fact, and the
    /// difference decides whether a file is Rejected (measured, not good enough) or
    /// Unevaluable (never measured).
    /// </summary>
    [Fact]
    public void StructuralIneligibility_PrinterMismatch_AndUnavailability_AreDistinct()
    {
        var structural = Member(0x06000001);
        var quality = Member(0x06000002);
        var acquisition = Member(0x06000003);
        var report = Build(
            [
                Mapped(structural, FileA, eligible: true),
                Mapped(quality, FileB, eligible: true),
                Mapped(acquisition, FileC, eligible: true),
            ],
            [
                Rejected(structural, FileA, SourceOracleCandidateLedger.CandidateReason.PrinterBodyIneligible),
                Rejected(quality, FileB, SourceOracleCandidateLedger.CandidateReason.PrinterDifferent),
                Rejected(acquisition, FileC, SourceOracleCandidateLedger.CandidateReason.SourceUnavailable, evaluated: false),
            ]);

        var byUrl = report.Files.ToDictionary(file => file.SourceUrl);
        Assert.Equal("Rejected", byUrl[FileA].Status);
        Assert.Equal("Structural", byUrl[FileA].RejectionFamily);
        Assert.Equal("Rejected", byUrl[FileB].Status);
        Assert.Equal("Quality", byUrl[FileB].RejectionFamily);
        Assert.Equal("Unevaluable", byUrl[FileC].Status);
        Assert.Equal("Acquisition", byUrl[FileC].RejectionFamily);

        Assert.Equal(2, report.FilesRejected);
        Assert.Equal(1, report.FilesUnevaluable);
        Assert.Equal(
            new[] { ("Acquisition", 1), ("Quality", 1), ("Structural", 1) },
            report.RejectionFamilies.Select(family => (family.Name, family.Count)).ToArray());
        Assert.Equal(
            new[] { "printer-body-ineligible", "printer-different", "source-unavailable" }.Order().ToArray(),
            report.RejectionReasons.Select(reason => reason.Name).ToArray());
    }

    /// <summary>
    /// A file whose members are all compiler-generated or otherwise not real-method
    /// targets gates nothing, so it is structurally rejected rather than vacuously
    /// qualified.
    /// </summary>
    [Fact]
    public void FileWithNoEligibleTarget_IsRejectedStructurally()
    {
        var report = Build(
            [Mapped(Member(0x06000001), FileA, eligible: false)],
            []);

        var file = Assert.Single(report.Files);
        Assert.Equal("Rejected", file.Status);
        Assert.Equal("Structural", file.RejectionFamily);
        Assert.Equal(["no-eligible-targets"], file.Reasons);
        Assert.Equal(1, file.MappedMembers);
        Assert.Equal(0, file.EligibleTargets);
        Assert.Equal(1, file.UnevaluatedMappedMembers);
    }

    /// <summary>
    /// Mapped members that are not eligible targets still count toward the file's scope.
    /// Reporting only the eligible ones would let "every eligible member is Printer
    /// exact" read as "the whole file is proven", which it is not.
    /// </summary>
    [Fact]
    public void QualifiedFile_PublishesItsUnevaluatedMappedMembers()
    {
        var eligible = Member(0x06000001);
        var report = Build(
            [
                Mapped(eligible, FileA, eligible: true),
                Mapped(Member(0x06000002), FileA, eligible: false),
                Mapped(Member(0x06000003), FileA, eligible: false),
            ],
            [Qualified(eligible, FileA, "statement.return")]);

        var file = Assert.Single(report.Files);
        Assert.Equal("Qualified", file.Status);
        Assert.Equal(3, file.MappedMembers);
        Assert.Equal(1, file.EligibleTargets);
        Assert.Equal(1, file.EvaluatedTargets);
        Assert.Equal(2, file.UnevaluatedMappedMembers);
    }

    /// <summary>
    /// Greedy ranking picks the largest remaining gain, then recomputes: the file that
    /// looked second-best before the first pick can drop to zero gain once its features
    /// are covered.
    /// </summary>
    [Fact]
    public void GreedyRanking_IsDeterministicAndUpdatesIncrementalGain()
    {
        var wide = Member(0x06000001);
        var overlapping = Member(0x06000002);
        var narrow = Member(0x06000003);
        var report = Build(
            [
                Mapped(wide, FileA, eligible: true),
                Mapped(overlapping, FileB, eligible: true),
                Mapped(narrow, FileC, eligible: true),
            ],
            [
                Qualified(wide, FileA, "statement.for", "statement.if", "statement.return"),
                Qualified(overlapping, FileB, "statement.for", "statement.if"),
                Qualified(narrow, FileC, "statement.throw"),
            ],
            baselineFeatures: ["statement.return"]);

        var ranked = report.Files.Where(file => file.Rank is not null).ToArray();
        Assert.Equal(3, ranked.Length);

        // A: two features are new (return is already enrolled), so it wins the first pick.
        Assert.Equal(FileA, ranked[0].SourceUrl);
        Assert.Equal(1, ranked[0].Rank);
        Assert.Equal(2, ranked[0].IncrementalFeatureCount);
        Assert.Equal(["statement.for", "statement.if"], ranked[0].IncrementalFeatures);

        // C adds one feature; B now adds none, so the gain recompute reorders them.
        Assert.Equal(FileC, ranked[1].SourceUrl);
        Assert.Equal(2, ranked[1].Rank);
        Assert.Equal(1, ranked[1].IncrementalFeatureCount);

        Assert.Equal(FileB, ranked[2].SourceUrl);
        Assert.Equal(3, ranked[2].Rank);
        Assert.Equal(0, ranked[2].IncrementalFeatureCount);
        Assert.Empty(ranked[2].IncrementalFeatures);
    }

    [Fact]
    public void AlreadyEnrolledFile_IsReportedButNotRankedAsANextCandidate()
    {
        var enrolled = Member(0x06000001);
        var candidate = Member(0x06000002);
        var report = Build(
            [
                Mapped(enrolled, FileA, eligible: true),
                Mapped(candidate, FileB, eligible: true),
            ],
            [
                Qualified(enrolled, FileA, "statement.return"),
                Qualified(candidate, FileB, "statement.if"),
            ],
            enrolledSourceUrls: [FileA]);

        var byUrl = report.Files.ToDictionary(file => file.SourceUrl);
        Assert.Equal("Enrolled", byUrl[FileA].Status);
        Assert.Null(byUrl[FileA].Rank);
        Assert.Equal("Qualified", byUrl[FileB].Status);
        Assert.Equal(1, byUrl[FileB].Rank);
        Assert.Equal(1, report.FilesEnrolled);
        Assert.Equal(1, report.FilesQualified);
    }

    /// <summary>
    /// Equal gain breaks on total feature count, then eligible-target count, then source
    /// URL ordinal — never on dictionary or input order.
    /// </summary>
    [Fact]
    public void GreedyRanking_BreaksTiesDeterministically()
    {
        var first = Member(0x06000001);
        var second = Member(0x06000002);
        var third = Member(0x06000003);

        // Same single new feature each, so the gain is tied at one for all three.
        SourceOracleCandidateLedger.Report Run(bool reversed)
        {
            SourceOracleCandidateLedger.CensusMember[] members =
            [
                Mapped(first, FileC, eligible: true),
                Mapped(second, FileB, eligible: true),
                Mapped(third, FileA, eligible: true),
                // FileB carries a second eligible member, so it outranks FileA on member
                // count once their feature counts tie.
                Mapped(Member(0x06000004), FileB, eligible: true),
            ];
            SourceOracleCandidateLedger.TargetOutcome[] outcomes =
            [
                Qualified(first, FileC, "statement.for", "statement.if"),
                Qualified(second, FileB, "statement.for"),
                Qualified(third, FileA, "statement.for"),
                Qualified(Member(0x06000004), FileB, "statement.for"),
            ];
            return Build(
                reversed ? [.. members.Reverse()] : members,
                reversed ? [.. outcomes.Reverse()] : outcomes);
        }

        foreach (bool reversed in new[] { false, true })
        {
            var ranked = Run(reversed).Files
                .Where(file => file.Rank is not null)
                .Select(file => file.SourceUrl)
                .ToArray();

            // C wins on total feature count, then B on eligible members, then A.
            Assert.Equal([FileC, FileB, FileA], ranked);
        }
    }

    [Fact]
    public void GreedyRanking_UsesTheCompleteFileIdentityAsTheFinalTieBreak()
    {
        var first = Member(0x06000001);
        var second = Member(0x06000002);
        var lowerChecksum = new SourceOracleCandidateLedger.FileIdentity(
            FileA,
            "SHA256",
            "AAA");
        var higherChecksum = new SourceOracleCandidateLedger.FileIdentity(
            FileA,
            "SHA256",
            "BBB");

        SourceOracleCandidateLedger.Report Run(bool reversed)
        {
            SourceOracleCandidateLedger.CensusMember[] members =
            [
                new(first, lowerChecksum, null, Eligible: true),
                new(second, higherChecksum, null, Eligible: true),
            ];
            SourceOracleCandidateLedger.TargetOutcome[] outcomes =
            [
                new(first, lowerChecksum, null, Evaluated: true, ["statement.return"]),
                new(second, higherChecksum, null, Evaluated: true, ["statement.return"]),
            ];
            return Build(
                reversed ? [.. members.Reverse()] : members,
                reversed ? [.. outcomes.Reverse()] : outcomes);
        }

        foreach (bool reversed in new[] { false, true })
        {
            Assert.Equal(
                ["AAA", "BBB"],
                Run(reversed).Files
                    .Where(file => file.Rank is not null)
                    .OrderBy(file => file.Rank)
                    .Select(file => file.Checksum)
                    .ToArray());
        }
    }

    /// <summary>
    /// One source file compiled into two assemblies is one candidate. Ranking it twice
    /// would double-count its coverage, and reporting only one assembly would hide which
    /// modules the qualification actually covers.
    /// </summary>
    [Fact]
    public void SharedSourceIdentity_GroupsAcrossAssembliesAndIsMarked()
    {
        var left = new SourceOracleCandidateLedger.MemberIdentity(
            "Left", Guid.Parse("11111111-1111-1111-1111-111111111111"), 0x06000001, "N.T", "M", 0);
        var right = new SourceOracleCandidateLedger.MemberIdentity(
            "Right", Guid.Parse("22222222-2222-2222-2222-222222222222"), 0x06000001, "N.T", "M", 0);
        var report = Build(
            [Mapped(left, FileA, eligible: true), Mapped(right, FileA, eligible: true)],
            [
                Qualified(left, FileA, "statement.return"),
                Qualified(right, FileA, "statement.if"),
            ]);

        var file = Assert.Single(report.Files);
        Assert.True(file.SharedAcrossAssemblies);
        Assert.Equal(["Left", "Right"], file.Assemblies);
        Assert.Equal(2, file.EligibleTargets);
        Assert.Equal(["statement.if", "statement.return"], file.Features);
    }

    /// <summary>
    /// The same file identity in one assembly is not marked shared, so the flag means
    /// what it says.
    /// </summary>
    [Fact]
    public void SingleAssemblyFile_IsNotMarkedShared()
    {
        var member = Member(0x06000001);
        var report = Build(
            [Mapped(member, FileA, eligible: true)],
            [Qualified(member, FileA, "statement.return")]);

        Assert.False(Assert.Single(report.Files).SharedAcrossAssemblies);
    }

    /// <summary>
    /// The durable report carries identities, counts, reasons, and feature names — never
    /// authored or printer source text, a diff, or a local path.
    ///
    /// <para>Non-vacuous by construction: the sentinels below are the actual authored
    /// body, printer body, diff detail, and source path of a row that is classified,
    /// built, and serialized. Adding any of them to an outcome or report field fails
    /// here.</para>
    /// </summary>
    [Fact]
    public void Json_ExcludesAuthoredAndPrinterSourceTextAndLocalPaths()
    {
        const string authoredSentinel = "AUTHORED_BODY_SENTINEL_MUST_NOT_SERIALIZE";
        const string printerSentinel = "PRINTER_BODY_SENTINEL_MUST_NOT_SERIALIZE";
        const string diffSentinel = "DIFF_DETAIL_SENTINEL_MUST_NOT_SERIALIZE";
        const string pathSentinel = "/home/sentinel/local/path/Oracle.cs";

        var record = new AuthoredSourceHarvest.CorpusRecord(
            Assembly: "Oracle",
            AssemblyVersion: "1.0.0.0",
            Tfm: "net11.0",
            Type: "N.T",
            Method: "M",
            Overload: 0,
            Signature: "M()",
            MetadataToken: 0x06000001,
            ParameterCount: 0,
            IlSize: 4,
            SourceUrl: FileA,
            ChecksumAlgorithm: "SHA256",
            Checksum: "ABC123",
            AuthoredBody: $"return 1; // {authoredSentinel}",
            ModuleVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PrinterBody: $"return 1; // {printerSentinel}",
            PrinterBodyVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion);
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("N.T", "M", 0, "M()"),
            ReturnToSenderSourceOutcome.ValidMatch,
            CompileBackStatus: null,
            Reason: "match",
            Detail: diffSentinel,
            SourcePath: pathSentinel,
            ExpectedBody: $"return 1; // {authoredSentinel}",
            ActualBody: $"return 1; // {printerSentinel}",
            PrinterExact: PrinterExactOutcome.Exact);

        var member = new SourceOracleCandidateLedger.MemberIdentity(
            "Oracle",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            0x06000001,
            "N.T",
            "M",
            0);
        var outcome = SourceOracleCandidateLedger.Classify(
            member,
            Identity(FileA),
            new AuthoredSourceOracleManifest.EvaluatedRow(record, result));
        Assert.Null(outcome.Reason);
        Assert.Contains("statement.return", outcome.Features);

        var report = SourceOracleCandidateLedger.Build(
            new SourceOracleCandidateLedger.LedgerInput(
                [
                    new SourceOracleCandidateLedger.ScannedAssembly(
                        "Oracle",
                        "1.0.0.0",
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        new string('a', 64)),
                ],
                [Mapped(member, FileA, eligible: true)],
                [outcome]),
            VerifiedBaseline(),
            Provenance());
        string json = SourceOracleCandidateLedger.SerializeReport(report);

        Assert.DoesNotContain(authoredSentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain(printerSentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain(diffSentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain(pathSentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain("authoredBody", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("printerBody", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"tfm\"", json, StringComparison.OrdinalIgnoreCase);
        // The evidence that is meant to be there still is, so the assertions above are
        // not passing because nothing was serialized.
        Assert.Contains("statement.return", json, StringComparison.Ordinal);
        Assert.Contains(member.ToString(), json, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"Qualified\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every reason has a family and a stable code, derived from the declaration so a new
    /// reason cannot enter the enum uncategorized or unnamed.
    /// </summary>
    [Fact]
    public void EveryCandidateReason_HasAFamilyAndAStableCode()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (SourceOracleCandidateLedger.CandidateReason reason
            in Enum.GetValues<SourceOracleCandidateLedger.CandidateReason>())
        {
            var family = SourceOracleCandidateLedger.FamilyOf(reason);
            Assert.True(Enum.IsDefined(family));
            string code = SourceOracleCandidateLedger.Code(reason);
            Assert.False(string.IsNullOrWhiteSpace(code));
            Assert.True(codes.Add(code), $"Reason code '{code}' is not unique.");
        }
    }

    /// <summary>
    /// Classification follows the nesting the source oracle declares: Valid, then
    /// Correct, then Printer exact, then a parseable inventory. Each step's failure is a
    /// different reason, so a Printer mismatch is never reported as an invalid body.
    /// </summary>
    [Theory]
    [InlineData("Invalid", "Exact", "not-valid")]
    [InlineData("ValidDifferent", "Exact", "not-correct")]
    [InlineData("UnsupportedTarget", "Exact", "unsupported-target")]
    [InlineData("SourceUnavailable", "Exact", "decompiler-not-full")]
    [InlineData("ValidMatch", "Different", "printer-different")]
    [InlineData("ValidMatch", "NotRecorded", "printer-not-recorded")]
    public void Classify_SeparatesEveryQualityStep(
        string outcome,
        string printerExact,
        string expected)
    {
        var classified = SourceOracleCandidateLedger.Classify(
            Member(0x06000001),
            Identity(FileA),
            Row(
                Enum.Parse<ReturnToSenderSourceOutcome>(outcome),
                Enum.Parse<PrinterExactOutcome>(printerExact)));

        Assert.NotNull(classified.Reason);
        Assert.Equal(expected, SourceOracleCandidateLedger.Code(classified.Reason!.Value));
    }

    /// <summary>
    /// A Correct member with no captured Printer body is structurally ineligible, not a
    /// printer defect: there was nothing to compare before normalization.
    /// </summary>
    [Fact]
    public void Classify_TreatsAMissingPrinterBodyAsStructural()
    {
        var classified = SourceOracleCandidateLedger.Classify(
            Member(0x06000001),
            Identity(FileA),
            Row(
                ReturnToSenderSourceOutcome.ValidMatch,
                PrinterExactOutcome.Exact,
                printerBody: null));

        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.PrinterBodyIneligible,
            classified.Reason);
        Assert.Equal(
            SourceOracleCandidateLedger.RejectionFamily.Structural,
            SourceOracleCandidateLedger.FamilyOf(classified.Reason!.Value));
    }

    /// <summary>
    /// A Printer body that does not parse cannot contribute an inventory, and saying so
    /// is different from every other rejection.
    /// </summary>
    [Fact]
    public void Classify_ReportsAnUninventoriablePrinterBody()
    {
        var classified = SourceOracleCandidateLedger.Classify(
            Member(0x06000001),
            Identity(FileA),
            Row(
                ReturnToSenderSourceOutcome.ValidMatch,
                PrinterExactOutcome.Exact,
                printerBody: "return ("));

        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.InventoryParseFailed,
            classified.Reason);
        Assert.Equal(
            SourceOracleCandidateLedger.RejectionFamily.Inventory,
            SourceOracleCandidateLedger.FamilyOf(classified.Reason!.Value));
    }

    /// <summary>
    /// A captured body at an unsupported Printer comparison version is not evidence at
    /// the current contract, so it rejects rather than being read as Printer exact.
    /// </summary>
    [Fact]
    public void Classify_RejectsAnUnsupportedPrinterComparisonVersion()
    {
        var classified = SourceOracleCandidateLedger.Classify(
            Member(0x06000001),
            Identity(FileA),
            Row(
                ReturnToSenderSourceOutcome.ValidMatch,
                PrinterExactOutcome.Exact,
                printerBodyVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion + 1));

        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.PrinterVersionUnsupported,
            classified.Reason);
    }

    // ------------------------------------------------------------------- baseline

    [Fact]
    public void Baseline_AcceptsAVerifiedEnrolledReport()
    {
        Assert.True(
            SourceOracleCandidateLedger.TryParseBaseline(
                BaselineJson(),
                "digest",
                out var baseline,
                out string? error),
            error);
        Assert.Equal(["statement.return"], baseline!.Features);
        Assert.Equal(1, baseline.FilesRegistered);
        Assert.Equal(PrinterSyntaxInventory.Version, baseline.SyntaxInventoryVersion);
        Assert.Equal("digest", baseline.Digest);
    }

    [Theory]
    [InlineData("zero-targets")]
    [InlineData("unmatched-row")]
    [InlineData("malformed-row")]
    [InlineData("zero-assemblies")]
    [InlineData("assembly-mismatch")]
    [InlineData("too-many-assemblies")]
    [InlineData("row-count-mismatch")]
    [InlineData("null-row")]
    [InlineData("corpus-row-mismatch")]
    public void Baseline_RejectsContradictoryInputCompleteness(string mutation)
    {
        var report = Baseline();
        report = mutation switch
        {
            "zero-targets" => report with
            {
                CorpusRows = 0,
                MatchedAssemblies = 0,
                CorpusAssemblies = 0,
                TargetsEvaluated = 0,
                Rows = [],
            },
            "unmatched-row" => report with { CorpusRows = 2, UnmatchedRows = 1 },
            "malformed-row" => report with { MalformedRows = 1 },
            "zero-assemblies" => report with
            {
                MatchedAssemblies = 0,
                CorpusAssemblies = 0,
            },
            "assembly-mismatch" => report with { MatchedAssemblies = 0 },
            "too-many-assemblies" => report with
            {
                MatchedAssemblies = 2,
                CorpusAssemblies = 2,
            },
            "row-count-mismatch" => report with { Rows = [] },
            "null-row" => report with { Rows = [null!] },
            "corpus-row-mismatch" => report with { CorpusRows = 2 },
            _ => throw new InvalidOperationException(),
        };
        Assert.True(report.InputsComplete);

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(
                SerializeBaseline(report),
                "digest",
                out _,
                out string? error));
        Assert.Contains("complete inputs", error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every way a report can fail to be verified enrolled evidence is rejected, because
    /// ranking against an unverified feature set would report enrolled coverage as new.
    /// </summary>
    [Theory]
    [InlineData("inputsComplete", "true", "false", "complete inputs")]
    [InlineData("\"passed\": true", "\"passed\": true", "\"passed\": false", "did not pass")]
    [InlineData("syntaxInventoryEvaluated", "true", "false", "did not evaluate a syntax inventory")]
    [InlineData("syntaxInventoryVersion", "1", "99", "unsupported")]
    public void Baseline_RejectsUnverifiedReports(
        string property,
        string from,
        string to,
        string expected)
    {
        string json = property.StartsWith('"')
            ? BaselineJson().Replace(from, to, StringComparison.Ordinal)
            : BaselineJson().Replace(
                $"\"{property}\": {from}",
                $"\"{property}\": {to}",
                StringComparison.Ordinal);
        Assert.NotEqual(BaselineJson(), json);

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains(expected, error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A legacy report that never judged a manifest is not a baseline: nothing is
    /// enrolled in it.
    /// </summary>
    [Fact]
    public void Baseline_RejectsALegacyReportWithNoManifest()
    {
        string json = SerializeBaseline(Baseline() with { SourceOracleManifest = null });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("judged no source-oracle manifest", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_RejectsUnsortedObservedFeatures()
    {
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                ObservedFeatures = ["statement.return", "expression.add"],
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("ordinal-sorted", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_RejectsDuplicateObservedFeatures()
    {
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                ObservedFeatures = ["statement.return", "statement.return"],
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("duplicate", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baseline_RejectsObservedFeaturesThatContradictTheFileInventory()
    {
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                ObservedFeatures = ["statement.if"],
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("union", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baseline_RejectsAnIncompleteTrackedFileCount()
    {
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                FilesInventoryTracked = 0,
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("every enrolled file", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baseline_RejectsANullFileInventoryEntry()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(BaselineJson())!.AsObject();
        document["sourceOracleManifest"]!["fileInventory"]!.AsArray()[0] = null;

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(
                document.ToJsonString(),
                "digest",
                out _,
                out string? error));
        Assert.Contains("source URL", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baseline_RejectsAVacuousPassingReport()
    {
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                FilesRegistered = 0,
                FilesValid = 0,
                FilesCorrect = 0,
                PrinterExactRequired = 0,
                PrinterExactPassing = 0,
                FilesInventoryTracked = 0,
                ObservedFeatures = [],
                FileInventory = [],
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.Contains("at least one enrolled file", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("correct")]
    [InlineData("exact-required")]
    [InlineData("exact-passing")]
    [InlineData("failure")]
    public void Baseline_RejectsContradictoryPassingInvariants(string mutation)
    {
        var report = Baseline();
        AuthoredSourceOracleManifest.Report manifest = report.SourceOracleManifest!;
        manifest = mutation switch
        {
            "valid" => manifest with { FilesValid = 0 },
            "correct" => manifest with { FilesCorrect = 0 },
            "exact-required" => manifest with { PrinterExactRequired = 0 },
            "exact-passing" => manifest with { PrinterExactPassing = 0 },
            "failure" => manifest with { Failures = ["contradiction"] },
            _ => throw new InvalidOperationException(),
        };

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(
                SerializeBaseline(report with { SourceOracleManifest = manifest }),
                "digest",
                out _,
                out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("correlation")]
    [InlineData("evaluated")]
    [InlineData("correct")]
    [InlineData("printer-exact")]
    public void Baseline_RejectsInsufficientEnrolledFileRowEvidence(string evidence)
    {
        var report = Baseline();
        var manifest = report.SourceOracleManifest! with
        {
            FilesRegistered = 2,
            FilesValid = 2,
            FilesCorrect = 2,
            PrinterExactRequired = 2,
            PrinterExactPassing = 2,
            FilesInventoryTracked = 2,
            FileInventory =
            [
                .. report.SourceOracleManifest!.FileInventory!,
                new AuthoredSourceOracleManifest.FileInventoryEntry(
                    FileB,
                    PrinterExact: true,
                    Features: ["statement.return"]),
            ],
        };
        report = report with
        {
            CorpusRows = 2,
            TargetsEvaluated = 2,
            Correct = 2,
            PrinterExact = 2,
            SourceOracleManifest = manifest,
            Rows = [.. report.Rows, .. report.Rows],
        };
        report = evidence switch
        {
            "correlation" => report,
            "evaluated" => report with
            {
                CorpusRows = 1,
                TargetsEvaluated = 1,
                Rows = [report.Rows[0]],
            },
            "correct" => report with { Correct = 1 },
            "printer-exact" => report with { PrinterExact = 1 },
            _ => throw new InvalidOperationException(),
        };

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(
                SerializeBaseline(report),
                "digest",
                out _,
                out string? error));
        Assert.Contains("row evidence", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("unsorted")]
    public void Baseline_RejectsMalformedPerFileFeatures(string kind)
    {
        IReadOnlyList<string> malformed = kind switch
        {
            "empty" => [],
            "blank" => [""],
            "duplicate" => ["statement.return", "statement.return"],
            "unsorted" => ["statement.return", "expression.identifier-name"],
            _ => throw new InvalidOperationException(),
        };
        var report = Baseline();
        string json = SerializeBaseline(report with
        {
            SourceOracleManifest = report.SourceOracleManifest! with
            {
                FileInventory =
                [
                    new AuthoredSourceOracleManifest.FileInventoryEntry(
                        FileA,
                        PrinterExact: true,
                        Features: malformed),
                ],
            },
        });

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(json, "digest", out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void HarvestInspection_AFailedFetchIsAnAcquisitionFailureNotMissingMapping()
    {
        var inspection = DotnetInspector.Services.PdbSourceAcquisition
            .MemberPdbAcquisitionFailed(
                new FindingSubject("M~source", "N.T.M"),
                new IOException("Could not fetch PDB source."));

        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.SourceAcquisitionFailed,
            AuthoredSourceHarvest.ClassifyUnavailableInspection(inspection));
    }

    [Fact]
    public void HarvestInspection_ChecksumVerifiedSlicingFailureIsStructural()
    {
        byte[] content = "// no declaration"u8.ToArray();
        var mapping = new MemberSourceObservation(
            new MemberAnchor("M~1", "M:N.T.M", "1", "N.T", "M"),
            MetadataToken: 0x06000001,
            DocumentRowId: 1,
            CanonicalPath: "T.cs",
            OriginalPath: "/_/T.cs",
            ResolvedUrl: FileA,
            StartLine: 1,
            EndLine: 1,
            IsPrimaryDocument: true);
        var document = new SourceDocumentObservation(
            CanonicalPath: "T.cs",
            OriginalPath: "/_/T.cs",
            DocumentRowId: 1,
            Storage: SourceDocumentStorage.SourceLink,
            ResolvedUrl: FileA,
            ChecksumAlgorithm: "SHA256",
            Checksum: Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(content)));
        var inspection = DotnetInspector.Services.PdbSourceAcquisition.FromContent(
            mapping,
            document,
            content,
            "M",
            new FindingSubject("M~source", "N.T.M"));

        Assert.IsType<FindingInspection<string>.Absent>(inspection.Lines.Value);
        Assert.Equal(
            DotnetInspector.Services.SourceChecksumVerification.Exact,
            inspection.ChecksumVerification);
        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.BodyExtractionFailed,
            AuthoredSourceHarvest.ClassifyUnavailableInspection(inspection));
    }

    /// <summary>
    /// A source-oracle manifest is not a benchmark report, and accepting one would rank
    /// against declared expectations rather than observed evidence.
    /// </summary>
    [Fact]
    public void Baseline_RejectsAManifestSuppliedInPlaceOfAReport()
    {
        const string manifest = """
            {
              "version": 1,
              "printerComparisonVersion": 1,
              "syntaxInventoryVersion": 1,
              "files": []
            }
            """;

        Assert.False(
            SourceOracleCandidateLedger.TryParseBaseline(manifest, "digest", out _, out string? error));
        Assert.Contains("not a current benchmark report", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_RejectsAMissingFile()
    {
        Assert.False(
            SourceOracleCandidateLedger.TryReadBaseline(
                Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json"),
                out _,
                out string? error));
        Assert.Contains("not found", error!, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- text card

    [Fact]
    public void Card_ReportsScopeStatusesAndRankedCandidates()
    {
        var qualified = Member(0x06000001);
        var rejected = Member(0x06000002);
        var report = Build(
            [
                Mapped(qualified, FileA, eligible: true),
                Mapped(Member(0x06000009), FileA, eligible: false),
                Mapped(rejected, FileB, eligible: true),
            ],
            [
                Qualified(qualified, FileA, "statement.if"),
                Rejected(rejected, FileB, SourceOracleCandidateLedger.CandidateReason.PrinterDifferent),
            ]);

        var writer = new StringWriter();
        SourceOracleCandidateLedger.WriteCard(report, writer);
        string card = writer.ToString();

        Assert.Contains("SOURCE-ORACLE CANDIDATE LEDGER", card, StringComparison.Ordinal);
        Assert.Contains("mapped members        : 3", card, StringComparison.Ordinal);
        Assert.Contains("eligible targets      : 2", card, StringComparison.Ordinal);
        Assert.Contains("qualified           : 1", card, StringComparison.Ordinal);
        Assert.Contains("rejected            : 1", card, StringComparison.Ordinal);
        Assert.Contains("printer-different", card, StringComparison.Ordinal);
        Assert.Contains("baseline features     : 1", card, StringComparison.Ordinal);
        Assert.Contains("#1", card, StringComparison.Ordinal);
        Assert.Contains(FileA, card, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_SaysSoWhenNothingQualified()
    {
        var report = Build(
            [Mapped(Member(0x06000001), FileA, eligible: true)],
            [Rejected(Member(0x06000001), FileA, SourceOracleCandidateLedger.CandidateReason.PrinterDifferent)]);

        var writer = new StringWriter();
        SourceOracleCandidateLedger.WriteCard(report, writer);

        Assert.Contains("no file qualified", writer.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------- measurement integrity

    /// <summary>
    /// The exit code separates measurement integrity from candidate verdicts: rejecting
    /// every file is a result, but deciding nothing is a failed measurement.
    ///
    /// <para>A partial outage still succeeds — the run measured some candidates and
    /// published the rest as Unevaluable — while a run in which nothing reached the
    /// oracle fails, because "0 qualified" out of nothing measured reads exactly like a
    /// scan that found no good candidates.</para>
    /// </summary>
    [Theory]
    // assemblies, file-attributed eligible targets, evaluated targets -> fails
    [InlineData(1, 10, 10, false)]
    [InlineData(1, 10, 1, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 0, true)]
    [InlineData(1, 10, 0, true)]
    public void MeasurementIntegrity_FailsOnlyWhenNothingWasDecided(
        int assemblies,
        int fileAttributedEligibleTargets,
        int evaluatedTargets,
        bool fails)
    {
        string? failure = SourceOracleCandidateLedger.MeasurementFailure(
            assemblies,
            fileAttributedEligibleTargets,
            evaluatedTargets);

        Assert.Equal(fails, failure is not null);
        if (fails)
            Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    // ------------------------------------------------------------------- canary

    /// <summary>
    /// The census reads a real portable PDB, not a harvest result.
    ///
    /// <para>Compiled-artifact canary rather than a synthetic seam: the denominator this
    /// ledger reports has to come from the compiler-produced MethodDef-to-document
    /// mapping, and a fixture assembly is the only thing that proves the mapping is read
    /// the way acquisition reads it. Every assertion holds whether or not the local build
    /// carries SourceLink, so the canary measures the census rather than the build
    /// configuration.</para>
    /// </summary>
    [Fact]
    public void Census_ReadsTheCompletePdbMappingFromARealAssembly()
    {
        string assemblyPath = typeof(LadderRung1.CombinedFrontier).Assembly.Location;
        var targets = RealMethodTargetEnumerator.Enumerate(assemblyPath);
        Assert.NotEmpty(targets);

        var assembly = new SourceOracleCandidateLedger.ScannedAssembly(
            AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath).Name,
            AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath).Version,
            AuthoredSourceHarvest.ReadModuleVersionId(assemblyPath),
            new string('a', 64));

        var syntheticUnmapped = targets[0] with { MetadataToken = 0x06FFFFFE };
        var targetsByToken = targets.ToDictionary(target => target.MetadataToken);
        targetsByToken.Add(syntheticUnmapped.MetadataToken, syntheticUnmapped);

        using var source = SourceLinkService.Open(assemblyPath);
        Assert.True(
            SourceOracleCandidateLedger.TryCensus(
                source,
                assembly,
                targetsByToken,
                out IReadOnlyList<SourceOracleCandidateLedger.CensusMember> members,
                out string? error),
            error);

        Assert.NotEmpty(members);

        // Every mapped member is either attributed to an immutable file identity or
        // carries the reason it is not. Neither state is silent.
        Assert.All(
            members,
            member => Assert.True(
                (member.File is not null) ^ (member.MappingReason is not null),
                member.Member.ToString()));

        // One token per member: the census picks a single primary document, exactly as
        // acquisition does, rather than emitting a row per mapped document.
        Assert.Equal(
            members.Count,
            members.Select(member => member.Member.MetadataToken).Distinct().Count());

        var eligibleTokens = members
            .Where(member => member.Eligible)
            .Select(member => member.Member.MetadataToken)
            .ToHashSet();
        var targetTokens = targetsByToken.Keys.ToHashSet();
        Assert.NotEmpty(eligibleTokens);
        Assert.True(targetTokens.SetEquals(eligibleTokens));
        var unmapped = Assert.Single(
            members,
            member => member.Member.MetadataToken == syntheticUnmapped.MetadataToken);
        Assert.Null(unmapped.File);
        Assert.Equal(
            SourceOracleCandidateLedger.CandidateReason.NoPdbSourceMapping,
            unmapped.MappingReason);

        // The denominator is the PDB mapping, not the eligible subset: this fixture's
        // constructors and accessors are mapped members that are not real-method targets.
        Assert.True(
            members.Count > eligibleTokens.Count,
            $"{members.Count} mapped member(s) vs {eligibleTokens.Count} eligible target(s).");
    }

    // ------------------------------------------------------------------- helpers

    static SourceOracleCandidateLedger.Report Build(
        IReadOnlyList<SourceOracleCandidateLedger.CensusMember> members,
        IReadOnlyList<SourceOracleCandidateLedger.TargetOutcome> outcomes,
        IReadOnlyList<string>? baselineFeatures = null,
        IReadOnlyList<string>? enrolledSourceUrls = null)
        => SourceOracleCandidateLedger.Build(
            new SourceOracleCandidateLedger.LedgerInput(
                [
                    new SourceOracleCandidateLedger.ScannedAssembly(
                        "Oracle",
                        "1.0.0.0",
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        new string('a', 64)),
                ],
                members,
                outcomes),
            VerifiedBaseline(baselineFeatures, enrolledSourceUrls),
            Provenance());

    static SourceOracleCandidateLedger.MemberIdentity Member(int token)
        => new(
            "Oracle",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            token,
            "N.T",
            $"M{token:X}",
            0);

    static SourceOracleCandidateLedger.FileIdentity Identity(string url)
        => SourceOracleCandidateLedger.FileIdentity.Create(url, "SHA256", "ABC123");

    static SourceOracleCandidateLedger.CensusMember Mapped(
        SourceOracleCandidateLedger.MemberIdentity member,
        string url,
        bool eligible)
        => new(member, Identity(url), null, eligible);

    static SourceOracleCandidateLedger.TargetOutcome Qualified(
        SourceOracleCandidateLedger.MemberIdentity member,
        string url,
        params string[] features)
        => new(member, Identity(url), null, Evaluated: true, Features: features);

    static SourceOracleCandidateLedger.TargetOutcome Rejected(
        SourceOracleCandidateLedger.MemberIdentity member,
        string url,
        SourceOracleCandidateLedger.CandidateReason reason,
        bool evaluated = true)
        => new(member, Identity(url), reason, evaluated, Features: []);

    static AuthoredCorpusHistoryStore.BenchmarkProvenance Provenance()
        => new("2026-01-01", new string('c', 40), "clean", true, false);

    static SourceOracleCandidateLedger.Baseline VerifiedBaseline(
        IReadOnlyList<string>? features = null,
        IReadOnlyList<string>? enrolledSourceUrls = null)
        => new(
            "digest",
            "2026-01-01",
            new string('b', 40),
            "clean",
            false,
            new string('d', 64),
            new string('e', 64),
            1,
            PrinterSyntaxInventory.Version,
            features ?? ["statement.return"],
            enrolledSourceUrls ?? []);

    static AuthoredSourceOracleManifest.EvaluatedRow Row(
        ReturnToSenderSourceOutcome outcome,
        PrinterExactOutcome printerExact,
        string? printerBody = "return 1;",
        int? printerBodyVersion = null)
    {
        var record = new AuthoredSourceHarvest.CorpusRecord(
            Assembly: "Oracle",
            AssemblyVersion: "1.0.0.0",
            Tfm: "net11.0",
            Type: "N.T",
            Method: "M",
            Overload: 0,
            Signature: "M()",
            MetadataToken: 0x06000001,
            ParameterCount: 0,
            IlSize: 4,
            SourceUrl: FileA,
            ChecksumAlgorithm: "SHA256",
            Checksum: "ABC123",
            AuthoredBody: "return 1;",
            ModuleVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PrinterBody: printerBody,
            PrinterBodyVersion: printerBody is null
                ? null
                : printerBodyVersion ?? AuthoredSourceOracleManifest.PrinterComparisonVersion);
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("N.T", "M", 0, "M()"),
            outcome,
            CompileBackStatus: null,
            Reason: "reason",
            Detail: null,
            SourcePath: null,
            ExpectedBody: null,
            ActualBody: null,
            PrinterExact: printerExact);
        return new AuthoredSourceOracleManifest.EvaluatedRow(record, result);
    }

    static AuthoredCorpusBenchmark.Report Baseline()
        => new(
            "2026-01-01",
            new string('b', 40),
            "clean",
            true,
            false,
            CorpusRows: 1,
            MatchedAssemblies: 1,
            CorpusAssemblies: 1,
            UnmatchedRows: 0,
            MalformedRows: 0,
            PoolSha256: new string('e', 64),
            CorpusSha256: new string('d', 64),
            TargetsEvaluated: 1,
            MethodologyVersion: AuthoredCorpusBenchmark.MethodologyVersion,
            InputsComplete: true,
            QualityContract: "Perfection",
            Correct: 1,
            PrinterComparisonVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion,
            PrinterExact: 1,
            PrinterDifferent: 0,
            PrinterNotRecorded: 0,
            ValidDifferent: 0,
            ValidBreakdown: new AuthoredCorpusBenchmark.ValidBreakdownReport(
                0,
                0,
                0,
                0,
                0,
                new AuthoredCorpusBenchmark.FrontierIlDiffAttributionReport(0, 0, 0, 0, 0),
                0),
            Invalid: 0,
            InvalidBreakdown: new AuthoredCorpusBenchmark.InvalidBreakdownReport(0, 0, 0),
            NotFull: 0,
            Drift: 0,
            Unsupported: 0,
            UnknownOutcome: 0,
            SourceOracleManifest: new AuthoredSourceOracleManifest.Report(
                FilesRegistered: 1,
                FilesValid: 1,
                FilesCorrect: 1,
                PrinterExactRequired: 1,
                PrinterExactPassing: 1,
                Passed: true,
                Failures: [],
                SyntaxInventoryVersion: PrinterSyntaxInventory.Version,
                SyntaxInventoryEvaluated: true,
                FilesInventoryTracked: 1,
                ObservedFeatures: ["statement.return"],
                FileInventory:
                [
                    new AuthoredSourceOracleManifest.FileInventoryEntry(
                        FileA,
                        PrinterExact: true,
                        Features: ["statement.return"]),
                ]),
            Ratchet: null,
            Rows:
            [
                new AuthoredCorpusBenchmark.RowReport(
                    Type: "N.T",
                    Method: "M",
                    Overload: 0,
                    Outcome: ReturnToSenderSourceOutcome.ValidMatch.ToString(),
                    TasteBucket: AuthoredCorpusBenchmark.TasteBucket.Correct.ToString(),
                    CompileBackStatus: null,
                    InvalidKind: null,
                    FaultIsolation: null,
                    FaultIsolationMethod: null,
                    UsedCompileBackFloor: false,
                    SupersededFaultIsolation: null,
                    SupersededFaultIsolationMethod: null,
                    Reason: "body_match",
                    Detail: null,
                    SourceFile: FileA,
                    PrinterExact: PrinterExactOutcome.Exact.ToString()),
            ]);

    static string SerializeBaseline(AuthoredCorpusBenchmark.Report report)
        => AuthoredCorpusBenchmark.SerializeReport(report);

    static string BaselineJson() => SerializeBaseline(Baseline());
}
