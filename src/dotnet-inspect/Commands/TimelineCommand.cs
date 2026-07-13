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
        => descriptor switch
        {
            var id when id == MetadataFindings.TypeDescriptor.Id =>
                BuildView(
                    vector,
                    typeFullName,
                    MetadataFindings.TypeDescriptor,
                    evaluated,
                    selectedSections,
                    new FindingCorrelationKey(
                        Subject(typeFullName),
                        MetadataFindings.TypeDescriptor,
                        new FindingKey(typeFullName)),
                    surface => MetadataFindings.InspectApiType(
                        surface,
                        Subject(typeFullName),
                        typeFullName),
                    (oldSurface, newSurface) => MetadataFindings.CompareApiType(
                        oldSurface,
                        newSurface,
                        Subject(typeFullName),
                        typeFullName)),
            var id when id == MetadataFindings.MemberDescriptor.Id =>
                BuildView(
                    vector,
                    typeFullName,
                    MetadataFindings.MemberDescriptor,
                    evaluated,
                    selectedSections,
                    null,
                    surface => MetadataFindings.InspectApiMembers(
                        surface,
                        Subject(typeFullName),
                        typeFullName),
                    (oldSurface, newSurface) => MetadataFindings.CompareApiMembers(
                        oldSurface,
                        newSurface,
                        Subject(typeFullName),
                        typeFullName)),
            var id when id == MetadataFindings.AttributeDescriptor.Id =>
                BuildView(
                    vector,
                    typeFullName,
                    MetadataFindings.AttributeDescriptor,
                    evaluated,
                    selectedSections,
                    null,
                    surface => MetadataFindings.InspectApiAttributes(
                        surface,
                        Subject(typeFullName),
                        typeFullName),
                    (oldSurface, newSurface) => MetadataFindings.CompareApiAttributes(
                        oldSurface,
                        newSurface,
                        Subject(typeFullName),
                        typeFullName)),
            _ => throw new InvalidOperationException(
                $"Unsupported Finding descriptor '{descriptor}'."),
        };

    static TimelineDocumentView BuildView<T>(
        PackageVersionVector vector,
        string typeFullName,
        FindingDescriptor descriptor,
        IReadOnlyList<TimelineEvaluation> evaluated,
        HashSet<string> selectedSections,
        FindingCorrelationKey? identityKey,
        Func<ApiSurface?, FindingInspection<T>> inspect,
        Func<ApiSurface?, ApiSurface?, FindingComparison<T>> compare)
        where T : notnull
    {
        var correlation = FindingCensusCorrelation<T>.Create(
            evaluated.Select(evaluation => new VersionedFindingInspection<T>(
                new FindingVersion(
                    evaluation.Address.Selector,
                    evaluation.Address.Version.ToNormalizedString(),
                    evaluation.Address.Position),
                evaluation.Error is null
                    ? inspect(evaluation.Surface)
                    : new FindingInspection<T>.Failed(
                        new InspectionError(
                            Subject(typeFullName),
                            descriptor,
                            evaluation.Error)))));
        var inspectionsByPosition = correlation.Inspections.ToDictionary(
            item => item.Version.Position);
        var identityByPosition = identityKey is null
            ? null
            : correlation.Correlate(identityKey).Timeline.ToDictionary(
                item => GetVersion(item).Position);
        List<TimelineEvaluationRow>? evaluationRows = selectedSections.Contains(EvaluationsSection)
            ? vector.Addresses.Select(address =>
            {
                if (!inspectionsByPosition.TryGetValue(address.Position, out var evaluation))
                {
                    return new TimelineEvaluationRow(
                        address.Selector,
                        address.Version.ToNormalizedString(),
                        "Unevaluated",
                        null,
                        null);
                }

                return identityByPosition is null
                    ? BuildCensusEvaluationRow(evaluation)
                    : BuildIdentityEvaluationRow(identityByPosition[address.Position]);
            }).ToList()
            : null;

        List<TimelineTransitionRow>? transitionRows = selectedSections.Contains(TransitionsSection)
            ? BuildTransitionRows(
                correlation,
                evaluated,
                descriptor.Id,
                typeFullName,
                compare)
            : null;

        return new TimelineDocumentView
        {
            Title = $"Timeline: {vector.PackageId}",
            Range = $"{vector.Start.ToNormalizedString()}..{vector.End.ToNormalizedString()}",
            Type = typeFullName,
            Finding = descriptor.Id,
            Recommendation = RecommendProbe(vector, typeFullName, descriptor.Id, evaluated),
            Evaluations = evaluationRows,
            Transitions = transitionRows,
        };
    }

    static TimelineEvaluationRow BuildCensusEvaluationRow<T>(
        VersionedFindingInspection<T> evaluation)
        where T : notnull
        => evaluation.Inspection switch
        {
            FindingInspection<T>.Complete complete => new TimelineEvaluationRow(
                evaluation.Version.Key,
                evaluation.Version.Display,
                "Complete",
                complete.Findings.Length,
                null),
            FindingInspection<T>.Absent absent => new TimelineEvaluationRow(
                evaluation.Version.Key,
                evaluation.Version.Display,
                "SubjectAbsent",
                0,
                absent.Detail),
            FindingInspection<T>.Failed failed => new TimelineEvaluationRow(
                evaluation.Version.Key,
                evaluation.Version.Display,
                "Failed",
                null,
                failed.Error.Reason),
        };

    static TimelineEvaluationRow BuildIdentityEvaluationRow<T>(
        FindingCorrelationPoint<T> point)
        where T : notnull
        => point.Value switch
        {
            FindingCorrelationPoint<T>.Present present => new TimelineEvaluationRow(
                present.Version.Key,
                present.Version.Display,
                "Present",
                1,
                null),
            FindingCorrelationPoint<T>.Missing missing => new TimelineEvaluationRow(
                missing.Version.Key,
                missing.Version.Display,
                "Missing",
                0,
                null),
            FindingCorrelationPoint<T>.SubjectAbsent absent => new TimelineEvaluationRow(
                absent.Version.Key,
                absent.Version.Display,
                "SubjectAbsent",
                0,
                absent.Detail),
            FindingCorrelationPoint<T>.Failed failed => new TimelineEvaluationRow(
                failed.Version.Key,
                failed.Version.Display,
                "Failed",
                null,
                failed.Error.Reason),
            _ => throw new InvalidOperationException(
                "Finding correlation returned an unknown point."),
        };

    static FindingVersion GetVersion<T>(FindingCorrelationPoint<T> point)
        where T : notnull
        => point.Value switch
        {
            FindingCorrelationPoint<T>.Present present => present.Version,
            FindingCorrelationPoint<T>.Missing missing => missing.Version,
            FindingCorrelationPoint<T>.SubjectAbsent absent => absent.Version,
            FindingCorrelationPoint<T>.Failed failed => failed.Version,
            _ => throw new InvalidOperationException(
                "Finding correlation returned an unknown point."),
        };

    static List<TimelineTransitionRow> BuildTransitionRows<T>(
        FindingCensusCorrelation<T> correlation,
        IReadOnlyList<TimelineEvaluation> evaluations,
        string descriptor,
        string typeFullName,
        Func<ApiSurface?, ApiSurface?, FindingComparison<T>> compare)
        where T : notnull
    {
        var ordered = correlation.Inspections;
        var evaluationsByPosition = evaluations.ToDictionary(item => item.Address.Position);
        List<TimelineTransitionRow> rows = [];
        for (int i = 1; i < ordered.Length; i++)
        {
            var oldInspection = ordered[i - 1];
            var newInspection = ordered[i];
            bool exact = newInspection.Version.Position - oldInspection.Version.Position == 1;
            string span = exact
                ? "Adjacent"
                : $"Gap ({newInspection.Version.Position - oldInspection.Version.Position - 1})";

            FindingComparison<T> comparison;
            if (oldInspection.Inspection is FindingInspection<T>.Failed
                || newInspection.Inspection is FindingInspection<T>.Failed)
            {
                comparison = correlation.Compare(
                    oldInspection.Version.Key,
                    newInspection.Version.Key);
            }
            else
            {
                if (!evaluationsByPosition.TryGetValue(
                        oldInspection.Version.Position,
                        out var oldEvaluation)
                    || !evaluationsByPosition.TryGetValue(
                        newInspection.Version.Position,
                        out var newEvaluation))
                {
                    throw new InvalidOperationException(
                        $"Timeline correlation lost an evaluated cell for {descriptor} "
                        + $"{oldInspection.Version.Key}..{newInspection.Version.Key}.");
                }

                comparison = compare(oldEvaluation.Surface, newEvaluation.Surface);
            }

            if (comparison.OldInspection != oldInspection.Inspection
                || comparison.NewInspection != newInspection.Inspection)
            {
                throw new InvalidOperationException(
                    $"Producer comparison for {descriptor} returned inspections that differ "
                    + $"from the correlated censuses at "
                    + $"{oldInspection.Version.Key}..{newInspection.Version.Key}.");
            }

            if (comparison.Value is FindingComparison<T>.Failed failure)
            {
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    "Failed",
                    descriptor,
                    typeFullName,
                    failure.Failure));
                continue;
            }

            var completeComparison = comparison.Value as FindingComparison<T>.Complete
                ?? throw new InvalidOperationException(
                    $"Producer comparison for {descriptor} returned an unknown outcome at "
                    + $"{oldInspection.Version.Key}..{newInspection.Version.Key}.");
            string? subjectTransition = (oldInspection.Inspection, newInspection.Inspection) switch
            {
                (FindingInspection<T>.Absent, FindingInspection<T>.Complete) => "SubjectAvailable",
                (FindingInspection<T>.Complete, FindingInspection<T>.Absent) => "SubjectUnavailable",
                _ => null,
            };
            if (subjectTransition is not null)
            {
                string detail = subjectTransition == "SubjectAvailable"
                    ? "The focused type became available to this census."
                    : "The focused type ceased to be available to this census.";
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    subjectTransition,
                    descriptor,
                    typeFullName,
                    exact ? detail : AppendGapQualification(detail)));
            }

            var changes = completeComparison.Pairs
                .Where(pair => pair.Kind != PairKind.Present)
                .Cast<IPairFinding>()
                .ToArray();
            if (changes.Length == 0 && subjectTransition is null)
            {
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    "None",
                    descriptor,
                    typeFullName,
                    exact ? null : "No change was observed across the evaluated gap."));
                continue;
            }

            rows.AddRange(changes.Select(pair => new TimelineTransitionRow(
                oldInspection.Version.Key,
                newInspection.Version.Key,
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
        => await EvaluateCellsAsync(addresses, async address =>
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
                return (null, result.Error);

            using var endpoint = result.Endpoint!;
            return (endpoint.Surface, null);
        });

    internal static async Task<List<TimelineEvaluation>> EvaluateCellsAsync(
        ImmutableArray<PackageVersionAddress> addresses,
        Func<PackageVersionAddress, Task<(ApiSurface? Surface, string? Error)>> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        List<TimelineEvaluation> evaluations = [];
        foreach (var address in addresses)
        {
            try
            {
                var result = await evaluate(address);
                evaluations.Add(new TimelineEvaluation(address, result.Surface, result.Error));
            }
            catch (Exception ex)
            {
                evaluations.Add(new TimelineEvaluation(
                    address,
                    null,
                    $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return evaluations;
    }

    internal static bool TryResolveTypeName(
        string requested,
        IReadOnlyList<TimelineEvaluation> evaluations,
        out string? typeFullName,
        out string? error)
    {
        var types = evaluations
            .Where(evaluation => evaluation.Surface is not null)
            .SelectMany(evaluation => evaluation.Surface!.Types)
            .ToArray();
        var exactMatches = types
            .Where(type => string.Equals(
                type.FullName,
                requested,
                StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (exactMatches.Length == 1)
        {
            typeFullName = exactMatches[0];
            error = null;
            return true;
        }

        var matches = types
            .Where(type =>
                string.Equals(type.Name, requested, StringComparison.OrdinalIgnoreCase)
                || type.FullName.EndsWith($".{requested}", StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
        string typeFullName,
        string descriptor,
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
        string range = $"{vector.PackageId}@{vector.Start.ToNormalizedString()}..{vector.End.ToNormalizedString()}";
        return $"Probe {address.Selector} ({address.Version.ToNormalizedString()}): "
            + $"dotnet-inspect timeline --package {ShellQuote(range)} "
            + $"--type {ShellQuote(typeFullName)} "
            + $"--finding {ShellQuote(descriptor)} "
            + $"--at {ShellQuote(address.Selector)}";
    }

    static string ShellQuote(string value)
        => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

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
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool NoHeader { get; init; }
    public bool Count { get; init; }
    public int? Rows { get; init; }
    public string[]? Select { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public bool IsTabular => Tabular || Tsv || Jsonl;
}

[JsonSerializable(typeof(TimelineDocumentView))]
internal partial class TimelineJsonContext : JsonSerializerContext
{
}
