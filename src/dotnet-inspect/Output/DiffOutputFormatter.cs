using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using ILInspector.Findings;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.Views;
using InertText;
using Markout;

using ILInspector.CSharp;

namespace DotnetInspector.Output;

/// <summary>
/// Formats diff command results for display.
/// </summary>
public static class DiffOutputFormatter
{
    /// <summary>
    /// Renders a type's simple name with generic arity expanded to a C#-friendly
    /// form (<c>JsonConverter`1</c> → <c>JsonConverter&lt;T&gt;</c>) for the human
    /// Markdown diff view only. Machine-facing tabular views (<c>--table</c>/<c>--tsv</c>/
    /// <c>--jsonl</c>) keep the canonical metadata name (arity backtick) so the Type
    /// field stays a stable, script-parseable identifier.
    /// </summary>
    private static InertString FormatTypeDisplayName(string typeFullName)
        => DiffViewText.Field(
            MetadataTypeNameFormatter.FormatGenericTypeName(TypeMatcher.GetSimpleName(typeFullName)));

    public static void RenderNameOnly(MarkoutWriter writer, IReadOnlyList<TypeDiff> typeDiffs)
    {
        foreach (var name in typeDiffs.Select(td => CSharpIdentifier.ContainRenderedText(td.TypeFullName)).OrderBy(n => n))
        {
            writer.WriteListItem(name);
        }
    }

