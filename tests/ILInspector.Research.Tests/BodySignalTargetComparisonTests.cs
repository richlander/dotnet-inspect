using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace ILInspector.Research.Tests;

/// <summary>
/// Gates for the body-signal target path: Research resolves body-signal
/// targets through the admitted descriptor, resolver, and Analysis method
/// population, and selects compared methods only from correspondence.
/// </summary>
public sealed class BodySignalTargetComparisonTests
{
    const string DiffSampleType = "DiffFixtureSample.DiffSample";

    static readonly FindingDescriptor[] AllRetained =
    [
        AnalysisFindings.AllocationDescriptor,
        AnalysisFindings.CallSiteDescriptor,
        AnalysisFindings.UnsafetyDescriptor,
    ];

    [Fact]
    public void ResearchTargetResolver_ResolvesBodySignalProfileThroughAdmittedEvidence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        int opens = 0;
        BodySignalComparisonInputOccurrence before = Occurrence(
            oldPath,
            onOpen: () => opens++);
        BodySignalComparisonInputOccurrence after = Occurrence(
            newPath,
            onOpen: () => opens++);
        ResearchAdmittedPopulation population = Admit([before], [after]);

        ResearchTargetResolution resolution = Resolve(
            population,
            DiffSampleType,
            "RegressesAllocInLoop");

        // Each admitted descriptor is opened exactly once, for resolution.
        Assert.Equal(2, opens);
        Assert.Equal(2, resolution.Attempts.Length);
        foreach (ResearchTargetAttempt attempt in resolution.Attempts)
        {
            var occurrence = Assert.IsType<BodySignalComparisonInputOccurrence>(
                population.GetInput(attempt.Request.Input).Occurrence);
            var resolved = Assert.IsType<ResearchTargetOutcome.Resolved>(
                attempt.Outcome);
            // The module and body identity come from the admitted Analysis
            // method population of that exact occurrence.
            Assert.Same(
                occurrence.Analysis.MethodPopulation.ModuleIdentity,
                resolved.Module);
            Assert.Same(occurrence.MethodPopulation, occurrence.Analysis.MethodPopulation);
            Assert.NotNull(resolved.BodyIdentity);
            MetadataMethodAddressAssert(resolved, occurrence);
        }
        Assert.IsType<ResearchTargetCorrespondenceOutcome.Paired>(
            Assert.Single(resolution.Correspondences));

