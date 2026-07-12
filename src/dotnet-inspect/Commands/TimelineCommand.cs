using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Findings;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Commands;

public static class TimelineCommand
{
    public const string Name = "timeline";
    public const string EvaluationsSection = "Evaluations";
    public const string TransitionsSection = "Transitions";

    public static async Task<int> ExecuteAsync(TimelineOptions options)
    {
        if (!TryValidate(options, out var range, out var descriptor, out var selectedSections, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        try
        {
            var vector = await PackageVersionVector.ResolveAsync(
                context.HttpClient,
                range!,
                options.SourceOptions,
                context.Logger.Log,
                options.IncludePrerelease);
            if (!TrySelectAddresses(vector, options.At, out var selectedAddresses, out error))
            {
                Console.Error.WriteLine($"Error: {error}");
                return 1;
            }

            var evaluations = await EvaluateAsync(
                context,
                vector.PackageId,
                selectedAddresses,
                options);
            if (!TryResolveTypeName(options.TypeName, evaluations, out var typeFullName, out error))
            {
                Console.Error.WriteLine($"Error: {error}");
                return 1;
            }

            var view = BuildView(
                vector,
                typeFullName!,
                descriptor!,
                evaluations,
                selectedSections);
            Write(view, options, selectedSections);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    internal static TimelineDocumentView BuildView(
        PackageVersionVector vector,
        string typeFullName,
        string descriptor,
        IReadOnlyList<TimelineEvaluation> evaluated,
        HashSet<string> selectedSections)
    {
        var evaluatedByPosition = evaluated.ToDictionary(item => item.Address.Position);
        List<TimelineEvaluationRow>? evaluationRows = selectedSections.Contains(EvaluationsSection)
            ? vector.Addresses.Select(address =>
            {
                if (!evaluatedByPosition.TryGetValue(address.Position, out var evaluation))
                {
                    return new TimelineEvaluationRow(
                        address.Selector,
                        address.Version.ToNormalizedString(),
                        "Unevaluated",
                        null,
                        null);
                }

                return BuildEvaluationRow(evaluation, descriptor, typeFullName);
            }).ToList()
            : null;

        List<TimelineTransitionRow>? transitionRows = selectedSections.Contains(TransitionsSection)
            ? BuildTransitionRows(evaluated, descriptor, typeFullName)
            : null;

        return new TimelineDocumentView
        {
            Title = $"Timeline: {vector.PackageId}",
            Range = $"{vector.Start.ToNormalizedString()}..{vector.End.ToNormalizedString()}",
            Type = typeFullName,
            Finding = descriptor,
            Recommendation = RecommendProbe(vector, evaluated),
            Evaluations = evaluationRows,
            Transitions = transitionRows,
        };
    }

    static TimelineEvaluationRow BuildEvaluationRow(
        TimelineEvaluation evaluation,
        string descriptor,
        string typeFullName)
    {
        if (evaluation.Error is not null)
        {
            return new TimelineEvaluationRow(
                evaluation.Address.Selector,
                evaluation.Address.Version.ToNormalizedString(),
                "Failed",
                null,
                evaluation.Error);
        }

        var inspection = Inspect(evaluation.Surface, descriptor, typeFullName);
        return inspection switch
        {
            InspectionProjection.Complete complete => new TimelineEvaluationRow(
                evaluation.Address.Selector,
                evaluation.Address.Version.ToNormalizedString(),
                descriptor == MetadataFindings.TypeDescriptor.Id
                    ? complete.Count == 0 ? "Missing" : "Present"
                    : "Complete",
                complete.Count,
                null),
            InspectionProjection.Absent absent => new TimelineEvaluationRow(
                evaluation.Address.Selector,
                evaluation.Address.Version.ToNormalizedString(),
                "SubjectAbsent",
                0,
                absent.Detail),
            InspectionProjection.Failed failed => new TimelineEvaluationRow(
                evaluation.Address.Selector,
                evaluation.Address.Version.ToNormalizedString(),
                "Failed",
                null,
                failed.Error),
            _ => throw new InvalidOperationException("Unknown inspection state."),
        };
    }

    static List<TimelineTransitionRow> BuildTransitionRows(
        IReadOnlyList<TimelineEvaluation> evaluations,
        string descriptor,
        string typeFullName)
    {
        var ordered = evaluations.OrderBy(item => item.Address.Position).ToArray();
        List<TimelineTransitionRow> rows = [];
        for (int i = 1; i < ordered.Length; i++)
        {
            var oldEvaluation = ordered[i - 1];
            var newEvaluation = ordered[i];
            bool exact = newEvaluation.Address.Position - oldEvaluation.Address.Position == 1;
            string span = exact
                ? "Adjacent"
                : $"Gap ({newEvaluation.Address.Position - oldEvaluation.Address.Position - 1})";

            if (oldEvaluation.Error is not null || newEvaluation.Error is not null)
            {
                rows.Add(new TimelineTransitionRow(
                    oldEvaluation.Address.Selector,
                    newEvaluation.Address.Selector,
                    span,
                    "Failed",
                    descriptor,
                    typeFullName,
                    oldEvaluation.Error ?? newEvaluation.Error));
                continue;
            }

            var pairs = Compare(
                oldEvaluation.Surface,
                newEvaluation.Surface,
                descriptor,
                typeFullName);
            if (pairs is null)
            {
                rows.Add(new TimelineTransitionRow(
                    oldEvaluation.Address.Selector,
                    newEvaluation.Address.Selector,
                    span,
                    "Failed",
                    descriptor,
                    typeFullName,
                    "Finding comparison failed."));
                continue;
            }

            var changes = pairs.Where(pair => pair.Kind != PairKind.Present).ToArray();
            if (changes.Length == 0)
            {
                rows.Add(new TimelineTransitionRow(
                    oldEvaluation.Address.Selector,
                    newEvaluation.Address.Selector,
                    span,
                    "None",
                    descriptor,
                    typeFullName,
                    exact ? null : "No change was observed across the evaluated gap."));
                continue;
            }

            rows.AddRange(changes.Select(pair => new TimelineTransitionRow(
                oldEvaluation.Address.Selector,
                newEvaluation.Address.Selector,
                span,
                pair.Kind.ToString(),
                descriptor,
                GetTarget(pair),
                exact ? pair.Detail : AppendGapQualification(pair.Detail))));
        }

        return rows;
    }

    static string? AppendGapQualification(string? detail)
        => string.IsNullOrEmpty(detail)
            ? "Observed across a gap; the exact transition version is unknown."
            : $"{detail}; observed across a gap; the exact transition version is unknown.";

    static IReadOnlyList<IPairFinding>? Compare(
        ApiSurface? oldSurface,
        ApiSurface? newSurface,
        string descriptor,
        string typeFullName)
        => descriptor switch
        {
            "api.type" => GetPairs(
                MetadataFindings.CompareApiType(oldSurface, newSurface, Subject(typeFullName), typeFullName)),
            "api.member" => GetPairs(
                MetadataFindings.CompareApiMembers(oldSurface, newSurface, Subject(typeFullName), typeFullName)),
            "api.attribute" => GetPairs(
                MetadataFindings.CompareApiAttributes(oldSurface, newSurface, Subject(typeFullName), typeFullName)),
            _ => throw new InvalidOperationException($"Unsupported Finding descriptor '{descriptor}'."),
        };

    static IReadOnlyList<IPairFinding>? GetPairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison.Value is FindingComparison<T>.Complete complete
            ? complete.Pairs.Cast<IPairFinding>().ToArray()
            : null;

    static InspectionProjection Inspect(
        ApiSurface? surface,
        string descriptor,
        string typeFullName)
        => descriptor switch
        {
            "api.type" => Project(
                MetadataFindings.InspectApiType(surface, Subject(typeFullName), typeFullName)),
            "api.member" => Project(
                MetadataFindings.InspectApiMembers(surface, Subject(typeFullName), typeFullName)),
            "api.attribute" => Project(
                MetadataFindings.InspectApiAttributes(surface, Subject(typeFullName), typeFullName)),
            _ => throw new InvalidOperationException($"Unsupported Finding descriptor '{descriptor}'."),
        };

    static InspectionProjection Project<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection.Value switch
        {
            FindingInspection<T>.Complete complete => new InspectionProjection.Complete(complete.Findings.Length),
            FindingInspection<T>.Absent absent => new InspectionProjection.Absent(absent.Detail),
            FindingInspection<T>.Failed failed => new InspectionProjection.Failed(failed.Error.Reason),
            _ => throw new InvalidOperationException("Unknown inspection state."),
        };

    static FindingSubject Subject(string typeFullName)
        => new($"api.type:{typeFullName}", typeFullName);

    static string GetTarget(IPairFinding pair)
    {
        var finding = pair.New ?? pair.Old;
        return finding switch
        {
            Finding<ApiTypeHandle> type => type.Payload.TypeFullName,
            Finding<ApiMemberHandle> member => member.Payload.Identity,
            Finding<ApiAttributeHandle> attribute => attribute.Payload.Attribute,
            _ => pair.Subject.Display,
        };
    }

    static async Task<List<TimelineEvaluation>> EvaluateAsync(
        CommandContext context,
        string packageId,
        ImmutableArray<PackageVersionAddress> addresses,
        TimelineOptions options)
    {
        List<TimelineEvaluation> evaluations = [];
        foreach (var address in addresses)
        {
            context.Logger.Log($"Evaluating {packageId}@{address.Version.ToNormalizedString()} ({address.Selector})");
            var result = await ApiSurfaceEndpointResolver.ResolveAsync(
                context.HttpClient,
                new AssemblySetRequest
                {
                    Packages = [$"{packageId}@{address.Version.ToNormalizedString()}"],
                    Tfm = options.Tfm,
                    SourceOptions = options.SourceOptions,
                    TempDirPrefix = "inspect-timeline",
                    IncludePackageRuntimeAssemblies = true,
                },
                options.IncludeAll,
                context.Logger);
            if (result.Error is not null)
            {
                evaluations.Add(new TimelineEvaluation(address, null, result.Error));
                continue;
            }

            using var endpoint = result.Endpoint!;
            evaluations.Add(new TimelineEvaluation(address, endpoint.Surface, null));
        }

        return evaluations;
    }

    static bool TryResolveTypeName(
        string requested,
        IReadOnlyList<TimelineEvaluation> evaluations,
        out string? typeFullName,
        out string? error)
    {
        var matches = evaluations
            .Where(evaluation => evaluation.Surface is not null)
            .SelectMany(evaluation => evaluation.Surface!.Types)
            .Where(type =>
                string.Equals(type.FullName, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.Name, requested, StringComparison.OrdinalIgnoreCase)
                || type.FullName.EndsWith($".{requested}", StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (matches.Length > 1)
        {
            typeFullName = null;
            error = $"Type selector '{requested}' is ambiguous: {string.Join(", ", matches)}.";
            return false;
        }

        typeFullName = matches.Length == 1 ? matches[0] : requested;
        error = null;
        return true;
    }

    static bool TrySelectAddresses(
        PackageVersionVector vector,
        IReadOnlyList<string> selectors,
        out ImmutableArray<PackageVersionAddress> addresses,
        out string? error)
    {
        if (selectors.Count == 0)
        {
            addresses = [];
            error = null;
            return true;
        }

        if (selectors.Any(selector => selector.Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            if (selectors.Count != 1)
            {
                addresses = [];
                error = "--at all cannot be combined with another --at selector.";
                return false;
            }

            addresses = vector.Addresses;
            error = null;
            return true;
        }

        var selected = new Dictionary<int, PackageVersionAddress>();
        foreach (string selector in selectors)
        {
            if (!vector.TrySelect(selector, out var address, out error))
            {
                addresses = [];
                return false;
            }

            selected[address!.Position] = address;
        }

        addresses = [.. selected.Values.OrderBy(address => address.Position)];
        error = null;
        return true;
    }

    static string? RecommendProbe(
        PackageVersionVector vector,
        IReadOnlyList<TimelineEvaluation> evaluations)
    {
        var evaluated = evaluations.Select(item => item.Address.Position).ToHashSet();
        if (evaluated.Count == vector.Addresses.Length)
            return null;

        int bestStart = -1;
        int bestLength = 0;
        int start = -1;
        for (int position = 0; position <= vector.Addresses.Length; position++)
        {
            bool unevaluated = position < vector.Addresses.Length && !evaluated.Contains(position);
            if (unevaluated && start < 0)
                start = position;
            if (!unevaluated && start >= 0)
            {
                int length = position - start;
                if (length > bestLength)
                {
                    bestStart = start;
                    bestLength = length;
                }
                start = -1;
            }
        }

        int probe = bestStart + ((bestLength - 1) / 2);
        var address = vector.Addresses[probe];
        return $"Probe {address.Selector} ({address.Version.ToNormalizedString()}) with --at {address.Selector}.";
    }

    static bool TryValidate(
        TimelineOptions options,
        out PackageVersionRange? range,
        out string? descriptor,
        out HashSet<string> selectedSections,
        out string? error)
    {
        range = null;
        descriptor = NormalizeDescriptor(options.Finding);
        selectedSections = ResolveSections(options.Select, out error);
        if (error is not null)
            return false;

        if (!PackageVersionRange.TryParse(options.PackageVersionRange, out range, out error))
        {
            error ??= $"Invalid package version range '{options.PackageVersionRange}'. Expected Package@A..B.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.TypeName))
        {
            error = "A type focus is required. Use --type TypeName or pass it after the package range.";
            return false;
        }

        if (descriptor is null)
        {
            error = $"Unknown Finding '{options.Finding}'. Use api.type, api.member, or api.attribute.";
            return false;
        }

        if (options.Count && selectedSections.Count != 1)
        {
            error = "--count requires exactly one selected section: Evaluations or Transitions.";
            return false;
        }
        if (options.IsTabular && selectedSections.Count != 1)
        {
            error = "Table, TSV, and JSONL output require exactly one selected section: Evaluations or Transitions.";
            return false;
        }

        error = null;
        return true;
    }

    static string? NormalizeDescriptor(string descriptor)
        => descriptor.ToLowerInvariant() switch
        {
            "api.type" => "api.type",
            "api.member" => "api.member",
            "api.attribute" => "api.attribute",
            _ => null,
        };

    static HashSet<string> ResolveSections(string[]? select, out string? error)
    {
        HashSet<string> sections = new(StringComparer.OrdinalIgnoreCase);
        if (select is null || select.Length == 0)
        {
            sections.Add(EvaluationsSection);
            sections.Add(TransitionsSection);
            error = null;
            return sections;
        }

        foreach (string value in select)
        {
            if (value.Equals(EvaluationsSection, StringComparison.OrdinalIgnoreCase))
                sections.Add(EvaluationsSection);
            else if (value.Equals(TransitionsSection, StringComparison.OrdinalIgnoreCase))
                sections.Add(TransitionsSection);
            else
            {
                error = $"Unknown timeline section '{value}'. Use Evaluations or Transitions.";
                return sections;
            }
        }

        error = null;
        return sections;
    }

    static void Write(
        TimelineDocumentView view,
        TimelineOptions options,
        HashSet<string> selectedSections)
    {
        if (options.Count)
        {
            int count = selectedSections.Contains(EvaluationsSection)
                ? view.Evaluations?.Count ?? 0
                : view.Transitions?.Count ?? 0;
            Console.WriteLine(count);
            return;
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                view,
                TimelineJsonContext.Default.TimelineDocumentView));
            return;
        }

        if (options.IsTabular)
        {
            if (selectedSections.Contains(EvaluationsSection))
            {
                var evaluations = new TimelineEvaluationsView { Rows = view.Evaluations };
                OutputFormatter.WriteProjectedTable(
                    Console.Out,
                    !options.NoHeader,
                    options.Tsv,
                    options.Jsonl,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            evaluations,
                            writer,
                            formatter,
                            TimelineViewContext.Default,
                            writerOptions),
                    options.Rows);
            }
            else
            {
                var transitions = new TimelineTransitionsView { Rows = view.Transitions };
                OutputFormatter.WriteProjectedTable(
                    Console.Out,
                    !options.NoHeader,
                    options.Tsv,
                    options.Jsonl,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            transitions,
                            writer,
                            formatter,
                            TimelineViewContext.Default,
                            writerOptions),
                    options.Rows);
            }
            return;
        }

        var writer = new MarkoutWriter(new MarkdownFormatter());
        TimelineViewContext.Default.Serialize(view, writer);
        Console.WriteLine(OutputFormatter.ApplyRowLimit(writer.ToString(), options.Rows));
    }

    internal sealed record TimelineEvaluation(
        PackageVersionAddress Address,
        ApiSurface? Surface,
        string? Error);

    abstract record InspectionProjection
    {
        public sealed record Complete(int Count) : InspectionProjection;
        public sealed record Absent(string? Detail) : InspectionProjection;
        public sealed record Failed(string Error) : InspectionProjection;
    }
}

public sealed record TimelineOptions
{
    public string PackageVersionRange { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string Finding { get; init; } = MetadataFindings.MemberDescriptor.Id;
    public string[] At { get; init; } = [];
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool IncludePrerelease { get; init; }
    public bool Verbose { get; init; }
    public bool JsonOutput { get; init; }
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public bool Count { get; init; }
    public int? Rows { get; init; }
    public string[]? Select { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public bool IsTabular => OneLine || Tsv || Jsonl;
}

[JsonSerializable(typeof(TimelineDocumentView))]
internal partial class TimelineJsonContext : JsonSerializerContext
{
}
