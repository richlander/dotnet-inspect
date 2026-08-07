using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
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
            CommandError.Write($"{error}");
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
                CommandError.Write($"{error}");
                return 1;
            }

            var evaluations = await EvaluateAsync(
                context,
                vector.PackageId,
                selectedAddresses,
                options);
            try
            {
                if (!TryResolveTypeName(options.TypeName, evaluations, out var typeFullName, out error))
                {
                    CommandError.Write($"{error}");
                    return 1;
                }

                var view = BuildView(
                    vector,
                    typeFullName!,
                    descriptor!,
                    evaluations,
                    selectedSections,
                    options.MemberName,
                    options.IncludeAll);
                Write(view, options, selectedSections);
                return 0;
            }
            finally
            {
                foreach (var evaluation in evaluations)
                    evaluation.Dispose();
            }
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    internal static TimelineDocumentView BuildView(
        PackageVersionVector vector,
        string typeFullName,
        string descriptor,
        IReadOnlyList<TimelineEvaluation> evaluated,
        HashSet<string> selectedSections,
        string? memberName = null,
        bool includeAll = false)
        => descriptor switch
        {
            var id when id == MetadataFindings.TypeDescriptor.Id =>
                BuildMetadataView(
                    vector,
                    typeFullName,
                    null,
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
                BuildMetadataView(
                    vector,
                    typeFullName,
                    memberName,
                    MetadataFindings.MemberDescriptor,
                    evaluated,
                    selectedSections,
                    ResolveMemberCorrelationKey(
                        typeFullName,
                        memberName,
                        evaluated),
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
                BuildMetadataView(
                    vector,
                    typeFullName,
                    null,
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
            var id when id == AnalysisFindings.AllocationDescriptor.Id =>
                BuildAllocationView(
                    vector,
                    typeFullName,
                    memberName!,
                    EvaluateAnalysis<AllocationOccurrence>(
                        evaluated,
                        typeFullName,
                        memberName!,
                        includeAll,
                        AnalysisFindings.AllocationDescriptor,
                        static (index, token, subject) =>
                        {
                            index.GetAllocationOccurrences().TryGetValue(token, out var occurrences);
                            return new FindingInspection<AllocationOccurrence>.Complete(
                                AnalysisFindings.InspectAllocations(
                                    occurrences.IsDefault ? [] : occurrences,
                                    subject));
                        }),
                    selectedSections),
            var id when id == AnalysisFindings.CallSiteDescriptor.Id =>
                BuildCallSiteView(
                    vector,
                    typeFullName,
                    memberName!,
                    EvaluateAnalysis<DirectCall>(
                        evaluated,
                        typeFullName,
                        memberName!,
                        includeAll,
                        AnalysisFindings.CallSiteDescriptor,
                        static (index, token, subject) =>
                        {
                            index.GetDirectCallsByCaller().TryGetValue(token, out var calls);
                            return new FindingInspection<DirectCall>.Complete(
                                AnalysisFindings.InspectCallSites(
                                    calls.IsDefault ? [] : calls,
                                    subject));
                        }),
                    selectedSections),
            var id when id == AnalysisFindings.UnsafetyDescriptor.Id =>
                BuildUnsafetyView(
                    vector,
                    typeFullName,
                    memberName!,
                    EvaluateAnalysis<UnsafetyOccurrence>(
                        evaluated,
                        typeFullName,
                        memberName!,
                        includeAll,
                        AnalysisFindings.UnsafetyDescriptor,
                        static (index, token, subject) =>
                        {
                            index.GetUnsafetyOccurrences().TryGetValue(token, out var occurrences);
                            return new FindingInspection<UnsafetyOccurrence>.Complete(
                                AnalysisFindings.InspectUnsafety(
                                    occurrences.IsDefault ? [] : occurrences,
                                    subject));
                        }),
                    selectedSections),
            _ => throw new InvalidOperationException(
                $"Unsupported Finding descriptor '{descriptor}'."),
        };

    static TimelineDocumentView BuildMetadataView<T>(
        PackageVersionVector vector,
        string typeFullName,
        string? memberName,
        FindingDescriptor descriptor,
        IReadOnlyList<TimelineEvaluation> evaluated,
        HashSet<string> selectedSections,
        FindingCorrelationKey? identityKey,
        Func<ApiSurface?, FindingInspection<T>> inspect,
        Func<ApiSurface?, ApiSurface?, FindingComparison<T>> compare)
        where T : notnull
    {
        var versioned = evaluated.Select(evaluation => new VersionedFindingInspection<T>(
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
                            evaluation.Error))))
            .ToArray();
        var evaluationsByPosition = evaluated.ToDictionary(item => item.Address.Position);
        return BuildCorrelatedView(
            vector,
            typeFullName,
            memberName,
            descriptor,
            versioned,
            selectedSections,
            identityKey,
            (oldPosition, newPosition, _, _) => compare(
                evaluationsByPosition[oldPosition].Surface,
                evaluationsByPosition[newPosition].Surface));
    }

    static FindingCorrelationKey? ResolveMemberCorrelationKey(
        string typeFullName,
        string? memberName,
        IReadOnlyList<TimelineEvaluation> evaluated)
    {
        if (string.IsNullOrWhiteSpace(memberName))
            return null;

        var selector = MemberTargetSelector.Parse(memberName);
        foreach (var evaluation in evaluated.OrderBy(item => item.Address.Position))
        {
            var type = evaluation.Surface?.Types.FirstOrDefault(type =>
                string.Equals(type.FullName, typeFullName, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                continue;

            var resolution = MemberTargetResolver.Resolve(type, selector);
            if (resolution.Found)
            {
                var handle = resolution.Target!.ApiMember;
                return new FindingCorrelationKey(
                    Subject(typeFullName),
                    MetadataFindings.MemberDescriptor,
                    new FindingKey(
                        handle.CanonicalSignature ?? handle.Identity,
                        type.FullName));
            }

            if (resolution.Diagnostic is { Kind: MemberTargetDiagnosticKind.AmbiguousMember
                    or MemberTargetDiagnosticKind.DigestAmbiguous
                    or MemberTargetDiagnosticKind.ConflictingSelectors } diagnostic)
            {
                throw new InvalidOperationException(diagnostic.Message);
            }
        }

        return new FindingCorrelationKey(
            Subject(typeFullName),
            MetadataFindings.MemberDescriptor,
            new FindingKey($"selector:{selector.NormalizedSelector}", typeFullName));
    }

    static TimelineDocumentView BuildAllocationView(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<AllocationOccurrence>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisView(
            vector,
            typeFullName,
            memberName,
            AnalysisFindings.AllocationDescriptor,
            evaluated,
            selectedSections,
            AnalysisFindings.CompareAllocations);

    static TimelineDocumentView BuildCallSiteView(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<DirectCall>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisView(
            vector,
            typeFullName,
            memberName,
            AnalysisFindings.CallSiteDescriptor,
            evaluated,
            selectedSections,
            AnalysisFindings.CompareCallSites);

    internal static TimelineDocumentView BuildUnsafetyView(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<UnsafetyOccurrence>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisView(
            vector,
            typeFullName,
            memberName,
            AnalysisFindings.UnsafetyDescriptor,
            evaluated,
            selectedSections,
            AnalysisFindings.CompareUnsafety);

    static TimelineDocumentView BuildAnalysisView<T>(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        FindingDescriptor descriptor,
        IReadOnlyList<TimelineFindingEvaluation<T>> evaluated,
        HashSet<string> selectedSections,
        Func<IEnumerable<T>, IEnumerable<T>, FindingSubject, int, FindingComparison<T>> compare)
        where T : notnull
    {
        var subject = MemberSubject(typeFullName, memberName);
        var versioned = evaluated.Select(evaluation => new VersionedFindingInspection<T>(
            new FindingVersion(
                evaluation.Address.Selector,
                evaluation.Address.Version.ToNormalizedString(),
                evaluation.Address.Position),
            evaluation.Inspection)).ToArray();
        return BuildCorrelatedView(
            vector,
            typeFullName,
            memberName,
            descriptor,
            versioned,
            selectedSections,
            null,
            (_, _, oldInspection, newInspection) => CompareAnalysis(
                oldInspection,
                newInspection,
                subject,
                compare));
    }

    static FindingComparison<T> CompareAnalysis<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection,
        FindingSubject subject,
        Func<IEnumerable<T>, IEnumerable<T>, FindingSubject, int, FindingComparison<T>> compare)
        where T : notnull
    {
        if (oldInspection is FindingInspection<T>.Complete oldComplete
            && newInspection is FindingInspection<T>.Complete newComplete)
        {
            return compare(
                oldComplete.Findings.Select(static finding => finding.Payload),
                newComplete.Findings.Select(static finding => finding.Payload),
                subject,
                100);
        }

        return FindingComparison.Compare(oldInspection, newInspection);
    }

    static IReadOnlyList<TimelineFindingEvaluation<T>> EvaluateAnalysis<T>(
        IReadOnlyList<TimelineEvaluation> evaluated,
        string typeFullName,
        string memberName,
        bool includeAll,
        FindingDescriptor descriptor,
        Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>> inspect)
        where T : notnull
    {
        var subject = MemberSubject(typeFullName, memberName);
        List<TimelineFindingEvaluation<T>> results = [];
        foreach (var evaluation in evaluated)
        {
            FindingInspection<T> inspection;
            try
            {
                if (evaluation.Error is not null)
                {
                    inspection = new FindingInspection<T>.Failed(
                        new InspectionError(subject, descriptor, evaluation.Error));
                }
                else
                {
                    inspection = InspectAnalysisEndpoint(
                        evaluation,
                        typeFullName,
                        memberName,
                        includeAll,
                        descriptor,
                        subject,
                        inspect);
                }
            }
            catch (Exception ex)
            {
                inspection = new FindingInspection<T>.Failed(
                    new InspectionError(
                        subject,
                        descriptor,
                        $"{ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                evaluation.Dispose();
            }

            results.Add(new TimelineFindingEvaluation<T>(evaluation.Address, inspection));
        }

        return results;
    }

    static FindingInspection<T> InspectAnalysisEndpoint<T>(
        TimelineEvaluation evaluation,
        string typeFullName,
        string memberName,
        bool includeAll,
        FindingDescriptor descriptor,
        FindingSubject subject,
        Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>> inspect)
        where T : notnull
    {
        if (evaluation.Endpoint is null)
            return new FindingInspection<T>.Absent("The package cell has no acquired assembly set.");

        return InspectAnalysisAssemblies<T>(
            evaluation.Endpoint.Paths,
            typeFullName,
            memberName,
            includeAll,
            descriptor,
            subject,
            inspect);
    }

    internal static FindingInspection<UnsafetyOccurrence> InspectUnsafetyAssemblies(
        IReadOnlyList<string> assemblyPaths,
        string typeFullName,
        string memberName,
        bool includeAll = false)
    {
        var subject = MemberSubject(typeFullName, memberName);
        return InspectAnalysisAssemblies<UnsafetyOccurrence>(
            assemblyPaths,
            typeFullName,
            memberName,
            includeAll,
            AnalysisFindings.UnsafetyDescriptor,
            subject,
            static (index, token, findingSubject) =>
            {
                index.GetUnsafetyOccurrences().TryGetValue(token, out var occurrences);
                return new FindingInspection<UnsafetyOccurrence>.Complete(
                    AnalysisFindings.InspectUnsafety(
                        occurrences.IsDefault ? [] : occurrences,
                        findingSubject));
            });
    }

    static FindingInspection<T> InspectAnalysisAssemblies<T>(
        IReadOnlyList<string> assemblyPaths,
        string typeFullName,
        string memberName,
        bool includeAll,
        FindingDescriptor descriptor,
        FindingSubject subject,
        Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>> inspect)
        where T : notnull
    {
        var selector = MemberTargetSelector.Parse(memberName);
        List<(string Path, ResolvedMemberTarget Target)> targets = [];
        bool typeFound = false;
        foreach (string path in assemblyPaths)
        {
            var surface = AssemblyReader.ExtractApiSurface(path, includeAll);
            var type = surface?.Types.FirstOrDefault(type =>
                string.Equals(type.FullName, typeFullName, StringComparison.OrdinalIgnoreCase));
            if (type is null)
                continue;

            typeFound = true;
            var resolution = MemberTargetResolver.Resolve(type, selector);
            if (resolution.Found)
            {
                targets.Add((path, resolution.Target!));
                continue;
            }

            if (resolution.Diagnostic is { Kind: MemberTargetDiagnosticKind.AmbiguousMember
                    or MemberTargetDiagnosticKind.DigestAmbiguous
                    or MemberTargetDiagnosticKind.ConflictingSelectors } diagnostic)
            {
                return new FindingInspection<T>.Failed(
                    new InspectionError(subject, descriptor, diagnostic.Message));
            }
        }

        if (targets.Count == 0)
        {
            string detail = typeFound
                ? $"Member '{memberName}' is absent."
                : $"Type '{typeFullName}' is absent.";
            return new FindingInspection<T>.Absent(detail);
        }
        if (targets.Count > 1)
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    $"Member '{memberName}' resolved in more than one package assembly."));
        }

        var (assemblyPath, target) = targets[0];
        if (target.Kind is not (MemberTargetKind.Constructor
            or MemberTargetKind.Finalizer
            or MemberTargetKind.Method
            or MemberTargetKind.Operator
            or MemberTargetKind.ExplicitInterfaceImplementation
            or MemberTargetKind.ExtensionMethod))
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    $"Finding '{descriptor.Id}' requires a method-like target; "
                    + $"'{memberName}' resolved to {target.Kind}."));
        }
        if (target.Body?.MetadataToken is not { } token)
        {
            return new FindingInspection<T>.Absent(
                $"Member '{memberName}' has no method-body target.");
        }

        var index = LibraryBodyIndex.Open(
            assemblyPath,
            includeAllocations: descriptor == AnalysisFindings.AllocationDescriptor,
            includeOpportunities: false,
            bodyScope: ImmutableHashSet.Create(token));
        return inspect(index, token, subject);
    }

    static TimelineDocumentView BuildCorrelatedView<T>(
        PackageVersionVector vector,
        string typeFullName,
        string? memberName,
        FindingDescriptor descriptor,
        IReadOnlyList<VersionedFindingInspection<T>> versioned,
        HashSet<string> selectedSections,
        FindingCorrelationKey? identityKey,
        Func<int, int, FindingInspection<T>, FindingInspection<T>, FindingComparison<T>> compare)
        where T : notnull
    {
        var correlation = FindingCensusCorrelation<T>.Create(versioned);
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
                descriptor.Id,
                typeFullName,
                memberName,
                identityKey,
                compare)
            : null;

        return new TimelineDocumentView
        {
            Title = $"Timeline: {vector.PackageId}",
            Range = $"{vector.Start.ToNormalizedString()}..{vector.End.ToNormalizedString()}",
            Type = typeFullName,
            Member = memberName,
            Finding = descriptor.Id,
            Recommendation = RecommendProbe(
                vector,
                typeFullName,
                memberName,
                descriptor.Id,
                correlation.Inspections.Select(item => item.Version.Position)),
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
        string descriptor,
        string typeFullName,
        string? memberName,
        FindingCorrelationKey? identityKey,
        Func<int, int, FindingInspection<T>, FindingInspection<T>, FindingComparison<T>> compare)
        where T : notnull
    {
        var ordered = correlation.Inspections;
        string focusTarget = memberName is null
            ? typeFullName
            : $"{typeFullName}.{memberName}";
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
                comparison = compare(
                    oldInspection.Version.Position,
                    newInspection.Version.Position,
                    oldInspection.Inspection,
                    newInspection.Inspection);
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
                    focusTarget,
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
                    ? $"The focused {(memberName is null ? "type" : "member")} became available to this census."
                    : $"The focused {(memberName is null ? "type" : "member")} ceased to be available to this census.";
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    subjectTransition,
                    descriptor,
                    focusTarget,
                    exact ? detail : AppendGapQualification(detail)));
            }

            var changes = completeComparison.Pairs
                .Where(pair => pair.Kind != PairKind.Present)
                .Cast<IPairFinding>()
                .Where(pair => identityKey is null
                    || ((pair.Old ?? pair.New) is Finding<T> finding
                        && Matches(identityKey, finding)))
                .ToArray();
            if (changes.Length == 0 && subjectTransition is null)
            {
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    "None",
                    descriptor,
                    focusTarget,
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

    static bool Matches<T>(FindingCorrelationKey key, Finding<T> finding)
        where T : notnull
        => finding.Subject.Key == key.Subject.Key
            && finding.Descriptor.Id == key.Descriptor.Id
            && finding.Key == key.Key;

    static string? AppendGapQualification(string? detail)
        => string.IsNullOrEmpty(detail)
            ? "Observed across a gap; the exact transition version is unknown."
            : $"{detail}; observed across a gap; the exact transition version is unknown.";

    static FindingSubject Subject(string typeFullName)
        => new($"api.type:{typeFullName}", typeFullName);

    static FindingSubject MemberSubject(string typeFullName, string memberName)
    {
        var selector = MemberTargetSelector.Parse(memberName);
        return new(
            $"analysis.member:{typeFullName}:{selector.NormalizedSelector}",
            $"{typeFullName}.{selector.NormalizedSelector}");
    }

    static string GetTarget(IPairFinding pair)
    {
        var finding = pair.New ?? pair.Old;
        return finding switch
        {
            Finding<ApiTypeHandle> type => type.Payload.TypeFullName,
            Finding<ApiMemberHandle> member => member.Payload.Identity,
            Finding<ApiAttributeHandle> attribute => attribute.Payload.Attribute,
            Finding<AllocationOccurrence> allocation => AllocationTarget(allocation),
            Finding<DirectCall> callSite => CallSiteTarget(callSite),
            Finding<UnsafetyOccurrence> unsafety => UnsafetyTarget(unsafety),
            _ => pair.Subject.Display,
        };
    }

    static string AllocationTarget(Finding<AllocationOccurrence> finding)
    {
        var occurrence = finding.Payload;
        var allocatedType = occurrence.AllocatedType?.ToQualifiedDisplayString()
            ?? occurrence.RuntimeAllocationType
            ?? occurrence.Detail
            ?? "?";
        return $"{finding.Subject.Display} :: {occurrence.Source}/{occurrence.Kind} {allocatedType}";
    }

    static string CallSiteTarget(Finding<DirectCall> finding)
    {
        var callee = finding.Payload.Callee;
        if (callee.Kind == MemberKind.Unsupported)
            return $"{finding.Subject.Display} :: {callee.DeclaringType.ToDisplayString()}";

        var typeArguments = callee.TypeArguments.IsDefaultOrEmpty
            ? ""
            : $"<{string.Join(", ", callee.TypeArguments.Select(type => type.ToQualifiedDisplayString()))}>";
        var parameters = string.Join(
            ", ",
            callee.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        var declaringType = callee.DeclaringType.ToQualifiedDisplayString();
        var calleeDisplay = callee.Kind == MemberKind.Constructor
            ? $"{declaringType}{typeArguments}({parameters})"
            : $"{declaringType}.{callee.Name}{typeArguments}({parameters})";
        return $"{finding.Subject.Display} :: {calleeDisplay}";
    }

    static string UnsafetyTarget(Finding<UnsafetyOccurrence> finding)
    {
        var occurrence = finding.Payload;
        string detail = string.IsNullOrWhiteSpace(occurrence.Detail)
            ? ""
            : $" {occurrence.Detail}";
        return $"{finding.Subject.Display} :: {occurrence.Kind}{detail}";
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
                    SourceOptions = NuGetSourceResolver.RestrictToSources(
                        options.SourceOptions,
                        address.ReportingSourceUrls),
                    TempDirPrefix = "inspect-timeline",
                    IncludePackageRuntimeAssemblies = true,
                },
                options.IncludeAll,
                context.Logger);
            if (result.Error is not null)
                return (null, result.Error, (ApiSurfaceEndpoint?)null);

            var endpoint = result.Endpoint!;
            return (endpoint.Surface, (string?)null, endpoint);
        });

    internal static async Task<List<TimelineEvaluation>> EvaluateCellsAsync(
        ImmutableArray<PackageVersionAddress> addresses,
        Func<PackageVersionAddress, Task<(
            ApiSurface? Surface,
            string? Error,
            ApiSurfaceEndpoint? Endpoint)>> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        List<TimelineEvaluation> evaluations = [];
        foreach (var address in addresses)
        {
            try
            {
                var result = await evaluate(address);
                evaluations.Add(new TimelineEvaluation(
                    address,
                    result.Surface,
                    result.Error,
                    result.Endpoint));
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
        string? memberName,
        string descriptor,
        IEnumerable<int> evaluatedPositions)
    {
        var evaluated = evaluatedPositions.ToHashSet();
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
            + (memberName is null ? "" : $"--member {ShellQuote(memberName)} ")
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
        selectedSections = ResolveSections(options.Select, options.SelectDefault, out error);
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
            error = $"Unknown Finding '{options.Finding}'. Use api.type, api.member, api.attribute, analysis.allocation, analysis.call-site, or analysis.unsafety.";
            return false;
        }

        bool analysis = IsAnalysisDescriptor(descriptor);
        if (analysis && string.IsNullOrWhiteSpace(options.MemberName))
        {
            error = $"--finding {descriptor} requires exactly one --member target.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(options.MemberName)
            && descriptor != MetadataFindings.MemberDescriptor.Id
            && !analysis)
        {
            error = $"--member is not supported with --finding {descriptor}.";
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
            "analysis.allocation" => "analysis.allocation",
            "analysis.call-site" => "analysis.call-site",
            "analysis.unsafety" => "analysis.unsafety",
            _ => null,
        };

    static bool IsAnalysisDescriptor(string descriptor)
        => descriptor == AnalysisFindings.AllocationDescriptor.Id
            || descriptor == AnalysisFindings.CallSiteDescriptor.Id
            || descriptor == AnalysisFindings.UnsafetyDescriptor.Id;

    // Bare -S asks for the fixed/bounded overview: the sections whose row count is structurally
    // constant across every target. Both timeline sections grow with the version range, so there
    // is no such subset here and bare -S is refused rather than silently widened to the default
    // view. It was refused before #3547 too, but by leaking the internal '@Default' marker into
    // the message; the refusal is what is preserved, not the spelling.
    static HashSet<string> ResolveSections(string[]? select, bool selectDefault, out string? error)
    {
        HashSet<string> sections = new(StringComparer.OrdinalIgnoreCase);
        if (selectDefault && (select is null || select.Length == 0))
        {
            error = "Bare -S has no fixed sections for timeline. Use -S Evaluations or -S Transitions.";
            return sections;
        }

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
            CountOutput.WriteCount(count);
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
        string? Error,
        ApiSurfaceEndpoint? Endpoint = null) : IDisposable
    {
        public void Dispose() => Endpoint?.Dispose();
    }

    internal sealed record TimelineFindingEvaluation<T>(
        PackageVersionAddress Address,
        FindingInspection<T> Inspection)
        where T : notnull;
}

public sealed record TimelineOptions
{
    public string PackageVersionRange { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? MemberName { get; init; }
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
    public RowWindow? Rows { get; init; }
    public string[]? Select { get; init; }
    public bool SelectDefault { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public bool IsTabular => Tabular || Tsv || Jsonl;
}

[JsonSerializable(typeof(TimelineDocumentView))]
internal partial class TimelineJsonContext : JsonSerializerContext
{
}
