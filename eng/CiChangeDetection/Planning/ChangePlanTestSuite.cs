using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// The planner's focused gate. Every assertion here runs through production
/// planner construction — the routing policy, the Git reader, the plan types,
/// the canonical serializer, the publisher, and the command boundary — rather
/// than a harness-built substitute.
/// </summary>
internal static class ChangePlanTestSuite
{
    private const string BaseObjectId =
        "1111111111111111111111111111111111111111";
    private const string CandidateObjectId =
        "2222222222222222222222222222222222222222";

    /// <summary>
    /// Runs the planner gate against the checked repository.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    internal static void Run(string repository)
    {
        string scratch = Path.Combine(
            repository,
            "artifacts",
            "ci-plan-fixtures");
        Directory.CreateDirectory(scratch);
        try
        {
            ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(repository);
            AssertPolicyLoadedCleanly(policy);
            AssertRoutingCanaries(policy);
            AssertConservativePolicies(scratch);
            AssertEventSemantics(policy);
            AssertRoundTripImplication();
            AssertParserFixtures();
            AssertSerialization(repository, policy);
            AssertStrictDeserialization(repository, policy);
            AssertTlaScope(repository, policy);
            AssertGitFixtures(scratch, repository);
            AssertRenameProvenanceFixtures(scratch);
            AssertCommandBoundary(scratch);
            AssertEntrypointContract(repository);
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    private static void AssertPolicyLoadedCleanly(ChangeRoutingPolicy policy)
    {
        if (policy.Diagnostics.Count != 0)
        {
            throw new InvalidOperationException(
                "The repository's routing policy data did not load cleanly: ["
                + string.Join(", ", policy.Diagnostics)
                + "].");
        }
    }

    /// <summary>
    /// Pure routing canaries. Each entry pins the exact raw selections for one
    /// classifier rule or first-match exclusion.
    /// </summary>
    /// <param name="policy">The loaded routing policy.</param>
    private static void AssertRoutingCanaries(ChangeRoutingPolicy policy)
    {
        (string Path, string Selected)[] canaries =
        [
            ("eng/ci-detect-changes.sh",
                "code,csharpdiff,decompiler,docs,ildiff,ilroundtrip,"
                + "packaging,shipped,web,skills,tla"),
            ("src/NetworkDestinationPolicy.cs", "code,decompiler,shipped,web"),
            ("src/UnionPolyfill.cs", "code,decompiler,shipped,web"),
            ("src/dotnet-inspect/Program.cs", "code,shipped"),
            ("src/ILInspector.Decompiler/Raise.cs",
                "code,csharpdiff,decompiler,shipped,web"),
            ("src/ILInspector.Metadata/Reader.cs",
                "code,decompiler,ilroundtrip,shipped,web"),
            ("src/DotnetInspector.Core/Core.cs",
                "code,decompiler,ilroundtrip,shipped,web"),
            ("src/dotnet-inspect/dotnet-inspect.csproj",
                "code,packaging,shipped"),
            ("src/Directory.Build.props",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,packaging,"
                + "shipped"),
            ("src/DotnetInspector.Queries.Tests/Q.cs", "code,decompiler"),
            ("src/DiffFixtures.V1/F.cs", "code,csharpdiff,decompiler,ildiff"),
            ("src/DiffFixtures.V2/F.cs", "code,csharpdiff,decompiler,ildiff"),
            ("fixtures/shared/DotnetInspector.Fixtures/BodyShapeFixture.cs",
                "code,decompiler"),
            ("tests/ILInspector.MetadataPrimitives.PlatformProbe/P.cs",
                "code,decompiler,web"),
            ("tests/DotnetInspector.Artifacts.Local.PlatformProbe/P.cs",
                "code,decompiler,web"),
            ("tests/ILInspector.JsExportSurface.TypeScriptFixtures/F.ts",
                "code,decompiler,web"),
            ("tests/ILInspector.JsExportSurface.Tests/Fixtures/"
                + "ts-jsexport-runtime/R.ts", "code,decompiler,web"),
            ("tests/DotnetInspector.ILRoundtrip.Tests/T.cs",
                "code,decompiler,ilroundtrip"),
            ("tests/Other/T.cs", "code,decompiler"),
            ("tools/DecompilerHarness/Notes.md", "docs"),
            ("tools/DecompilerHarness/Baseline.txt", "decompiler,docs"),
            ("tools/DecompilerHarness/Harness.cs", "code,decompiler"),
            ("tools/CSharpDiffHarness/H.cs", "csharpdiff,decompiler"),
            ("tools/IlDiffHarness/H.cs", "decompiler,ildiff"),
            ("tools/DiffHarnessCommon/C.cs", "csharpdiff,decompiler,ildiff"),
            ("eng/test-ci-change-detection.cs", "code"),
            ("eng/inspect-web-gate-projects.txt", "code,docs,web"),
            ("eng/CiChangeDetection/PromotionWorkflowContract.cs", "code,web"),
            ("eng/CiChangeDetection/DetectionTestSuite.cs", "code"),
            ("eng/package-fixtures/a.nupkg", "code"),
            ("eng/package-manifest-corpus.json", "code"),
            ("eng/verify-package-manifest-corpus.cs", "code"),
            ("eng/prepare-decompiler-assertion-corpus.sh", "code"),
            ("eng/prepare-decompiler-corpus.sh", "code"),
            ("eng/prepare-decompiler-opt-in-corpus.sh", "code"),
            ("eng/prepare-decompiler-pr-corpus.sh", "code"),
            ("eng/prepare-authored-source-oracles.sh", "code"),
            ("eng/report-decompiler-opt-in-corpus-drift.sh", "code"),
            ("eng/prepare-decompiler-package-sweep.cs", "code"),
            ("eng/prepare-evil-corpus.sh", "code"),
            ("docs/data/nuget-top-packages.lock.json", "code,docs"),
            ("docs/data/nuget-top-packages.json", "code,docs"),
            ("eng/restore-iltools.sh", "code"),
            ("eng/activate-iltools.sh", "code"),
            ("eng/test-ts-jsexport-context-aot.sh", "code"),
            ("eng/run-method-semantics-platform-probe.sh", "code,web"),
            ("eng/run-local-path-admission-platform-probe.sh", "code,web"),
            ("eng/test-ts-jsexport-typescript.sh", "web"),
            ("eng/generate-inspect-web-multi-facade-canary.sh", "web"),
            ("eng/test-inspect-web-multi-facade-canary.sh", "web"),
            ("eng/validate-inspect-web-promotion.cs", "web"),
            ("eng/validate-inspect-web-promotion.sh", "web"),
            ("eng/generate-inspect-web-engine-facade.sh", "web"),
            ("eng/InspectWebAsyncLoweringReceipt.targets",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,web"),
            ("eng/verify-inspect-web-async-deployment.sh", "web"),
            ("eng/BannedSymbols.txt", "code,docs,web"),
            (".gitattributes", "code"),
            ("install.ps1", "code"),
            ("eng/decompiler-gate-expected-classes.txt",
                "code,decompiler,docs"),
            ("eng/check-decompiler-gate.cs", "decompiler"),
            ("eng/decompiler-gate-known-red.txt", "decompiler,docs"),
            ("eng/decompiler-gate-skip-projects.txt", "decompiler,docs"),
            ("eng/restore-ilassembler.sh", "code,ilroundtrip"),
            ("prototypes/inspect-web/README.md", "docs"),
            ("prototypes/inspect-web/index.html", "web"),
            ("prototypes/annotated-source-viewer/app.js", "web"),
            ("Directory.Build.props",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,packaging,"
                + "shipped,web"),
            ("Directory.Build.targets",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,packaging,"
                + "shipped,web"),
            ("Directory.Packages.props",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,packaging,"
                + "shipped,web"),
            ("dotnet-inspect.slnx",
                "code,csharpdiff,decompiler,ildiff,ilroundtrip,web"),
            ("global.json", "decompiler,packaging,shipped"),
            (".github/workflows/ci.yml",
                "code,csharpdiff,decompiler,ildiff,packaging,shipped,web,"
                + "skills,tla"),
            (".github/workflows/release.yml", "code,packaging"),
            (".github/workflows/deploy-inspect-web.yml", "web"),
            (".github/workflows/deploy-inspect-web-coreclr.yml", "web"),
            (".github/workflows/promote-inspect-web.yml", "web"),
            (".github/workflows/other.yml", "code"),
            (".markdownlint.yaml", "docs"),
            ("docs/.markdownlint-cli2.jsonc", "docs"),
            ("docs/design/ci-change-plan.md", "docs"),
            ("skills/a/SKILL.md", "docs,skills"),
            ("skills/a/b/SKILL.md", "docs"),
            ("skills/a/notes.md", "docs"),
            ("docs/models/m/Spec.tla", "docs,tla"),
            ("docs/models/m/Spec.cfg", "docs,tla"),
            ("docs/design/models/m/Spec.TLA", "docs,tla"),
            ("docs/models/Root.tla", "docs,tla"),
            ("docs/design/models/Root.cfg", "docs,tla"),
            ("eng/run-tla-checks.sh", "tla"),
            ("eng/test-tla-checks.sh", "tla"),
            ("eng/tla-module-overrides.txt", "docs,tla"),
            ("eng/tla-expected-exit-codes.txt", "docs,tla"),
            ("docs/design/models/m/README.md", "docs"),
        ];

        foreach ((string path, string selected) in canaries)
        {
            RoutingSelections actual = policy.Route(Evidence(path));
            string rendered = Render(actual);
            if (rendered != selected)
            {
                throw new InvalidOperationException(
                    $"Routing canary {path} selected [{rendered}], "
                    + $"expected [{selected}].");
            }
        }

        // A change set is the union of its records, and the classifier owner
        // is absorbing.
        if (Render(policy.Route(Evidence("README.md", "src/a/b.cs")))
            != "code,decompiler,docs,shipped")
        {
            throw new InvalidOperationException(
                "Multi-record routing did not union its records.");
        }

        if (Render(policy.Route(
            Evidence("README.md", "eng/ci-detect-changes.sh")))
            != Render(RoutingSelections.All))
        {
            throw new InvalidOperationException(
                "The classifier owner did not select every validation.");
        }
    }

    /// <summary>
    /// The two deliberately conservative inventory policies. Neither is an
    /// accidental parsing fallback: each produces a valid plan carrying its
    /// own bounded diagnostic code.
    /// </summary>
    /// <param name="scratch">The scratch directory.</param>
    private static void AssertConservativePolicies(string scratch)
    {
        string empty = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(empty);
            if (!policy.Diagnostics.SequenceEqual(
                [
                    PlanDiagnosticCodes.DecompilerSkipInventoryUnavailable,
                    PlanDiagnosticCodes.InspectWebInventoryUnavailable,
                ]))
            {
                throw new InvalidOperationException(
                    "Missing inventories did not produce their diagnostics: ["
                    + string.Join(", ", policy.Diagnostics)
                    + "].");
            }

            // A missing inspect-web inventory broadens `web` to every src
            // change rather than narrowing it.
            if (Render(policy.Route(Evidence("src/dotnet-inspect/Program.cs")))
                != "code,decompiler,shipped,web")
            {
                throw new InvalidOperationException(
                    "A missing inspect-web inventory did not broaden the "
                    + "Browser/Wasm lane.");
            }

            // A missing decompiler skip inventory exempts nothing.
            if (!policy.Route(Evidence("src/dotnet-inspect/Program.cs"))
                .Decompiler)
            {
                throw new InvalidOperationException(
                    "A missing decompiler skip inventory exempted a project.");
            }

            PlanningResult result = ChangePlanner.Compose(
                Provenance(PlanEventKind.PullRequestSyntheticCandidate),
                Evidence("src/dotnet-inspect/Program.cs"),
                policy);
            if (result.Plan.Diagnostics.Count != 2
                || !result.Plan.Validations.Test)
            {
                throw new InvalidOperationException(
                    "A conservative policy choice did not reach a valid plan.");
            }

            // A malformed inventory is the same policy, not a different one.
            string malformed = Path.Combine(empty, "eng");
            Directory.CreateDirectory(malformed);
            File.WriteAllText(
                Path.Combine(malformed, "inspect-web-gate-projects.txt"),
                "src/../escape\n");
            File.WriteAllText(
                Path.Combine(malformed, "decompiler-gate-skip-projects.txt"),
                "/absolute\n");
            ChangeRoutingPolicy malformedPolicy =
                ChangeRoutingPolicy.Load(empty);
            if (malformedPolicy.Diagnostics.Count != 2)
            {
                throw new InvalidOperationException(
                    "A malformed inventory did not produce its diagnostics.");
            }

            Directory.CreateDirectory(
                Path.Combine(empty, "src", "duplicate"));
            File.WriteAllLines(
                Path.Combine(malformed, "inspect-web-gate-projects.txt"),
                ["src/duplicate", "src/duplicate"]);
            File.WriteAllLines(
                Path.Combine(malformed, "decompiler-gate-skip-projects.txt"),
                ["src/duplicate", "src/duplicate"]);
            ChangeRoutingPolicy duplicatePolicy =
                ChangeRoutingPolicy.Load(empty);
            if (duplicatePolicy.Diagnostics.Count != 2)
            {
                throw new InvalidOperationException(
                    "Duplicate inventory roots were not treated as malformed.");
            }
        }
        finally
        {
            TryDelete(empty);
        }
    }

    private static void AssertEventSemantics(ChangeRoutingPolicy policy)
    {
        RoutingSelections all = RoutingSelections.All;
        foreach (PlanEventKind kind in new[]
        {
            PlanEventKind.PullRequestSyntheticCandidate,
            PlanEventKind.MergeGroup,
        })
        {
            ValidationSelections selections =
                ValidationSelections.FromRouting(all, kind);
            if (!(selections.Test
                && !selections.DependencyPolicy
                && selections.CSharpDiffSmoke
                && selections.DecompilerGates
                && selections.Markdownlint
                && selections.IlDiffSmoke
                && selections.IlRoundTrip
                && selections.Pack
                && selections.BuildNet10
                && selections.InspectWeb
                && selections.SkillGate
                && selections.Tla))
            {
                throw new InvalidOperationException(
                    $"Pre-merge event {kind} did not select every "
                    + "validation.");
            }
        }

        ValidationSelections pushed =
            ValidationSelections.FromRouting(all, PlanEventKind.Push);
        if (pushed.Test
            || !pushed.DependencyPolicy
            || pushed.CSharpDiffSmoke
            || pushed.DecompilerGates
            || pushed.IlDiffSmoke
            || pushed.IlRoundTrip
            || pushed.Pack
            || pushed.BuildNet10
            || pushed.SkillGate)
        {
            throw new InvalidOperationException(
                "A push selected a pre-merge validation.");
        }

        if (!pushed.Markdownlint || !pushed.InspectWeb || !pushed.Tla)
        {
            throw new InvalidOperationException(
                "A push dropped an ungated validation.");
        }

        // A neighbouring documentation-only candidate selects documentation
        // validation and no content gate.
        ValidationSelections docsOnly = ValidationSelections.FromRouting(
            policy.Route(Evidence("docs/design/ci-change-plan.md")),
            PlanEventKind.PullRequestSyntheticCandidate);
        if (!docsOnly.Markdownlint
            || docsOnly.Test
            || docsOnly.DecompilerGates
            || docsOnly.InspectWeb
            || docsOnly.Tla)
        {
            throw new InvalidOperationException(
                "A documentation-only candidate selected a content gate.");
        }
    }

    private static void AssertRoundTripImplication()
    {
        // Every routed ilroundtrip rule must also reach the test lane, and the
        // plan type must refuse the combination outright.
        ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(
            Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)
                ?? "/", "nonexistent-ci-plan-policy-root"));
        foreach (string path in new[]
        {
            "tests/DotnetInspector.ILRoundtrip.Tests/T.cs",
            "eng/restore-ilassembler.sh",
            "src/ILInspector.Metadata/Reader.cs",
            "src/ILInspector.MetadataPrimitives/P.cs",
            "src/DotnetInspector.Core/Core.cs",
            "Directory.Build.props",
            "Directory.Build.targets",
            "dotnet-inspect.sln",
            "dotnet-inspect.slnx",
        })
        {
            RoutingSelections routing = policy.Route(Evidence(path));
            if (!routing.IlRoundtrip || !routing.Code)
            {
                throw new InvalidOperationException(
                    $"{path} did not select ilroundtrip and code.");
            }
        }

        AssertRefusal(
            PlanRefusalCategory.PlanSerialization,
            () => new ValidationSelections(
                test: false,
                dependencyPolicy: false,
                cSharpDiffSmoke: false,
                decompilerGates: false,
                markdownlint: false,
                ilDiffSmoke: false,
                ilRoundTrip: true,
                pack: false,
                buildNet10: false,
                inspectWeb: false,
                skillGate: false,
                tla: false));
    }

