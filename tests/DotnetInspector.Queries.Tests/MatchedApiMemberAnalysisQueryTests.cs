using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using Inspector.Artifacts;
using Inspector.Findings;
using ILInspector.Analysis;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class MatchedApiMemberAnalysisQueryTests
{
    static CancellationToken Cancellation =>
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReorderedMethodDefsAnalyzeExactImplementationTarget()
    {
        await using Scenario scenario =
            await Scenario.CreateRefLib("Widget", "Allocate");

        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.MethodExact,
            result.Resolution.Status);
        Assert.Equal(
            MethodCorrespondenceStatus.Exact,
            result.Resolution.MethodCorrespondenceStatus);
        Assert.NotEqual(
            result.Resolution.SurfaceMethodToken,
            result.Resolution.ImplementationMethodToken);
        FindingInspection<AllocationOccurrence>.Complete inspection =
            Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
                result.Inspection.Value);
        Finding<AllocationOccurrence> finding =
            Assert.Single(inspection.Findings);
        Assert.Equal(
            AnalysisFindings.AllocationDescriptor,
            finding.Descriptor);
        Assert.Contains(
            "Helper",
            finding.Key.IdentityKey,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedCompileFallbackUsesExactApiMethod()
    {
        await using Scenario scenario =
            await Scenario.CreateLibOnly("Widget", "Transform");

        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.SameImageExact,
            result.Resolution.Status);
        Assert.Null(result.Resolution.MethodCorrespondenceStatus);
        Assert.Equal(
            result.Resolution.SurfaceMethodToken,
            result.Resolution.ImplementationMethodToken);
        var inspection =
            Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
                result.Inspection.Value);
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public async Task NonMethodAndBodylessTargetsAreNoApplicableInput()
    {
        await using Scenario property =
            await Scenario.CreateRefLib("Helper", "Value");
        MatchedApiMemberAnalysisResult<AllocationOccurrence> propertyResult =
            await property.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.NoApplicableDeclaration,
            propertyResult.Resolution.Status);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            Assert.IsType<FindingInspection<AllocationOccurrence>.Absent>(
                propertyResult.Inspection.Value).Kind);

        await using Scenario bodyless =
            await Scenario.CreateRefLib("Bodyless", "Run");
        MatchedApiMemberAnalysisResult<AllocationOccurrence> bodylessResult =
            await bodyless.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.Bodyless,
            bodylessResult.Resolution.Status);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            Assert.IsType<FindingInspection<AllocationOccurrence>.Absent>(
                bodylessResult.Inspection.Value).Kind);
    }

    [Fact]
    public async Task ReferenceOnlySurfaceFailsWithoutEmptyCensus()
    {
        await using Scenario scenario =
            await Scenario.CreateRefOnly("Widget", "Transform");

        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus
                .ImplementationParticipantUnavailable,
            result.Resolution.Status);
        Assert.IsType<FindingInspection<AllocationOccurrence>.Failed>(
            result.Inspection.Value);
    }

    [Fact]
    public async Task NativeFindingProducersPreserveKeysAndEmptyCensuses()
    {
        await using Scenario allocations =
            await Scenario.CreateRefLib("Widget", "Allocate");
        Finding<AllocationOccurrence> allocation =
            Assert.Single(
                Assert.IsType<
                    FindingInspection<AllocationOccurrence>.Complete>(
                    (await allocations.Inspect(
                        MatchedApiMemberAnalysisQuery.InspectAllocations))
                    .Inspection.Value).Findings);
        Assert.Equal(
            AnalysisFindings.AllocationDescriptor,
            allocation.Descriptor);

        await using Scenario calls =
            await Scenario.CreateRefLib("Widget", "CallHelper");
        Finding<DirectCall> call =
            Assert.Single(
                Assert.IsType<FindingInspection<DirectCall>.Complete>(
                    (await calls.Inspect(
                        MatchedApiMemberAnalysisQuery.InspectCallSites))
                    .Inspection.Value).Findings);
        Assert.Equal(
            AnalysisFindings.CallSiteDescriptor,
            call.Descriptor);
        Assert.Contains(
            "Read",
            call.Key.IdentityKey,
            StringComparison.Ordinal);

        await using Scenario unsafety =
            await Scenario.CreateRefLib("Widget", "Invoke");
        var unsafeInspection =
            Assert.IsType<FindingInspection<UnsafetyOccurrence>.Complete>(
                (await unsafety.Inspect(
                    MatchedApiMemberAnalysisQuery.InspectUnsafety))
                .Inspection.Value);
        Assert.NotEmpty(unsafeInspection.Findings);
        Assert.All(
            unsafeInspection.Findings,
            finding => Assert.Equal(
                AnalysisFindings.UnsafetyDescriptor,
                finding.Descriptor));

        await using Scenario empty =
            await Scenario.CreateRefLib("Widget", "Transform");
        Assert.Empty(
            Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
                (await empty.Inspect(
                    MatchedApiMemberAnalysisQuery.InspectAllocations))
                .Inspection.Value).Findings);
    }

    [Fact]
    public async Task MethodCorrespondenceNonSuccessRemainsFailed()
    {
        await using Scenario scenario =
            await Scenario.CreateRefLib("KindShape", "TransformKind");

        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.MethodAbsent,
            result.Resolution.Status);
        Assert.Equal(
            MethodCorrespondenceStatus.Absent,
            result.Resolution.MethodCorrespondenceStatus);
        Assert.IsType<FindingInspection<AllocationOccurrence>.Failed>(
            result.Inspection.Value);
    }

    [Theory]
    [InlineData(
        MethodCorrespondenceStatus.Absent,
        MatchedApiMemberBodyResolutionStatus.MethodAbsent)]
    [InlineData(
        MethodCorrespondenceStatus.Ambiguous,
        MatchedApiMemberBodyResolutionStatus.MethodAmbiguous)]
    [InlineData(
        MethodCorrespondenceStatus.Failed,
        MatchedApiMemberBodyResolutionStatus.MethodFailed)]
    public void MethodCorrespondenceStatusProjectionPreservesNativeNonSuccess(
        MethodCorrespondenceStatus status,
        MatchedApiMemberBodyResolutionStatus expected)
    {
        Assert.Equal(
            expected,
            MatchedApiMemberAnalysisQuery.NonExactStatus(status));
    }

    [Fact]
    public async Task AvaloniaRefLibMatchUsesImplementationRole()
    {
        await using Scenario scenario =
            await Scenario.CreateAvalonia();

        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.MethodExact,
            result.Resolution.Status);
        Assert.Equal(
            MethodCorrespondenceStatus.Exact,
            result.Resolution.MethodCorrespondenceStatus);
        Assert.Equal(
            "ref/net8.0/Avalonia.Base.dll",
            result.Resolution.SurfaceAssetPath);
        Assert.Equal(
            "lib/net8.0/Avalonia.Base.dll",
            result.Resolution.ImplementationAssetPath);
        Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
            result.Inspection.Value);
    }

    [Fact]
    public async Task ResultRemainsUsableAfterWorkspaceClose()
    {
        Scenario scenario =
            await Scenario.CreateRefLib("Widget", "Allocate");
        MatchedApiMemberAnalysisResult<AllocationOccurrence> result =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        await scenario.DisposeAsync();

        Assert.Equal(
            MatchedApiMemberBodyResolutionStatus.MethodExact,
            result.Resolution.Status);
        Assert.Single(
            Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
                result.Inspection.Value).Findings);
        string json = JsonSerializer.Serialize(result);
        Assert.Contains(
            AnalysisFindings.AllocationDescriptor.Id,
            json,
            StringComparison.Ordinal);
        Assert.Contains("findings", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicResultClosureIsResourceFree()
    {
        await using Scenario scenario =
            await Scenario.CreateRefLib("Widget", "Allocate");
        MatchedApiMemberAnalysisResult<AllocationOccurrence> instance =
            await scenario.Inspect(
                MatchedApiMemberAnalysisQuery.InspectAllocations);

        Type[] roots =
        [
            typeof(MatchedApiMemberAnalysisResult<AllocationOccurrence>),
            typeof(MatchedApiMemberAnalysisResult<DirectCall>),
            typeof(MatchedApiMemberAnalysisResult<UnsafetyOccurrence>),
            typeof(MatchedApiMemberBodyResolutionEvidence),
            typeof(MatchedApiMemberMethodCandidate),
        ];
        var types = new HashSet<Type>();
        foreach (Type root in roots)
            VisitType(root, types);
        VisitValue(instance, new HashSet<object>(
            ReferenceEqualityComparer.Instance));

        static void VisitType(Type candidate, HashSet<Type> seen)
        {
            candidate = Nullable.GetUnderlyingType(candidate) ?? candidate;
            if (candidate.IsGenericParameter || !seen.Add(candidate))
                return;

            AssertAllowed(candidate);
            if (candidate.IsArray)
            {
                VisitType(candidate.GetElementType()!, seen);
                return;
            }
            if (candidate.IsGenericType)
            {
                foreach (Type argument in candidate.GetGenericArguments())
                    VisitType(argument, seen);
            }
            if (candidate.IsPrimitive
                || candidate.IsEnum
                || candidate == typeof(string)
                || candidate.Namespace?.StartsWith(
                    "System",
                    StringComparison.Ordinal) is true)
            {
                return;
            }

            if (candidate.BaseType is { } baseType
                && baseType != typeof(object))
            {
                VisitType(baseType, seen);
            }
            foreach (Type nested in candidate.GetNestedTypes(
                BindingFlags.Public))
            {
                VisitType(CloseNested(candidate, nested), seen);
            }
            foreach (FieldInfo field in candidate.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(object))
                {
                    Assert.True(
                        IsUnion(candidate),
                        $"{candidate.FullName}.{field.Name} exposes an "
                        + "unbounded object payload.");
                    continue;
                }
                VisitType(field.FieldType, seen);
            }
            foreach (PropertyInfo property in candidate.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly))
            {
                if (property.PropertyType == typeof(object))
                {
                    Assert.True(
                        IsUnion(candidate),
                        $"{candidate.FullName}.{property.Name} exposes an "
                        + "unbounded object payload.");
                    continue;
                }
                VisitType(property.PropertyType, seen);
            }
        }

        static void VisitValue(
            object? value,
            HashSet<object> seen)
        {
            if (value is null)
                return;
            Type type = value.GetType();
            AssertAllowed(type);
            if (type.IsPrimitive
                || type.IsEnum
                || value is string
                || value is Type
                || value is MemberInfo)
            {
                return;
            }
            if (!type.IsValueType && !seen.Add(value))
                return;
            if (value is IEnumerable sequence)
            {
                foreach (object? item in sequence)
                    VisitValue(item, seen);
                return;
            }

            for (Type? current = type;
                current is not null && current != typeof(object);
                current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly))
                {
                    VisitValue(field.GetValue(value), seen);
                }
            }
        }

        static void AssertAllowed(Type type)
        {
            Type[] forbidden =
            [
                typeof(InspectionWorkspace),
                typeof(PackageRootBinding),
                typeof(PackageRootRealization),
                typeof(PackageAssemblyContextRealization),
                typeof(PackageAssemblyRoleParticipant),
                typeof(AssemblyContextGroup),
                typeof(AssemblyContextParticipant),
                typeof(ResolvedAssemblyReference),
                typeof(AssemblyAcquisitionRegistration),
                typeof(IPackageContent),
                typeof(AssemblyImageSnapshot),
                typeof(Stream),
                typeof(Delegate),
                typeof(Exception),
            ];
            Assert.DoesNotContain(
                forbidden,
                blocked => blocked.IsAssignableFrom(type));
            Assert.False(type.IsByRefLike, type.FullName);
            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
        }

        static Type CloseNested(Type owner, Type nested)
        {
            if (!nested.ContainsGenericParameters)
                return nested;
            Type[] arguments = owner.GetGenericArguments();
            return nested.GetGenericTypeDefinition()
                .MakeGenericType(arguments);
        }

        static bool IsUnion(Type type) =>
            type.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.Name == "UnionAttribute");
    }

    delegate MatchedApiMemberAnalysisResult<T> Inspect<T>(
        PackageAssemblyContextRealization realization,
        ApiCoordinateCorrespondenceResult correspondence,
        FindingSubject subject,
        CancellationToken cancellationToken)
        where T : notnull;

    sealed class Scenario : IAsyncDisposable
    {
        readonly InspectionWorkspace _workspace;
        readonly CoordinatePackageObservation _destination;
        readonly FindingSubject _subject;
        readonly IDisposable? _resource;

        Scenario(
            InspectionWorkspace workspace,
            CoordinatePackageObservation destination,
            ApiCoordinateCorrespondenceResult correspondence,
            FindingSubject subject,
            IDisposable? resource)
        {
            _workspace = workspace;
            _destination = destination;
            Correspondence = correspondence;
            _subject = subject;
            _resource = resource;
        }

        public ApiCoordinateCorrespondenceResult Correspondence { get; }

        public static Task<Scenario> CreateRefLib(
            string type,
            string member) =>
            Create(
                type,
                member,
                [
                    ("ref/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceSurface
                            .AssemblyPath()),
                ],
                [
                    ("ref/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceSurface
                            .AssemblyPath()),
                    ("lib/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                            .AssemblyPath()),
                ]);

        public static Task<Scenario> CreateLibOnly(
            string type,
            string member) =>
            Create(
                type,
                member,
                [
                    ("lib/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                            .AssemblyPath()),
                ],
                [
                    ("lib/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                            .AssemblyPath()),
                ]);

        public static Task<Scenario> CreateRefOnly(
            string type,
            string member) =>
            Create(
                type,
                member,
                [
                    ("ref/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceSurface
                            .AssemblyPath()),
                ],
                [
                    ("ref/net11.0/ILInspector.Analysis.MethodCorrespondenceFixture.dll",
                        FixtureCatalog.AnalysisMethodCorrespondenceSurface
                            .AssemblyPath()),
                ]);

        public static async Task<Scenario> CreateAvalonia()
        {
            var packages = new ApiCoordinateMatchTestPackages();
            PackageRootBinding source;
            PackageRootBinding destination;
            try
            {
                source = await packages.BindingAsync(
                    "Avalonia",
                    "11.3.14",
                    "net8.0");
                destination = await packages.BindingAsync(
                    "Avalonia",
                    "12.1.2",
                    "net8.0");
            }
            catch
            {
                packages.Dispose();
                throw;
            }

            return await Create(
                "Avalonia",
                "11.3.14",
                "12.1.2",
                "Avalonia.Data.MultiBinding",
                ".ctor",
                "net8.0",
                source,
                destination,
                packages);
        }

        static async Task<Scenario> Create(
            string type,
            string member,
            (string Entry, string Image)[] sourceAssets,
            (string Entry, string Image)[] destinationAssets)
        {
            PackageRootBinding source =
                CoordinateLibraryPairingQueryTests.Binding(
                    "1.0.0",
                    sourceAssets);
            PackageRootBinding destination =
                CoordinateLibraryPairingQueryTests.Binding(
                    "2.0.0",
                    destinationAssets);
            return await Create(
                "coordinate.sample",
                "1.0.0",
                "2.0.0",
                $"MethodCorrespondenceFixture.{type}",
                member,
                "net11.0",
                source,
                destination,
                resource: null);
        }

        static async Task<Scenario> Create(
            string packageId,
            string sourceVersion,
            string destinationVersion,
            string type,
            string member,
            string framework,
            PackageRootBinding source,
            PackageRootBinding destination,
            IDisposable? resource)
        {
            var workspace = new InspectionWorkspace();
            try
            {
                WorkspaceScopeSnapshot current =
                    Assert.IsType<WorkspaceScopeReadResult.Available>(
                        await workspace.GetScopeSnapshotAsync()).Snapshot;
                WorkspaceScopeSnapshot scope =
                    Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                        await workspace.ReplaceScopeAsync(
                            current.Revision,
                            [source, destination],
                            DateTimeOffset.UtcNow.AddMinutes(1),
                            Cancellation)).Snapshot;
                CoordinatePackageObservation sourceObservation =
                    await Observe(workspace, source, scope);
                CoordinatePackageObservation destinationObservation =
                    await Observe(workspace, destination, scope);
                var request = new ApiCoordinateMatchRequest(
                    packageId,
                    sourceVersion,
                    destinationVersion,
                    type,
                    member,
                    framework);
                ApiCoordinateSourceSelectionResult selection =
                    await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
                        workspace,
                        sourceObservation,
                        request,
                        cancellationToken: Cancellation);
                StructuralSubjectIdentity.MemberSubject selected =
                    Assert.IsType<
                        StructuralSubjectIdentity.MemberSubject>(
                        selection.Subject);
                ApiCoordinateCorrespondenceResult correspondence =
                    await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                        workspace,
                        selected,
                        DeclarationKind(selection.MemberKind),
                        sourceObservation,
                        destinationObservation,
                        Cancellation);
                Assert.Equal(
                    ApiCoordinateCorrespondenceStatus.Exact,
                    correspondence.Status);
                string typeName =
                    selected.Identity.DeclaringType.ToEscapedFullName();
                return new(
                    workspace,
                    destinationObservation,
                    correspondence,
                    new FindingSubject(
                        $"member:{typeName}:{selected.Identity.Member.StableSelector}",
                        $"{typeName}.{selected.Identity.Member.MemberName}"),
                    resource);
            }
            catch
            {
                await workspace.DisposeAsync();
                resource?.Dispose();
                throw;
            }
        }

        public async Task<MatchedApiMemberAnalysisResult<T>> Inspect<T>(
            Inspect<T> inspect)
            where T : notnull
        {
            ArtifactRootResult<MatchedApiMemberAnalysisResult<T>> access =
                await _workspace.ExecutePackageRootQueryAsync(
                    _destination.Correspondence,
                    _destination.Generation,
                    (realization, token) =>
                        ValueTask.FromResult(
                            inspect(
                                realization,
                                Correspondence,
                                _subject,
                                token)),
                    _destination.BindingPolicy,
                    Cancellation);
            return Assert.IsType<
                ArtifactRootResult<
                    MatchedApiMemberAnalysisResult<T>>.Available>(
                    access).Value;
        }

        public async ValueTask DisposeAsync()
        {
            await _workspace.DisposeAsync();
            _resource?.Dispose();
        }

        static async Task<CoordinatePackageObservation> Observe(
            InspectionWorkspace workspace,
            PackageRootBinding binding,
            WorkspaceScopeSnapshot scope) =>
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(
                    workspace,
                    binding,
                    scope.FindPackageOccurrence(binding)!,
                    Cancellation)).Observation;

        static ApiDeclarationKind DeclarationKind(
            MemberTargetKind? kind) =>
            kind switch
            {
                MemberTargetKind.Field => ApiDeclarationKind.Field,
                MemberTargetKind.Property => ApiDeclarationKind.Property,
                MemberTargetKind.Event => ApiDeclarationKind.Event,
                MemberTargetKind.Constructor
                    or MemberTargetKind.Finalizer
                    or MemberTargetKind.Method
                    or MemberTargetKind.Operator
                    or MemberTargetKind.ExplicitInterfaceImplementation
                    or MemberTargetKind.ExtensionMethod =>
                    ApiDeclarationKind.Method,
                _ => throw new InvalidOperationException(
                    "The selected Member has no supported declaration kind."),
            };
    }
}
