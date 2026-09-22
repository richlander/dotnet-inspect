using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Findings;
using Markout;

namespace DotnetInspect.Cli.Commands;

public partial class ApiCommand
{
    internal static void WriteSourceInspectionDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string message = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(diagnostics),
                        diagnostic.Severity,
                        "Unknown inspection diagnostic severity.");
            }
        }
    }

    private static bool TryPopulateSource(
        TypeView view,
        ApiOptions options)
    {
        if (!TryCreateSourceDocument(
                options,
                out CliSourceDocument? source,
                out string failure))
        {
            CommandError.Write(failure);
            return false;
        }

        WriteSourceNotes(source);
        view.MemberCode ??= new MemberCodeView();
        view.MemberCode.SourceCode =
            new CodeSection("csharp", source.Content);
        return true;
    }

    private static int WriteSourceJson(ApiOptions options)
    {
        if (IsColumnProjectionRequested(options))
            return RejectColumnProjectionUnderJson(
                suggestPayloadProjection: true);

        if (!TryCreateSourceDocument(
                options,
                out CliSourceDocument? source,
                out string failure))
        {
            CommandError.Write(failure);
            return 1;
        }

        WriteSourceNotes(source);
        JsonOutputHelper.Write(
            source,
            CliSourceDocumentJsonContext.Default.CliSourceDocument,
            CliSourceDocumentCompactJsonContext.Default.CliSourceDocument,
            options.CompactJson);
        return 0;
    }

    private static bool TryCreateSourceDocument(
        ApiOptions options,
        [NotNullWhen(true)] out CliSourceDocument? source,
        out string failure)
    {
        switch (options)
        {
            case TypeOptions
            {
                TypeSourceInspection.Content:
                    AssemblyTypeSourceEntry.Available available,
            }:
                source = CreateSourceDocument(available);
                failure = "";
                return true;
            case TypeOptions
            {
                TypeSourceInspection.Content:
                    AssemblyTypeSourceEntry.Unavailable unavailable,
            }:
                source = null;
                failure = TypeSourceUnavailable(unavailable);
                return false;
            case TypeOptions
            {
                TypeSourceInspection.Content:
                    AssemblyTypeSourceEntry.Rejected rejected,
            }:
                source = null;
                failure =
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}";
                return false;
            case TypeOptions:
                source = null;
                failure =
                    "The completed type Source inspection is unavailable.";
                return false;
            case MemberOptions
            {
                MemberSourceInspection.Content:
                    AssemblyMemberSourceEntry.Available available,
            }:
                source = CreateSourceDocument(available);
                failure = "";
                return true;
            case MemberOptions
            {
                MemberSourceInspection.Content:
                    AssemblyMemberSourceEntry.Unavailable unavailable,
            }:
                source = null;
                failure = MemberSourceUnavailable(unavailable);
                return false;
            case MemberOptions
            {
                MemberSourceInspection.Content:
                    AssemblyMemberSourceEntry.Rejected rejected,
            }:
                source = null;
                failure =
                    $"{rejected.Failure.Kind}: {rejected.Failure.Detail}";
                return false;
            case MemberOptions:
                source = null;
                failure =
                    "Source requires an exact method or property/event accessor "
                    + "with a physical MethodDef token.";
                return false;
            default:
                source = null;
                failure =
                    "The completed Source inspection is unavailable.";
                return false;
        }
    }

    private static CliSourceDocument CreateSourceDocument(
        AssemblyTypeSourceEntry.Available available) =>
        available.Source switch
        {
            AssemblyTypeSource.Pdb pdb =>
                new CliSourceDocument(
                    "pdb",
                    PdbSourceProvenance(pdb.Provenance),
                    PdbSourceLocation(pdb.Inspection.Document),
                    FallbackReason: null,
                    pdb.Text,
                    TypeEvidence(pdb.Inspection)),
            AssemblyTypeSource.Decompiled decompiled =>
                new CliSourceDocument(
                    "decompiled",
                    DecompiledSourceProvenance(available.Subject),
                    Location: null,
                    PdbSourceAttemptReason(decompiled.PdbAttempt),
                    decompiled.Text,
                    TypeEvidence(decompiled.PdbAttempt)),
            _ => throw new InvalidOperationException(
                "Unknown available type Source result."),
        };

    private static CliSourceDocument CreateSourceDocument(
        AssemblyMemberSourceEntry.Available available) =>
        available.Source switch
        {
            AssemblyMemberSource.Pdb pdb =>
                new CliSourceDocument(
                    "pdb",
                    PdbSourceProvenance(pdb.Provenance),
                    PdbSourceLocation(pdb.Inspection.Document),
                    FallbackReason: null,
                    pdb.Text,
                    TypeEvidence: null),
            AssemblyMemberSource.Decompiled decompiled =>
                new CliSourceDocument(
                    "decompiled",
                    DecompiledSourceProvenance(available.Subject),
                    Location: null,
                    PdbSourceAttemptReason(decompiled.PdbAttempt),
                    decompiled.Text,
                    TypeEvidence: null),
            _ => throw new InvalidOperationException(
                "Unknown available member Source result."),
        };

    private static string TypeSourceUnavailable(
        AssemblyTypeSourceEntry.Unavailable unavailable)
    {
        string message =
            $"{unavailable.Failure.Kind}: {unavailable.Failure.Detail}";
        if (unavailable.PdbAttempt is { } pdb)
            message += $" PDB source unavailable: {PdbSourceAttemptReason(pdb)}";
        if (unavailable.DecompiledAttempt is { } decompiled)
            message +=
                $" Decompiled source unavailable: "
                + $"{DecompilerAttemptReason(decompiled)}";
        return message;
    }

    private static string MemberSourceUnavailable(
        AssemblyMemberSourceEntry.Unavailable unavailable)
    {
        string message =
            $"{unavailable.Failure.Kind}: {unavailable.Failure.Detail}";
        if (unavailable.PdbAttempt is { } pdb)
            message += $" PDB source unavailable: {PdbSourceAttemptReason(pdb)}";
        if (unavailable.DecompiledAttempt is { } decompiled)
            message +=
                $" Decompiled source unavailable: "
                + $"{DecompilerAttemptReason(decompiled)}";
        return message;
    }

    private static string PdbSourceAttemptReason(
        PdbTypeSourceInspection inspection)
    {
        string? integrityFailure = inspection.Outcome switch
        {
            PdbTypeSourceOutcome.ChecksumMismatch =>
                "PDB source checksum did not match the portable PDB.",
            PdbTypeSourceOutcome.ChecksumUnavailable =>
                "The portable PDB does not provide a usable source checksum.",
            PdbTypeSourceOutcome.ChecksumUnsupported =>
                "The portable PDB uses an unsupported source checksum.",
            _ => null,
        };
        return integrityFailure
        ?? FindingReason(inspection.Lines)
        ?? inspection.Outcome switch
        {
            PdbTypeSourceOutcome.PortablePdbUnavailable =>
                "No portable PDB is available for the selected type.",
            PdbTypeSourceOutcome.PortablePdbAcquisitionFailed =>
                "Portable PDB acquisition failed.",
            PdbTypeSourceOutcome.SourceMappingUnavailable =>
                "The selected type has no portable-PDB source mapping.",
            PdbTypeSourceOutcome.SourceDocumentUnavailable
                or PdbTypeSourceOutcome.SourceAcquisitionUnavailable =>
                "No checksum-matching PDB source could be acquired.",
            PdbTypeSourceOutcome.SourceAcquisitionFailed
                or PdbTypeSourceOutcome.SourceExtractionFailed
                or PdbTypeSourceOutcome.InspectionFailed =>
                "PDB source inspection failed.",
            PdbTypeSourceOutcome.SourceDeadlineExceeded =>
                "Authored source acquisition exceeded its deadline.",
            PdbTypeSourceOutcome.SourceLimitExceeded =>
                "Authored source acquisition exceeded a configured limit.",
            _ => "No checksum-matching PDB source could be acquired.",
        };
    }

    private static string PdbSourceAttemptReason(
        PdbMemberSourceInspection inspection)
    {
        string? integrityFailure = inspection.Outcome switch
        {
            PdbMemberSourceOutcome.ChecksumMismatch =>
                "PDB source checksum did not match the portable PDB.",
            PdbMemberSourceOutcome.ChecksumUnavailable =>
                "The portable PDB does not provide a usable source checksum.",
            PdbMemberSourceOutcome.ChecksumUnsupported =>
                "The portable PDB uses an unsupported source checksum.",
            _ => null,
        };
        return integrityFailure
        ?? FindingReason(inspection.Lines)
        ?? inspection.Outcome switch
        {
            PdbMemberSourceOutcome.PortablePdbUnavailable =>
                NoPortablePdbReason,
            PdbMemberSourceOutcome.PortablePdbAcquisitionFailed =>
                "Portable PDB acquisition failed.",
            PdbMemberSourceOutcome.SourceMappingUnavailable =>
                NoPdbSourceMappingReason,
            PdbMemberSourceOutcome.SourceDocumentUnavailable
                or PdbMemberSourceOutcome.SourceAcquisitionUnavailable =>
                NoMatchingPdbSourceReason,
            PdbMemberSourceOutcome.SourceAcquisitionFailed
                or PdbMemberSourceOutcome.SourceExtractionFailed
                or PdbMemberSourceOutcome.InspectionFailed =>
                PdbSourceInspectionFailedReason,
            PdbMemberSourceOutcome.NoVouchedDeclaration =>
                NoPdbDeclarationReason + " " + NoPdbDeclarationDetail,
            PdbMemberSourceOutcome.SourceTooComplex =>
                SourceTooComplexReason,
            PdbMemberSourceOutcome.InvalidSequencePointCoordinates =>
                SourceCoordinatesInvalidReason,
            PdbMemberSourceOutcome.SourceDeadlineExceeded =>
                "Authored source acquisition exceeded its deadline.",
            PdbMemberSourceOutcome.SourceLimitExceeded =>
                "Authored source acquisition exceeded a configured limit.",
            _ => NoMatchingPdbSourceReason,
        };
    }

    private static string? FindingReason(
        FindingInspection<string> inspection)
    {
        string? detail = inspection.Value switch
        {
            FindingInspection<string>.Absent absent =>
                absent.Detail,
            FindingInspection<string>.Failed failed =>
                failed.Error.Reason,
            _ => null,
        };
        detail = detail?.Trim();
        return detail is null
            || detail.Length == 0
            || detail.Equals("Unavailable:", StringComparison.OrdinalIgnoreCase)
            || detail.Equals("Absent:", StringComparison.OrdinalIgnoreCase)
                ? null
                : detail;
    }

    private static string DecompilerAttemptReason(
        ILInspector.Decompiler.CSharpDecompilationAttempt attempt) =>
        attempt.DiagnosticSummary is { Length: > 0 } detail
            ? detail
            : $"decompilation ended with status {attempt.Status}";

    private static string PdbSourceProvenance(
        AssemblyPdbSourceProvenance provenance)
    {
        if (provenance.RepositoryUrl is { Length: > 0 } repository
            && provenance.Revision is { Length: > 0 } revision)
        {
            return "PDB-checksum-verified authored source "
                + $"associated with {repository} at {revision}";
        }
        if (provenance.RepositoryUrl is { Length: > 0 } repositoryOnly)
        {
            return "PDB-checksum-verified authored source "
                + $"associated with {repositoryOnly}";
        }
        if (provenance.Revision is { Length: > 0 } revisionOnly)
        {
            return "PDB-checksum-verified authored source "
                + $"at {revisionOnly}";
        }
        return "PDB-checksum-verified authored source";
    }

    private static string DecompiledSourceProvenance(
        AssemblyContextSubject subject) =>
        subject.Provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                $"dotnet-inspect decompilation of {package.PackageId} "
                + $"{package.PackageVersion}"
                + (package.AssetPath is { Length: > 0 } asset
                    ? $" {asset}"
                    : ""),
            AssemblyResolutionProvenance.PlatformAsset platform =>
                $"dotnet-inspect decompilation of {subject.Identity.Name} "
                + $"from {platform.Framework}"
                + (platform.FrameworkVersion is { Length: > 0 } version
                    ? $" {version}"
                    : ""),
            AssemblyResolutionProvenance.ProjectAsset project =>
                $"dotnet-inspect decompilation of {subject.Identity.Name} "
                + $"from project {project.Project}",
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                $"dotnet-inspect decompilation of embedded "
                + $"{embedded.DeclaredName}",
            _ =>
                $"dotnet-inspect decompilation of {subject.Identity.Name}",
        };

    private static string? PdbSourceLocation(
        ILInspector.SourceLink.SourceDocumentObservation? document) =>
        document?.ResolvedUrl
        ?? document?.OriginalPath;

    private static CliTypeSourceEvidence? TypeEvidence(
        PdbTypeSourceInspection inspection)
    {
        if (inspection.Scope is null
            && inspection.Strength is null
            && !inspection.IsPartial
            && inspection.AdditionalDocuments.Count == 0)
        {
            return null;
        }

        return new CliTypeSourceEvidence(
            inspection.Scope switch
            {
                PdbTypeSourceUnitScope.PrimaryTypeDocument =>
                    "primary_type_document",
                PdbTypeSourceUnitScope.AdditionalTypeDocument =>
                    "additional_type_document",
                _ => null,
            },
            inspection.Strength switch
            {
                PdbTypeSourceMappingStrength.CorrelatedTypeDocument =>
                    "correlated_type_document",
                PdbTypeSourceMappingStrength.InferredTypeDocument =>
                    "inferred_type_document",
                _ => null,
            },
            inspection.IsPartial,
            [
                .. inspection.AdditionalDocuments.Select(
                    document => new CliSourceAdditionalDocument(
                        document.OriginalPath,
                        document.ResolvedUrl)),
            ]);
    }

    private static void WriteSourceNotes(CliSourceDocument source)
    {
        CommandError.WriteNote(
            $"Source provider: {source.Provider}.");
        CommandError.WriteNote(
            $"Source provenance: {source.Provenance}.");
        if (source.Location is { Length: > 0 } location)
            CommandError.WriteNote($"Source location: {location}.");
        if (source.FallbackReason is { Length: > 0 } fallback)
        {
            CommandError.WriteNote(
                $"Authored source unavailable; using decompiled fallback: "
                + $"{fallback}");
        }
        if (source.TypeEvidence is { } typeEvidence)
        {
            string scope =
                typeEvidence.Scope ?? "unspecified_scope";
            string strength =
                typeEvidence.MappingStrength ?? "unspecified_mapping";
            CommandError.WriteNote(
                $"Type source evidence: {scope}, {strength}, "
                + $"partial={typeEvidence.IsPartial.ToString().ToLowerInvariant()}, "
                + $"additional_documents={typeEvidence.AdditionalDocuments.Length}.");
        }
    }
}

internal sealed record CliSourceDocument(
    string Provider,
    string Provenance,
    string? Location,
    string? FallbackReason,
    string Content,
    CliTypeSourceEvidence? TypeEvidence);

internal sealed record CliTypeSourceEvidence(
    string? Scope,
    string? MappingStrength,
    bool IsPartial,
    CliSourceAdditionalDocument[] AdditionalDocuments);

internal sealed record CliSourceAdditionalDocument(
    string OriginalPath,
    string? ResolvedUrl);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CliSourceDocument))]
internal partial class CliSourceDocumentJsonContext :
    JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliSourceDocument))]
internal partial class CliSourceDocumentCompactJsonContext :
    JsonSerializerContext;