    /// <summary>
    /// Raw <c>--name-status -z</c> parser fixtures, including a path that is
    /// not valid UTF-8 but must still route correctly.
    /// </summary>
    private static void AssertParserFixtures()
    {
        ChangeEvidence evidence = GitCandidateReader.ParseNameStatusStream(
            Utf8("M\0src/a.cs\0D\0docs/b.md\0A\0tests/c.cs\0T\0tools/d.cs\0"));
        if (evidence.RecordCount != 4
            || GitFixtureRepository.Render(evidence)
                != "M:src/a.cs, D:docs/b.md, A:tests/c.cs, T:tools/d.cs")
        {
            throw new InvalidOperationException(
                "The parser did not preserve status and order: "
                + GitFixtureRepository.Render(evidence));
        }

        if (GitCandidateReader.ParseNameStatusStream([]).RecordCount != 0)
        {
            throw new InvalidOperationException(
                "A valid empty diff was not a valid zero-record change set.");
        }

        // A path whose bytes are not valid UTF-8 must survive acquisition and
        // route on its bytes rather than on a replacement decoding.
        byte[] invalid =
        [
            (byte)'M', 0,
            (byte)'s', (byte)'r', (byte)'c', (byte)'/',
            0xFF, 0xFE,
            (byte)'.', (byte)'c', (byte)'s', 0,
        ];
        ChangeEvidence invalidEvidence =
            GitCandidateReader.ParseNameStatusStream(invalid);
        if (invalidEvidence.RecordCount != 1
            || invalidEvidence.Records[0].Path.Length != 9
            || invalidEvidence.Records[0].Path[4] != 0xFF)
        {
            throw new InvalidOperationException(
                "Invalid UTF-8 path bytes were not preserved.");
        }

        ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(
            Environment.CurrentDirectory);
        if (!policy.Route(invalidEvidence).Code)
        {
            throw new InvalidOperationException(
                "An invalidly encoded src path did not route to code.");
        }

        (PlanRefusalCategory Category, byte[] Stream)[] refusals =
        [
            (PlanRefusalCategory.EvidenceFraming, Utf8("M\0src/a.cs")),
            (PlanRefusalCategory.EvidenceFraming, Utf8("M")),
            (PlanRefusalCategory.EvidenceStatus, Utf8("R100\0src/a.cs\0")),
            (PlanRefusalCategory.EvidenceStatus, Utf8("\0src/a.cs\0")),
            (PlanRefusalCategory.EvidenceStatus, Utf8("U\0src/a.cs\0")),
            (PlanRefusalCategory.EvidenceStatus, Utf8("m\0src/a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0/src/a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src/\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src//a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0./src/a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0../src/a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src/./a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src/../a.cs\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src/.\0")),
            (PlanRefusalCategory.EvidencePath, Utf8("M\0src/..\0")),
            (PlanRefusalCategory.EvidenceDuplicate,
                Utf8("M\0src/a.cs\0D\0src/a.cs\0")),
        ];
        foreach ((PlanRefusalCategory category, byte[] stream) in refusals)
        {
            AssertRefusal(
                category,
                () => GitCandidateReader.ParseNameStatusStream(stream));
        }
    }

