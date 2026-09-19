using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

public static class TimelineCommand
{
    public const string Name = "timeline";
    public const string EvaluationsSection = TimelineSections.Evaluations;
    public const string TransitionsSection = TimelineSections.Transitions;

    public static async Task<int> ExecuteAsync(TimelineOptions options)
    {
        if (!TryResolveSections(options, out var selectedSections))
            return 1;

        if (!TryValidate(options, selectedSections, out var range, out var descriptor, out var error))
        {
            CommandError.Write($"{error}");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        try
        {
            string workingDirectory = Directory.GetCurrentDirectory();
            await using PackageRangeExtraction? rangeExtraction = DotnetInspector.Networking.HttpClientFactory.IsOffline
                ? null
                : await PackageExtractor.OpenPackageRangeAsync(
                    context.HttpClient, range!, context.Logger.Log, "inspect-timeline",
                    options.SourceOptions, options.IncludePrerelease,
                    context.CreatePackageSourceComposition);
            var vector = rangeExtraction?.Vector ?? await PackageVersionVector.ResolveAsync(
                context.HttpClient, range!, options.SourceOptions,
                context.Logger.Log, options.IncludePrerelease);
            if (!TrySelectAddresses(
                    vector,
                    options.At,
                    options.MaxProbes,
                    out var selectedAddresses,
                    out error))
            {
                CommandError.Write($"{error}");
                return 1;
            }

            string sourceReplayArguments = "";
            if (rangeExtraction is not null
                && (options.MaxProbes is not null
                    || selectedAddresses.Length < vector.Addresses.Length))
            {
                NuGetSourceOptions sourceOptions = options.SourceOptions ?? NuGetSourceOptions.Default;
                if (sourceOptions.ConfigFile is null)
                    sourceOptions = sourceOptions with { ConfigDirectory = sourceOptions.ConfigDirectory ?? workingDirectory };
                if (!PackageReplaySourceArguments.TryCreate(
                        sourceOptions, Name, out PackageReplaySources? replaySources,
                        out error, workingDirectory: workingDirectory))
                {
                    CommandError.Write(error!);
                    return 1;
                }
                sourceReplayArguments = PackageReplaySourceArguments.Format(replaySources);
            }

            var evaluations = await EvaluateAsync(
                context,
                vector.PackageId,
                selectedAddresses,
                options,
                rangeExtraction);
            try
            {
                if (!TryResolveTypeName(options.TypeName, evaluations, out var typeFullName, out error))
                {
                    CommandError.Write($"{error}");
                    return 1;
                }

                TimelineBuildResult result;
                while (true)
                {
                    result = BuildViewResult(
                        vector,
                        typeFullName!,
                        descriptor!,
                        evaluations,
                        selectedSections,
                        options.MemberName,
                        options.IncludeAll,
                        disposeAnalysisEndpoints: false);
                    if (options.MaxProbes is not int maxProbes
                        || evaluations.Count >= maxProbes
                        || SelectProbePosition(result.ChangedIntervals) is not int probe)
                    {
                        break;
                    }

                    evaluations.AddRange(await EvaluateAsync(
                        context,
                        vector.PackageId,
                        [vector.Addresses[probe]],
                        options,
                        rangeExtraction));
                }

                TimelineDocumentView view = result.View;
                if (options.MaxProbes is int probeLimit)
                {
                    view.Recommendation = BuildBisectRecommendation(
                        vector,
                        typeFullName!,
                        options.MemberName,
                        descriptor!,
                        probeLimit,
                        evaluations.Count,
                        result.ChangedIntervals,
                        BuildDiffReplayArguments(sourceReplayArguments, options));
                }
                else if (view.Recommendation is not null)
                {
                    string replayArguments = BuildTimelineReplayArguments(
                        sourceReplayArguments,
                        options);
                    if (replayArguments.Length > 0)
                        view.Recommendation += " " + replayArguments.TrimStart();
                }
                return Write(view, options, selectedSections);
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
        => BuildViewResult(
            vector,
            typeFullName,
            descriptor,
            evaluated,
            selectedSections,
            memberName,
            includeAll,
            disposeAnalysisEndpoints: true).View;

    static TimelineBuildResult BuildViewResult(
        PackageVersionVector vector,
        string typeFullName,
        string descriptor,
        IReadOnlyList<TimelineEvaluation> evaluated,
        HashSet<string> selectedSections,
        string? memberName = null,
        bool includeAll = false,
        bool disposeAnalysisEndpoints = false)
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
                BuildAllocationResult(
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
                        },
                        disposeAnalysisEndpoints),
                    selectedSections),
            var id when id == AnalysisFindings.CallSiteDescriptor.Id =>
                BuildCallSiteResult(
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
                            index.GetDirectCallsByEvidenceMethod()
                                .TryGetValue(token, out var calls);
                            return new FindingInspection<DirectCall>.Complete(
                                AnalysisFindings.InspectCallSites(
                                    calls.IsDefault ? [] : calls,
                                    subject));
                        },
                        disposeAnalysisEndpoints),
                    selectedSections),
            var id when id == AnalysisFindings.UnsafetyDescriptor.Id =>
                BuildUnsafetyResult(
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
                        },
                        disposeAnalysisEndpoints),
                    selectedSections),
            _ => throw new InvalidOperationException(
                $"Unsupported Finding descriptor '{descriptor}'."),
        };

    static TimelineBuildResult BuildMetadataView<T>(
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
                string.Equals(type.FullName, typeFullName, StringComparison.Ordinal));
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

    internal static TimelineDocumentView BuildAllocationView(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<AllocationOccurrence>> evaluated,
        HashSet<string> selectedSections)
        => BuildAllocationResult(
            vector,
            typeFullName,
            memberName,
            evaluated,
            selectedSections).View;

    static TimelineBuildResult BuildAllocationResult(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<AllocationOccurrence>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisResult(
            vector,
            typeFullName,
            memberName,
            AnalysisFindings.AllocationDescriptor,
            evaluated,
            selectedSections,
            AnalysisFindings.CompareAllocations);

    internal static TimelineDocumentView BuildCallSiteView(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<DirectCall>> evaluated,
        HashSet<string> selectedSections)
        => BuildCallSiteResult(
            vector,
            typeFullName,
            memberName,
            evaluated,
            selectedSections).View;

    static TimelineBuildResult BuildCallSiteResult(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<DirectCall>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisResult(
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
        => BuildUnsafetyResult(
            vector,
            typeFullName,
            memberName,
            evaluated,
            selectedSections).View;

    static TimelineBuildResult BuildUnsafetyResult(
        PackageVersionVector vector,
        string typeFullName,
        string memberName,
        IReadOnlyList<TimelineFindingEvaluation<UnsafetyOccurrence>> evaluated,
        HashSet<string> selectedSections)
        => BuildAnalysisResult(
            vector,
            typeFullName,
            memberName,
            AnalysisFindings.UnsafetyDescriptor,
            evaluated,
            selectedSections,
            AnalysisFindings.CompareUnsafety);

    static TimelineBuildResult BuildAnalysisResult<T>(
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
        if (oldInspection.Value is FindingInspection<T>.Complete oldComplete
            && newInspection.Value is FindingInspection<T>.Complete newComplete)
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
        Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>> inspect,
        bool disposeEndpoints)
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
                if (disposeEndpoints)
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
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    "The package cell has no acquired assembly set."));
        }

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
        List<(string Path, ApiSurface? Surface)> surfaces = [];
        foreach (string path in assemblyPaths)
        {
            // Metadata admission raises these instead of collapsing an
            // unsupported or malformed image into a null surface, so the
            // timeline reports which mechanism rejected the assembly rather
            // than losing it behind the generic no-surface arm.
            string reason;
            try
            {
                surfaces.Add(
                    (path, AssemblyReader.ExtractApiSurface(path, includeAll)));
                continue;
            }
            catch (UnsupportedMetadataFormatException)
            {
                reason =
                    $"The API surface in '{path}' could not be inspected "
                    + "because it uses an unsupported metadata format.";
            }
            catch (MalformedMetadataRootException ex)
            {
                reason =
                    $"The API surface in '{path}' could not be inspected "
                    + $"because its metadata root is malformed ({ex.Reason}).";
            }

            return new FindingInspection<T>.Failed(
                new InspectionError(subject, descriptor, reason));
        }

        return InspectAnalysisAssemblies(
            surfaces,
            typeFullName,
            memberName,
            descriptor,
            subject,
            inspect);
    }

    internal static FindingInspection<T> InspectAnalysisAssemblies<T>(
        IReadOnlyList<(string Path, ApiSurface? Surface)> assemblies,
        string typeFullName,
        string memberName,
        FindingDescriptor descriptor,
        FindingSubject subject,
        Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>> inspect)
        where T : notnull
    {
        var selector = MemberTargetSelector.Parse(memberName);
        List<(string Path, ResolvedMemberTarget Target)> targets = [];
        bool typeFound = false;
        foreach (var (path, surface) in assemblies)
        {
            if (surface is null)
            {
                return new FindingInspection<T>.Failed(
                    new InspectionError(
                        subject,
                        descriptor,
                        $"The API surface in '{path}' could not be inspected."));
            }

            var type = surface?.Types.FirstOrDefault(type =>
                string.Equals(type.FullName, typeFullName, StringComparison.Ordinal));
            if (type is null)
            {
                var typeInspection = MetadataFindings.InspectApiType(
                    surface,
                    subject,
                    typeFullName);
                if (typeInspection.Value
                    is FindingInspection<ApiTypeHandle>.Failed failure)
                {
                    return new FindingInspection<T>.Failed(
                        new InspectionError(
                            subject,
                            descriptor,
                            $"The API surface in '{path}' is incomplete for "
                            + $"type '{typeFullName}': {failure.Error.Reason}"));
                }

                continue;
            }

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
            return new FindingInspection<T>.Absent(
                FindingInspectionAbsenceKind.SubjectAbsent,
                detail);
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
            return new FindingInspection<T>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                $"Member '{memberName}' resolved to {target.Kind} and has no "
                + $"method-body input for finding '{descriptor.Id}'.");
        }
        if (target.ApiMember.Member.HasMethodBody is false)
        {
            return new FindingInspection<T>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                $"Member '{memberName}' has no method-body target.");
        }
        if (target.ApiMember.Member.HasMethodBody is null)
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    $"Method-body presence is unavailable for member '{memberName}'."));
        }
        if (target.Body?.MetadataToken is not { } token)
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    $"Method-body identity is unavailable for member '{memberName}'."));
        }

        var session = OpenAnalysisSession(
            assemblyPath,
            descriptor,
            token);
        if (session.BodyIndex.Diagnostics.FirstOrDefault() is { } analysisDiagnostic)
        {
            return new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    $"Method-body analysis failed for member '{memberName}': "
                    + analysisDiagnostic.Message));
        }
        return inspect(session.BodyIndex, token, subject);
    }

    internal static MethodBodyInspectionSession OpenAnalysisSession(
        string assemblyPath,
        FindingDescriptor descriptor,
        int token)
        => MethodBodyInspectionSession.Open(
            assemblyPath,
            includeAllocations:
                descriptor == AnalysisFindings.AllocationDescriptor,
            includeOpportunities: false,
            bodyScope: ImmutableHashSet.Create(token));

    static TimelineBuildResult BuildCorrelatedView<T>(
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

        List<TimelineTransitionRow> transitionRows = BuildTransitionRows(
            correlation,
            descriptor.Id,
            typeFullName,
            memberName,
            identityKey,
            compare,
            out List<TimelineChangedGap> changedGaps);

        return new TimelineBuildResult(
            new TimelineDocumentView
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
                    correlation.Inspections.Select(item => item.Version.Position),
                    changedGaps),
                Evaluations = evaluationRows,
                Transitions = selectedSections.Contains(TransitionsSection)
                    ? transitionRows
                    : null,
            },
            changedGaps);
    }

    static TimelineEvaluationRow BuildCensusEvaluationRow<T>(
        VersionedFindingInspection<T> evaluation)
        where T : notnull
        => evaluation.Inspection.Value switch
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
                InspectionStateName(absent.Kind),
                0,
                absent.Detail),
            FindingInspection<T>.Failed failed => new TimelineEvaluationRow(
                evaluation.Version.Key,
                evaluation.Version.Display,
                "Failed",
                null,
                failed.Error.Reason),
            _ => throw new InvalidOperationException(
                "Finding inspection returned an unknown outcome."),
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
            FindingCorrelationPoint<T>.NoApplicableInput absent => new TimelineEvaluationRow(
                absent.Version.Key,
                absent.Version.Display,
                "NoApplicableInput",
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
            FindingCorrelationPoint<T>.NoApplicableInput absent => absent.Version,
            FindingCorrelationPoint<T>.Failed failed => failed.Version,
            _ => throw new InvalidOperationException(
                "Finding correlation returned an unknown point."),
        };

    internal static List<TimelineTransitionRow> BuildTransitionRows<T>(
        FindingCensusCorrelation<T> correlation,
        string descriptor,
        string typeFullName,
        string? memberName,
        FindingCorrelationKey? identityKey,
        Func<int, int, FindingInspection<T>, FindingInspection<T>, FindingComparison<T>> compare)
        where T : notnull
        => BuildTransitionRows(
            correlation,
            descriptor,
            typeFullName,
            memberName,
            identityKey,
            compare,
            out _);

    static List<TimelineTransitionRow> BuildTransitionRows<T>(
        FindingCensusCorrelation<T> correlation,
        string descriptor,
        string typeFullName,
        string? memberName,
        FindingCorrelationKey? identityKey,
        Func<int, int, FindingInspection<T>, FindingInspection<T>, FindingComparison<T>> compare,
        out List<TimelineChangedGap> changedGaps)
        where T : notnull
    {
        var ordered = correlation.Inspections;
        string focusTarget = memberName is null
            ? typeFullName
            : $"{typeFullName}.{memberName}";
        List<TimelineTransitionRow> rows = [];
        changedGaps = [];
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
            FindingInspectionTransition topology =
                completeComparison.Transition;
            string? topologyTransition = topology.IsSameTopology
                ? null
                : $"{InspectionStateName(topology.Old)}To"
                    + InspectionStateName(topology.New);
            if (topologyTransition is not null)
            {
                string detail =
                    $"The focused {(memberName is null ? "type" : "member")} "
                    + $"inspection changed from "
                    + $"{InspectionStateName(topology.Old)} to "
                    + $"{InspectionStateName(topology.New)}.";
                rows.Add(new TimelineTransitionRow(
                    oldInspection.Version.Key,
                    newInspection.Version.Key,
                    span,
                    topologyTransition,
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
            if (changes.Length == 0 && topologyTransition is null)
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

            changedGaps.Add(new TimelineChangedGap(
                oldInspection.Version.Position,
                newInspection.Version.Position));

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

    static string InspectionStateName(FindingInspectionAbsenceKind kind)
        => kind switch
        {
            FindingInspectionAbsenceKind.SubjectAbsent => "SubjectAbsent",
            FindingInspectionAbsenceKind.NoApplicableInput =>
                "NoApplicableInput",
            _ => throw new InvalidOperationException(
                $"Unsupported Finding inspection absence kind '{kind}'."),
        };

    static string InspectionStateName(FindingInspectionState state)
        => state switch
        {
            FindingInspectionState.Complete => "Complete",
            FindingInspectionState.SubjectAbsent => "SubjectAbsent",
            FindingInspectionState.NoApplicableInput => "NoApplicableInput",
            _ => throw new InvalidOperationException(
                $"Unsupported Finding inspection state '{state}'."),
        };

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
            Finding<AllocationOccurrence> allocation => FindingTargetFormatter.Format(allocation),
            Finding<DirectCall> callSite => FindingTargetFormatter.Format(callSite),
            Finding<UnsafetyOccurrence> unsafety => FindingTargetFormatter.Format(unsafety),
            _ => pair.Subject.Display,
        };
    }

    static async Task<List<TimelineEvaluation>> EvaluateAsync(
        CommandContext context,
        string packageId,
        ImmutableArray<PackageVersionAddress> addresses,
        TimelineOptions options,
        PackageRangeExtraction? rangeExtraction)
        => await EvaluateCellsAsync(addresses, async address =>
        {
            context.Logger.Log($"Evaluating {packageId}@{address.Version.ToNormalizedString()} ({address.Selector})");
            if (rangeExtraction is not null)
            {
                PackageExtractionOutcome outcome = await rangeExtraction.ExtractAsync(address.Selector);
                if (!outcome.IsSuccess)
                    return (null, outcome.ErrorMessage, (ApiSurfaceEndpoint?)null);

                var acquired = ApiSurfaceEndpointResolver.Resolve(
                    outcome.Result!, options.Tfm, options.IncludeAll, context.Logger);
                return (acquired.Endpoint?.Surface, acquired.Error, acquired.Endpoint);
            }

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
        string[] typeNames = evaluations
            .Where(evaluation => evaluation.Surface is not null)
            .SelectMany(evaluation =>
                FindingTypeNames.EnumerateResolvable(evaluation.Surface!))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string? ordinalMatch = typeNames.FirstOrDefault(typeName =>
            string.Equals(typeName, requested, StringComparison.Ordinal));
        if (ordinalMatch is not null)
        {
            typeFullName = ordinalMatch;
            error = null;
            return true;
        }

        string[] exactMatches = typeNames
            .Where(typeName => string.Equals(
                typeName,
                requested,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            typeFullName = exactMatches[0];
            error = null;
            return true;
        }
        if (exactMatches.Length > 1)
        {
            typeFullName = null;
            error =
                $"Type selector '{requested}' is ambiguous: "
                + $"{string.Join(", ", exactMatches)}.";
            return false;
        }

        string[] matches = typeNames
            .Where(typeName =>
                TypeMatcher.MatchesTypeFilter(typeName, requested))
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
        int? maxProbes,
        out ImmutableArray<PackageVersionAddress> addresses,
        out string? error)
    {
        if (maxProbes is not null)
        {
            var endpoints = new Dictionary<int, PackageVersionAddress>
            {
                [vector.Addresses[0].Position] = vector.Addresses[0],
                [vector.Addresses[^1].Position] = vector.Addresses[^1],
            };
            addresses = [.. endpoints.Values.OrderBy(address => address.Position)];
            error = null;
            return true;
        }

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
        IEnumerable<int> evaluatedPositions,
        IReadOnlyList<TimelineChangedGap> changedGaps)
    {
        var evaluated = evaluatedPositions.ToHashSet();
        if (SelectProbePosition(changedGaps) is not int probe)
            return null;

        var address = vector.Addresses[probe];
        string range = $"{vector.PackageId}@{vector.Start.ToNormalizedString()}..{vector.End.ToNormalizedString()}";
        string selections = string.Join(
            " ",
            evaluated
                .Append(probe)
                .Order()
                .Select(position =>
                    $"--at {ShellCommandText.Quote(vector.Addresses[position].Selector)}"));
        return $"Probe {address.Selector} ({address.Version.ToNormalizedString()}): "
            + $"dotnet-inspect timeline --package {ShellCommandText.Quote(range)} "
            + $"--type {ShellCommandText.Quote(typeFullName)} "
            + (memberName is null ? "" : $"--member {ShellCommandText.Quote(memberName)} ")
            + $"--finding {ShellCommandText.Quote(descriptor)} "
            + selections;
    }

    static int? SelectProbePosition(
        IReadOnlyList<TimelineChangedGap> changedIntervals)
    {
        TimelineChangedGap? interval = changedIntervals
            .Where(candidate => candidate.EndPosition - candidate.StartPosition > 1)
            .OrderByDescending(candidate => candidate.EndPosition - candidate.StartPosition)
            .ThenBy(candidate => candidate.StartPosition)
            .FirstOrDefault();
        return interval is null
            ? null
            : interval.StartPosition
                + ((interval.EndPosition - interval.StartPosition) / 2);
    }

    static string? BuildBisectRecommendation(
        PackageVersionVector vector,
        string typeFullName,
        string? memberName,
        string descriptor,
        int probeLimit,
        int evaluatedCount,
        IReadOnlyList<TimelineChangedGap> changedIntervals,
        string replayArguments)
    {
        TimelineChangedGap? unresolved = changedIntervals
            .Where(candidate => candidate.EndPosition - candidate.StartPosition > 1)
            .OrderByDescending(candidate => candidate.EndPosition - candidate.StartPosition)
            .ThenBy(candidate => candidate.StartPosition)
            .FirstOrDefault();
        if (unresolved is not null)
        {
            string start = vector.Addresses[unresolved.StartPosition]
                .Version.ToNormalizedString();
            string end = vector.Addresses[unresolved.EndPosition]
                .Version.ToNormalizedString();
            return $"Bisection stopped after {evaluatedCount} of {probeLimit} probes; "
                + $"the changed interval {start}..{end} is not adjacent. "
                + "Increase --max-probes to continue.";
        }

        TimelineChangedGap? boundary = changedIntervals
            .Where(candidate => candidate.EndPosition - candidate.StartPosition == 1)
            .OrderBy(candidate => candidate.StartPosition)
            .FirstOrDefault();
        if (boundary is null)
            return null;

        string oldVersion = vector.Addresses[boundary.StartPosition]
            .Version.ToNormalizedString();
        string newVersion = vector.Addresses[boundary.EndPosition]
            .Version.ToNormalizedString();
        string range = $"{vector.PackageId}@{oldVersion}..{newVersion}";
        string command = "dotnet-inspect diff "
            + $"--package {ShellCommandText.Quote(range)} "
            + $"-t {ShellCommandText.Quote(typeFullName)} "
            + (memberName is null
                ? ""
                : $"-m {ShellCommandText.Quote(memberName)} ")
            + $"--finding {ShellCommandText.Quote(descriptor)}";
        if (replayArguments.Length > 0)
            command += " " + replayArguments;
        return $"Confirm adjacent boundary {oldVersion}..{newVersion}: {command}";
    }

    static string BuildTimelineReplayArguments(
        string sourceReplayArguments,
        TimelineOptions options)
        => JoinReplayArguments(
            sourceReplayArguments,
            options.Tfm is null
                ? null
                : $"--tfm {ShellCommandText.Quote(options.Tfm)}",
            options.IncludePrerelease ? "--preview" : null,
            options.IncludeAll ? "--all" : null);

    static string BuildDiffReplayArguments(
        string sourceReplayArguments,
        TimelineOptions options)
        => JoinReplayArguments(
            sourceReplayArguments,
            options.Tfm is null
                ? null
                : $"--tfm {ShellCommandText.Quote(options.Tfm)}",
            options.IncludeAll ? "--all" : null);

    static string JoinReplayArguments(params string?[] arguments)
        => string.Join(
            " ",
            arguments.Where(static argument =>
                !string.IsNullOrWhiteSpace(argument)));

    static bool TryValidate(
        TimelineOptions options,
        HashSet<string> selectedSections,
        out PackageVersionRange? range,
        out string? descriptor,
        out string? error)
    {
        range = null;
        descriptor = NormalizeDescriptor(options.Finding);

        if (!PackageVersionRange.TryParse(options.PackageVersionRange, out range, out error))
        {
            error ??= $"Invalid package version range '{options.PackageVersionRange}'. Expected Package@A..B.";
            return false;
        }

        if (options.MaxProbes is not null && options.At.Length > 0)
        {
            error = "--max-probes cannot be combined with --at; use one automatic or manual selection mode.";
            return false;
        }
        if (options.MaxProbes is < 2)
        {
            error = "--max-probes must be at least 2 so both range endpoints can be evaluated.";
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

        if (!options.Count && options.IsTabular && selectedSections.Count != 1)
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

    // Timeline has no fixed-size overview: both sections grow with the version range. Preserve
    // the deliberate bare -S refusal while routing named selection through the shared resolver.
    internal static bool TryResolveSections(
        TimelineOptions options,
        out HashSet<string> sections)
    {
        sections = new(StringComparer.OrdinalIgnoreCase);
        if (options.SelectDefault && (options.Select is null || options.Select.Length == 0))
        {
            CommandError.Write(
                "Bare -S has no fixed sections for timeline. Use -S Evaluations or -S Transitions.");
            return false;
        }

        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            TimelineSections.Catalog.SelectableSectionNames,
            infoSections: [],
            TimelineSections.Catalog.SelectionCategoryMap,
            selectDefault: false);
        if (SelectOutput.WriteUnresolved(selection))
            return false;

        sections = selection.Sections
            ?? new HashSet<string>(
                [EvaluationsSection, TransitionsSection],
                StringComparer.OrdinalIgnoreCase);
        return true;
    }

    internal static int Write(
        TimelineDocumentView view,
        TimelineOptions options,
        HashSet<string> selectedSections)
    {
        if (options.Count)
        {
            var schema = TimelineSections.CreateSchema();
            if (!ProjectionDiagnostics.ValidateProjection(
                    schema, selectedSections, options.Fields, options.Columns))
            {
                return 1;
            }

            if (!TryApplyRowSelection(
                    view,
                    selectedSections,
                    options.RowSelection,
                    out view))
            {
                return 1;
            }

            var writerOptions = OutputFormatter.CreateProjectedWriterOptions(
                options.Columns, options.Fields);
            var projection = new CountProjection();
            if (selectedSections.Contains(EvaluationsSection))
            {
                writerOptions.IncludeSections = [EvaluationsSection];
                projection.Merge(CountProjectionFormatter.Capture(
                    new TimelineEvaluationsView { Rows = view.Evaluations },
                    TimelineViewContext.Default,
                    writerOptions));
            }
            if (selectedSections.Contains(TransitionsSection))
            {
                writerOptions.IncludeSections = [TransitionsSection];
                projection.Merge(CountProjectionFormatter.Capture(
                    new TimelineTransitionsView { Rows = view.Transitions },
                    TimelineViewContext.Default,
                    writerOptions));
            }

            var ordered = new[] { EvaluationsSection, TransitionsSection }
                .Where(selectedSections.Contains)
                .ToArray();
            CountOutput.Write(
                projection,
                ordered.Length > 1 ? ordered : null,
                options.JsonOutput ? OutputFormat.Json
                    : options.Jsonl ? OutputFormat.Jsonl
                    : options.Tsv ? OutputFormat.Tsv
                    : options.Tabular ? OutputFormat.Table
                    : OutputFormat.Markdown,
                options.NoHeader);
            return 0;
        }

        if (!TryApplyRowSelection(
                view,
                selectedSections,
                options.RowSelection,
                out view))
        {
            return 1;
        }

        if (options.JsonOutput)
        {
            if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                return 1;

            Console.WriteLine(JsonSerializer.Serialize(
                view,
                TimelineJsonContext.Default.TimelineDocumentView));
            return 0;
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
                    maxRows: null);
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
                    maxRows: null);
            }
            return 0;
        }

        var writer = new MarkoutWriter(new MarkdownFormatter());
        TimelineViewContext.Default.Serialize(view, writer);
        Console.WriteLine(writer.ToString().TrimEnd());
        return 0;
    }

    private static bool TryApplyRowSelection(
        TimelineDocumentView view,
        HashSet<string> selectedSections,
        RowSelectionIntent<string>? rowSelection,
        out TimelineDocumentView selectedView)
    {
        selectedView = view;
        if (rowSelection is not { Operations.Count: > 0 })
            return true;

        IReadOnlyList<TimelineEvaluationRow>? evaluations = view.Evaluations;
        if (selectedSections.Contains(EvaluationsSection)
            && !CliSemanticRowSelection.TrySelect(
                rowSelection,
                view.Evaluations ?? [],
                EvaluationsSection,
                FormatRowSelectionFailure,
                out evaluations))
        {
            return false;
        }

        IReadOnlyList<TimelineTransitionRow>? transitions = view.Transitions;
        if (selectedSections.Contains(TransitionsSection)
            && !CliSemanticRowSelection.TrySelect(
                rowSelection,
                view.Transitions ?? [],
                TransitionsSection,
                FormatRowSelectionFailure,
                out transitions))
        {
            return false;
        }

        selectedView = new TimelineDocumentView
        {
            Title = view.Title,
            Range = view.Range,
            Type = view.Type,
            Member = view.Member,
            Finding = view.Finding,
            Recommendation = view.Recommendation,
            Evaluations = evaluations is null ? null : [.. evaluations],
            Transitions = transitions is null ? null : [.. transitions],
        };
        return true;
    }

    private static string FormatRowSelectionFailure(
        RowsCohortSemanticFailure<string> failure) =>
        $"Timeline row selection stage {failure.Failure.StageNumber} "
        + $"for '{failure.Identity}' requires row "
        + $"{failure.Failure.RequiredPosition}, but only "
        + $"{failure.Failure.AvailableCount} rows are available.";

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

    sealed record TimelineBuildResult(
        TimelineDocumentView View,
        IReadOnlyList<TimelineChangedGap> ChangedIntervals);

    internal sealed record TimelineChangedGap(
        int StartPosition,
        int EndPosition);
}

public sealed record TimelineOptions : IProjectionOptions
{
    public string PackageVersionRange { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? MemberName { get; init; }
    public string Finding { get; init; } = MetadataFindings.MemberDescriptor.Id;
    public string[] At { get; init; } = [];
    public int? MaxProbes { get; init; }
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
    public RowSelectionIntent<string>? RowSelection { get; init; }
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
