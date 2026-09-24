#:project ../src/DotnetInspector.ResearchQueries/DotnetInspector.ResearchQueries.csproj

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Inspector.Findings;

return await RetirementReport.RunAsync(args);

static class RetirementReport
{
    const string LegacyTag = "v0.26.0";
    const string FullyLegacyProductVersion = "0.25.0";
    const string ShippedContinuityVersion = "0.26.0";

    static readonly IReadOnlyDictionary<string, string> CorpusIdentities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MessagePack.dll"] = "nuget:MessagePack@2.5.192",
            ["MimeKit.dll"] = "nuget:MimeKit@4.8.0",
            ["Npgsql.dll"] = "nuget:Npgsql@8.0.4",
            ["Pipelines.Sockets.Unofficial.dll"] =
                "nuget:Pipelines.Sockets.Unofficial@2.2.8",
            ["Prometheus.NetStandard.dll"] = "nuget:prometheus-net@8.2.1",
            ["QuanTAlib.dll"] = "nuget:QuanTAlib@0.1.0",
            ["System.Text.Json.dll"] = "nuget:System.Text.Json@5.0.2",
            ["TouchSocket.dll"] = "nuget:TouchSocket@3.1.5",
            ["ZLinq.dll"] = "nuget:ZLinq@1.4.9",
        };

    public static async Task<int> RunAsync(string[] args)
    {
        Options options = Options.Parse(args);
        VerifyLegacyOracleUnchanged();

        string head = RunGit("rev-parse", "HEAD");
        string fullyLegacyCommit =
            RunGit("rev-parse", $"v{FullyLegacyProductVersion}");
        string continuityCommit = RunGit("rev-parse", LegacyTag);
        string fullyLegacyProduct =
            await RunCommandAsync(
                "dnx",
                $"dotnet-inspect@{FullyLegacyProductVersion}",
                "-y",
                "--",
                "--version");
        string continuityProduct =
            await RunCommandAsync(
                "dnx",
                $"dotnet-inspect@{ShippedContinuityVersion}",
                "-y",
                "--",
                "--version");

        List<Input> inputs = LoadInputs(options);
        var rows = new List<LedgerRow>();
        var summaries = new List<InputSummary>();

        rows.Add(new(
            Domain: "provenance",
            Input: "comparison",
            Facet: "baseline",
            Identity: "source",
            Legacy: $"{fullyLegacyProduct.Trim()} | {fullyLegacyCommit}",
            Generic: $"{continuityProduct.Trim()} | {continuityCommit} | {head}",
            Classification: "Parity",
            Basis: "v0.25.0 is the fully legacy product baseline; the three "
                + "in-tree ArrayPool-specific oracle files are unchanged from "
                + "published v0.26.0."));

        foreach (Input input in inputs.OrderBy(static input => input.Identity))
        {
            Console.Error.WriteLine($"Comparing {input.Identity}");
            summaries.Add(
                await CompareInputAsync(input, options.CurrentCli, rows));
        }

        rows.Sort(LedgerRowComparer.Instance);
        Directory.CreateDirectory(
            Path.GetDirectoryName(options.JsonlPath)
                ?? throw new InvalidOperationException(
                    "The JSONL output must have a parent directory."));
        await File.WriteAllLinesAsync(
            options.JsonlPath,
            rows.Select(SerializeRow));
        Directory.CreateDirectory(
            Path.GetDirectoryName(options.MarkdownPath)
                ?? throw new InvalidOperationException(
                    "The Markdown output must have a parent directory."));
        await File.WriteAllTextAsync(
            options.MarkdownPath,
            FormatMarkdown(
                head,
                fullyLegacyCommit,
                continuityCommit,
                fullyLegacyProduct.Trim(),
                continuityProduct.Trim(),
                summaries,
                rows));

        int defects = rows.Count(static row =>
            row.Classification == "Defect");
        Console.WriteLine(
            $"Wrote {rows.Count} ledger rows; {defects} defects.");
        return defects == 0 ? 0 : 2;
    }

    static List<Input> LoadInputs(Options options)
    {
        var inputs = new List<Input>();
        foreach (string line in File.ReadLines(options.CorpusList))
        {
            string path = Path.GetFullPath(line.Trim());
            string fileName = Path.GetFileName(path);
            if (!CorpusIdentities.TryGetValue(
                    fileName,
                    out string? identity))
            {
                throw new InvalidOperationException(
                    $"No pinned package identity is registered for '{fileName}'.");
            }
            inputs.Add(new(identity, path, "package"));
        }
        inputs.AddRange(options.Fixtures);

        foreach (Input input in inputs)
        {
            if (!File.Exists(input.Path))
            {
                throw new FileNotFoundException(
                    $"Comparison input '{input.Identity}' does not exist.",
                    input.Path);
            }
        }
        return inputs;
    }

    static async Task<InputSummary> CompareInputAsync(
        Input input,
        string currentCli,
        List<LedgerRow> rows)
    {
        int firstRow = rows.Count;
        try
        {
            CommandResult oldPublished = await RunLibraryTriageAsync(
                "dnx",
                [
                    $"dotnet-inspect@{FullyLegacyProductVersion}",
                    "-y",
                    "--",
                ],
                input.Path);
            CommandResult continuityPublished = await RunLibraryTriageAsync(
                "dnx",
                [
                    $"dotnet-inspect@{ShippedContinuityVersion}",
                    "-y",
                    "--",
                ],
                input.Path);
            CommandResult current = await RunLibraryTriageAsync(
                "dotnet",
                [currentCli],
                input.Path);
            AddCliRow(
                input,
                "v0.25.0-to-v0.26.0",
                oldPublished,
                continuityPublished,
                rows);
            AddCliRow(
                input,
                "v0.26.0-to-current",
                continuityPublished,
                current,
                rows);

            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(input.Path)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
            ResourceEffectAdmission admission =
                ArrayPoolResourceEffectModel.Create();
            LibraryBodyAnalysisExecution genericExecution =
                LibraryBodyAnalysisService.ExecutePath(
                    input.Path,
                    LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                        admission),
                    resolver);
            LibraryBodyIndex legacyIndex = LibraryBodyIndex.Open(
                input.Path,
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.OwnershipFlow);

            int lifecycleCount = CompareLifecycle(
                input,
                genericExecution,
                rows);
            (int rootCount, int pathCount) = await CompareResearchAsync(
                input,
                resolver,
                admission,
                legacyIndex,
                genericExecution.ResourceOwnership,
                rows);
            IReadOnlyList<LedgerRow> inputRows =
                rows.Skip(firstRow).ToArray();
            return new(
                input.Identity,
                lifecycleCount,
                rootCount,
                pathCount,
                inputRows.Count(row =>
                    row.Classification == "IntentionalImprovement"),
                inputRows.Count(row =>
                    row.Classification == "Defect"));
        }
        catch (Exception ex)
        {
            rows.Add(new(
                "operation",
                input.Identity,
                "execution",
                "comparison",
                ex.GetType().Name,
                ex.Message,
                "Defect",
                "The comparison must execute successfully on every pinned input."));
            return new(input.Identity, 0, 0, 0, 0, 1);
        }
    }

    static void AddCliRow(
        Input input,
        string identity,
        CommandResult legacy,
        CommandResult generic,
        List<LedgerRow> rows)
    {
        string[] legacyLines = NormalizeCommandResult(legacy);
        string[] genericLines = NormalizeCommandResult(generic);
        bool equal = legacyLines.SequenceEqual(
            genericLines,
            StringComparer.Ordinal);
        bool shippedCompatibilityChange =
            !equal && identity == "v0.25.0-to-v0.26.0";
        rows.Add(new(
            "published-cli",
            input.Identity,
            "resource-triage-jsonl",
            identity,
            CommandSummary(legacyLines),
            CommandSummary(genericLines),
            equal
                ? "Parity"
                : shippedCompatibilityChange
                    ? "AcceptedCompatibilityChange"
                    : "Defect",
            equal
                ? "Sorted Resource Triage JSONL rows, diagnostics, and exit "
                    + "status are byte-identical."
                : shippedCompatibilityChange
                    ? "v0.26.0 already shipped the generic lifecycle migration "
                        + "and its explicit incomplete-analysis diagnostics; "
                        + "v0.25.0 remains the fully legacy behavior record."
                    : "The current Resource Triage JSONL, diagnostics, or exit "
                        + "status changed from v0.26.0."));
    }

    static int CompareLifecycle(
        Input input,
        LibraryBodyAnalysisExecution genericExecution,
        List<LedgerRow> rows)
    {
        var subject = new FindingSubject(input.Identity, input.Identity);
        FindingInspection<ResourceLifecycleOccurrence> legacyResult =
            ResourceLifecycleAnalysis.InspectAssembly(input.Path, subject);
        FindingInspection<ResourceLifecycleOccurrence> genericResult =
            ResourceLifecycleAnalysis.Inspect(
                genericExecution.ResourceLifecycle,
                subject);
        var legacy =
            legacyResult.Value
                as FindingInspection<ResourceLifecycleOccurrence>.Complete;
        var generic =
            genericResult.Value
                as FindingInspection<ResourceLifecycleOccurrence>.Complete;
        bool stateEqual =
            legacyResult.Value.GetType() == genericResult.Value.GetType();
        bool typedFailureTransition =
            legacy is not null
            && genericResult.Value
                is FindingInspection<ResourceLifecycleOccurrence>.Failed;
        rows.Add(new(
            "lifecycle",
            input.Identity,
            "result-state",
            "whole-library",
            InspectionState(legacyResult),
            InspectionState(genericResult),
            stateEqual
                ? "Parity"
                : typedFailureTransition
                    ? "AcceptedCompatibilityChange"
                    : "Defect",
            stateEqual
                ? "Both engines publish the same typed inspection state."
                : typedFailureTransition
                    ? "The shipped generic Resource Triage contract fails "
                        + "closed when producer-wide typed limitations prevent "
                        + "a complete census "
                        + "(resource-lifecycle-analysis.md"
                        + "#resource-triage-migration)."
                    : "The engines publish incompatible inspection states."));
        if (legacy is null || generic is null)
            return 0;

        ResourceTriageAssessment[] legacyAssessments =
        [
            .. ResourceTriageAnalysis.Assess(legacy)
                .OrderBy(static assessment => assessment.CandidateId),
        ];
        ResourceTriageAssessment[] genericAssessments =
        [
            .. ResourceTriageAnalysis.Assess(generic)
                .OrderBy(static assessment => assessment.CandidateId),
        ];
        var legacyById = legacyAssessments.ToDictionary(
            static assessment => assessment.CandidateId,
            StringComparer.Ordinal);
        var genericById = genericAssessments.ToDictionary(
            static assessment => assessment.CandidateId,
            StringComparer.Ordinal);
        foreach (string candidateId in legacyById.Keys
            .Union(genericById.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            legacyById.TryGetValue(
                candidateId,
                out ResourceTriageAssessment? legacyAssessment);
            genericById.TryGetValue(
                candidateId,
                out ResourceTriageAssessment? genericAssessment);
            bool equal =
                legacyAssessment is not null
                && genericAssessment is not null
                && LifecycleEquivalent(
                    legacyAssessment,
                    genericAssessment);
            bool genericAddition =
                legacyAssessment is null
                && genericAssessment is not null;
            rows.Add(new(
                "lifecycle",
                input.Identity,
                "typed-assessment",
                candidateId,
                LifecycleSummary(legacyAssessment),
                LifecycleSummary(genericAssessment),
                equal
                    ? "Parity"
                    : genericAddition
                        ? "IntentionalImprovement"
                        : "Defect",
                equal
                    ? "ResourceTriageAssessment value equality, excluding "
                        + "incidental stream ordinal, covers "
                        + "Finding identity and payload, acquisition and boundary "
                        + "coordinates, actionability, reason, impact, remediation, "
                        + "confidence, descriptor, and detail."
                    : genericAddition
                        ? "The generic root-local lifecycle contract preserves "
                            + "a sound positive outcome that the narrower "
                            + "ArrayPool-specific oracle did not publish."
                        : "The typed Resource Triage assessments differ."));
        }

        bool populationEqual =
            legacyAssessments.Length == genericAssessments.Length
            && legacyAssessments.Zip(genericAssessments)
                .All(pair => LifecycleEquivalent(
                    pair.First,
                    pair.Second));
        bool genericSuperset =
            legacyAssessments.All(expected =>
                genericById.TryGetValue(
                    expected.CandidateId,
                    out ResourceTriageAssessment? actual)
                && LifecycleEquivalent(expected, actual));
        rows.Add(new(
            "lifecycle",
            input.Identity,
            "population",
            "all-assessments",
            $"{legacyAssessments.Length} assessments",
            $"{genericAssessments.Length} assessments",
            populationEqual
                ? "Parity"
                : genericSuperset
                    ? "IntentionalImprovement"
                    : "Defect",
            populationEqual
                ? "The ordered complete typed assessment populations are equal."
                : genericSuperset
                    ? "Every legacy assessment remains exact and the generic "
                        + "root-local contract publishes additional sound "
                        + "positive outcomes."
                    : "The generic population does not preserve every legacy "
                        + "typed assessment."));
        return genericAssessments.Length;
    }

    static async Task<(int RootCount, int PathCount)> CompareResearchAsync(
        Input input,
        AssemblyDependencyResolver resolver,
        ResourceEffectAdmission admission,
        LibraryBodyIndex legacyIndex,
        LibraryResourceOwnershipAnalysisResult genericOwnership,
        List<LedgerRow> rows)
    {
        Dictionary<int, ArrayPoolOwnershipMethodEvidence> legacyRoots =
            legacyIndex.ArrayPoolOwnership
                .Where(static method => !method.Rents.IsEmpty)
                .ToDictionary(static method => method.Method.MetadataToken);
        Dictionary<int, ResourceOwnershipMethodSummary> genericRoots =
            genericOwnership.Methods
                .Where(method => method.Acquisitions.Any(acquisition =>
                    acquisition.Obligation.ResourceKinds.Any(kind =>
                        kind.Identity
                            == ArrayPoolResourceEffectModel.BufferKind)))
                .ToDictionary(static method => method.Method.MetadataToken);
        int[] roots =
        [
            .. legacyRoots.Keys
                .Union(genericRoots.Keys)
                .Order(),
        ];

        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                input.Path,
                AssemblyResolutionProvenance.Local(
                    "ArrayPool ownership retirement comparison"));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [new AssemblyContextParticipant(source, resolver)]);
        int pathCount = 0;

        foreach (int root in roots)
        {
            legacyRoots.TryGetValue(
                root,
                out ArrayPoolOwnershipMethodEvidence? legacyRoot);
            genericRoots.TryGetValue(
                root,
                out ResourceOwnershipMethodSummary? genericRoot);
            string method = MethodLabel(
                legacyRoot?.Method ?? genericRoot!.Method);
            bool rootParity =
                legacyRoot is not null && genericRoot is not null;
            bool genericRootAddition =
                legacyRoot is null && genericRoot is not null;
            rows.Add(new(
                "research",
                input.Identity,
                "root-population",
                method,
                legacyRoot is null
                    ? "absent"
                    : $"{legacyRoot.Rents.Length} acquisitions",
                genericRoot is null
                    ? "absent"
                    : $"{genericRoot.Acquisitions.Count(acquisition =>
                        acquisition.Obligation.ResourceKinds.Any(kind =>
                            kind.Identity
                                == ArrayPoolResourceEffectModel.BufferKind))} acquisitions",
                rootParity
                    ? "Parity"
                    : genericRootAddition
                        ? "IntentionalImprovement"
                        : "Defect",
                rootParity
                    ? "Both engines select the same physical focus method."
                    : genericRootAddition
                        ? "The generic Resource Occurrence contract recognizes "
                            + "an ArrayPool acquisition root outside the "
                            + "narrower legacy flow producer."
                        : "The generic engine omitted a legacy ArrayPool "
                            + "acquisition root."));
            CompareResearchAcquisitions(
                input,
                method,
                legacyRoot,
                genericRoot,
                rows);

            using var graph = new MemberCallGraphSession(
                group,
                source,
                root,
                new MemberCallGraphOptions
                {
                    Features =
                        LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.OwnershipFlow,
                    ResourceEffects = admission,
                });
            MemberCallGraphView view = graph.Callers();
            CallGraphProjection projection =
                CallGraphProjection.Create(
                    view.CallerRoot,
                    view.CalleeRoot);
            AnnotatedCallGraphOwnershipInspection legacy =
                ArrayPoolOwnershipPathFindings.Inspect(
                    view,
                    projection);
            ResourceOwnershipPathInspection generic =
                ResourceOwnershipPathFindings.Inspect(
                    view,
                    projection,
                    ResourceOwnershipSearchOptions.ArrayPool);
            pathCount += generic.Findings.Length;
            CompareResearchLimits(input, method, legacy, generic, rows);
            CompareResearchPaths(input, method, view, legacy, generic, rows);
        }
        return (roots.Length, pathCount);
    }

    static void CompareResearchLimits(
        Input input,
        string method,
        AnnotatedCallGraphOwnershipInspection legacy,
        ResourceOwnershipPathInspection generic,
        List<LedgerRow> rows)
    {
        bool equal = legacy.Limits == generic.Limits;
        string classification =
            equal
                ? "Parity"
                : "IntentionalImprovement";
        rows.Add(new(
            "research",
            input.Identity,
            "operation-completeness",
            method,
            legacy.Limits.ToString(),
            generic.Limits.ToString(),
            classification,
            classification switch
            {
                "Parity" =>
                    "Both engines report the same operation-level limits.",
                "IntentionalImprovement" =>
                    "The generic owner reports independent traversal, body, "
                    + "correspondence, and publication limits instead of "
                    + "preserving the legacy aggregate classification.",
                _ => throw new InvalidOperationException(),
            }));
    }

    static void CompareResearchPaths(
        Input input,
        string method,
        MemberCallGraphView view,
        AnnotatedCallGraphOwnershipInspection legacy,
        ResourceOwnershipPathInspection generic,
        List<LedgerRow> rows)
    {
        Dictionary<string, List<Finding<ArrayPoolOwnershipPathWitness>>>
            legacyByPath = legacy.Findings
                .GroupBy(finding => CanonicalPath(
                    view.FocusModuleVersionId,
                    view.FocusMethodToken,
                    finding.Payload))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .OrderBy(finding => finding.Key.IdentityKey)
                        .ToList(),
                    StringComparer.Ordinal);
        Dictionary<string, List<Finding<ResourceOwnershipPathWitness>>>
            genericByPath = generic.Findings
                .GroupBy(finding => CanonicalPath(finding.Payload))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .OrderBy(finding => finding.Key.IdentityKey)
                        .ToList(),
                    StringComparer.Ordinal);

        foreach (string path in legacyByPath.Keys
            .Union(genericByPath.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            legacyByPath.TryGetValue(
                path,
                out List<Finding<ArrayPoolOwnershipPathWitness>>? oldFindings);
            genericByPath.TryGetValue(
                path,
                out List<Finding<ResourceOwnershipPathWitness>>? newFindings);
            int oldCount = oldFindings?.Count ?? 0;
            int newCount = newFindings?.Count ?? 0;
            bool equal = oldCount == newCount;
            bool genericAddition =
                oldCount == 0 && newCount > 0;
            bool terminalTransferSuppression =
                oldCount > 0
                && newCount == 0
                && oldFindings!.All(oldFinding =>
                    oldFinding.Payload.Outcome
                        == ArrayPoolOwnershipUseKind.ReturnedToPool
                    && generic.Findings.Any(newFinding =>
                        newFinding.Payload.Obligation.Call.ILOffset
                            == oldFinding.Payload.RentOffset
                        && newFinding.Payload.Outcome
                            == ResourceOwnershipPathOutcome.Stored));
            rows.Add(new(
                "research",
                input.Identity,
                "semantic-path",
                $"{method}|{path}",
                $"{oldCount} witnesses",
                $"{newCount} witnesses",
                equal
                    ? "Parity"
                    : genericAddition || terminalTransferSuppression
                        ? "IntentionalImprovement"
                        : "Defect",
                equal
                    ? "Acquisition, ordered physical forwarding coordinates, "
                        + "terminal outcome, and sink identity are equal."
                    : genericAddition
                        ? "The generic typed ownership contract proves a "
                            + "terminal path outside the narrower legacy flow "
                            + "producer."
                        : terminalTransferSuppression
                            ? "The generic path stops at the proven field-store "
                                + "terminal and does not claim a later release "
                                + "through the stored alias; field reachability "
                                + "is outside the owned contract."
                            : "The generic engine omitted or changed a legacy "
                                + "semantic ownership path."));
            if (!equal)
                continue;

            for (int index = 0; index < oldCount; index++)
            {
                Finding<ArrayPoolOwnershipPathWitness> oldFinding =
                    oldFindings![index];
                Finding<ResourceOwnershipPathWitness> newFinding =
                    newFindings![index];
                bool keyEqual =
                    oldFinding.Key == newFinding.Key;
                rows.Add(new(
                    "research",
                    input.Identity,
                    "finding-identity",
                    $"{method}|{path}|{index}",
                    oldFinding.Key.IdentityKey,
                    newFinding.Key.IdentityKey,
                    keyEqual ? "Parity" : "IntentionalImprovement",
                    keyEqual
                        ? "Finding identity is unchanged."
                        : "The generic owner intentionally adds resource-kind "
                            + "identity and bound resource arguments to the key "
                            + "(generic-research-ownership-paths.md"
                            + "#research-composition-contract)."));
                rows.Add(new(
                    "research",
                    input.Identity,
                    "path-completeness",
                    $"{method}|{path}|{index}",
                    "not represented",
                    newFinding.Payload.IsComplete.ToString(),
                    "IntentionalImprovement",
                    "The generic owner adds path-local completeness separately "
                        + "from operation-level limits "
                        + "(generic-research-ownership-paths.md"
                        + "#research-composition-contract)."));
            }
        }
    }

    static void CompareResearchAcquisitions(
        Input input,
        string method,
        ArrayPoolOwnershipMethodEvidence? legacy,
        ResourceOwnershipMethodSummary? generic,
        List<LedgerRow> rows)
    {
        int[] legacyOffsets =
        [
            .. legacy?.Rents.Select(static rent => rent.RentOffset)
                ?? [],
        ];
        int[] genericOffsets =
        [
            .. generic?.Acquisitions
                .Where(acquisition =>
                    acquisition.Obligation.ResourceKinds.Any(kind =>
                        kind.Identity
                            == ArrayPoolResourceEffectModel.BufferKind))
                .Select(acquisition =>
                    acquisition.Obligation.Call.ILOffset)
                ?? [],
        ];
        foreach (int offset in legacyOffsets
            .Union(genericOffsets)
            .Order())
        {
            int legacyCount = legacyOffsets.Count(value => value == offset);
            int genericCount = genericOffsets.Count(value => value == offset);
            bool equal = legacyCount == genericCount;
            bool genericAddition =
                legacyCount == 0 && genericCount > 0;
            rows.Add(new(
                "research",
                input.Identity,
                "acquisition",
                $"{method}|IL_{offset:X4}",
                $"{legacyCount} roots",
                $"{genericCount} roots",
                equal
                    ? "Parity"
                    : genericAddition
                        ? "IntentionalImprovement"
                        : "Defect",
                equal
                    ? "Both engines preserve the same acquisition coordinate "
                        + "and multiplicity."
                    : genericAddition
                        ? "The generic Resource Occurrence contract recognizes "
                            + "a typed ArrayPool acquisition omitted by the "
                            + "legacy producer."
                        : "The generic engine omitted or duplicated a legacy "
                            + "ArrayPool acquisition coordinate."));
        }
    }

    static string InspectionState(
        FindingInspection<ResourceLifecycleOccurrence> inspection) =>
        inspection.Value switch
        {
            FindingInspection<ResourceLifecycleOccurrence>.Complete complete =>
                $"Complete({complete.Findings.Length})",
            FindingInspection<ResourceLifecycleOccurrence>.Failed failed =>
                $"Failed({failed.Error.Reason})",
            FindingInspection<ResourceLifecycleOccurrence>.Absent absent =>
                $"Absent({absent.Kind}: {absent.Detail})",
            null => "no active result",
            object value => value.GetType().Name,
        };

    static bool LifecycleEquivalent(
        ResourceTriageAssessment left,
        ResourceTriageAssessment right) =>
        string.Equals(
            left.CandidateId,
            right.CandidateId,
            StringComparison.Ordinal)
        && left.Source.Subject == right.Source.Subject
        && left.Source.Descriptor == right.Source.Descriptor
        && left.Source.Key == right.Source.Key
        && left.Source.Payload == right.Source.Payload
        && string.Equals(
            left.Source.Detail,
            right.Source.Detail,
            StringComparison.Ordinal)
        && left.Boundaries.AsSpan().SequenceEqual(
            right.Boundaries.AsSpan())
        && left.Actionability == right.Actionability
        && left.Reason == right.Reason
        && left.Impact == right.Impact
        && left.Remediation == right.Remediation
        && left.Confidence == right.Confidence;

    static string LifecycleSummary(
        ResourceTriageAssessment? assessment)
    {
        if (assessment is null)
            return "absent";
        ResourceLifecycleOccurrence payload = assessment.Source.Payload;
        return $"{assessment.Source.Key.IdentityKey} | "
            + $"{MethodLabel(payload.Method)} | "
            + $"IL_{payload.AcquireOffset:X4} | "
            + $"{assessment.Actionability} | "
            + $"{string.Join(",", payload.Boundaries.Select(boundary =>
                $"IL_{boundary.ILOffset:X4}:{MemberLabel(boundary.Operation)}"))}";
    }

    static string CanonicalPath(
        Guid focusModuleVersionId,
        int focusMethodToken,
        ArrayPoolOwnershipPathWitness witness) =>
        $"{focusModuleVersionId:N}:{focusMethodToken:X8}:"
        + $"{witness.RentOffset:X8}|"
        + $"{CanonicalSteps(witness.Steps.Select(step => (
            step.EdgeRow,
            step.CallerModuleVersionId,
            step.CallerMethodToken,
            step.ILOffset,
            step.OperandToken,
            step.CalleeParameterIndex)))}|"
        + $"{CanonicalOutcome(witness.Outcome)}|"
        + $"{witness.SinkModuleVersionId:N}:"
        + $"{witness.SinkMethodToken:X8}:"
        + $"{witness.SinkParameterIndex}:"
        + $"{witness.SinkOffset:X8}";

    static string CanonicalPath(ResourceOwnershipPathWitness witness)
    {
        ResourceOccurrenceCallSite call = witness.Obligation.Call;
        return $"{call.Method.ModuleVersionId:N}:"
            + $"{call.Method.MetadataToken:X8}:"
            + $"{call.ILOffset:X8}|"
            + $"{CanonicalSteps(witness.Steps.Select(step => (
                step.EdgeRow,
                step.CallerModuleVersionId,
                step.CallerMethodToken,
                step.ILOffset,
                step.OperandToken,
                step.CalleeParameterIndex)))}|"
            + $"{witness.Outcome}|"
            + $"{witness.SinkModuleVersionId:N}:"
            + $"{witness.SinkMethodToken:X8}:"
            + $"{witness.SinkParameterIndex}:"
            + $"{witness.SinkOffset:X8}";
    }

    static string CanonicalSteps(
        IEnumerable<(int EdgeRow, Guid ModuleVersionId, int MethodToken,
            int ILOffset, int OperandToken, int ParameterIndex)> steps) =>
        string.Join(
            ">",
            steps.Select(step =>
                $"{step.EdgeRow}:{step.ModuleVersionId:N}:"
                + $"{step.MethodToken:X8}:{step.ILOffset:X8}:"
                + $"{step.OperandToken:X8}:{step.ParameterIndex}"));

    static ResourceOwnershipPathOutcome CanonicalOutcome(
        ArrayPoolOwnershipUseKind outcome) =>
        outcome switch
        {
            ArrayPoolOwnershipUseKind.ReturnedToPool =>
                ResourceOwnershipPathOutcome.Released,
            ArrayPoolOwnershipUseKind.Stored =>
                ResourceOwnershipPathOutcome.Stored,
            ArrayPoolOwnershipUseKind.ReturnedToCaller =>
                ResourceOwnershipPathOutcome.ReturnedToCaller,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    static string MethodLabel(MethodIdentity method) =>
        $"{method.ModuleVersionId:N}:{method.MetadataToken:X8}:"
        + $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}";

    static string MemberLabel(MemberRef member) =>
        $"{member.DeclaringType.ToQualifiedDisplayString()}::{member.Name}"
        + $"({string.Join(",", member.ParameterTypes.Select(
            static parameter => parameter.ToQualifiedDisplayString()))})";

    static string[] NormalizeLines(string value) =>
    [
        .. value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .Order(StringComparer.Ordinal),
    ];

    static string[] NormalizeCommandResult(CommandResult result) =>
    [
        $"exit={result.ExitCode}",
        .. NormalizeLines(result.StandardOutput)
            .Select(static line => $"stdout:{line}"),
        .. NormalizeLines(result.StandardError)
            .Select(static line => $"stderr:{line}"),
    ];

    static string Fingerprint(IEnumerable<string> lines)
    {
        byte[] content = Encoding.UTF8.GetBytes(
            string.Join('\n', lines));
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(content))
            .ToLowerInvariant();
    }

    static string CommandSummary(IReadOnlyList<string> lines) =>
        $"{lines.Count} records | sha256:{Fingerprint(lines)} | "
        + string.Join(" || ", lines);

    static string SerializeRow(LedgerRow row)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("domain", row.Domain);
            writer.WriteString("input", row.Input);
            writer.WriteString("facet", row.Facet);
            writer.WriteString("identity", row.Identity);
            writer.WriteString("legacy", row.Legacy);
            writer.WriteString("generic", row.Generic);
            writer.WriteString("classification", row.Classification);
            writer.WriteString("basis", row.Basis);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static async Task<CommandResult> RunLibraryTriageAsync(
        string executable,
        IReadOnlyList<string> prefix,
        string path)
    {
        var arguments = new List<string>(prefix)
        {
            "library",
            path,
            "-S",
            "Resource Triage",
            "--jsonl",
        };
        return await RunCommandResultAsync(executable, [.. arguments]);
    }

    static string RunGit(params string[] arguments) =>
        RunCommandAsync("git", arguments).GetAwaiter().GetResult().Trim();

    static void VerifyLegacyOracleUnchanged()
    {
        string[] files =
        [
            "src/ILInspector.Analysis/ArrayPoolOwnershipFlow.cs",
            "src/ILInspector.Analysis/LeakTriage.cs",
            "src/DotnetInspector.ResearchQueries/ArrayPoolOwnershipPathFindings.cs",
        ];
        RunCommandAsync(
            "git",
            [
                "diff",
                "--exit-code",
                $"{LegacyTag}..HEAD",
                "--",
                .. files,
            ]).GetAwaiter().GetResult();
    }

    static async Task<string> RunCommandAsync(
        string executable,
        params string[] arguments)
    {
        CommandResult result =
            await RunCommandResultAsync(executable, arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{executable} {string.Join(" ", arguments)}' failed with "
                + $"exit code {result.ExitCode}: "
                + $"{result.StandardError.Trim()}");
        }
        return result.StandardOutput;
    }

    static async Task<CommandResult> RunCommandResultAsync(
        string executable,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException(
                $"Could not start '{executable}'.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await outputTask;
        string error = await errorTask;
        return new(process.ExitCode, output, error);
    }

    static string FormatMarkdown(
        string head,
        string fullyLegacyCommit,
        string continuityCommit,
        string fullyLegacyProduct,
        string continuityProduct,
        IReadOnlyList<InputSummary> summaries,
        IReadOnlyList<LedgerRow> rows)
    {
        int parity = rows.Count(static row =>
            row.Classification == "Parity");
        int improvements = rows.Count(static row =>
            row.Classification == "IntentionalImprovement");
        int accepted = rows.Count(static row =>
            row.Classification == "AcceptedCompatibilityChange");
        int defects = rows.Count(static row =>
            row.Classification == "Defect");
        int addedLifecycle = rows.Count(static row =>
            row.Domain == "lifecycle"
            && row.Facet == "typed-assessment"
            && row.Classification == "IntentionalImprovement");
        int addedRoots = rows.Count(static row =>
            row.Domain == "research"
            && row.Facet == "root-population"
            && row.Classification == "IntentionalImprovement");
        int addedAcquisitions = rows.Count(static row =>
            row.Domain == "research"
            && row.Facet == "acquisition"
            && row.Classification == "IntentionalImprovement");
        int addedPaths = rows.Count(static row =>
            row.Domain == "research"
            && row.Facet == "semantic-path"
            && row.Classification == "IntentionalImprovement"
            && row.Legacy == "0 witnesses");
        int suppressedLegacyPaths = rows.Count(static row =>
            row.Domain == "research"
            && row.Facet == "semantic-path"
            && row.Classification == "IntentionalImprovement"
            && row.Generic == "0 witnesses");
        int publishedCompatibilityChanges = rows.Count(static row =>
            row.Domain == "published-cli"
            && row.Classification == "AcceptedCompatibilityChange");
        int typedFailureTransitions = rows.Count(static row =>
            row.Domain == "lifecycle"
            && row.Facet == "result-state"
            && row.Classification == "AcceptedCompatibilityChange");
        var text = new StringBuilder();
        text.AppendLine("# ArrayPool ownership retirement report");
        text.AppendLine();
        text.AppendLine(
            defects == 0
                ? "**Verdict:** the generic lifecycle and Research ownership "
                    + "paths are ready to replace the ArrayPool-specific "
                    + "implementations for the measured scope."
                : $"**Verdict:** retirement is blocked by {defects} "
                    + "unclassified or defective differences.");
        text.AppendLine();
        text.AppendLine("## Baselines");
        text.AppendLine();
        text.AppendLine(
            $"- Fully legacy product: `{fullyLegacyProduct}` "
            + $"(`{fullyLegacyCommit}`).");
        text.AppendLine(
            $"- Shipped continuity product: `{continuityProduct}` "
            + $"(`{continuityCommit}`).");
        text.AppendLine(
            $"- Generic comparison head: `{head}`.");
        text.AppendLine(
            "- The in-tree `LeakTriage`, `ArrayPoolOwnershipFlow`, and "
            + "`ArrayPoolOwnershipPathFindings` oracle files are byte-for-byte "
            + "unchanged from the v0.26.0 source tag; the producer refuses to "
            + "run if that condition is false.");
        text.AppendLine();
        text.AppendLine("## Method");
        text.AppendLine();
        text.AppendLine(
            "- Public product continuity compares sorted `Resource Triage` "
            + "JSONL from v0.25.0, v0.26.0, and the current CLI.");
        text.AppendLine(
            "- Lifecycle compares typed whole-library result states first, "
            + "then complete `ResourceTriageAssessment` values when both "
            + "engines complete.");
        text.AppendLine(
            "- Research compares acquisition roots, physical forwarding "
            + "coordinates, terminal outcome and sink identity, operation "
            + "limits, Finding identity, and path-local completeness over one "
            + "shared call-graph projection.");
        text.AppendLine(
            "- The JSONL ledger beside this report is the complete "
            + "machine-readable classification. No display text is used as a "
            + "typed comparison authority.");
        text.AppendLine();
        text.AppendLine("## Observed differences");
        text.AppendLine();
        text.AppendLine(
            $"- Lifecycle preserves every comparable legacy assessment and "
            + $"adds {addedLifecycle} owner-specified positive assessments. "
            + "Finding ordinals are excluded because they are stream metadata; "
            + "identity, payload, coordinates, boundaries, policy fields, "
            + "descriptor, and detail remain exact.");
        text.AppendLine(
            $"- Research adds {addedRoots} focus roots, "
            + $"{addedAcquisitions} acquisition coordinates, and "
            + $"{addedPaths} typed terminal paths that the narrower legacy "
            + "producer omitted.");
        text.AppendLine(
            $"- Research suppresses {suppressedLegacyPaths} legacy release "
            + "paths after the same obligation already reached a proven "
            + "field-store terminal; the generic contract does not traverse "
            + "the stored alias.");
        text.AppendLine(
            $"- v0.25.0 to v0.26.0 records "
            + $"{publishedCompatibilityChanges} already-shipped public JSONL, "
            + $"diagnostic, or exit-status changes and "
            + $"{typedFailureTransitions} corresponding typed fail-closed "
            + "transitions. Every v0.26.0-to-current comparison is exact.");
        text.AppendLine();
        text.AppendLine("## Population");
        text.AppendLine();
        text.AppendLine(
            "| Input | Lifecycle findings | Research roots | Research paths | "
            + "Intentional improvements | Defects |");
        text.AppendLine(
            "| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (InputSummary summary in summaries
            .OrderBy(static summary => summary.Input, StringComparer.Ordinal))
        {
            text.AppendLine(
                $"| `{summary.Input}` | {summary.LifecycleFindings} | "
                + $"{summary.ResearchRoots} | {summary.ResearchPaths} | "
                + $"{summary.IntentionalImprovements} | {summary.Defects} |");
        }
        text.AppendLine();
        text.AppendLine("## Classification");
        text.AppendLine();
        text.AppendLine(
            $"The ledger contains {rows.Count} rows: {parity} `Parity`, "
            + $"{improvements} `IntentionalImprovement`, {accepted} "
            + $"`AcceptedCompatibilityChange`, and {defects} `Defect`.");
        text.AppendLine();
        text.AppendLine(
            "`IntentionalImprovement` is limited to owner-specified generic "
            + "behavior: additional typed Resource Occurrence roots and "
            + "root-local lifecycle outcomes, typed resource-aware Finding "
            + "identity, path-local and operation-level completeness, and "
            + "stopping at a proven field-store terminal. An omitted legacy "
            + "acquisition or assessment, changed shared coordinate or sink, "
            + "or other unowned difference is a `Defect`.");
        text.AppendLine();
        text.AppendLine("## Reproduction");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine(
            "dotnet run eng/prepare-resource-triage-corpus.cs -- \\");
        text.AppendLine("  artifacts/resource-triage-corpus.txt");
        text.AppendLine(
            "dotnet build src/DotnetInspect.Cli/DotnetInspect.Cli.csproj \\");
        text.AppendLine("  -c Release");
        text.AppendLine(
            "dotnet build fixtures/analysis/"
            + "ILInspector.Analysis.OwnershipFlowFixtures/"
            + "ILInspector.Analysis.OwnershipFlowFixtures.csproj -c Release");
        text.AppendLine(
            "dotnet build fixtures/analysis/"
            + "ILInspector.Analysis.LookalikeFixtures/"
            + "ILInspector.Analysis.LookalikeFixtures.csproj -c Release");
        text.AppendLine(
            "dotnet run eng/produce-arraypool-ownership-retirement-report.cs -- \\");
        text.AppendLine(
            "  --corpus artifacts/resource-triage-corpus.txt \\");
        text.AppendLine(
            "  --current-cli artifacts/bin/dotnet-inspect/release/"
            + "dotnet-inspect.dll \\");
        text.AppendLine(
            "  --fixture fixture:ownership-flow=artifacts/bin/"
            + "ILInspector.Analysis.OwnershipFlowFixtures/release/"
            + "ILInspector.Analysis.OwnershipFlowFixtures.dll \\");
        text.AppendLine(
            "  --fixture fixture:arraypool-lookalikes=artifacts/bin/"
            + "ILInspector.Analysis.LookalikeFixtures/release/"
            + "ILInspector.Analysis.LookalikeFixtures.dll \\");
        text.AppendLine(
            "  --jsonl docs/evidence/arraypool-ownership-retirement.jsonl \\");
        text.AppendLine(
            "  --markdown docs/evidence/arraypool-ownership-retirement.md");
        text.AppendLine("```");
        return text.ToString();
    }

    sealed record Options(
        string CorpusList,
        string CurrentCli,
        string JsonlPath,
        string MarkdownPath,
        ImmutableArray<Input> Fixtures)
    {
        internal static Options Parse(string[] args)
        {
            string? corpus = null;
            string? currentCli = null;
            string? jsonl = null;
            string? markdown = null;
            var fixtures = ImmutableArray.CreateBuilder<Input>();
            for (int index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                    throw Usage();
                string option = args[index];
                string value = args[index + 1];
                switch (option)
                {
                    case "--corpus":
                        corpus = value;
                        break;
                    case "--current-cli":
                        currentCli = value;
                        break;
                    case "--jsonl":
                        jsonl = value;
                        break;
                    case "--markdown":
                        markdown = value;
                        break;
                    case "--fixture":
                        int separator = value.IndexOf('=');
                        if (separator <= 0
                            || separator == value.Length - 1)
                        {
                            throw Usage();
                        }
                        fixtures.Add(new(
                            value[..separator],
                            Path.GetFullPath(value[(separator + 1)..]),
                            "fixture"));
                        break;
                    default:
                        throw Usage();
                }
            }
            if (corpus is null
                || currentCli is null
                || jsonl is null
                || markdown is null
                || fixtures.Count == 0)
            {
                throw Usage();
            }
            return new(
                Path.GetFullPath(corpus),
                Path.GetFullPath(currentCli),
                Path.GetFullPath(jsonl),
                Path.GetFullPath(markdown),
                fixtures.ToImmutable());
        }

        static ArgumentException Usage() => new(
            "Usage: dotnet run eng/produce-arraypool-ownership-retirement-report.cs "
            + "-- --corpus <list> --current-cli <dll> --fixture <id=path> "
            + "[--fixture <id=path> ...] --jsonl <path> --markdown <path>");
    }

    sealed record Input(string Identity, string Path, string Kind);

    sealed record LedgerRow(
        string Domain,
        string Input,
        string Facet,
        string Identity,
        string Legacy,
        string Generic,
        string Classification,
        string Basis);

    sealed record InputSummary(
        string Input,
        int LifecycleFindings,
        int ResearchRoots,
        int ResearchPaths,
        int IntentionalImprovements,
        int Defects);

    sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    sealed class LedgerRowComparer : IComparer<LedgerRow>
    {
        internal static LedgerRowComparer Instance { get; } = new();

        public int Compare(LedgerRow? left, LedgerRow? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            int value = string.Compare(
                left.Domain,
                right.Domain,
                StringComparison.Ordinal);
            if (value != 0)
                return value;
            value = string.Compare(
                left.Input,
                right.Input,
                StringComparison.Ordinal);
            if (value != 0)
                return value;
            value = string.Compare(
                left.Facet,
                right.Facet,
                StringComparison.Ordinal);
            if (value != 0)
                return value;
            return string.Compare(
                left.Identity,
                right.Identity,
                StringComparison.Ordinal);
        }
    }
}