    private static void AssertSerialization(
        string repository,
        ChangeRoutingPolicy policy)
    {
        PlanningResult result = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            ChangeEvidence.Create(
            [
                new ChangeRecord(
                    ChangeStatus.Modified,
                    Utf8("docs/models/m/Spec.tla")),
                new ChangeRecord(ChangeStatus.Added, Utf8("README.md")),
            ]),
            policy);

        const string Golden =
            "{\"schemaVersion\":2,\"status\":\"planned\",\"provenance\":"
            + "{\"kind\":\"pullRequestSyntheticCandidate\",\"baseObjectId\":"
            + "\"1111111111111111111111111111111111111111\","
            + "\"candidateObjectId\":"
            + "\"2222222222222222222222222222222222222222\"},\"input\":"
            + "{\"recordCount\":2,\"sha256\":"
            + "\"e2942177c268e91967eeb66ed6c48b8e8e426158f30a8f3371de8322"
            + "439a2a05\"},\"validations\":{\"test\":false,"
            + "\"dependencyPolicy\":false,"
            + "\"csharpDiffSmoke\":false,\"decompilerGates\":false,"
            + "\"markdownlint\":true,\"ilDiffSmoke\":false,"
            + "\"ilRoundTrip\":false,\"pack\":false,\"buildNet10\":false,"
            + "\"inspectWeb\":false,\"skillGate\":false,\"tla\":true},"
            + "\"scopes\":{\"tla\":{\"artifact\":\"ci-plan-tla-paths0\","
            + "\"framing\":\"pathBytesNulTerminated\",\"recordCount\":1,"
            + "\"sha256\":\"c2965478b65cc2a4d5329c0634d39a072c6d0adf0669a2"
            + "96136b5527a57a5955\"}},\"diagnostics\":[]}";

