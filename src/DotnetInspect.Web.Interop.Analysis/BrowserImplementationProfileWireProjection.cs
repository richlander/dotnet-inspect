using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.Analysis;

internal static class BrowserImplementationProfileWireProjection
{
    const int SchemaVersion = 2;

    internal static BrowserImplementationProfiles Project(
        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>>
                inspection,
        BrowserCompileLibraryAvailability compileLibrary)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(compileLibrary);

        BrowserAnalysisInspectionShare share =
            BrowserAnalysisInspectionProjection.Project(inspection.Share);
        BrowserAnalysisInspectionDiagnostic[] diagnostics =
        [
            .. inspection.Diagnostics.Select(
                BrowserAnalysisInspectionProjection.Project),
        ];
        return inspection.Content switch
        {
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Available
                    available =>
                new(
                    SchemaVersion,
                    "available",
                    Project(available.Subject),
                    Project(available.Value),
                    Failure: null,
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Rejected
                    rejected =>
                new(
                    SchemaVersion,
                    "rejected",
                    Project(rejected.Subject),
                    Content: null,
                    new(
                        rejected.Failure.Kind.ToString(),
                        rejected.Failure.Detail,
                        rejected.Failure.MetadataRootReason?.ToString()),
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Failed failed =>
                new(
                    SchemaVersion,
                    "failed",
                    Project(failed.Subject),
                    Content: null,
                    new(
                        failed.Error.GetType().Name,
                        failed.Error.Message,
                        MetadataRootReason: null),
                    share,
                    diagnostics,
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown implementation-profile inspection outcome."),
        };
    }

    internal static BrowserImplementationProfiles Unavailable(
        string kind,
        string detail,
        BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            SchemaVersion,
            "unavailable",
            Subject: null,
            Content: null,
            new(kind, detail, MetadataRootReason: null),
            Share: null,
            Diagnostics: [],
            compileLibrary);

    static BrowserImplementationProfileContent Project(
        AssemblyImplementationProfileFamilyInspection inspection)
    {
        Dictionary<string, MethodIdentity> methods = CollectMethods(inspection);
        return new(
            [
                .. inspection.Members.Select(Project),
            ],
            [
                .. methods
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => Project(pair.Key, pair.Value)),
            ],
            [
                .. inspection.Profiles.Select(member =>
                    Project(member)),
            ],
            Project(inspection.Coverage),
            [
                .. inspection.OverloadRelationships.Select(
                    static relationship =>
                        new BrowserImplementationProfileRelationship(
                            MethodKey(relationship.Caller),
                            MethodKey(relationship.Callee),
                            MethodKey(relationship.EvidenceMethod),
                            relationship.ILOffset,
                            relationship.Kind.ToString())),
            ],
            [
                .. inspection.GeneratedFrameworkTypes
                    .Select(static type => type.ToQualifiedDisplayString())
                    .Order(StringComparer.Ordinal),
            ],
            [
                .. inspection.Diagnostics.Select(Project),
            ],
            [
                .. inspection.ApiSurfaceInspectionFailures.Select(Project),
            ]);
    }

    static Dictionary<string, MethodIdentity> CollectMethods(
        AssemblyImplementationProfileFamilyInspection inspection)
    {
        var methods = new Dictionary<string, MethodIdentity>(
            StringComparer.Ordinal);

        foreach (AssemblyImplementationProfileMember member
            in inspection.Profiles)
        {
            Add(methods, member.Profile.Method);
            Add(methods, member.Profile.EvidenceMethod);
        }
        foreach (MethodIdentity method in inspection.Coverage.DeclaredMethods)
            Add(methods, method);
        foreach (MethodIdentity method
            in inspection.Coverage.ManagedMethodBodies)
        {
            Add(methods, method);
        }
        foreach (MethodIdentity method
            in inspection.Coverage.ProfiledEvidenceBodies)
        {
            Add(methods, method);
        }
        foreach (ImplementationProfileUnavailableBody body
            in inspection.Coverage.UnavailableBodies)
        {
            if (body.EvidenceMethod is { } method)
                Add(methods, method);
        }
        foreach (OverloadCallRelationship relationship
            in inspection.OverloadRelationships)
        {
            Add(methods, relationship.Caller);
            Add(methods, relationship.Callee);
            Add(methods, relationship.EvidenceMethod);
        }

        return methods;
    }

    static BrowserImplementationProfilePublicMember Project(
        ImplementationProfilePublicMember member) =>
        new(
            member.TypeDefinitionId,
            member.Member,
            member.StableSelector,
            [.. member.BodyTokens]);

    static void Add(
        Dictionary<string, MethodIdentity> methods,
        MethodIdentity method)
    {
        string key = MethodKey(method);
        if (methods.TryGetValue(key, out MethodIdentity? existing)
            && existing != method)
        {
            throw new InvalidOperationException(
                $"Implementation-profile method key '{key}' is ambiguous.");
        }
        methods[key] = method;
    }

    static BrowserImplementationProfileMethod Project(
        string key,
        MethodIdentity method)
    {
        string declaringType = method.DeclaringType.ToQualifiedDisplayString();
        string[] parameterTypes =
        [
            .. method.ParameterTypes.Select(
                static type => type.ToQualifiedDisplayString()),
        ];
        string genericParameters = method.GenericParameterNames.Length == 0
            ? ""
            : $"<{string.Join(", ", method.GenericParameterNames)}>";
        return new(
            key,
            method.AssemblyName,
            method.ModuleVersionId.ToString("D"),
            declaringType,
            method.Name,
            parameterTypes,
            method.ReturnType.ToQualifiedDisplayString(),
            method.MetadataToken,
            method.IsStatic,
            method.IsExtension,
            method.CallerUnsafeMode.ToString(),
            method.GenericArity,
            [.. method.GenericParameterNames],
            $"{declaringType}.{method.Name}{genericParameters}"
                + $"({string.Join(", ", parameterTypes)})");
    }

    static BrowserImplementationProfile Project(
        AssemblyImplementationProfileMember member)
    {
        MethodImplementationProfile profile = member.Profile;
        return new(
            MethodKey(profile.Method),
            MethodKey(profile.EvidenceMethod),
            profile.ILBytes,
            profile.InstructionCount,
            profile.DistinctOpcodeCount,
            profile.BasicBlockCount,
            profile.BranchCount,
            profile.ConditionalBranchCount,
            profile.SwitchCount,
            profile.SwitchTargetCount,
            profile.NormalFlowCyclomaticComplexity,
            profile.LoopCount,
            profile.CatchCount,
            profile.FilterCount,
            profile.FinallyCount,
            profile.FaultCount,
            profile.LocalCount,
            profile.DirectCallCount,
            profile.DistinctCalleeCount,
            profile.AllocationCount,
            profile.ThrowCount,
            profile.Async,
            profile.Unsafe,
            profile.ReflectionCallCount,
            profile.IncomingOverloadCallerCount,
            profile.OutgoingOverloadTargetCount,
            profile.IsComplete,
            [.. profile.IncompleteReasons],
            [
                .. member.PublicMembers.Select(
                    static publicMember =>
                        new BrowserImplementationProfilePublicMember(
                            publicMember.TypeDefinitionId,
                            publicMember.Member,
                            publicMember.StableSelector,
                            [.. publicMember.BodyTokens])),
            ]);
    }

    static BrowserImplementationProfileCoverage Project(
        ImplementationProfilePopulationCoverageReceipt coverage) =>
        new(
            coverage.WasRequested,
            coverage.HasFullMethodEvidenceScope,
            [
                .. coverage.DeclaredMethods.Select(MethodKey),
            ],
            [
                .. coverage.ManagedMethodBodies.Select(MethodKey),
            ],
            [
                .. coverage.ProfiledEvidenceBodies.Select(MethodKey),
            ],
            [
                .. coverage.UnavailableBodies.Select(
                    static body =>
                        new BrowserImplementationProfileUnavailableBody(
                            body.EvidenceMethod is { } method
                                ? MethodKey(method)
                                : null,
                            body.MethodToken,
                            body.Reason.ToString(),
                            body.Diagnostic is { } diagnostic
                                ? Project(diagnostic)
                                : null)),
            ],
            [
                .. coverage.Diagnostics.Select(Project),
            ]);

    static BrowserImplementationProfileAnalysisDiagnostic Project(
        AnalysisDiagnostic diagnostic) =>
        new(
            diagnostic.MethodToken,
            diagnostic.Method,
            diagnostic.Message,
            diagnostic.SourceMethodToken,
            diagnostic.DeclaringType?.ToQualifiedDisplayString(),
            diagnostic.SourceDeclaringType?.ToQualifiedDisplayString());

    static BrowserImplementationProfileApiSurfaceFailure Project(
        ApiSurfaceInspectionFailure failure) =>
        new(
            failure.Operation,
            failure.SubjectToken,
            failure.Mechanism.ToString(),
            failure.Kind,
            failure.Detail,
            failure.SubjectAssembly is { } subject
                ? Project(subject)
                : null,
            failure.DependencyAssembly is { } dependency
                ? Project(dependency)
                : null);

    static BrowserImplementationProfileSubject Project(
        AssemblyContextSubject subject) =>
        new(
            Project(subject.Identity),
            subject.Registration.ModuleVersionId?.ToString("D"),
            Project(subject.Provenance));

    static BrowserAnalysisAssemblyIdentity Project(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(),
            identity.Culture,
            identity.PublicKeyToken);

    static BrowserImplementationProfileProvenance Project(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                new(
                    "package",
                    package.PackageId,
                    package.PackageVersion,
                    package.Tfm,
                    FrameworkVersion: null,
                    package.Rid,
                    package.AssetPath,
                    ResolverSource: null,
                    Project: null,
                    ContentRef: null,
                    Digest: null,
                    DeclaredName: null),
            AssemblyResolutionProvenance.PlatformAsset platform =>
                new(
                    "platform",
                    PackageId: null,
                    PackageVersion: null,
                    platform.Framework,
                    platform.FrameworkVersion,
                    RuntimeIdentifier: null,
                    AssetPath: null,
                    platform.ResolverSource,
                    Project: null,
                    ContentRef: null,
                    Digest: null,
                    DeclaredName: null),
            AssemblyResolutionProvenance.ProjectAsset project =>
                new(
                    "project",
                    PackageId: null,
                    PackageVersion: null,
                    project.Tfm,
                    FrameworkVersion: null,
                    project.Rid,
                    AssetPath: null,
                    ResolverSource: null,
                    project.Project,
                    ContentRef: null,
                    Digest: null,
                    DeclaredName: null),
            AssemblyResolutionProvenance.LocalAsset local =>
                new(
                    "local",
                    PackageId: null,
                    PackageVersion: null,
                    Framework: null,
                    FrameworkVersion: null,
                    RuntimeIdentifier: null,
                    AssetPath: null,
                    local.ResolverSource,
                    Project: null,
                    ContentRef: null,
                    Digest: null,
                    DeclaredName: null),
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(
                    "embedded",
                    PackageId: null,
                    PackageVersion: null,
                    Framework: null,
                    FrameworkVersion: null,
                    RuntimeIdentifier: null,
                    AssetPath: null,
                    ResolverSource: null,
                    Project: null,
                    embedded.ContentRef,
                    embedded.Digest,
                    embedded.DeclaredName),
            AssemblyResolutionProvenance.DesignatedAsset designated =>
                new(
                    "designated",
                    PackageId: null,
                    PackageVersion: null,
                    Framework: null,
                    FrameworkVersion: null,
                    RuntimeIdentifier: null,
                    AssetPath: null,
                    designated.ResolverSource,
                    Project: null,
                    ContentRef: null,
                    Digest: null,
                    DeclaredName: null),
            _ => throw new InvalidOperationException(
                "Unknown implementation-profile provenance."),
        };

    static string MethodKey(MethodIdentity method) =>
        $"{method.ModuleVersionId:N}:{method.MetadataToken:X8}";
}
