using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Output;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

/// <summary>Inspects type relationships or explicit asset dependency roots.</summary>
public static partial class DependsCommand
{
    internal const int UncertifiedScanExitCode = 3;

    private static void WriteRejectionWarnings(
        IReadOnlyList<TypeDependencyScanDiagnostic> diagnostics)
    {
        foreach (TypeDependencyScanDiagnostic diagnostic in diagnostics)
        {
            string mechanism = diagnostic.Failure.Kind switch
            {
                CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                    "unsupported metadata format (Windows Metadata)",
                CandidateOpenFailureKind.ResourceBudget =>
                    $"resource budget ({diagnostic.Failure.Detail})",
                CandidateOpenFailureKind.Unreadable =>
                    $"unreadable image ({diagnostic.Failure.Detail})",
                _ when diagnostic.Failure.MetadataRootReason is { } reason =>
                    $"malformed metadata root ({reason})",
                _ => "invalid image",
            };
            CommandError.WriteWarning(
                $"Excluded '{CSharpIdentifier.ContainRenderedText(diagnostic.Subject)}' from the dependency scan: {mechanism}.",
                "Results may be incomplete.");
        }
    }
}
