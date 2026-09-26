using System.Globalization;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using InertText;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>Shared Markout document for reverse type-declaration results.</summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class TypeDeclarationLocatorView
{
    private TypeDeclarationLocatorView()
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public string Title { get; private init; } =
        "Type declaration locations";

    [MarkoutIgnore, JsonIgnore]
    public string Description { get; private init; } =
        "Known declaration choices and the evidence bounding them.";

    public string Status { get; private init; } = "";
    public string Completion { get; private init; } = "";

    [MarkoutPropertyName("Known candidates")]
    public string KnownCandidates { get; private init; } = "";

    [MarkoutPropertyName("Selected candidates")]
    public string SelectedCandidates { get; private init; } = "";

    [MarkoutSection(
        Name = "Results",
        EmptyText = "No candidate rows were selected.")]
    public List<TypeDeclarationLocatorRowView> Results
    { get; private init; } = [];

    [MarkoutSection(Name = "Coverage")]
    public List<TypeDeclarationLocatorCoverageRowView> RequestCoverage
    { get; private init; } = [];

    [MarkoutSection(
        Name = "Gaps",
        EmptyText = "No coverage or selection gaps were reported.")]
    public List<TypeDeclarationLocatorGapRowView> Gaps
    { get; private init; } = [];

    public static TypeDeclarationLocatorView Create(
        TypeDeclarationLocatorSectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            TypeDeclarationLocatorSectionResult.Rejected rejected =>
                Rejected(rejected),
            TypeDeclarationLocatorSectionResult.Evaluated evaluated =>
                Evaluated(evaluated),
            _ => throw new InvalidOperationException(
                "Unknown type declaration locator section result."),
        };
    }

    private static TypeDeclarationLocatorView Rejected(
        TypeDeclarationLocatorSectionResult.Rejected result)
    {
        string detail =
            result.RequestIndex is { } requestIndex
                ? $"{result.RejectionKind} at request {requestIndex + 1}"
                : result.PopulationFailure is { } populationFailure
                    ? $"{result.RejectionKind}: {populationFailure}"
                    : result.RejectionKind.ToString();
        return new()
        {
            Status = "Rejected",
            Completion = "Unavailable",
            KnownCandidates = "0",
            SelectedCandidates = "0",
            Gaps =
            [
                new(
                    "Request",
                    "Rejected",
                    Safe(detail)),
            ],
        };
    }

    private static TypeDeclarationLocatorView Evaluated(
        TypeDeclarationLocatorSectionResult.Evaluated result)
    {
        bool complete =
            result.VisibilityFailure is null
            && result.Answers.All(static answer => answer.IsComplete);
        var gaps = new List<TypeDeclarationLocatorGapRowView>();
        foreach (TypeDeclarationLocatorContextCoverage context
            in result.Contexts)
        {
            foreach (TypeDeclarationLocatorContextFailure failure
                in context.Failures)
            {
                gaps.Add(
                    new(
                        $"Context {context.ContextOrder + 1}",
                        failure switch
                        {
                            TypeDeclarationLocatorContextFailure.ContextLoad load => load.Code.ToString(),
                            TypeDeclarationLocatorContextFailure.ReferenceSource source =>
                                $"{source.Outcome}: {source.Code}",
                            TypeDeclarationLocatorContextFailure.ReferenceImage image =>
                                image.Failure.Kind.ToString(),
                            TypeDeclarationLocatorContextFailure.PackageScopeSelection package =>
                                package.Status.ToString(),
                            _ => throw new InvalidOperationException("Unknown declaration context failure."),
                        },
                        Safe(failure.Message)));
            }
        }
        foreach (TypeDeclarationLocatorMemberCoverage member
            in result.Members.Where(
                static member => !member.IsComplete))
        {
            string detail =
                member.CandidateFailure?.Detail
                ?? member.WorkspaceFailure?.ToString()
                ?? (member.UnsupportedDeclarations.IsEmpty
                    ? member.Outcome.ToString()
                    : string.Join(
                        ", ",
                        member.UnsupportedDeclarations.Select(
                            static declaration =>
                                declaration.Name
                                    .ToMetadataFullName())));
            gaps.Add(
                new(
                    ObservationScope(member.Observation),
                    member.Outcome.ToString(),
                    Safe(detail)));
        }
        if (result.RowSelectionFailure is { } selection)
        {
            gaps.Add(
                new(
                    Request(selection.Request),
                    "Row selection",
                    $"Stage {selection.StageNumber} requires row "
                        + $"{selection.RequiredPosition}, but only "
                        + $"{selection.AvailableCount} are available."));
        }
        if (result.VisibilityFailure is { } visibilityFailure)
        {
            gaps.Add(new("Request", "Visibility selection", visibilityFailure.ToString()));
        }
        foreach (TypeDeclarationLocatorSectionAnswer answer in result.Answers)
        {
            if (answer.Visibility is not { } visibility)
                continue;
            foreach (TypeDeclarationVisibilityUnknownCandidate unknown in visibility.UnknownCandidates)
            {
                gaps.Add(new(
                    $"{Request(answer.Request)}; {ObservationScope(unknown.Candidate.Observation)}; "
                        + Origin(unknown.Candidate.Observation),
                    "Visibility unknown",
                    Safe($"{unknown.Candidate.Name.ToMetadataFullName()}: unavailable "
                        + string.Join(", ", unknown.Facets))));
            }
        }

        return new()
        {
            Status =
                result.VisibilityFailure is not null
                    ? "Visibility selection failed"
                    : result.RowSelectionFailure is not null
                        ? "Row selection failed"
                        : "Evaluated",
            Completion = complete ? "Complete" : "Incomplete",
            KnownCandidates =
                result.Answers.Sum(
                    static answer =>
                        answer.AvailableCandidateCount)
                    .ToString(CultureInfo.InvariantCulture),
            SelectedCandidates =
                result.Answers.Sum(
                    static answer => answer.Candidates.Length)
                    .ToString(CultureInfo.InvariantCulture),
            Results =
            [
                .. result.Answers.SelectMany(
                    answer =>
                        answer.Candidates.Select(
                            candidate =>
                                Row(answer.Request, candidate))),
            ],
            RequestCoverage =
            [
                .. result.Answers.Select(
                    static answer =>
                        new TypeDeclarationLocatorCoverageRowView(
                            Request(answer.Request),
                            answer.AvailableCandidateCount.ToString(
                                CultureInfo.InvariantCulture),
                            answer.Candidates.Length.ToString(
                                CultureInfo.InvariantCulture),
                            YesNo(answer.IsRealizationComplete),
                            YesNo(answer.IsEvaluationComplete),
                            YesNo(answer.IsComplete))),
            ],
            Gaps = gaps,
        };
    }

    private static TypeDeclarationLocatorRowView Row(
        TypeDeclarationLocatorSectionRequest request,
        TypeDeclarationLocatorSectionCandidate candidate) =>
        new(
            Request(request),
            Safe(candidate.Name.ToMetadataFullName()),
            candidate.DeclarationKind.ToString(),
            Source(candidate.Coordinate),
            Library(candidate.Coordinate),
            Origin(candidate.Observation),
            Context(candidate.Observation));

    private static string Request(
        TypeDeclarationLocatorSectionRequest request) =>
        Safe(
            request switch
            {
                TypeDeclarationLocatorSectionRequest.ExactRequest exact =>
                    exact.Name.ToMetadataFullName(),
                TypeDeclarationLocatorSectionRequest.PatternRequest pattern =>
                    pattern.Text,
                TypeDeclarationLocatorSectionRequest.NamespaceRequest
                    @namespace =>
                    @namespace.Name,
                _ => throw new InvalidOperationException(
                    "Unknown locator request."),
            });

    private static string Source(
        TypeDeclarationLocatorSectionCoordinate coordinate) =>
        coordinate switch
        {
            TypeDeclarationLocatorSectionCoordinate.PackageCoordinate =>
                "Package",
            TypeDeclarationLocatorSectionCoordinate.PlatformCoordinate =>
                "Platform",
            TypeDeclarationLocatorSectionCoordinate.ProjectCoordinate =>
                "Project",
            TypeDeclarationLocatorSectionCoordinate.LocalCoordinate =>
                "Local",
            _ => throw new InvalidOperationException(
                "Unknown locator coordinate."),
        };

    private static string Library(
        TypeDeclarationLocatorSectionCoordinate coordinate)
    {
        var identity = coordinate.LibraryIdentity;
        string version =
            identity.Version?.ToString()
            ?? "version unavailable";
        return Safe($"{identity.Name} {version}");
    }

    private static string Origin(
        TypeDeclarationLocatorObservation observation) =>
        Safe(
            observation.Realization switch
            {
                TypeDeclarationLocatorRealization.PackageRealization
                    package =>
                    package.Producer,
                TypeDeclarationLocatorRealization.PlatformRealization
                    platform =>
                    platform.Producer,
                TypeDeclarationLocatorRealization.PlatformReferenceRealization reference =>
                    reference.Source.Authority,
                TypeDeclarationLocatorRealization.EmbeddedRealization =>
                    "Embedded content",
                _ => throw new InvalidOperationException(
                    "Unknown locator realization."),
            });

    private static string Context(
        TypeDeclarationLocatorObservation observation)
    {
        string sourceContext =
            observation.Realization switch
            {
                TypeDeclarationLocatorRealization.PackageRealization
                    package =>
                    Join(
                        package.Framework,
                        package.RuntimeIdentifier),
                TypeDeclarationLocatorRealization.PlatformRealization
                    platform =>
                    Join(
                        platform.Family,
                        platform.Version,
                        platform.Framework,
                        platform.Assembly,
                        platform.Role?.ToString()),
                TypeDeclarationLocatorRealization.EmbeddedRealization
                    embedded =>
                    embedded.ContentRef,
                TypeDeclarationLocatorRealization.PlatformReferenceRealization reference =>
                    Join("Reference", reference.Source.Family.ToString(), reference.Source.Version,
                        reference.Source.Framework, reference.Path),
                _ => throw new InvalidOperationException(
                    "Unknown locator realization."),
            };
        return Safe(
            $"context {observation.ContextOrder + 1}, "
                + $"member {observation.MemberOrder + 1}; "
                + $"{sourceContext}");
    }

    private static string ObservationScope(
        TypeDeclarationLocatorObservation observation) =>
        Safe(
            $"{observation.AssemblyIdentity.Name} "
                + $"(context {observation.ContextOrder + 1}, "
                + $"member {observation.MemberOrder + 1})");

    private static string Join(params string?[] parts) =>
        string.Join(
            ", ",
            parts.Where(static part =>
                !string.IsNullOrWhiteSpace(part)));

    private static string YesNo(bool value) =>
        value ? "Yes" : "No";

    private static string Safe(string value) =>
        new InertString(TextPolicy.Field, value).ToString();
}

[MarkoutSerializable]
public sealed record TypeDeclarationLocatorRowView(
    string Request,
    string Type,
    string Kind,
    string Source,
    string Library,
    string Origin,
    string Context);

[MarkoutSerializable]
public sealed record TypeDeclarationLocatorCoverageRowView(
    string Request,
    string Available,
    string Selected,
    string Realized,
    string Evaluated,
    string Complete);

[MarkoutSerializable]
public sealed record TypeDeclarationLocatorGapRowView(
    string Scope,
    string Kind,
    string Detail);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(TypeDeclarationLocatorView))]
[MarkoutContext(typeof(TypeDeclarationLocatorRowView))]
[MarkoutContext(typeof(TypeDeclarationLocatorCoverageRowView))]
[MarkoutContext(typeof(TypeDeclarationLocatorGapRowView))]
public partial class TypeDeclarationLocatorViewContext
    : MarkoutSerializerContext;