        byte[] serialized = ChangePlanSerializer.Serialize(result.Plan);
        string text = Encoding.UTF8.GetString(serialized);
        if (text != Golden)
        {
            throw new InvalidOperationException(
                $"The serialized plan drifted from its golden form:\n{text}");
        }

        if (serialized.Any(value => value is < 0x20 or > 0x7E))
        {
            throw new InvalidOperationException(
                "The serialized plan contains non-ASCII or control bytes.");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            throw new InvalidOperationException(
                "The serialized plan contains a newline.");
        }

        // The digests are the acquired bytes' digests, not a re-derivation.
        if (result.Plan.Input.Sha256
            != Digest.LowercaseSha256(
                Utf8("M\0docs/models/m/Spec.tla\0A\0README.md\0")))
        {
            throw new InvalidOperationException(
                "The input digest is not the canonical record stream digest.");
        }

        if (!result.HasTlaScope
            || result.Plan.TlaScope is null
            || result.Plan.TlaScope.Sha256
                != Digest.LowercaseSha256(result.TlaScopeBytes))
        {
            throw new InvalidOperationException(
                "The scope digest does not verify against its exact bytes.");
        }

        // Serialization is deterministic across repeated construction.
        PlanningResult repeated = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            ChangeEvidence.Create(
            [
                new ChangeRecord(
                    ChangeStatus.Modified,
                    Utf8("docs/models/m/Spec.tla")),
                new ChangeRecord(ChangeStatus.Added, Utf8("README.md")),
            ]),
            ChangeRoutingPolicy.Load(repository));
        if (Encoding.UTF8.GetString(
            ChangePlanSerializer.Serialize(repeated.Plan)) != Golden)
        {
            throw new InvalidOperationException(
                "Repeated planning produced a different serialization.");
        }

        AssertRefusal(
            PlanRefusalCategory.PlanOverflow,
            () => ChangePlanSerializer.Deserialize(
                new byte[ChangePlan.MaximumSerializedBytes + 1]));
    }

    private static void AssertStrictDeserialization(
        string repository,
        ChangeRoutingPolicy policy)
    {
        PlanningResult result = ChangePlanner.Compose(
            Provenance(PlanEventKind.Push),
            Evidence("README.md"),
            policy);
        byte[] serialized = ChangePlanSerializer.Serialize(result.Plan);
        ChangePlan parsed = ChangePlanSerializer.Deserialize(serialized);
        if (!ChangePlanSerializer.Serialize(parsed).AsSpan()
            .SequenceEqual(serialized))
        {
            throw new InvalidOperationException(
                "A serialized plan did not round-trip through the reader.");
        }

        string text = Encoding.UTF8.GetString(serialized);
        (string Description, string Mutated)[] rejections =
        [
            ("trailing content", $"{text} "),
            ("leading whitespace", $" {text}"),
            ("interior whitespace",
                text.Replace(
                    "\"status\":\"planned\"",
                    "\"status\": \"planned\"")),
            ("non-canonical property order",
                text.Replace(
                    "{\"schemaVersion\":2,\"status\":\"planned\"",
                    "{\"status\":\"planned\",\"schemaVersion\":2")),
            ("escaped member name",
                text.Replace("schemaVersion", "schema\\u0056ersion")),
            ("non-canonical number",
                text.Replace("\"schemaVersion\":2", "\"schemaVersion\":2e0")),
            ("control character", $"\n{text}"),
            ("truncated document", text[..^1]),
            ("unknown member",
                text.Replace("\"status\":", "\"extra\":1,\"status\":")),
            ("missing member", text.Replace(",\"diagnostics\":[]", "")),
            ("duplicate member",
                text.Replace(
                    "\"schemaVersion\":2",
                    "\"schemaVersion\":2,\"schemaVersion\":2")),
            ("mistyped boolean", text.Replace("\"test\":false", "\"test\":0")),
            ("mistyped count",
                text.Replace("\"recordCount\":1", "\"recordCount\":\"1\"")),
            ("unsupported version",
                text.Replace("\"schemaVersion\":2", "\"schemaVersion\":3")),
            ("unsupported status",
                text.Replace("\"planned\"", "\"refused\"")),
            ("invalid digest",
                text.Replace(result.Plan.Input.Sha256, new string('z', 64))),
            ("abbreviated object ID",
                text.Replace(BaseObjectId, BaseObjectId[..7])),
            ("zero object ID",
                text.Replace(BaseObjectId, new string('0', 40))),
            ("broken invariant",
                text.Replace("\"ilRoundTrip\":false", "\"ilRoundTrip\":true")),
            ("unsupported diagnostic",
                text.Replace("\"diagnostics\":[]", "\"diagnostics\":[\"x\"]")),
            ("malformed descriptor",
                text.Replace(
                    "\"scopes\":{}",
                    "\"scopes\":{\"tla\":{\"artifact\":\"x\"}}")),
        ];
        foreach ((string description, string mutated) in rejections)
        {
            if (mutated == text)
            {
                throw new InvalidOperationException(
                    $"The {description} mutation did not change the plan.");
            }

            AssertRefusal(
                null,
                () => ChangePlanSerializer.Deserialize(Utf8(mutated)),
                description);
        }

        // A selected TLA+ validation always carries its scope descriptor.
        PlanningResult tla = ChangePlanner.Compose(
            Provenance(PlanEventKind.Push),
            Evidence("eng/run-tla-checks.sh"),
            ChangeRoutingPolicy.Load(repository));
        string tlaText = Encoding.UTF8.GetString(
            ChangePlanSerializer.Serialize(tla.Plan));
        AssertRefusal(
            PlanRefusalCategory.PlanSerialization,
            () => ChangePlanSerializer.Deserialize(Utf8(
                tlaText[..tlaText.IndexOf(",\"scopes\":", StringComparison.Ordinal)]
                + ",\"scopes\":{},\"diagnostics\":[]}")));
    }

    private static void AssertTlaScope(
        string repository,
        ChangeRoutingPolicy policy)
    {
        // Infrastructure paths select the lane without contributing content,
        // so the scope is valid and empty.
        PlanningResult infrastructure = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            Evidence("eng/run-tla-checks.sh", "eng/tla-module-overrides.txt"),
            policy);
        if (!infrastructure.Plan.Validations.Tla
            || infrastructure.Plan.TlaScope is null
            || infrastructure.Plan.TlaScope.RecordCount != 0
            || !infrastructure.HasTlaScope
            || infrastructure.TlaScopeBytes.Length != 0
            || infrastructure.Plan.TlaScope.Sha256
                != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca4959"
                    + "91b7852b855")
        {
            throw new InvalidOperationException(
                "An infrastructure-only TLA+ selection did not produce a "
                + "valid zero-record scope.");
        }

        // The exact-outcome manifest is consumer input: a changed manifest
        // makes the runner select every model directory it names.
        PlanningResult manifest = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            Evidence("eng/tla-expected-exit-codes.txt"),
            policy);
        if (!manifest.HasTlaScope
            || Encoding.UTF8.GetString(manifest.TlaScopeBytes)
                != "eng/tla-expected-exit-codes.txt\0"
            || manifest.Plan.TlaScope?.RecordCount != 1)
        {
            throw new InvalidOperationException(
                "The TLA+ exact-outcome manifest did not enter its scope.");
        }

        // Consumer inputs enter the scope in plan order; pure infrastructure
        // and unrelated paths do not.
        PlanningResult mixed = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            Evidence(
                "docs/models/b/Second.cfg",
                "eng/run-tla-checks.sh",
                "eng/tla-expected-exit-codes.txt",
                "docs/design/models/a/First.TLA",
                "README.md"),
            policy);
        if (!mixed.HasTlaScope
            || Encoding.UTF8.GetString(mixed.TlaScopeBytes)
                != "docs/models/b/Second.cfg\0"
                    + "eng/tla-expected-exit-codes.txt\0"
                    + "docs/design/models/a/First.TLA\0"
            || mixed.Plan.TlaScope?.RecordCount != 3
            || mixed.Plan.TlaScope.Artifact != "ci-plan-tla-paths0"
            || mixed.Plan.TlaScope.Framing != "pathBytesNulTerminated")
        {
            throw new InvalidOperationException(
                "The TLA+ scope did not contain exactly its consumer inputs.");
        }

        // No TLA+ selection means no descriptor at all.
        PlanningResult none = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            Evidence("README.md"),
            ChangeRoutingPolicy.Load(repository));
        if (none.Plan.Validations.Tla
            || none.Plan.Scopes.Count != 0
            || none.HasTlaScope)
        {
            throw new InvalidOperationException(
                "An unselected TLA+ lane still produced scope evidence.");
        }

        AssertRefusal(
            PlanRefusalCategory.PlanSerialization,
            () => _ = new PlanningResult(infrastructure.Plan, null));
        AssertRefusal(
            PlanRefusalCategory.PlanSerialization,
            () => _ = new PlanningResult(none.Plan, []));
        AssertRefusal(
            PlanRefusalCategory.EvidenceFraming,
            () => _ = new PlanningResult(
                infrastructure.Plan,
                Utf8("docs/models/m/Spec.tla")));
        AssertRefusal(
            PlanRefusalCategory.PlanSerialization,
            () => _ = new PlanningResult(
                infrastructure.Plan,
                Utf8("docs/models/m/Other.tla\0")));

        byte[] exactPath = TlaPath(ChangePlanner.MaximumScopeBytes - 1);
        PlanningResult exactLimit = ChangePlanner.Compose(
            Provenance(PlanEventKind.PullRequestSyntheticCandidate),
            ChangeEvidence.Create(
            [
                new ChangeRecord(ChangeStatus.Modified, exactPath),
            ]),
            policy);
        if (!exactLimit.HasTlaScope
            || exactLimit.TlaScopeBytes.Length
                != ChangePlanner.MaximumScopeBytes)
        {
            throw new InvalidOperationException(
                "The exact TLA+ scope byte limit was not preserved.");
        }

        byte[] oversizedPath = TlaPath(ChangePlanner.MaximumScopeBytes);
        AssertRefusal(
            PlanRefusalCategory.ScopeOverflow,
            () => _ = ChangePlanner.Compose(
                Provenance(PlanEventKind.PullRequestSyntheticCandidate),
                ChangeEvidence.Create(
                [
                    new ChangeRecord(ChangeStatus.Modified, oversizedPath),
                ]),
                policy));
    }

    private static void AssertGitFixtures(string scratch, string repository)
    {
        using GitFixtureRepository fixture =
            GitFixtureRepository.Create(scratch);
        fixture.Write("src/kept.cs", "one\n");
        fixture.Write("src/removed.cs", "two\n");
        fixture.Write("src/typed.cs", "three\n");
        fixture.Write("docs/models/m/Old.tla", "model\n");
        fixture.Write("notes/a b.txt", "space\n");
        fixture.Write("notes/-leading.txt", "dash\n");
        fixture.Write("notes/\"quoted\".txt", "quote\n");
        fixture.Write("notes/$(rm -rf).txt", "metacharacter\n");
        fixture.Write("notes/new\nline.txt", "newline\n");
        fixture.Write("notes/tab\there.txt", "tab\n");
        string emptyBase = fixture.CommitAll("base");

        // A commit that changes nothing is a valid empty change set.
        string emptyCandidate = fixture.CommitAll("empty");
        ChangeEvidence empty = GitCandidateReader.ReadChanges(
            fixture.Root,
            GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                emptyBase,
                emptyCandidate));
        if (empty.RecordCount != 0
            || empty.Sha256
                != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca49599"
                    + "1b7852b855")
        {
            throw new InvalidOperationException(
                "An empty diff was not a valid zero-record change set.");
        }

        fixture.Write("src/kept.cs", "one changed\n");
        fixture.Remove("src/removed.cs");
        fixture.ReplaceWithSymbolicLink("src/typed.cs", "kept.cs");
        fixture.Write("src/added.cs", "four\n");
        fixture.Write("notes/a b.txt", "space changed\n");
        fixture.Write("notes/-leading.txt", "dash changed\n");
        fixture.Write("notes/\"quoted\".txt", "quote changed\n");
        fixture.Write("notes/$(rm -rf).txt", "metacharacter changed\n");
        fixture.Write("notes/new\nline.txt", "newline changed\n");
        fixture.Write("notes/tab\there.txt", "tab changed\n");
        fixture.Write("docs/models/m/New.tla", "model\n");
        fixture.Remove("docs/models/m/Old.tla");
        string candidate = fixture.CommitAll("candidate");

        CandidateProvenance provenance =
            GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.MergeGroup,
                emptyCandidate,
                candidate);
        _ = GitCandidateReader.ResolveProvenance(
            fixture.Root,
            PlanEventKind.PullRequestSyntheticCandidate,
            emptyCandidate,
            candidate);
        AssertRefusal(
            PlanRefusalCategory.CandidateMismatch,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.PullRequestSyntheticCandidate,
                emptyBase,
                candidate));
        ChangeEvidence evidence =
            GitCandidateReader.ReadChanges(fixture.Root, provenance);
        string rendered = GitFixtureRepository.Render(evidence);
        foreach (string expected in new[]
        {
            "M:src/kept.cs",
            "D:src/removed.cs",
            "T:src/typed.cs",
            "A:src/added.cs",
            "M:notes/a b.txt",
            "M:notes/-leading.txt",
            "M:notes/\"quoted\".txt",
            "M:notes/$(rm -rf).txt",
            "M:notes/new\nline.txt",
            "M:notes/tab\there.txt",
            "A:docs/models/m/New.tla",
            "D:docs/models/m/Old.tla",
        })
        {
            if (!rendered.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The Git fixture did not report {expected}: {rendered}");
            }
        }

        if (evidence.RecordCount != 12)
        {
            throw new InvalidOperationException(
                $"The Git fixture reported {evidence.RecordCount} records: "
                + rendered);
        }

        // A rename arrives as a deletion plus an addition, so both sides route.
        PlanningResult result = ChangePlanner.Compose(
            provenance,
            evidence,
            ChangeRoutingPolicy.Load(repository));
        if (!result.Plan.Validations.Tla
            || result.Plan.TlaScope?.RecordCount != 2
            || !result.HasTlaScope
            || Encoding.UTF8.GetString(result.TlaScopeBytes)
                != "docs/models/m/New.tla\0docs/models/m/Old.tla\0")
        {
            throw new InvalidOperationException(
                "A rename inside a model directory did not scope both sides: "
                + rendered);
        }

        // Missing endpoints, mismatched candidates, and malformed identifiers
        // refuse rather than fall back.
        AssertRefusal(
            PlanRefusalCategory.CandidateMismatch,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                candidate,
                emptyCandidate));
        AssertRefusal(
            PlanRefusalCategory.EndpointUnresolved,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                new string('a', 40),
                candidate));
        AssertRefusal(
            PlanRefusalCategory.ObjectIdFormat,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                candidate[..7],
                candidate));
        AssertRefusal(
            PlanRefusalCategory.ObjectIdFormat,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                new string('0', 40),
                candidate));
        AssertRefusal(
            PlanRefusalCategory.ObjectIdFormat,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                candidate.ToUpperInvariant(),
                candidate));
        AssertRefusal(
            PlanRefusalCategory.ObjectIdFormat,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                "HEAD~1",
                candidate));

        // A tree object is not a commit endpoint.
        string tree = fixture
            .Git("rev-parse", "--verify", $"{candidate}^{{tree}}")
            .Trim();
        AssertRefusal(
            PlanRefusalCategory.EndpointUnresolved,
            () => GitCandidateReader.ResolveProvenance(
                fixture.Root,
                PlanEventKind.Push,
                tree,
                candidate));
    }

    /// <summary>
    /// The #5347 contract fixture: a base rename moves a PR-authored edit into
    /// a model path in the synthetic candidate. Because the planner compares
    /// the same two endpoints the jobs check, the gate is selected without a
    /// second candidate relation. The inverse rename must not select unchanged
    /// model content.
    /// </summary>
    /// <param name="scratch">The scratch directory.</param>
    /// <param name="repository">The repository root directory.</param>
    private static void AssertRenameProvenanceFixtures(string scratch)
    {
        using (GitFixtureRepository into = GitFixtureRepository.Create(scratch))
        {
            into.Write("prototypes/X.tla", "spec\n");
            _ = into.CommitAll("root");

            // The base moved the file into a model directory.
            into.Write("docs/models/x/X.tla", "spec\n");
            into.Remove("prototypes/X.tla");
            string movedBase = into.CommitAll("base rename");

            // The synthetic candidate carries the PR edit at the moved path.
            into.Write("docs/models/x/X.tla", "spec edited\n");
            string candidate = into.CommitAll("candidate");

            PlanningResult result = ChangePlanner.Plan(
                into.Root,
                PlanEventKind.PullRequestSyntheticCandidate,
                movedBase,
                candidate);
            if (!result.Plan.Validations.Tla
                || !result.HasTlaScope
                || Encoding.UTF8.GetString(result.TlaScopeBytes)
                    != "docs/models/x/X.tla\0")
            {
                throw new InvalidOperationException(
                    "A base rename into a model path did not select TLA+ "
                    + "with its scoped evidence.");
            }
        }

        using (GitFixtureRepository outOf =
            GitFixtureRepository.Create(scratch))
        {
            outOf.Write("docs/models/x/X.tla", "spec\n");
            outOf.Write("prototypes/other.txt", "other\n");
            _ = outOf.CommitAll("root");

            // The base moved the model out; the candidate edits only the
            // unrelated file.
            outOf.Write("prototypes/X.tla", "spec\n");
            outOf.Remove("docs/models/x/X.tla");
            string movedBase = outOf.CommitAll("base rename");

            outOf.Write("prototypes/other.txt", "other edited\n");
            string candidate = outOf.CommitAll("candidate");

            PlanningResult result = ChangePlanner.Plan(
                outOf.Root,
                PlanEventKind.PullRequestSyntheticCandidate,
                movedBase,
                candidate);
            if (result.Plan.Validations.Tla || result.Plan.Scopes.Count != 0)
            {
                throw new InvalidOperationException(
                    "A base rename out of a model path selected unchanged "
                    + "model content.");
            }
        }
    }

    private static void AssertCommandBoundary(string scratch)
    {
        using GitFixtureRepository fixture =
            GitFixtureRepository.Create(scratch);
        fixture.Write("docs/models/m/Spec.tla", "spec\n");
        string baseCommit = fixture.CommitAll("base");
        fixture.Write("docs/models/m/Spec.tla", "spec edited\n");
        fixture.Write("src/a.cs", "code\n");
        string candidate = fixture.CommitAll("candidate");

        string evidenceDirectory = Path.Combine(fixture.Root, "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        string scopePath = ChangePlanPublisher.ScopePath(
            evidenceDirectory,
            PlanScopeDescriptor.TlaArtifact);

        StringWriter output = new();
        StringWriter error = new();
        int status = ChangePlanCommand.Execute(
            [
                "pull-request",
                "--base", baseCommit,
                "--candidate", candidate,
                "--evidence-directory", evidenceDirectory,
                "--repository", fixture.Root,
            ],
            output,
            error);
        string emitted = output.ToString();
        if (status != 0
            || error.ToString().Length != 0
            || !emitted.EndsWith('\n')
            || emitted.Count(character => character == '\n') != 1)
        {
            throw new InvalidOperationException(
                $"The planner command did not publish one plan line: "
                + $"status {status}, stdout [{emitted}], stderr "
                + $"[{error}].");
        }

        // The published line is exactly the serialized plan plus the CLI's own
        // newline, and the scope file verifies against its descriptor.
        ChangePlan published = ChangePlanSerializer.Deserialize(
            Utf8(emitted[..^1]));
        byte[] scopeBytes = File.ReadAllBytes(scopePath);
        if (published.TlaScope is null
            || Digest.LowercaseSha256(scopeBytes) != published.TlaScope.Sha256
            || Encoding.UTF8.GetString(scopeBytes)
                != "docs/models/m/Spec.tla\0"
            || !published.Validations.Test)
        {
            throw new InvalidOperationException(
                "The published plan did not describe its scoped evidence.");
        }

        string orderFile = Path.Combine(scratch, "diff-order");
        File.WriteAllText(orderFile, "src/*\ndocs/*\n");
        _ = fixture.Git("config", "diff.orderFile", orderFile);
        StringWriter orderedOutput = new();
        StringWriter orderedError = new();
        int orderedStatus = ChangePlanCommand.Execute(
            [
                "pull-request",
                "--base", baseCommit,
                "--candidate", candidate,
                "--evidence-directory", evidenceDirectory,
                "--repository", fixture.Root,
            ],
            orderedOutput,
            orderedError);
        if (orderedStatus != 0
            || orderedError.ToString().Length != 0
            || orderedOutput.ToString() != emitted)
        {
            throw new InvalidOperationException(
                "Ambient Git diff ordering changed the canonical plan: "
                + $"status {orderedStatus}, stdout [{orderedOutput}], "
                + $"stderr [{orderedError}].");
        }

        // The public façade is the same boundary.
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        StringWriter facadeOutput = new();
        StringWriter facadeError = new();
        int facadeStatus;
        try
        {
            Console.SetOut(facadeOutput);
            Console.SetError(facadeError);
            facadeStatus = ChangePlanApp.Run(
            [
                "push",
                "--base", baseCommit,
                "--candidate", candidate,
                "--evidence-directory", evidenceDirectory,
                "--repository", fixture.Root,
            ]);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        if (facadeStatus != 0
            || !facadeOutput.ToString().Contains(
                "\"kind\":\"push\"",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The public planner façade did not publish a push plan: "
                + facadeOutput);
        }

        File.WriteAllBytes(scopePath, Utf8("stale/path\0"));
        StringWriter emptyOutput = new();
        StringWriter emptyError = new();
        int emptyStatus = ChangePlanCommand.Execute(
            [
                "push",
                "--base", candidate,
                "--candidate", candidate,
                "--evidence-directory", evidenceDirectory,
                "--repository", fixture.Root,
            ],
            emptyOutput,
            emptyError);
        ChangePlan emptyPlan = ChangePlanSerializer.Deserialize(
            Utf8(emptyOutput.ToString()[..^1]));
        if (emptyStatus != 0
            || emptyError.ToString().Length != 0
            || emptyPlan.Validations.Tla
            || File.Exists(scopePath))
        {
            throw new InvalidOperationException(
                "A successful non-TLA plan left stale scope evidence.");
        }

        // Every refusal exits nonzero with an absolutely empty stdout, a
        // bounded ASCII category diagnostic, and no scope file left behind.
        (string Description, string[] Arguments)[] refusals =
        [
            ("no arguments", []),
            ("unknown event",
                [
                    "workflow-dispatch",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("missing candidate",
                [
                    "push",
                    "--base", baseCommit,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("repeated option",
                [
                    "push",
                    "--base", baseCommit,
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("repeated evidence directory",
                [
                    "push",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("dangling option",
                [
                    "push",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository",
                ]),
            ("abbreviated object ID",
                [
                    "push",
                    "--base", baseCommit[..7],
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("zero object ID",
                [
                    "push",
                    "--base", new string('0', 40),
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("missing endpoint",
                [
                    "push",
                    "--base", new string('a', 40),
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("checked-candidate mismatch",
                [
                    "push",
                    "--base", candidate,
                    "--candidate", baseCommit,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", fixture.Root,
                ]),
            ("missing evidence directory",
                [
                    "push",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory",
                    Path.Combine(fixture.Root, "absent"),
                    "--repository", fixture.Root,
                ]),
            ("missing repository",
                [
                    "push",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", Path.Combine(fixture.Root, "absent"),
                ]),
            ("repository subdirectory",
                [
                    "push",
                    "--base", baseCommit,
                    "--candidate", candidate,
                    "--evidence-directory", evidenceDirectory,
                    "--repository", Path.Combine(fixture.Root, "src"),
                ]),
        ];

        foreach ((string description, string[] arguments) in refusals)
        {
            File.WriteAllBytes(scopePath, Utf8("stale/path\0"));
            StringWriter refusedOutput = new();
            StringWriter refusedError = new();
            int refusedStatus = ChangePlanCommand.Execute(
                arguments,
                refusedOutput,
                refusedError);
            string diagnostic = refusedError.ToString();
            if (refusedStatus == 0
                || refusedOutput.ToString().Length != 0
                || diagnostic.Length > 320
                || !diagnostic.StartsWith(
                    "ci-plan refused: ",
                    StringComparison.Ordinal)
                || diagnostic.Any(character =>
                    character is not ((>= ' ' and <= '~') or '\n')))
            {
                throw new InvalidOperationException(
                    $"The {description} refusal was not a clean refusal: "
                    + $"status {refusedStatus}, stdout "
                    + $"[{refusedOutput}], stderr [{diagnostic}].");
            }

            if (description == "no arguments"
                && diagnostic
                    != $"ci-plan refused: usage: {ChangePlanCommand.Usage}"
                        + Environment.NewLine)
            {
                throw new InvalidOperationException(
                    "The usage refusal did not preserve the complete syntax.");
            }

            bool cannotIdentifyEvidenceDirectory =
                description is "no arguments"
                    or "missing evidence directory";
            if (!cannotIdentifyEvidenceDirectory && File.Exists(scopePath))
            {
                throw new InvalidOperationException(
                    $"The {description} refusal left a scope file behind.");
            }
        }
    }

    /// <summary>
    /// Pins the file-based entrypoint to this assembly's public façade. The
    /// entrypoint is not routed by the legacy classifier, so this gate is what
    /// keeps it from drifting away from the planner it publishes.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    private static void AssertEntrypointContract(string repository)
    {
        string entrypoint = Path.Combine(repository, "eng", "ci-plan.cs");
        string text = File.ReadAllText(entrypoint).ReplaceLineEndings("\n");
        const string Expected =
            "#:project CiChangeDetection/CiChangeDetection.csproj\n"
            + "\n"
            + "using CiChangeDetection;\n"
            + "\n"
            + "return ChangePlanApp.Run(args);\n";
        if (text != Expected)
        {
            throw new InvalidOperationException(
                "eng/ci-plan.cs must remain the planner's file-based "
                + $"entrypoint shim:\n{text}");
        }
    }

    private static ChangeEvidence Evidence(params string[] paths) =>
        ChangeEvidence.Create(paths.Select(path =>
            new ChangeRecord(ChangeStatus.Modified, Utf8(path))));

    private static byte[] TlaPath(int length)
    {
        byte[] prefix = Utf8("docs/models/m/");
        byte[] suffix = Utf8(".tla");
        if (length < prefix.Length + suffix.Length + 1)
        {
            throw new InvalidOperationException(
                "The requested TLA+ path is too short.");
        }

        byte[] path = new byte[length];
        prefix.CopyTo(path, 0);
        path.AsSpan(prefix.Length, length - prefix.Length - suffix.Length)
            .Fill((byte)'x');
        suffix.CopyTo(path, length - suffix.Length);
        return path;
    }

    private static CandidateProvenance Provenance(PlanEventKind kind) =>
        CandidateProvenance.Create(kind, BaseObjectId, CandidateObjectId);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string Render(RoutingSelections selections)
    {
        List<string> selected = [];
        if (selections.Code)
        {
            selected.Add("code");
        }

        if (selections.CSharpDiff)
        {
            selected.Add("csharpdiff");
        }

        if (selections.Decompiler)
        {
            selected.Add("decompiler");
        }

        if (selections.Docs)
        {
            selected.Add("docs");
        }

        if (selections.IlDiff)
        {
            selected.Add("ildiff");
        }

        if (selections.IlRoundtrip)
        {
            selected.Add("ilroundtrip");
        }

        if (selections.Packaging)
        {
            selected.Add("packaging");
        }

        if (selections.Shipped)
        {
            selected.Add("shipped");
        }

        if (selections.Web)
        {
            selected.Add("web");
        }

        if (selections.Skills)
        {
            selected.Add("skills");
        }

        if (selections.Tla)
        {
            selected.Add("tla");
        }

        return string.Join(',', selected);
    }

    private static void AssertRefusal(
        PlanRefusalCategory? expected,
        Action action,
        string? description = null)
    {
        try
        {
            action();
        }
        catch (PlanRefusalException refusal)
        {
            if (expected is null || refusal.Category == expected)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Expected refusal category {expected}"
                + $"{(description is null ? "" : $" for {description}")}, "
                + $"got {refusal.Category}: {refusal.Message}");
        }

        throw new InvalidOperationException(
            $"Expected a planner refusal"
            + $"{(description is null ? "" : $" for {description}")}"
            + $"{(expected is null ? "" : $" of category {expected}")}.");
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