    public static DiffTableView BuildTableView(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var rows = typeDiffs.OrderBy(td => td.TypeFullName).Select(td =>
        {
            string symbol;
            string detail;

            if (td.IsAdded)
            {
                symbol = "+";
                detail = "added";
            }
            else if (td.IsRemoved)
            {
                symbol = "-";
                detail = "removed";
            }
            else if (td.BreakingCount > 0)
            {
                symbol = "x";
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }
            else
            {
                symbol = "~";
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }

            return new DiffTableRow(
                DiffViewText.Field(symbol),
                DiffViewText.Field(TypeMatcher.GetSimpleName(td.TypeFullName)),
                DiffViewText.Field(detail));
        }).ToList();

        return new DiffTableView(
            DiffViewText.Field($"API Diff: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"),
            DiffViewText.Field(FormatSummaryCounts(
                totalBreaking,
                totalAdditive,
                totalPotentiallyBreaking)))
        {
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static DiffDetailedChangesView BuildDetailedChangesView(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var rows = typeDiffs
            .OrderBy(td => td.TypeFullName, StringComparer.Ordinal)
            .SelectMany(td => td.Changes.Select(change => BuildDetailedRow(td.TypeFullName, change)))
            .ToList();

        return new DiffDetailedChangesView(
            DiffViewText.Field($"API Diff: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"),
            DiffViewText.Field(FormatSummaryCounts(
                totalBreaking,
                totalAdditive,
                totalPotentiallyBreaking)))
        {
            Rows = rows.Count > 0 ? rows : null
        };
    }

    public static DiffDocumentView BuildDocumentView(
        string name,
        string fromVersion,
        string toVersion,
        DiffDetailedChangesView? changes,
        AnalysisDiffView? analysisDiff,
        ImplementationDiffView? implementationDiff,
        FindingTransitionsView? findingTransitions,
        IReadOnlyList<ApiDiffInspectionFailure> inspectionFailures)
        => new(
            DiffViewText.Field($"Diff: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"),
            changes?.SummaryText,
            analysisDiff?.SummaryText,
            DistinctStatusMessage(analysisDiff),
            implementationDiff?.SummaryText,
            DistinctStatusMessage(implementationDiff),
            findingTransitions is null
                ? null
                : DiffViewText.Field(findingTransitions.Status.Message),
            inspectionFailures.Count == 0
                ? null
                : DiffViewText.Prose(
                    "API comparison is incomplete because metadata "
                    + $"inspection reported {inspectionFailures.Count} "
                    + "failure(s)."))
        {
            Changes = changes?.Rows,
            AnalysisDiff = analysisDiff?.Rows,
            ImplementationDiff = implementationDiff?.Rows,
            FindingTransitions = findingTransitions?.Rows,
            InspectionFailures =
                BuildInspectionFailureRows(inspectionFailures),
        };

    public static string RenderDocumentView(DiffDocumentView view, MarkoutWriterOptions? options = null)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        writer.WriteHeading(1, view.Title);
        writer.WriteParagraph($"**Versions:** {view.Versions}");

        if (view.ChangesSummary is not null)
        {
            WriteDocumentSection(
                writer,
                "Changes",
                view.ChangesSummary,
                null,
                ["Change", "Classification", "Type", "Member", "Kind", "Detail", "Old", "New"],
                ["change", "classification", "type", "member", "kind", "detail", "old", "new"],
                view.Changes?.Select(row => new[]
                {
                    row.Change, row.Classification, row.Type, row.Member,
                    row.Kind, row.Detail, row.Old, row.New
                }));
        }

        if (view.AnalysisDiffSummary is not null)
        {
            WriteDocumentSection(
                writer,
                "Analysis Diff",
                view.AnalysisDiffSummary,
                view.AnalysisDiffNote,
                ["Member", "Signal", "Old", "New", "Delta", "Shape", "Evidence"],
                ["member", "signal", "old", "new", "delta", "shape", "evidence"],
                view.AnalysisDiff?.Select(row => new[]
                {
                    MarkoutInline.Code(row.MemberText), row.Signal, row.Old, row.New, row.Delta,
                    row.Shape ?? "", row.Evidence ?? ""
                }));
        }

        if (view.ImplementationDiffSummary is not null)
        {
            WriteDocumentSection(
                writer,
                "Implementation Diff",
                view.ImplementationDiffSummary,
                view.ImplementationDiffNote,
                ["Member", "Mechanism", "Difference", "Change", "Evidence"],
                ["member", "mechanism", "difference", "change", "evidence"],
                view.ImplementationDiff?.Select(row => new[]
                {
                    row.Member, row.Mechanism, row.Difference, row.Change, row.Evidence
                }));
        }

        if (view.FindingTransitionsSummary is not null)
        {
            WriteDocumentSection(
                writer,
                "Finding Transitions",
                view.FindingTransitionsSummary,
                null,
                ["Transition", "Finding", "Target", "From", "To", "Old", "New", "Detail"],
                ["transition", "finding", "target", "from", "to", "old", "new", "detail"],
                view.FindingTransitions?.Select(row => new[]
                {
                    row.Transition, row.Finding, row.Target, row.From,
                    row.To, row.Old, row.New, row.Detail ?? ""
                }));
        }

        if (view.InspectionFailuresSummary is not null)
        {
            WriteDocumentSection(
                writer,
                "Inspection Failures",
                view.InspectionFailuresSummary,
                null,
                [
                    "Side",
                    "Assembly",
                    "Operation",
                    "Subject",
                    "Mechanism",
                    "Kind",
                    "Detail",
                    "Dependency Assembly",
                ],
                [
                    "side",
                    "assembly",
                    "operation",
                    "subject",
                    "mechanism",
                    "kind",
                    "detail",
                    "dependency_assembly",
                ],
                view.InspectionFailures?.Select(row => new[]
                {
                    row.Side,
                    row.Assembly,
                    row.Operation,
                    row.Subject,
                    row.Mechanism,
                    row.Kind,
                    row.Detail,
                    row.DependencyAssembly ?? "",
                }));
        }

        return writer.ToString().TrimEnd();
    }

    private static void WriteDocumentSection(
        MarkoutWriter writer,
        string name,
        string summary,
        string? note,
        string[] headers,
        string[] stableHeaders,
        IEnumerable<string[]>? rows)
    {
        writer.WriteHeading(2, name);
        writer.WriteParagraph(summary);
        if (note is not null)
            writer.WriteCallout(CalloutSeverity.Note, note);
        if (rows is not null)
            writer.WriteTable(headers, stableHeaders, rows.ToArray());
    }

    private static InertString? DistinctStatusMessage(AnalysisDiffView? view)
        => view is not null && !string.Equals(view.Status.Message, view.Summary, StringComparison.Ordinal)
            ? DiffViewText.Prose(view.Status.Message)
            : null;

    private static InertString? DistinctStatusMessage(ImplementationDiffView? view)
        => view is not null && !string.Equals(view.Status.Message, view.Summary, StringComparison.Ordinal)
            ? DiffViewText.Prose(view.Status.Message)
            : null;

    public static FindingTransitionsView BuildFindingTransitionsView(
        string name,
        IReadOnlyList<FindingTransitionRow> rows,
        string fromVersion,
        string toVersion)
        => new(
            DiffViewText.Field($"Finding Transitions: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"))
        {
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, "No selected Finding exists at either endpoint.")
                : new Callout(CalloutSeverity.Note, $"{rows.Count} selected Finding transition{(rows.Count == 1 ? "" : "s")}."),
            Rows = rows.Count > 0 ? rows.ToList() : null
        };

