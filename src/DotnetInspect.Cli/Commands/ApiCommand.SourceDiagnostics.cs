using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspect.Cli.CommandLine;
using CSharpText.MemberSlicing;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Markout;
using Markout.Formatting;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// </summary>
public partial class ApiCommand
{

    /// <summary>
    /// Stands in for PDB Source when the selected member carries no IL body. A C# comment
    /// so it reads naturally inside the section's <c>csharp</c> fence, mirroring how
    /// <see cref="SourceTextDiffRenderer"/> reports an unavailable diff input (issue #3299).
    /// </summary>
    internal const string BodylessMemberNote =
        "// This member has no IL body, so it has no PDB source to show.";

    internal const string NoPdbDeclarationReason =
        "This member's PDB source range does not identify one declaration that can be shown.";

    internal const string NoPdbDeclarationDetail =
        "Generated members and ambiguous or structurally unknown source ranges can have this shape.";

    /// <summary>
    /// Stands in for PDB Source when the selected member has an IL body but its source range
    /// does not identify one declaration that can be shown. Generated members may map to
    /// a type header or initializer, and structurally unknown ranges are deliberately not guessed;
    /// saying so beats rendering unrelated or truncated source (issue #3299's principle, applied
    /// to a second cause).
    /// </summary>
    internal const string NoPdbDeclarationNote =
        "// " + NoPdbDeclarationReason + "\n"
        + "// " + NoPdbDeclarationDetail;

    internal const string SourceTooComplexReason =
        "PDB source extraction stopped because the source exceeds the lexical complexity limit.";

    internal const string SourceTooComplexNote =
        "// " + SourceTooComplexReason;

    internal const string SourceCoordinatesInvalidReason =
        "PDB source extraction stopped because the portable-PDB sequence-point coordinates "
        + "cannot address the verified source.";

    internal const string SourceCoordinatesInvalidNote =
        "// " + SourceCoordinatesInvalidReason;

    internal const string NoPortablePdbReason =
        "No portable PDB is available for the selected member.";

    internal const string NoPdbSourceMappingReason =
        "The selected member has no portable-PDB source mapping.";

    internal const string NoMatchingPdbSourceReason =
        "No checksum-matching PDB source could be acquired locally or through SourceLink.";

    internal const string PdbSourceInspectionFailedReason =
        "PDB source inspection failed.";

    internal static string? PdbSourceUnavailableNote(MemberOptions options) =>
        options.MemberHasNoBody
            ? BodylessMemberNote
            : options.MemberSourceTooComplex
                ? SourceTooComplexNote
                : options.MemberSourceCoordinatesInvalid
                    ? SourceCoordinatesInvalidNote
                    : options.MemberHasNoPdbDeclaration
                        ? NoPdbDeclarationNote
                        : options.PdbSourceUnavailableReason is { Length: > 0 } reason
                            ? $"// {reason}"
                            : null;

    private static void PopulatePdbSource(
        TypeView view,
        MemberOptions options)
    {
        if (PdbAttempt(options.MemberSourceComparison)
            is AssemblyMemberPdbSourceAttempt.Available available)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection(
                    "csharp",
                    available.Inspection.Text!);
            return;
        }

