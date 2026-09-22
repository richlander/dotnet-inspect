using System.Reflection;
using System.Reflection.Emit;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextCallCensusQueryTests
{
    static string IndirectPath =>
        FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath();
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    [Fact]
    public async Task ExecutePublishesOneCanonicalMultiParticipantCensus()
    {
        await using CensusContext forward =
            CensusContext.Create(
                IndirectPath,
                CallerPath,
                TargetPath);
        await using CensusContext reverse =
            CensusContext.Create(
                TargetPath,
                CallerPath,
                IndirectPath);

        AssemblyContextCallCensusResult first =
            AssemblyContextCallCensusQuery.Execute(
                forward.Group,
                TestContext.Current.CancellationToken);
        AssemblyContextCallCensusResult second =
            AssemblyContextCallCensusQuery.Execute(
                reverse.Group,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            first.Subjects.Select(subject => subject.Identity.Name),
            second.Subjects.Select(subject => subject.Identity.Name));
        Assert.Equal(
            first.Participants.Select(
                participant => participant.Subject.Identity.Name),
            second.Participants.Select(
                participant => participant.Subject.Identity.Name));
        Assert.Equal(
            first.Members.Select(MemberFingerprint),
            second.Members.Select(MemberFingerprint));
        Assert.Equal(
            first.Occurrences.Select(OccurrenceFingerprint),
            second.Occurrences.Select(OccurrenceFingerprint));
        Assert.Equal(3, first.Participants.Length);
        Assert.Equal(
            first.Participants.Length,
            first.Receipt!.ParticipantCount);
        Assert.Equal(first.Members.Length, first.Receipt.MemberCount);
        Assert.Equal(
            first.Occurrences.Length,
            first.Receipt.OccurrenceCount);
        Assert.Equal(
            first.UnresolvedOccurrences.Length,
            first.Receipt.UnresolvedOccurrenceCount);
        Assert.Equal(
            first.VersionSkewedBindings.Length,
            first.Receipt.VersionSkewedBindingCount);
        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Source.Identity.Name.Contains(
                    "IndirectCaller",
                    StringComparison.Ordinal)
                && occurrence.TargetMethod.Name == "Run"
                && occurrence.Target.Identity.Name.Contains(
                    "Caller",
                    StringComparison.Ordinal));
        Assert.Equal(
            2,
            first.Occurrences.Count(occurrence =>
                occurrence.SourceMethod.Name == "RunTwice"
                && occurrence.TargetMethod.Name == "Echo"));
        AssemblyContextCallCensusOccurrence generated = Assert.Single(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "AsyncRoot"
                && occurrence.TargetMethod.Name == "AsyncUse");
        Assert.NotEqual(
            generated.SourceMethod.MetadataToken,
            generated.Call.EvidenceMethod.MetadataToken);
        Assert.Equal(
            generated.Call.EvidenceMethod.MetadataToken,
            generated.OrderingKey.EvidenceMethod.MetadataToken);
    }

    [Fact]
    public async Task ExecuteRetainsPositiveEvidenceWhenOneParticipantFails()
    {
        ResolvedAssemblyReference caller =
            ResolvedAssemblyReference.CreateFromPath(
                CallerPath,
                AssemblyResolutionProvenance.Local(
                    "call-census test"));
        ResolvedAssemblyReference target =
            ResolvedAssemblyReference.CreateFromPath(
                TargetPath,
                AssemblyResolutionProvenance.Local(
                    "call-census test"));
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                target.Identity,
                path: null,
                () => new MemoryStream([0x00, 0x01, 0x02]),
                AssemblyResolutionProvenance.Local(
                    "malformed call-census test"));
        await using CensusContext context =
            CensusContext.Create(caller, malformed);

        AssemblyContextCallCensusResult result =
            AssemblyContextCallCensusQuery.Execute(
                context.Group,
                TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Subjects.Length);
        Assert.Single(result.Participants);
        Assert.IsType<AssemblyContextCallCensusFailure.Rejected>(
            Assert.Single(result.Failures));
        Assert.NotNull(result.Receipt);
        Assert.Contains(
            result.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "RunOuter"
                && occurrence.TargetMethod.Name == "Run");
        Assert.Contains(
            result.UnresolvedOccurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Call.Callee.Name == "Ping");
        Assert.True(result.Diagnostics.IsIncomplete);
    }

    [Fact]
    public async Task ExecuteRejectsDuplicatePhysicalArtifacts()
    {
        await using CensusContext context =
            CensusContext.Create(TargetPath, TargetPath);

        AssemblyContextCallCensusRequestException exception =
            Assert.Throws<AssemblyContextCallCensusRequestException>(
                () => AssemblyContextCallCensusQuery.Execute(
                    context.Group,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "distinct physical assembly artifacts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutePreservesVersionSkewEvidenceAcrossInputOrder()
    {
        await using CensusContext forward =
            CensusContext.Create(
                CallerPath,
                TargetPath,
                TargetV2Path);
        await using CensusContext reverse =
            CensusContext.Create(
                TargetV2Path,
                TargetPath,
                CallerPath);

        AssemblyContextCallCensusResult first =
            AssemblyContextCallCensusQuery.Execute(
                forward.Group,
                TestContext.Current.CancellationToken);
        AssemblyContextCallCensusResult second =
            AssemblyContextCallCensusQuery.Execute(
                reverse.Group,
                TestContext.Current.CancellationToken);

        Assert.NotEmpty(first.VersionSkewedBindings);
        Assert.Equal(
            first.VersionSkewedBindings.Select(
                VersionSkewFingerprint),
            second.VersionSkewedBindings.Select(
                VersionSkewFingerprint));
        Assert.Equal(
            first.VersionSkewedBindings.Length,
            first.Diagnostics.VersionSkewedBindingCount);
        Assert.Contains(
            first.Occurrences,
            occurrence =>
                occurrence.SourceMethod.Name == "Run"
                && occurrence.Target.Identity.Version
                    == new Version(1, 0, 0, 0));
    }

    [Fact]
    public async Task ExecutePreservesExactParticipantPolicySelection()
    {
        (string directory, ResolvedAssemblyReference caller,
            ResolvedAssemblyReference selected,
            ResolvedAssemblyReference shadow) =
                BuildSameIdentityCallFixture();
        try
        {
            var policy = new SelectedAssemblyPolicy(
                [caller, selected, shadow],
                selected);
            await using CensusContext context =
                CensusContext.Create(
                    policy,
                    caller,
                    selected,
                    shadow);

            AssemblyContextCallCensusResult result =
                AssemblyContextCallCensusQuery.Execute(
                    context.Group,
                    TestContext.Current.CancellationToken);

            AssemblyContextCallCensusOccurrence occurrence =
                Assert.Single(
                    result.Occurrences,
                    occurrence =>
                        occurrence.SourceMethod.Name == "Run"
                        && occurrence.TargetMethod.Name == "Ping");
            Assert.Same(
                selected.Registration,
                occurrence.Target.Registration);
            Assert.DoesNotContain(
                result.UnresolvedOccurrences,
                occurrence =>
                    occurrence.SourceMethod.Name == "Run"
                    && occurrence.Call.Callee.Name == "Ping");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string MemberFingerprint(
        AssemblyContextCallCensusMember member) =>
        string.Join(
            "|",
            member.OrderingKey.Assembly.Name,
            member.OrderingKey.ModuleVersionId,
            member.OrderingKey.MetadataToken,
            member.HasBody);

    static string OccurrenceFingerprint(
        AssemblyContextCallCensusOccurrence occurrence) =>
        string.Join(
            "|",
            occurrence.SourceOrderingKey.Assembly.Name,
            occurrence.SourceOrderingKey.MetadataToken,
            occurrence.TargetOrderingKey.Assembly.Name,
            occurrence.TargetOrderingKey.MetadataToken,
            occurrence.OrderingKey.EvidenceMethod.MetadataToken,
            occurrence.OrderingKey.ILOffset,
            occurrence.OrderingKey.OperandToken,
            occurrence.OrderingKey.Kind);

    static string VersionSkewFingerprint(
        ILInspector.Analysis.CatalogCallCensusVersionSkewEvidence
            evidence) =>
        string.Join(
            "|",
            evidence.CallSite.Storage.ModuleVersionId,
            evidence.CallSite.Storage.MethodToken,
            evidence.CallSite.Storage.ILOffset,
            evidence.Requested,
            evidence.Selected,
            string.Join(",", evidence.AdmittedAlternatives));

    static (string Directory, ResolvedAssemblyReference Caller,
        ResolvedAssemblyReference Selected,
        ResolvedAssemblyReference Shadow)
        BuildSameIdentityCallFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-call-census-query-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targetName = new AssemblyName("CallCensusQueryTarget")
        {
            Version = new Version(1, 0, 0, 0),
        };
        string selectedPath =
            Path.Combine(directory, "selected.dll");
        MethodBuilder selectedMethod =
            BuildTarget(targetName, selectedPath);
        string shadowPath =
            Path.Combine(directory, "shadow.dll");
        _ = BuildTarget(targetName, shadowPath);

        var callerName = new AssemblyName("CallCensusQueryCaller")
        {
            Version = new Version(1, 0, 0, 0),
        };
        var callerAssembly = new PersistedAssemblyBuilder(
            callerName,
            typeof(object).Assembly);
        ModuleBuilder callerModule =
            callerAssembly.DefineDynamicModule(callerName.Name!);
        TypeBuilder callerType = callerModule.DefineType(
            "Consumer.Entry",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed);
        MethodBuilder run = callerType.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = run.GetILGenerator();
        il.Emit(OpCodes.Call, selectedMethod);
        il.Emit(OpCodes.Ret);
        _ = callerType.CreateType();
        string callerPath =
            Path.Combine(directory, "caller.dll");
        callerAssembly.Save(callerPath);
        return (
            directory,
            Reference(callerPath),
            Reference(selectedPath),
            Reference(shadowPath));

        static MethodBuilder BuildTarget(
            AssemblyName assemblyName,
            string path)
        {
            var assembly = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            ModuleBuilder module =
                assembly.DefineDynamicModule(assemblyName.Name!);
            TypeBuilder type = module.DefineType(
                "Target.Api",
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);
            MethodBuilder ping = type.DefineMethod(
                "Ping",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            ping.GetILGenerator().Emit(OpCodes.Ret);
            _ = type.CreateType();
            assembly.Save(path);
            return ping;
        }

        static ResolvedAssemblyReference Reference(string path) =>
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "call-census query policy test"));
    }

    sealed class SelectedAssemblyPolicy(
        IReadOnlyList<ResolvedAssemblyReference> assemblies,
        ResolvedAssemblyReference selected)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            if (request.Target
                    is not AssemblyBindingTarget.AssemblyReference reference)
            {
                return Unavailable();
            }

            ResolvedAssemblyReference? match =
                assemblies.FirstOrDefault(assembly =>
                    assembly.Identity.IsEquivalentTo(
                        reference.Identity));
            if (match is null)
                return Unavailable();

            return new(
                Version,
                AssemblyBindingSelection.Found(
                    selected.Identity.IsEquivalentTo(
                        reference.Identity)
                            ? selected
                            : match));
        }

        AssemblyBindingSelectionSnapshot Unavailable() =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }

    sealed class CensusContext : IAsyncDisposable
    {
        CensusContext(
            InspectionWorkspace workspace,
            AssemblyContextGroup group)
        {
            Workspace = workspace;
            Group = group;
        }

        internal InspectionWorkspace Workspace { get; }
        internal AssemblyContextGroup Group { get; }

        internal static CensusContext Create(params string[] paths) =>
            Create(
                paths.Select(path =>
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "call-census test")))
                    .ToArray());

        internal static CensusContext Create(
            params ResolvedAssemblyReference[] assemblies)
        {
            var policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    assemblies.Select(assembly => (
                        assembly,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(
                                    assembly.Path
                                        ?? CallerPath)
                                {
                                    PreferImplementationAssemblies = true,
                                    AllowPlatformAssemblyVersionRollForward =
                                        true,
                                }))));
            return Create(policy, assemblies);
        }

        internal static CensusContext Create(
            IAssemblyBindingPolicy policy,
            params ResolvedAssemblyReference[] assemblies)
        {
            var workspace = new InspectionWorkspace();
            AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    assemblies.Select(assembly =>
                        new AssemblyContextParticipant(
                            assembly,
                            policy)));
            return new(workspace, group);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