    public static string RenderFindingTransitionsView(FindingTransitionsView view, MarkoutWriterOptions? options = null)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    public static DiffFullView BuildFullView(
        string name,
        IReadOnlyList<TypeDiff> typeDiffs,
        string fromVersion,
        string toVersion) =>
        BuildFullView(
            name,
            typeDiffs,
            [],
            fromVersion,
            toVersion);

    static DiffFullView BuildFullView(
        string name,
        IReadOnlyList<TypeDiff> typeDiffs,
        IReadOnlyList<ApiDiffInspectionFailure> inspectionFailures,
        string fromVersion,
        string toVersion)
    {
        var view = new DiffFullView(
            DiffViewText.Field($"API Diff: {name}"),
            DiffViewText.Field($"**{fromVersion}** → **{toVersion}**"),
            InertString.Empty)
        {
            InspectionFailures =
                BuildInspectionFailureRows(inspectionFailures),
        };

        if (typeDiffs.Count == 0)
        {
            view.Status = new Callout(
                CalloutSeverity.Note,
                inspectionFailures.Count == 0
                    ? "No API changes detected."
                    : "API comparison is incomplete because metadata inspection failed.");
            return view;
        }

        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        view.SummaryText = DiffViewText.Field(
            $"**Summary:** {FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking)} across {typeDiffs.Count} types");

        view.BreakingChanges = BuildChangeRows(ChangeClassification.Breaking, typeDiffs);
        view.PotentiallyBreakingChanges = BuildChangeRows(ChangeClassification.PotentiallyBreaking, typeDiffs);
        view.AdditiveChanges = BuildChangeRows(ChangeClassification.Additive, typeDiffs);
        if (inspectionFailures.Count > 0)
        {
            view.Status = new Callout(
                CalloutSeverity.Note,
                "API comparison is incomplete because metadata inspection failed.");
        }

        return view;
    }