        // The admitted descriptor and Analysis evidence are validated against
        // each other: Analysis from a different image is a typed failure.
        ResearchAdmittedPopulation mismatched = Admit(
            [Occurrence(oldPath, analysis: Analyze(newPath))],
            []);
        ResearchTargetAttempt failedAttempt = Assert.Single(
            Resolve(mismatched, DiffSampleType, "RegressesAllocInLoop").Attempts);
        var failed = Assert.IsType<ResearchTargetOutcome.Failed>(
            failedAttempt.Outcome);
        Assert.Contains(
            failed.Diagnostic.Kind,
            new[]
            {
                ResearchTargetDiagnosticKind.AssemblyIdentityMismatch,
                ResearchTargetDiagnosticKind.ModuleIdentityMismatch,
            });
    }

    [Fact]
    public void BodySignalComparison_SelectsMembersOnlyFromCorrespondence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();
        ResearchAdmittedPopulation population = Admit(
            [Occurrence(oldPath)],
            [Occurrence(newPath)]);
        ResearchTargetResolution resolution = Resolve(
            population,
            DiffSampleType,
            "RegressesAllocInLoop");
        var paired = Assert.IsType<ResearchTargetCorrespondenceOutcome.Paired>(
            Assert.Single(resolution.Correspondences));

        var compared = Assert.IsType<BodySignalTargetComparisonOutcome.Compared>(
            BodySignalTargetComparison.Compare(population, resolution, AllRetained));

        // Every row and every retained comparison belongs to the one subject
        // the Paired outcome selected by resolved address.
        Assert.Same(paired, Assert.Single(compared.Correspondences));
        Assert.NotEmpty(compared.Comparison.Changes);
        ResearchSubjectKey subject = Assert.Single(
            compared.Comparison.Changes
                .Select(change => change.Subject)
                .Concat(compared.Comparison.RetainedComparisons.Items
                    .Select(retained => retained.Subject))
                .Distinct());
        Assert.Contains("RegressesAllocInLoop", subject.Display, StringComparison.Ordinal);
        Assert.Equal(
            AllRetained.Select(descriptor => descriptor.Id).Order(StringComparer.Ordinal),
            compared.Comparison.RetainedComparisons.Items
                .Select(retained => retained.Descriptor.Id)
                .Order(StringComparer.Ordinal));

        // The selected endpoint methods produce exactly the rows the
        // untargeted whole-assembly census produces for the same subject.
        ResearchComparison census = ResearchDiff.CompareAssemblies(
            oldPath,
            newPath,
            new ResearchDiffOptions(ResearchChangeMechanism.BodySignals)
            {
                RetainedComparisonDescriptorIds =
                    [.. AllRetained.Select(descriptor => descriptor.Id)],
            });
        Assert.Equal(
            RowShapes(census.Changes.Where(change => change.Subject.Id == subject.Id)),
            RowShapes(compared.Comparison.Changes));

        // A one-sided outcome compares its present endpoint against a proven
        // SubjectAbsent inspection instead of collapsing to an empty census.
        ResearchTargetResolution removed = Resolve(
            population,
            "DiffFixtureSample.MethodRemovalSample",
            "Removed:1");
        Assert.IsType<ResearchTargetCorrespondenceOutcome.BeforeOnly>(
            Assert.Single(removed.Correspondences));
        var oneSided = Assert.IsType<BodySignalTargetComparisonOutcome.Compared>(
            BodySignalTargetComparison.Compare(
                population,
                removed,
                [AnalysisFindings.CallSiteDescriptor]));
        RetainedFindingComparison<DirectCall> calls = Assert.Single(
            oneSided.Comparison.RetainedComparisons.Get<DirectCall>(
                AnalysisFindings.CallSiteDescriptor));
        var complete = Assert.IsType<FindingComparison<DirectCall>.Complete>(
            calls.Comparison.Value);
        Assert.False(complete.Transition.IsSameTopology);
        Assert.Equal(FindingInspectionState.SubjectAbsent, complete.Transition.New);

        // An unsafe body change on the selected method keeps its unsafe row.
        ResearchTargetResolution unsafeTarget = Resolve(
            population,
            DiffSampleType,
            "AddsUnsafe");
        var unsafeCompared = Assert.IsType<BodySignalTargetComparisonOutcome.Compared>(
            BodySignalTargetComparison.Compare(population, unsafeTarget, []));
        ResearchChange stackallocRow = Assert.Single(
            unsafeCompared.Comparison.Changes,
            change => change.Descriptor.Id == "unsafe.stackalloc.added");
        Assert.Contains("AddsUnsafe", stackallocRow.Subject.Display, StringComparison.Ordinal);
        Assert.All(
            unsafeCompared.Comparison.Changes,
            change => Assert.Equal(stackallocRow.Subject, change.Subject));
    }

    [Fact]
    public void BodySignalComparison_ResolvedTargetWithoutAnalysisMethodIsTypedFailure()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        // The After Analysis population carries the exact module identity of
        // its image but declares no methods, so the resolved target selects no
        // Analysis method on that side.
        ResearchAdmittedPopulation population = Admit(
            [Occurrence(oldPath)],
            [Occurrence(newPath, analysis: EmptyAnalysis(newPath))]);
        ResearchTargetResolution resolution = Resolve(
            population,
            DiffSampleType,
            "RegressesAllocInLoop");
        Assert.All(
            resolution.Attempts,
            attempt => Assert.IsType<ResearchTargetOutcome.Resolved>(attempt.Outcome));

        var failed = Assert.IsType<BodySignalTargetComparisonOutcome.Failed>(
            BodySignalTargetComparison.Compare(population, resolution, AllRetained));

        Assert.Equal(resolution.Correspondences, failed.Correspondences);
        BodySignalTargetFailure missing = Assert.Single(
            failed.Failures,
            failure => failure.Kind
                == BodySignalTargetFailureKind.EndpointMethodNotInAnalysis);
        Assert.NotNull(missing.Attempt);
        Assert.Equal(ResearchComparisonSide.After, missing.Attempt.Request.Side);
        Assert.Contains("RegressesAllocInLoop", missing.Summary, StringComparison.Ordinal);
        Assert.All(
            failed.Failures,
            failure => Assert.True(
                Enum.IsDefined(failure.Kind),
                failure.Kind.ToString()));
        Assert.Null(
            typeof(BodySignalTargetComparisonOutcome.Failed).GetProperty(
                nameof(BodySignalTargetComparisonOutcome.Compared.Comparison)));
    }

    [Fact]
    public void ResearchBodySignalTargetPath_HasNoStringKeyedIdentityBag()
    {
        Type[] types =
        [
            typeof(BodySignalComparisonInputOccurrence),
            typeof(BodySignalTargetComparison),
            typeof(BodySignalTargetComparisonOutcome),
            .. typeof(BodySignalTargetComparisonOutcome)
                .GetNestedTypes(BindingFlags.Public),
            typeof(BodySignalTargetFailure),
            typeof(SelectedBodySignalEndpoint),
            typeof(SelectedBodySignalPair),
            typeof(BodySignalEndpointTopology),
            typeof(ResearchTargetEvidence),
        ];
        MethodInfo[] methods =
        [
            .. typeof(BodySignalTargetComparison).GetMethods(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly),
            typeof(ResearchDiff).GetMethod(
                nameof(ResearchDiff.CompareSelectedBodySignals),
                BindingFlags.NonPublic | BindingFlags.Static)!,
        ];
        Assert.Contains(
            methods,
            method => method.Name == nameof(BodySignalTargetComparison.Compare));

        IEnumerable<Type> signatureTypes =
            types.SelectMany(type =>
                    type.GetFields(
                            BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.Instance
                                | BindingFlags.Static)
                        .Select(field => field.FieldType)
                        .Concat(type.GetProperties(
                                BindingFlags.Public
                                    | BindingFlags.NonPublic
                                    | BindingFlags.Instance)
                            .Select(property => property.PropertyType))
                        .Concat(type.GetConstructors(
                                BindingFlags.Public
                                    | BindingFlags.NonPublic
                                    | BindingFlags.Instance)
                            .SelectMany(constructor => constructor.GetParameters())
                            .Select(parameter => parameter.ParameterType)))
                .Concat(methods.SelectMany(method =>
                    method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType)))
                .SelectMany(ComponentTypes);

        Assert.DoesNotContain(signatureTypes, IsStringKeyedIdentityBag);

        // The gate is not vacuous: it reports the retired string identity bag.
        Assert.True(IsStringKeyedIdentityBag(typeof(IReadOnlySet<string>)));
        Assert.True(IsStringKeyedIdentityBag(typeof(ImmutableHashSet<string>)));
        Assert.True(IsStringKeyedIdentityBag(
            typeof(Dictionary<string, ResearchSubjectKey>)));

        static bool IsStringKeyedIdentityBag(Type type)
        {
            if (!type.IsGenericType
                || type.GetGenericArguments()[0] != typeof(string))
            {
                return false;
            }

            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(IReadOnlySet<>)
                || definition == typeof(ISet<>)
                || definition == typeof(HashSet<>)
                || definition == typeof(ImmutableHashSet<>)
                || definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)
                || definition == typeof(ImmutableDictionary<,>)
                || type.GetInterfaces().Any(candidate =>
                    candidate.IsGenericType
                    && (candidate.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)
                        || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
                    && candidate.GetGenericArguments()[0] == typeof(string));
        }

        static IEnumerable<Type> ComponentTypes(Type type)
        {
            yield return type;
            if (type.HasElementType)
            {
                foreach (Type component in ComponentTypes(type.GetElementType()!))
                    yield return component;
            }
            if (!type.IsGenericType)
                yield break;
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type component in ComponentTypes(argument))
                    yield return component;
            }
        }
    }

    [Fact]
    public void ResearchDiff_BodySignalsRejectStringMemberTargets()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ResearchDiff.Compare(
                new ResearchDiffInput([]) { BodySignalAnalyses = [] },
                new ResearchDiffInput([]) { BodySignalAnalyses = [] },
                new ResearchDiffOptions(
                    ResearchChangeMechanism.BodySignals,
                    MemberTargetIdentities:
                        new HashSet<string>(StringComparer.Ordinal) { "member" })));
        Assert.Contains(nameof(BodySignalTargetComparison), exception.Message, StringComparison.Ordinal);
    }

    static void MetadataMethodAddressAssert(
        ResearchTargetOutcome.Resolved resolved,
        BodySignalComparisonInputOccurrence occurrence)
    {
        Assert.NotNull(resolved.Address);
        Assert.Equal(
            occurrence.MethodPopulation.ModuleIdentity.ModuleVersionId,
            resolved.Address!.Value.ModuleVersionId);
        Assert.Contains(
            occurrence.MethodPopulation.DeclaredMethods,
            method => method.MetadataToken == resolved.Address.Value.Token);
    }

    static string[] RowShapes(IEnumerable<ResearchChange> changes)
        =>
        [
            .. changes
                .Select(change =>
                    $"{change.Descriptor.Id}|{change.Kind}|{change.OldValue}|"
                    + $"{change.NewValue}|{change.Delta}|{change.Shape}")
                .Order(StringComparer.Ordinal),
        ];

    static ResearchTargetResolution Resolve(
        ResearchAdmittedPopulation population,
        string declaringType,
        string selector)
    {
        var request = new ResearchTargetPlanningRequest(
            population,
            population.Inputs.Select(input =>
                new ResearchTargetInputRoleAssignment(
                    input,
                    ResearchTargetInputRole.Implementation)),
            [
                new ResearchCarriedMemberSelection(
                    population.Questions[0].Id,
                    Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.ParseSerialized(declaringType)).Name,
                    MemberTargetSelector.Parse(selector)),
            ]);
        return Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
            ResearchTargetResolver.Resolve(
                request,
                TestContext.Current.CancellationToken)).Resolution;
    }

    static ResearchAdmittedPopulation Admit(
        ResearchComparisonInputOccurrence[] before,
        ResearchComparisonInputOccurrence[] after)
        => Assert.IsType<ResearchAdmissionOutcome.Admitted>(
            ResearchComparisonAdmission.Admit(
                new ResearchComparisonAdmissionRequest(
                    ResearchComparisonProfile.BodySignal,
                    [new ResearchComparisonAdmissionQuestion(before, after)])))
            .Population;

    static BodySignalComparisonInputOccurrence Occurrence(
        string path,
        BodySignalAnalysisInput? analysis = null,
        Action? onOpen = null)
        => new(
            ResolvedAssemblyReference.Create(
                ReadIdentity(path).Identity,
                path,
                () =>
                {
                    onOpen?.Invoke();
                    return File.OpenRead(path);
                },
                AssemblyResolutionProvenance.Local(
                    "body-signal target comparison test")),
            DecompilerMetadataSource.DefaultAssemblyReferenceResolver(path),
            analysis ?? Analyze(path));

    static BodySignalAnalysisInput Analyze(string path)
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence
                        | LibraryBodyAnalysisFeatures.Allocations
                        | LibraryBodyAnalysisFeatures.OptimizationOpportunities));
        return new(
            execution.Allocations,
            execution.Safety,
            execution.CallGraph,
            execution.Optimization);
    }

    static BodySignalAnalysisInput EmptyAnalysis(string path)
    {
        (AssemblyReferenceIdentity identity, Guid mvid) = ReadIdentity(path);
        return BodySignalAnalysisTestInput.FromIndex(
            LibraryBodyIndex.FromEvidence(
                [],
                [],
                moduleIdentity: new(identity, mvid)));
    }

    static (AssemblyReferenceIdentity Identity, Guid Mvid) ReadIdentity(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        MetadataReader reader = pe.GetMetadataReader();
        return (
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
            reader.GetGuid(reader.GetModuleDefinition().Mvid));
    }
}
