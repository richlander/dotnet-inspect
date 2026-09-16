using System.Globalization;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class PackageAssemblySemanticQuerySections
{
    public static SectionCatalog<PackageAssemblySemanticQueryView> Catalog
    { get; } =
        new SectionPipeline<PackageAssemblySemanticQueryView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<PackageRows>()
            .Compile();

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<PackageAssemblySemanticQueryView>()!
            .ToDocumentSchema();

    public static PackageAssemblySemanticQueryView CreateDocument(
        string literal,
        string targetFramework,
        IReadOnlyList<PackageAssemblySemanticQueryResult> results,
        PackageAssemblySemanticQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(document);

        return new()
        {
            TitleText = InertString.Format(
                TextPolicy.Field,
                $"Package Query library literal: {literal}"),
            DescriptionText = new(
                TextPolicy.Prose,
                $"{Count(document.MatchedPackageCount)} matching packages from "
                + $"{Count(document.CandidateCount)} candidates; "
                + $"{Count(document.OccurrenceCount)} decoded literal uses; "
                + $"target framework {targetFramework}; "
                + $"misses {Count(document.SemanticMissCount)}, "
                + $"not applicable {Count(document.NotApplicableCount)}, "
                + $"failures {Count(document.FailureCount)}."),
            Results =
            [
                .. results.Select(
                    result => new PackageAssemblySemanticQueryRow(result)),
            ],
        };
    }

    public sealed class PackageRows
        : ISectionDescriptor<PackageAssemblySemanticQueryView>
    {
        public static string Name => PackageProfileSections.Packages;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(
            PackageAssemblySemanticQueryView model) =>
            model.Results.Count > 0;
    }

    private static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    internal static string Describe(
        PackageAssemblyEvaluationOutcome.Failure failure)
    {
        string detail = failure.Reason switch
        {
            PackageAssemblyFailureReason.InvalidSelection selection =>
                $"Asset selection reported {selection.Status}.",
            PackageAssemblyFailureReason.EntryByteLimit limit =>
                $"The selected entry exceeds the {Count(limit.MaximumEntryBytes)}-byte entry limit "
                + $"or the {Count(limit.MaximumRetainedImageBytes)}-byte retained-image limit.",
            PackageAssemblyFailureReason.ArtifactPublication publication =>
                "Artifact publication was refused: "
                + string.Join(
                    ", ",
                    publication.Failures.Select(
                        static value =>
                            $"{value.Kind}/{value.DiagnosticCode}")),
            PackageAssemblyFailureReason.NotAssembly notAssembly =>
                $"The selected entry is not an assembly ({notAssembly.AdmissionStage}: {notAssembly.Kind}).",
            PackageAssemblyFailureReason.ProjectionRejected projection =>
                $"Assembly projection was refused: {projection.Failure}.",
            PackageAssemblyFailureReason.QueryRejected query =>
                $"The assembly query was refused: {query.Failure}.",
            PackageAssemblyFailureReason.SemanticRejection rejection =>
                $"The literal producer rejected the assembly: "
                + $"{rejection.Rejection.Kind} at method "
                + $"0x{rejection.Rejection.Site.MethodDefinitionToken:X8}.",
            PackageAssemblyFailureReason.SemanticWorkLimit limit =>
                $"The literal producer reached its {limit.Limit} work limit.",
            PackageAssemblyFailureReason.CandidateCleanup cleanup =>
                $"Candidate cleanup was incomplete: "
                + $"{DescribeCleanup(cleanup.Evidence)}.",
            _ => "",
        };
        string stage =
            $"Assembly evaluation failed at {failure.Reason.Stage}.";
        string incomplete = failure.Cleanup is { } evidence
            ? $" Candidate cleanup was incomplete: "
                + $"{DescribeCleanup(evidence)}."
            : "";
        return detail.Length == 0
            ? stage + incomplete
            : $"{stage} {detail}{incomplete}";
    }

    private static string DescribeCleanup(
        PackageAssemblyEvaluationCleanupEvidence evidence)
    {
        var parts = new List<string>(
            evidence.CandidateFailures.Length + 1);
        if (evidence.ProjectionCleanup is { } projection)
            parts.Add($"projection cleanup {projection}");
        foreach (PackageAssemblyCandidateCleanupFailure failure
            in evidence.CandidateFailures)
        {
            parts.Add($"{failure.Stage} x{Count(failure.Count)}");
        }

        return parts.Count == 0
            ? "no recorded stage"
            : string.Join(", ", parts);
    }

    private static string Count(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