    public static string RenderFullMarkdown(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion, MarkoutWriterOptions? options = null)
    {
        var view = BuildFullView(name, typeDiffs, fromVersion, toVersion);
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    public static string RenderFullMarkdown(
        string name,
        IReadOnlyList<TypeDiff> typeDiffs,
        IReadOnlyList<ApiDiffInspectionFailure> inspectionFailures,
        string fromVersion,
        string toVersion,
        MarkoutWriterOptions? options = null)
    {
        var view = BuildFullView(
            name,
            typeDiffs,
            inspectionFailures,
            fromVersion,
            toVersion);
        var writer =
            new MarkoutWriter(
                new MarkdownFormatter(),
                options);
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    static List<DiffInspectionFailureRow>? BuildInspectionFailureRows(
        IReadOnlyList<ApiDiffInspectionFailure> failures) =>
        failures.Count == 0
            ? null
            :
            [
                .. failures.Select(
                    static failure =>
                        new DiffInspectionFailureRow(
                            failure.Side,
                            failure.SubjectAssembly is null
                                ? Path.GetFileName(
                                    failure.SourceAssemblyPath ?? "")
                                : AssemblyIdentityFormatter.Format(
                                    failure.SubjectAssembly),
                            failure.Operation,
                            $"0x{failure.SubjectToken:X8}",
                            failure.Mechanism.ToString(),
                            failure.Kind,
                            CSharpIdentifier.ContainRenderedText(
                                failure.Detail),
                            failure.DependencyAssembly is null
                                ? null
                                : AssemblyIdentityFormatter.Format(
                                    failure.DependencyAssembly))),
            ];

    /// <summary>
    /// Builds the Analysis Diff view with a caller-supplied summary line.
    /// </summary>
    public static AnalysisDiffView BuildAnalysisDiffView(
        string name,
        IReadOnlyList<AnalysisDiffRow> rows,
        string summary,
        string fromVersion,
        string toVersion,
        bool decorateMember = true)
        => new(
            DiffViewText.Field($"Analysis Diff: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"),
            DiffViewText.Field(summary))
        {
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, summary)
                : new Callout(CalloutSeverity.Note, "Analysis signal changes are body-level evidence, not public API compatibility changes."),
            Rows = rows.Count > 0
                ? rows.Select(row => decorateMember
                    ? row with { MemberText = MarkoutInline.CodeText(row.MemberText) }
                    : row).ToList()
                : null
        };

    /// <summary>
    /// Renders an Analysis Diff view to markdown.
    /// </summary>
    public static string RenderAnalysisDiffView(AnalysisDiffView view, MarkoutWriterOptions? options = null)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    public static string RenderAnalysisDiffMarkdown(string name, IReadOnlyList<AnalysisDiffRow> rows, string fromVersion, string toVersion)
    {
        var summary = rows.Count == 0 ? "No analysis signal changes detected." : $"{rows.Count} changed analysis signals";
        return RenderAnalysisDiffView(BuildAnalysisDiffView(name, rows, summary, fromVersion, toVersion));
    }

    public static ImplementationDiffView BuildImplementationDiffView(
        string name,
        ImplementationDiffResult diff,
        string fromVersion,
        string toVersion,
        AssemblyMemberSourcePairResult? selectedSource = null)
    {
        List<ImplementationDiffRow> rows = [];
        foreach (var member in diff.Members)
        {
            foreach (var change in member.Changes)
            {
                AddImplementationChangeRows(rows, member.Subject.Display, change);
            }

            var sourceComparison = member.SourceComparison;
            if (sourceComparison is not null && !member.HasSourceChanges)
            {
                string? sourceState = SourceState(sourceComparison);
                if (sourceState is not null)
                {
                    rows.Add(new ImplementationDiffRow(
                        member.Subject.Display,
                        "PDB Source",
                        "",
                        sourceComparison.IsExact ? "unavailable" : "changed",
                        sourceState));
                }
            }
        }

        if (selectedSource is not null)
            AddSelectedSourceRows(rows, selectedSource);

        var csharpCount = rows.Count(row => row.Mechanism == "C#");
        var ilCount = rows.Count(row => row.Mechanism == "IL");
        var sourceCount = rows.Count(row => row.Mechanism == "PDB Source");
        bool hasSourceLane = selectedSource is not null || diff.Members.Any(member =>
            member.SourceComparison is not null);
        var summary = selectedSource is not null
            ? $"1 selected member; {csharpCount} decompiled C#, {ilCount} IL, and {sourceCount} PDB Source "
              + $"evidence row{(rows.Count == 1 ? "" : "s")}."
            : rows.Count == 0
            ? "No implementation differences detected."
            : !hasSourceLane
                ? $"{diff.Members.Count} changed member{(diff.Members.Count == 1 ? "" : "s")}; "
                  + $"{csharpCount} C# and {ilCount} IL evidence row{(rows.Count == 1 ? "" : "s")}."
                : $"{diff.Members.Count} changed member{(diff.Members.Count == 1 ? "" : "s")}; "
                  + $"{csharpCount} decompiled C#, {ilCount} IL, and {sourceCount} PDB Source "
                  + $"evidence row{(rows.Count == 1 ? "" : "s")}.";

        return new ImplementationDiffView(
            DiffViewText.Field($"Implementation Diff: {name}"),
            DiffViewText.Field($"{fromVersion} -> {toVersion}"),
            DiffViewText.Field(summary))
        {
            Status = rows.Count == 0
                ? new Callout(CalloutSeverity.Note, summary)
                : new Callout(
                    CalloutSeverity.Note,
                    hasSourceLane
                        ? "C# is decompiled evidence; PDB Source is checksum-verified PDB-mapped evidence; IL is shipped body evidence. These peer lanes do not replace one another and are not public API compatibility."
                        : "C# and IL implementation evidence is body-level evidence, not public API compatibility."),
            Rows = rows.Count > 0 ? rows : null
        };
    }

    static void AddImplementationChangeRows(
        List<ImplementationDiffRow> rows,
        string member,
        ResearchChange change)
    {
        string mechanism = change.Mechanism switch
        {
            ResearchChangeMechanism.CSharp => "C#",
            ResearchChangeMechanism.IlBody => "IL",
            ResearchChangeMechanism.Source => "PDB Source",
            _ => change.Mechanism.ToString()
        };
        var evidenceLines = ImplementationDiff.UnifiedLines(change);
        string difference = change.Mechanism == ResearchChangeMechanism.IlBody
            ? (change.IlBodyDiff?.Outcome ?? IlBodyDiffOutcome.Unavailable).ToString()
            : "";
        string changeKind = change.Kind.ToString().ToLowerInvariant();
        if (evidenceLines.IsDefaultOrEmpty)
        {
            rows.Add(new ImplementationDiffRow(
                member, mechanism, difference, changeKind,
                change.Detail ?? change.Descriptor.Title));
            return;
        }
        foreach (string evidence in evidenceLines)
            rows.Add(new ImplementationDiffRow(member, mechanism, difference, changeKind, evidence));
    }

    static void AddSelectedSourceRows(
        List<ImplementationDiffRow> rows,
        AssemblyMemberSourcePairResult pair)
    {
        var anchor = pair.Request.Member;
        var subject = ResearchMemberIdentity.SubjectFromAnchor(
            anchor, $"{anchor.TypeFullName}.{anchor.MemberName}");
        if (pair.Status == AssemblyMemberSourcePairStatus.Compared
            && pair.Comparison is FindingComparison<string>.Complete comparison)
        {
            var changes = ImplementationDiff.ToSourceChanges(pair.Comparison, subject);
            foreach (var change in changes)
                AddImplementationChangeRows(rows, subject.Display, change);
            foreach (var line in comparison.Pairs)
            {
                if (line is PairFinding<string>.Present { Difference: FindingDifferenceKind.Moved } moved)
                {
                    rows.Add(new ImplementationDiffRow(
                        subject.Display, "PDB Source", "Moved", "moved",
                        $"declaration line {moved.Old.Ordinal + 1} -> {moved.New.Ordinal + 1}: {moved.New.Payload}"));
                }
            }
            if (pair.IsExact)
            {
                rows.Add(new ImplementationDiffRow(
                    subject.Display, "PDB Source", "Exact", "unchanged",
                    "old: complete; new: complete; checksum-verified declarations are unchanged."));
            }
            return;
        }

        string? failure = pair.Failure?.Detail
            ?? (pair.Comparison is FindingComparison<string>.Failed failedComparison
                ? failedComparison.Failure
                : null);
        string endpoints =
            $"old: {SourceEndpointState(pair.Before)}; new: {SourceEndpointState(pair.After)}";
        bool failed = pair.Status == AssemblyMemberSourcePairStatus.Failed
            || SourceEndpointFailed(pair.Before)
            || SourceEndpointFailed(pair.After);
        rows.Add(new ImplementationDiffRow(
            subject.Display,
            "PDB Source",
            pair.Status.ToString(),
            failed ? "failed" : "unavailable",
            failure is null ? endpoints : $"{failure}; {endpoints}"));
    }

    static bool SourceEndpointFailed(AssemblyMemberSourcePairEndpoint endpoint)
        => endpoint is AssemblyMemberSourcePairEndpoint.Failed
            or AssemblyMemberSourcePairEndpoint.Rejected
            or AssemblyMemberSourcePairEndpoint.Resolved
            {
                Source: AssemblyMemberPdbSourceAttempt.Unavailable
                {
                    Inspection.Lines.Value: FindingInspection<string>.Failed
                }
            };

    static string SourceEndpointState(AssemblyMemberSourcePairEndpoint endpoint)
        => endpoint switch
        {
            AssemblyMemberSourcePairEndpoint.Resolved
            {
                Source: AssemblyMemberPdbSourceAttempt.Available
            } => "complete",
            AssemblyMemberSourcePairEndpoint.Resolved
            {
                Source: AssemblyMemberPdbSourceAttempt.Unavailable unavailable
            } => SourceInspectionState(unavailable.Inspection),
            AssemblyMemberSourcePairEndpoint.NotFound missing =>
                $"{missing.Failure.Kind}: {missing.Failure.Detail}",
            AssemblyMemberSourcePairEndpoint.Rejected rejected =>
                $"{rejected.Failure.Kind}: {rejected.Failure.Detail}",
            AssemblyMemberSourcePairEndpoint.Failed failed =>
                $"{failed.Failure.Kind}: {failed.Failure.Detail}",
            _ => throw new InvalidOperationException("Unknown selected source endpoint.")
        };

    static string SourceInspectionState(PdbMemberSourceInspection inspection)
        => $"{inspection.Outcome}: " + (inspection.Lines.Value switch
        {
            FindingInspection<string>.Failed failed => failed.Error.Reason,
            FindingInspection<string>.Absent absent => absent.Detail ?? "unavailable",
            _ => throw new InvalidOperationException("Unavailable source carried complete evidence.")
        });

    static string? SourceState(ILInspector.Findings.FindingComparison<string> comparison)
    {
        if (comparison is ILInspector.Findings.FindingComparison<string>.Failed failed)
            return failed.Failure;

        bool oldAbsent = comparison.OldInspection.Value
            is ILInspector.Findings.FindingInspection<string>.Absent;
        bool newAbsent = comparison.NewInspection.Value
            is ILInspector.Findings.FindingInspection<string>.Absent;
        if (!oldAbsent && !newAbsent)
            return null;

        string oldState = oldAbsent
            ? ((ILInspector.Findings.FindingInspection<string>.Absent)
                comparison.OldInspection.Value).Detail ?? "unavailable"
            : "complete";
        string newState = newAbsent
            ? ((ILInspector.Findings.FindingInspection<string>.Absent)
                comparison.NewInspection.Value).Detail ?? "unavailable"
            : "complete";
        return $"old: {oldState}; new: {newState}";
    }

    public static string RenderImplementationDiffView(ImplementationDiffView view, MarkoutWriterOptions? options = null)
    {
        var writer = new MarkoutWriter(new MarkdownFormatter(), options);
        DiffViewContext.Default.Serialize(view, writer);
        return writer.ToString().TrimEnd();
    }

    internal static string FormatSummaryCounts(int breaking, int additive, int potentiallyBreaking)
    {
        var parts = new List<string>(3);
        if (breaking > 0) parts.Add($"{breaking} breaking");
        if (additive > 0) parts.Add($"{additive} additive");
        if (potentiallyBreaking > 0) parts.Add($"{potentiallyBreaking} potentially breaking");
        return parts.Count > 0 ? string.Join(", ", parts) : "no changes";
    }

    private static List<DiffChangeRow>? BuildChangeRows(ChangeClassification classification, IReadOnlyList<TypeDiff> typeDiffs)
    {
        var rows = new List<DiffChangeRow>();

        foreach (var td in typeDiffs.OrderBy(td => td.TypeFullName))
        {
            InertString typeName = FormatTypeDisplayName(td.TypeFullName);
            foreach (var change in td.Changes.Where(c => c.Classification == classification))
            {
                InertString message = change.GetMessageText();
                if (change.Kind == ChangeKind.MemberSignatureChanged
                    && change.GetOldValueText() is { } oldText
                    && change.GetNewValueText() is { } newText)
                {
                    InertString oldValue = MarkoutInline.CodeText(
                        oldText);
                    InertString newValue = MarkoutInline.CodeText(
                        newText);
                    message = InertString.Format(
                        TextPolicy.Field,
                        $"{message}: {oldValue} -> {newValue}");
                }
                rows.Add(new DiffChangeRow(typeName, message));
            }
        }

        return rows.Count > 0 ? rows : null;
    }

    private static DiffDetailedChangeRow BuildDetailedRow(string typeFullName, ApiChange change)
        => new(
            DiffViewText.Field(ChangeSymbol(change.Classification, change.Kind)),
            DiffViewText.Field(ClassificationText(change.Classification)),
            DiffViewText.Field(TypeMatcher.GetSimpleName(typeFullName)),
            DiffViewText.Field(
                change.Subject?.OldMember?.StableSelector
                    ?? change.Subject?.NewMember?.StableSelector
                    ?? ""),
            DiffViewText.Field(ChangeKindText(change.Kind)),
            change.GetMessageText(),
            change.GetOldValueText() ?? InertString.Empty,
            change.GetNewValueText() ?? InertString.Empty);

    private static string ChangeSymbol(ChangeClassification classification, ChangeKind kind)
        => kind switch
        {
            ChangeKind.TypeAdded or ChangeKind.MemberAdded => "+",
            ChangeKind.TypeRemoved or ChangeKind.MemberRemoved => "-",
            _ when classification == ChangeClassification.Breaking => "x",
            _ => "~"
        };

    private static string ClassificationText(ChangeClassification classification)
        => classification switch
        {
            ChangeClassification.Breaking => "breaking",
            ChangeClassification.Additive => "additive",
            ChangeClassification.PotentiallyBreaking => "potentially-breaking",
            _ => classification.ToString()
        };

    private static string ChangeKindText(ChangeKind kind)
        => kind.ToString();
}
