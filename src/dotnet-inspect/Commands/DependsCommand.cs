using ILInspector.CSharp;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Walks dependency graphs upward: type hierarchies, library references, or package dependencies.
/// </summary>
public class DependsCommand
{
    /// <summary>
    /// Returned when the target type was not found. The caller can fall back to library mode.
    /// </summary>
    internal const int TypeNotFoundExitCode = 2;

    /// <summary>
    /// Returned when the scan produced usable output but at least one candidate
    /// assembly was excluded, so completeness is unknown. The result is not a
    /// certified success and must not be consumed as one.
    /// </summary>
    internal const int UncertifiedScanExitCode = 3;

    /// <summary>
    /// The outcome of a type dependency scan. <see cref="ExitCode"/> reports
    /// what the scan found, so the caller can still fall back to library mode
    /// or diagnose an absence. <see cref="Uncertified"/> reports separately
    /// that a candidate was excluded. The two must stay separate: folding
    /// uncertainty into the exit code hides the outcome the caller dispatches
    /// on, which silently withholds an answer the caller would otherwise emit.
    /// </summary>
    internal readonly record struct TypeDependsOutcome(
        int ExitCode,
        bool Uncertified);

    internal static async Task<TypeDependsOutcome> ExecuteTypeDependsAsync(
        DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var result = await DependencyGraphService.BuildTypeDependencyTreeAsync(
                context.HttpClient, options, logger);

            // A rejected participant scopes to itself and leaves the rest of
            // the scan intact, but the resulting graph is uncertified: it may
            // omit edges the rejected assembly would have contributed. Name
            // each rejection before any result or absence claim, so neither a
            // partial graph nor a "not found" is reported as certified.
            WriteRejectionWarnings(result.Rejections);
            bool uncertified = result.Rejections.Count > 0;

            if (!result.Found)
            {
                // Report the absence as an absence so the caller can still fall
                // back or diagnose it, and carry the uncertainty alongside.
                return new TypeDependsOutcome(TypeNotFoundExitCode, uncertified);
            }

            if (result.Relationships.Count == 0)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return Certified(0, uncertified);
                }

                CommandError.WriteLine(
                    $"Type '{ContainLabel(result.MatchedType ?? options.TargetType)}' has no type dependencies beyond System.Object.");
                return Certified(0, uncertified);
            }

            DependencyGraphOutputAdapter.Write(
                DependencyGraphProjection.FromType(result),
                options);

            return Certified(0, uncertified);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return new TypeDependsOutcome(1, false);
        }
    }

    public static async Task<int> ExecuteLibraryDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            var libraryName = options.LibraryName!;
            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                context.HttpClient, libraryName, options.SourceOptions, logger);
            if (result is LibraryDependencyGraphResult.Error error)
            {
                CommandError.Write($"{error.Message}");
                if (error.HintInput != null)
                    NamespacePrefixHints.WriteIfLikelyNamespacePrefix(error.HintInput);
                return 1;
            }
            if (result is LibraryDependencyGraphResult.Empty empty)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return 0;
                }

                CommandError.WriteLine($"No assembly references found in '{empty.AssemblyName}'.");
                return 0;
            }

            var graph = (LibraryDependencyGraphResult.Graph)result;
            DependencyGraphOutputAdapter.Write(
                DependencyGraphProjection.FromLibrary(graph),
                options);
            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    public static async Task<int> ExecutePackageDependsAsync(DependsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            var packageRef = options.PackageName!;
            var result = await DependencyGraphService.BuildPackageDependencyTreeAsync(
                context.HttpClient, packageRef, options.Tfm, options.SourceOptions, logger);
            if (result is PackageDependencyGraphResult.Error error)
            {
                // Detail lists the TFMs a package actually offers, which comes
                // straight out of its .nuspec. It used to go out as its own
                // unindented line, so a hostile targetFramework attribute
                // containing a line separator forged a diagnostic under it.
                CommandError.Write(error.Message, error.Detail is null ? [] : [error.Detail]);
                return 1;
            }
            if (result is PackageDependencyGraphResult.Empty empty)
            {
                if (options.Count)
                {
                    WriteCount(0);
                    return 0;
                }

                CommandError.WriteLine(empty.Message);
                return 0;
            }

            var graph = (PackageDependencyGraphResult.Graph)result;
            DependencyGraphOutputAdapter.Write(
                DependencyGraphProjection.FromPackage(graph),
                options);
            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    /// <summary>
    /// A tree label is written straight into the terminal beside a box-drawing
    /// gutter, so an ESC or a bidi override in a metadata name rewrites the
    /// shape of the tree itself (issue #3319). Containment goes here, at
    /// construction, so every renderer of the node inherits it.
    /// </summary>
    private static void WriteRejectionWarnings(
        IReadOnlyList<TypeDependencyRejection> rejections)
    {
        foreach (TypeDependencyRejection rejection in rejections)
        {
            string mechanism = rejection.Kind switch
            {
                TypeDependencyRejectionKind.UnsupportedMetadataFormat =>
                    "unsupported metadata format (Windows Metadata)",
                TypeDependencyRejectionKind.MalformedMetadataRoot =>
                    $"malformed metadata root ({rejection.MetadataRootReason})",
                _ => "invalid image",
            };
            CommandError.WriteWarning(
                $"Excluded '{ContainLabel(Path.GetFileName(rejection.AssemblyPath))}' from the dependency scan: {mechanism}.",
                "Results may be incomplete.");
        }
    }

    private static TypeDependsOutcome Certified(int exitCode, bool uncertified)
        => new(
            uncertified && exitCode == 0 ? UncertifiedScanExitCode : exitCode,
            uncertified);

    private static string ContainLabel(string label)
        => CSharpIdentifier.ContainRenderedText(label);

    private static void WriteCount(int count) =>
        CountOutput.WriteCount(count);
}