        string? note = options.MemberHasNoBody
            ? BodylessMemberNote
            : options.MemberSourceComparison is { } comparison
                ? $"// {PdbSourceUnavailableReason(comparison)}"
            : options.MethodSource is { } resolvedSource
                ? null
                : PdbSourceUnavailableNote(options);
        if (options.MethodSource is { } source
            && options.MemberSourceComparison is null)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection("csharp", source.SourceCode);
        }
        else if (note is not null)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection("csharp", note);
            view.MemberCode.PdbSourceUnavailable = true;
        }
    }

    private static void PopulateSourceDiff(
        TypeView view,
        IReadOnlySet<string> requestedSections,
        bool sourceTooComplex,
        bool sourceCoordinatesInvalid,
        AssemblyMemberSourceComparisonEntry? comparison,
        MemberSourceDiffPresentationResult? presentationResult,
        bool detailed)
    {
        if (!requestedSections.Contains(SectionNames.SourceDiff))
            return;

        view.MemberCode ??= new MemberCodeView();
        if (sourceTooComplex)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "PDB Source unavailable because PDB source extraction exceeded "
                + "the lexical complexity limit.");
            return;
        }
        if (sourceCoordinatesInvalid)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "PDB Source unavailable because portable-PDB sequence-point coordinates "
                + "cannot address the verified source.");
            return;
        }

        if (comparison is null)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "Member source comparison was not available.");
            return;
        }

        MemberSourceDiffPresentationResult result =
            presentationResult
            ?? MemberSourceDiffPresentationAdapter.Create(comparison);
        SourceDiffOutput diff = result switch
        {
            MemberSourceDiffPresentationResult.Available available =>
                SourceTextDiffRenderer.CreateOutput(
                    available.Presentation,
                    detailed),
            MemberSourceDiffPresentationResult.Failed failed =>
                new SourceDiffOutput(
                    $"Source diff projection failed: {failed.Failure.Detail}"),
            MemberSourceDiffPresentationResult.Unavailable unavailable =>
                new SourceDiffOutput(
                    SourceDiffUnavailableReason(unavailable.Comparison)),
            _ => throw new InvalidOperationException(
                "Unknown member source diff presentation result."),
        };

        if (PdbAttempt(comparison)
                is AssemblyMemberPdbSourceAttempt.Available pdb
            && pdb.Inspection.Document is { } document
            && document.ChecksumAlgorithm is { Length: > 0 } checksumAlgorithm
            && document.Checksum is { Length: > 0 } checksum
            && pdb.Inspection.ChecksumVerification is
                SourceChecksumVerification.Exact
                    or SourceChecksumVerification.LineEndingNormalized)
        {
            string location = CSharpText.CSharpIdentifier.ContainRenderedText(
                document.ResolvedUrl ?? document.OriginalPath);
            string algorithm = CSharpText.CSharpIdentifier.ContainRenderedText(
                checksumAlgorithm);
            string containedChecksum =
                CSharpText.CSharpIdentifier.ContainRenderedText(checksum);
            string integrity = pdb.Inspection.ChecksumVerification switch
            {
                SourceChecksumVerification.Exact =>
                    $"PDB source document bytes match portable-PDB {algorithm} checksum {containedChecksum}.",
                SourceChecksumVerification.LineEndingNormalized =>
                    $"PDB source document matches portable-PDB {algorithm} checksum {containedChecksum} "
                    + "after CR/LF normalization.",
                _ => throw new InvalidOperationException("Checksum evidence requires a successful verification."),
            };
            diff = diff.WithMetadata(
                new Markout.MarkoutField("PDB source", location),
                new Markout.MarkoutField("Integrity", integrity));
        }

        view.MemberCode.SourceDiffCode = diff;
    }

    private static AssemblyMemberPdbSourceAttempt? PdbAttempt(
        AssemblyMemberSourceComparisonEntry? comparison)
        => comparison switch
        {
            AssemblyMemberSourceComparisonEntry.Available available =>
                available.Pdb,
            AssemblyMemberSourceComparisonEntry.Unavailable unavailable =>
                unavailable.Pdb,
            _ => null,
        };

    internal static string SourceDiffUnavailableReason(
        AssemblyMemberSourceComparisonEntry comparison)
        => comparison switch
        {
            AssemblyMemberSourceComparisonEntry.Available available =>
                available.Pdb
                    is AssemblyMemberPdbSourceAttempt.Unavailable
                    ? $"Source diff unavailable: PDB comparison unavailable: "
                        + $"{StatusReason(PdbAttemptReason(available.Pdb))}."
                    : $"Source diff unavailable: Decompiled comparison unavailable: "
                        + $"{StatusReason(DecompilerAttemptReason(available.Decompiled))}.",
            AssemblyMemberSourceComparisonEntry.Unavailable unavailable =>
                $"Source diff unavailable: PDB comparison unavailable: "
                + $"{StatusReason(PdbAttemptReason(unavailable.Pdb))}; "
                + "Decompiled comparison unavailable: "
                + $"{StatusReason(DecompilerAttemptReason(unavailable.Decompiled))}.",
            AssemblyMemberSourceComparisonEntry.NotFound notFound =>
                $"Source diff unavailable: {notFound.Failure.Detail}",
            AssemblyMemberSourceComparisonEntry.Failed failed =>
                $"Source diff unavailable: {failed.Failure.Detail}",
            AssemblyMemberSourceComparisonEntry.Rejected =>
                "Source diff unavailable because the selected assembly image was rejected.",
            _ => throw new InvalidOperationException(
                "Unknown member source comparison result."),
        };

    private static string? ExactSourceFailure(
        MemberOptions options)
    {
        if (options.ExactIncludeSections?
                .Contains(SectionNames.SourceDiff) == true)
        {
            return options.MemberSourceDiffPresentation switch
            {
                MemberSourceDiffPresentationResult.Available => null,
                MemberSourceDiffPresentationResult.Failed failed =>
                    $"Source diff projection failed: {failed.Failure.Detail}",
                MemberSourceDiffPresentationResult.Unavailable unavailable =>
                    SourceDiffUnavailableReason(unavailable.Comparison),
                null => options.PdbSourceUnavailableReason,
                _ => throw new InvalidOperationException(
                    "Unknown member source diff presentation result."),
            };
        }

        return options.ExactIncludeSections?
                .Contains(SectionNames.PdbSource) == true
            ? options.PdbSourceUnavailableReason
            : null;
    }

    private static string StatusReason(string reason)
    {
        string[] lines = reason
            .ReplaceLineEndings("\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        return string.Join(
                " ",
                lines.Select(line => line.TrimStart('/', ' ')))
            .TrimEnd('.');
    }

    internal static string PdbSourceUnavailableReason(
        AssemblyMemberSourceComparisonEntry comparison)
        => PdbAttempt(comparison) is { } attempt
            ? PdbAttemptReason(attempt)
            : comparison switch
            {
                AssemblyMemberSourceComparisonEntry.NotFound notFound =>
                    notFound.Failure.Detail,
                AssemblyMemberSourceComparisonEntry.Failed failed =>
                    failed.Failure.Detail,
                AssemblyMemberSourceComparisonEntry.Rejected =>
                    "The selected assembly image was rejected.",
                _ => "PDB source is unavailable.",
            };

    private static string? MemberSourceUrl(MemberOptions? options)
        => PdbAttempt(options?.MemberSourceComparison) switch
        {
            AssemblyMemberPdbSourceAttempt.Available
            {
                Inspection.Document: { } document
            } => document.ResolvedUrl ?? document.OriginalPath,
            _ => options?.MethodSource?.SourceUrl,
        };

    private static string PdbAttemptReason(
        AssemblyMemberPdbSourceAttempt attempt)
        => attempt switch
        {
            AssemblyMemberPdbSourceAttempt.Available =>
                "PDB comparison is available",
            AssemblyMemberPdbSourceAttempt.Unavailable unavailable =>
                unavailable.Inspection.Outcome switch
                {
                    PdbMemberSourceOutcome.PortablePdbUnavailable =>
                        NoPortablePdbReason,
                    PdbMemberSourceOutcome.PortablePdbAcquisitionFailed =>
                        "Portable PDB acquisition failed.",
                    PdbMemberSourceOutcome.SourceMappingUnavailable =>
                        NoPdbSourceMappingReason,
                    PdbMemberSourceOutcome.NoVouchedDeclaration =>
                        NoPdbDeclarationReason + " "
                            + NoPdbDeclarationDetail,
                    PdbMemberSourceOutcome.SourceTooComplex =>
                        SourceTooComplexReason,
                    PdbMemberSourceOutcome.InvalidSequencePointCoordinates =>
                        SourceCoordinatesInvalidReason,
                    PdbMemberSourceOutcome.SourceExtractionFailed
                        or PdbMemberSourceOutcome.InspectionFailed =>
                        PdbSourceInspectionFailedReason,
                    _ => NoMatchingPdbSourceReason,
                },
            _ => throw new InvalidOperationException(
                "Unknown PDB source attempt."),
        };

    private static string DecompilerAttemptReason(
        AssemblyMemberDecompiledSourceAttempt attempt)
        => attempt switch
        {
            AssemblyMemberDecompiledSourceAttempt.Available =>
                "available",
            AssemblyMemberDecompiledSourceAttempt.Unavailable unavailable =>
                unavailable.Status switch
                {
                    Decompiler.CSharpDecompilationStatus.Absent =>
                        "the member has no renderable body",
                    Decompiler.CSharpDecompilationStatus.Incomplete =>
                        $"decompilation incomplete: {unavailable.FailureDetail}",
                    _ => $"decompilation failed: {unavailable.FailureDetail}",
                },
            _ => throw new InvalidOperationException(
                "Unknown decompiled source attempt."),
        };

}
