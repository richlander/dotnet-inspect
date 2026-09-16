using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>The outcome of resolving one exact API Member to its implementation MethodDef.</summary>
public enum MatchedApiMemberBodyResolutionStatus
{
    NoApplicableDeclaration,
    InvalidAssociation,
    SurfaceImageUnavailable,
    ImplementationParticipantUnavailable,
    ImplementationImageUnavailable,
    SameImageExact,
    MethodExact,
    MethodAbsent,
    MethodAmbiguous,
    MethodFailed,
    Bodyless,
}

/// <summary>One detached implementation MethodDef candidate.</summary>
public readonly record struct MatchedApiMemberMethodCandidate(
    Guid ModuleVersionId,
    int MethodToken);

/// <summary>
/// Detached evidence for resolving an exact API Member to its package implementation body.
/// </summary>
public sealed record MatchedApiMemberBodyResolutionEvidence(
    MatchedApiMemberBodyResolutionStatus Status,
    ApiDeclarationKind DeclarationKind,
    MetadataTypeDefinitionName DeclaringType,
    MemberAnchor Member,
    AssemblyReferenceIdentity SurfaceAssembly,
    Guid SurfaceModuleVersionId)
{
    public string? SurfaceAssetPath { get; init; }
    public int? SurfaceMethodToken { get; init; }
    public AssemblyReferenceIdentity? ImplementationAssembly { get; init; }
    public string? ImplementationAssetPath { get; init; }
    public Guid? ImplementationModuleVersionId { get; init; }
    public int? ImplementationMethodToken { get; init; }
    public MethodCorrespondenceStatus? MethodCorrespondenceStatus { get; init; }
    public ImmutableArray<MatchedApiMemberMethodCandidate> Candidates { get; init; } = [];
    public string? Detail { get; init; }
}

/// <summary>One native Analysis Finding census and its detached body-resolution evidence.</summary>
public sealed record MatchedApiMemberAnalysisResult<T>(
    MatchedApiMemberBodyResolutionEvidence Resolution,
    FindingInspection<T> Inspection)
    where T : notnull;

/// <summary>
/// Resolves an exact matched API Member to its implementation MethodDef and projects native
/// Analysis Findings.
/// </summary>
public static class MatchedApiMemberAnalysisQuery
{
    public static InspectionQuery<
        MatchedApiMemberAnalysisResult<AllocationOccurrence>> AllocationDefinition { get; } =
        new("Matched API Member allocation analysis", InspectionCost.Unbounded);

    public static InspectionQuery<
        MatchedApiMemberAnalysisResult<DirectCall>> CallSiteDefinition { get; } =
        new("Matched API Member call-site analysis", InspectionCost.Unbounded);

    public static InspectionQuery<
        MatchedApiMemberAnalysisResult<UnsafetyOccurrence>> UnsafetyDefinition { get; } =
        new("Matched API Member unsafety analysis", InspectionCost.Unbounded);

    public static MatchedApiMemberAnalysisResult<AllocationOccurrence>
        InspectAllocations(
            PackageAssemblyContextRealization realization,
            ApiCoordinateCorrespondenceResult correspondence,
            FindingSubject subject,
            CancellationToken cancellationToken = default) =>
        Execute(
            realization,
            correspondence,
            subject,
            AnalysisFindings.AllocationDescriptor,
            static (analysis, findingSubject) =>
                AnalysisFindings.InspectAllocations(
                    analysis.Allocations,
                    findingSubject),
            cancellationToken);

    public static MatchedApiMemberAnalysisResult<DirectCall>
        InspectCallSites(
            PackageAssemblyContextRealization realization,
            ApiCoordinateCorrespondenceResult correspondence,
            FindingSubject subject,
            CancellationToken cancellationToken = default) =>
        Execute(
            realization,
            correspondence,
            subject,
            AnalysisFindings.CallSiteDescriptor,
            static (analysis, findingSubject) =>
                AnalysisFindings.InspectCallSites(
                    analysis.DirectCalls,
                    findingSubject),
            cancellationToken);

    public static MatchedApiMemberAnalysisResult<UnsafetyOccurrence>
        InspectUnsafety(
            PackageAssemblyContextRealization realization,
            ApiCoordinateCorrespondenceResult correspondence,
            FindingSubject subject,
            CancellationToken cancellationToken = default) =>
        Execute(
            realization,
            correspondence,
            subject,
            AnalysisFindings.UnsafetyDescriptor,
            static (analysis, findingSubject) =>
                AnalysisFindings.InspectUnsafety(
                    analysis.UnsafetyOccurrences,
                    findingSubject),
            cancellationToken);

    static MatchedApiMemberAnalysisResult<T> Execute<T>(
        PackageAssemblyContextRealization realization,
        ApiCoordinateCorrespondenceResult correspondence,
        FindingSubject subject,
        FindingDescriptor descriptor,
        Func<
            AssemblyMethodAnalysis,
            FindingSubject,
            ImmutableArray<Finding<T>>> project,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        (
            ApiDeclarationCorrespondenceResult strict,
            ApiDeclarationReference target,
            StructuralSubjectIdentity.MemberSubject destination) =
            ExactMember(correspondence);

        MatchedApiMemberBodyResolutionEvidence initial =
            new(
                MatchedApiMemberBodyResolutionStatus.InvalidAssociation,
                target.Kind,
                target.DeclaringType,
                target.Member!,
                target.Endpoint.Identity,
                target.Endpoint.ModuleVersionId)
            {
                SurfaceMethodToken =
                    target.Location.MethodAddress?.Token,
            };

        PackageAssemblyRoleParticipant? surface =
            realization.SurfaceParticipants.FirstOrDefault(candidate =>
                target.Endpoint.Registration.Matches(
                    candidate.Participant.Assembly.Registration));
        if (surface is null
            || strict.Destination is null
            || !strict.Destination.Registration.Matches(
                surface.Participant.Assembly.Registration)
            || target.DeclaringType != destination.Identity.DeclaringType
            || target.Member != destination.Identity.Member)
        {
            return Failed<T>(
                initial with
                {
                    Detail =
                        "The exact destination declaration is not associated with "
                        + "one surface participant in the supplied package realization.",
                },
                subject,
                descriptor);
        }

        initial = initial with
        {
            SurfaceAssetPath = surface.Asset.Path.ToString(),
        };
        if (target.Kind != ApiDeclarationKind.Method)
        {
            MatchedApiMemberBodyResolutionEvidence inapplicable =
                initial with
                {
                    Status =
                        MatchedApiMemberBodyResolutionStatus
                            .NoApplicableDeclaration,
                    Detail =
                        $"The exact API declaration is {target.Kind} and has no "
                        + $"method-body input for finding '{descriptor.Id}'.",
                };
            return new(
                inapplicable,
                new FindingInspection<T>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    inapplicable.Detail));
        }
        if (target.Location.MethodAddress is not { } surfaceMethod)
        {
            return Failed<T>(
                initial with
                {
                    Detail =
                        "The exact API Method declaration has no physical "
                        + "MethodDef address.",
                },
                subject,
                descriptor);
        }

        initial = initial with
        {
            SurfaceMethodToken = surfaceMethod.Token,
        };
        PackageAssemblyRoleParticipant? implementation =
            realization.ImplementationParticipant(surface);
        if (implementation is null)
        {
            return Failed<T>(
                initial with
                {
                    Status =
                        MatchedApiMemberBodyResolutionStatus
                            .ImplementationParticipantUnavailable,
                    Detail =
                        "The exact surface participant has no owner-paired "
                        + "implementation participant.",
                },
                subject,
                descriptor);
        }

        MatchedApiMemberBodyResolutionEvidence withImplementation =
            initial with
            {
                ImplementationAssembly =
                    implementation.Participant.Assembly.Identity,
                ImplementationAssetPath =
                    implementation.Asset.Path.ToString(),
            };
        BodyResolution resolution = ResolveBody(
            realization,
            surface,
            implementation,
            surfaceMethod,
            withImplementation,
            cancellationToken);
        if (resolution.Evidence.Status
            == MatchedApiMemberBodyResolutionStatus.Bodyless)
        {
            return new(
                resolution.Evidence,
                new FindingInspection<T>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    resolution.Evidence.Detail));
        }
        if (resolution.MethodToken is not { } methodToken)
        {
            return Failed<T>(
                resolution.Evidence,
                subject,
                descriptor);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AssemblyContextEntry<AssemblyMethodAnalysis> entry =
            AssemblyContextMethodAnalysisQuery.ExecuteParticipant(
                realization.ImplementationGroup!,
                implementation.Participant,
                methodToken);
        return entry switch
        {
            AssemblyContextEntry<AssemblyMethodAnalysis>.Available available =>
                Complete(
                    resolution.Evidence,
                    available.Value,
                    subject,
                    descriptor,
                    project),
            AssemblyContextEntry<AssemblyMethodAnalysis>.Rejected rejected =>
                Failed<T>(
                    resolution.Evidence with
                    {
                        Detail =
                            $"The implementation image was rejected during Analysis "
                            + $"({rejected.Failure.Kind}): {rejected.Failure.Detail}",
                    },
                    subject,
                    descriptor),
            AssemblyContextEntry<AssemblyMethodAnalysis>.Failed failed =>
                Failed<T>(
                    resolution.Evidence with
                    {
                        Detail =
                            $"Implementation method Analysis failed: "
                            + $"{failed.Error.GetType().Name}: {failed.Error.Message}",
                    },
                    subject,
                    descriptor),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context method Analysis outcome."),
        };
    }

    static (
        ApiDeclarationCorrespondenceResult Strict,
        ApiDeclarationReference Target,
        StructuralSubjectIdentity.MemberSubject Destination)
        ExactMember(ApiCoordinateCorrespondenceResult correspondence)
    {
        if (correspondence.Status != ApiCoordinateCorrespondenceStatus.Exact
            || correspondence.Correspondence
                is not { IsExact: true, Target: { Member: not null } target }
                    strict
            || correspondence.Destination
                is not StructuralSubjectIdentity.MemberSubject destination)
        {
            throw new ArgumentException(
                "Matched API Member Analysis requires one exact successful "
                + "Member correspondence.",
                nameof(correspondence));
        }

        return (strict, target, destination);
    }

    static BodyResolution ResolveBody(
        PackageAssemblyContextRealization realization,
        PackageAssemblyRoleParticipant surface,
        PackageAssemblyRoleParticipant implementation,
        MetadataMethodAddress surfaceMethod,
        MatchedApiMemberBodyResolutionEvidence evidence,
        CancellationToken cancellationToken)
    {
        bool sameImage =
            ReferenceEquals(
                surface.Participant,
                implementation.Participant);
        AssemblyImageAccessResult<BodyResolution> surfaceAccess =
            realization.SurfaceGroup.UseSnapshot(
                surface.Participant,
                cancellationToken,
                surfaceSnapshot =>
                {
                    if (sameImage)
                    {
                        return ResolveSameImage(
                            surfaceSnapshot,
                            surfaceMethod,
                            evidence);
                    }

                    AssemblyImageAccessResult<BodyResolution>
                        implementationAccess =
                            realization.ImplementationGroup!.UseSnapshot(
                                implementation.Participant,
                                cancellationToken,
                                implementationSnapshot =>
                                    ResolveCrossImage(
                                        surfaceSnapshot,
                                        implementationSnapshot,
                                        surfaceMethod,
                                        evidence,
                                        cancellationToken));
                    return implementationAccess switch
                    {
                        AssemblyImageAccessResult<BodyResolution>.Available
                            available => available.Value,
                        AssemblyImageAccessResult<BodyResolution>.Rejected
                            rejected => new(
                                evidence with
                                {
                                    Status =
                                        MatchedApiMemberBodyResolutionStatus
                                            .ImplementationImageUnavailable,
                                    Detail =
                                        $"The paired implementation image is "
                                        + $"unavailable ({rejected.Failure.Kind}): "
                                        + rejected.Failure.Detail,
                                },
                                MethodToken: null),
                        _ => throw new InvalidOperationException(
                            "Unknown implementation image-access outcome."),
                    };
                });

        return surfaceAccess switch
        {
            AssemblyImageAccessResult<BodyResolution>.Available available =>
                available.Value,
            AssemblyImageAccessResult<BodyResolution>.Rejected rejected =>
                new(
                    evidence with
                    {
                        Status =
                            MatchedApiMemberBodyResolutionStatus
                                .SurfaceImageUnavailable,
                        Detail =
                            $"The exact API surface image is unavailable "
                            + $"({rejected.Failure.Kind}): "
                            + rejected.Failure.Detail,
                    },
                    MethodToken: null),
            _ => throw new InvalidOperationException(
                "Unknown surface image-access outcome."),
        };
    }

    static BodyResolution ResolveSameImage(
        AssemblyImageSnapshot snapshot,
        MetadataMethodAddress method,
        MatchedApiMemberBodyResolutionEvidence evidence)
    {
        try
        {
            using var image = new PEReader(snapshot.Content);
            MetadataReader reader = image.GetMetadataReader();
            if (snapshot.ModuleVersionId != evidence.SurfaceModuleVersionId
                || !method.BelongsTo(reader)
                || !ValidMethod(reader, method.Handle))
            {
                return new(
                    evidence with
                    {
                        Status =
                            MatchedApiMemberBodyResolutionStatus
                                .InvalidAssociation,
                        Detail =
                            "The API MethodDef address does not belong to the "
                            + "exact shared surface/implementation image.",
                    },
                    MethodToken: null);
            }

            MethodDefinition definition =
                reader.GetMethodDefinition(method.Handle);
            MatchedApiMemberBodyResolutionEvidence exact =
                evidence with
                {
                    ImplementationModuleVersionId =
                        snapshot.ModuleVersionId,
                    ImplementationMethodToken = method.Token,
                    Candidates =
                    [
                        new(snapshot.ModuleVersionId, method.Token),
                    ],
                    Status =
                        definition.RelativeVirtualAddress == 0
                            ? MatchedApiMemberBodyResolutionStatus.Bodyless
                            : MatchedApiMemberBodyResolutionStatus
                                .SameImageExact,
                    Detail =
                        definition.RelativeVirtualAddress == 0
                            ? "The exact implementation MethodDef has no managed IL body."
                            : null,
                };
            return new(
                exact,
                definition.RelativeVirtualAddress == 0
                    ? null
                    : method.Token);
        }
        catch (Exception ex) when (ex is
            BadImageFormatException
            or InvalidOperationException
            or ArgumentException)
        {
            return new(
                evidence with
                {
                    Status =
                        MatchedApiMemberBodyResolutionStatus.MethodFailed,
                    Detail =
                        $"The shared API/implementation MethodDef could not be "
                        + $"validated: {ex.GetType().Name}: {ex.Message}",
                },
                MethodToken: null);
        }
    }

    static BodyResolution ResolveCrossImage(
        AssemblyImageSnapshot surfaceSnapshot,
        AssemblyImageSnapshot implementationSnapshot,
        MetadataMethodAddress surfaceMethod,
        MatchedApiMemberBodyResolutionEvidence evidence,
        CancellationToken cancellationToken)
    {
        try
        {
            using var surfaceImage =
                new PEReader(surfaceSnapshot.Content);
            using var implementationImage =
                new PEReader(implementationSnapshot.Content);
            MetadataReader surfaceReader =
                surfaceImage.GetMetadataReader();
            MetadataReader implementationReader =
                implementationImage.GetMetadataReader();
            cancellationToken.ThrowIfCancellationRequested();
            MethodCorrespondenceResult correspondence =
                MethodCorrespondenceResolver.Resolve(
                    surfaceReader,
                    surfaceMethod,
                    implementationReader);
            cancellationToken.ThrowIfCancellationRequested();

            ImmutableArray<MatchedApiMemberMethodCandidate> candidates =
            [
                .. correspondence.Candidates.Select(candidate =>
                    new MatchedApiMemberMethodCandidate(
                        candidate.ModuleVersionId,
                        candidate.Token)),
            ];
            MatchedApiMemberBodyResolutionEvidence resolved =
                evidence with
                {
                    ImplementationModuleVersionId =
                        implementationSnapshot.ModuleVersionId,
                    MethodCorrespondenceStatus =
                        correspondence.Status,
                    Candidates = candidates,
                };
            if (correspondence.Target is not { } target)
            {
                MatchedApiMemberBodyResolutionStatus status =
                    NonExactStatus(correspondence.Status);
                return new(
                    resolved with
                    {
                        Status = status,
                        Detail =
                            correspondence.Failure
                            ?? "Implementation MethodDef correspondence did not complete.",
                    },
                    MethodToken: null);
            }

            if (!target.BelongsTo(implementationReader)
                || !ValidMethod(implementationReader, target.Handle))
            {
                return new(
                    resolved with
                    {
                        Status =
                            MatchedApiMemberBodyResolutionStatus
                                .MethodFailed,
                        Detail =
                            "The exact implementation MethodDef address does not "
                            + "belong to the paired implementation image.",
                    },
                    MethodToken: null);
            }

            MethodDefinition definition =
                implementationReader.GetMethodDefinition(target.Handle);
            bool hasBody = definition.RelativeVirtualAddress != 0;
            MatchedApiMemberBodyResolutionEvidence exact =
                resolved with
                {
                    ImplementationMethodToken = target.Token,
                    Status =
                        hasBody
                            ? MatchedApiMemberBodyResolutionStatus.MethodExact
                            : MatchedApiMemberBodyResolutionStatus.Bodyless,
                    Detail =
                        hasBody
                            ? null
                            : "The exact implementation MethodDef has no managed IL body.",
                };
            return new(exact, hasBody ? target.Token : null);
        }
        catch (Exception ex) when (ex is
            BadImageFormatException
            or InvalidOperationException
            or ArgumentException)
        {
            return new(
                evidence with
                {
                    Status =
                        MatchedApiMemberBodyResolutionStatus.MethodFailed,
                    ImplementationModuleVersionId =
                        implementationSnapshot.ModuleVersionId,
                    Detail =
                        $"Implementation MethodDef correspondence failed: "
                        + $"{ex.GetType().Name}: {ex.Message}",
                },
                MethodToken: null);
        }
    }

    static MatchedApiMemberAnalysisResult<T> Complete<T>(
        MatchedApiMemberBodyResolutionEvidence resolution,
        AssemblyMethodAnalysis analysis,
        FindingSubject subject,
        FindingDescriptor descriptor,
        Func<
            AssemblyMethodAnalysis,
            FindingSubject,
            ImmutableArray<Finding<T>>> project)
        where T : notnull
    {
        if (!analysis.Diagnostics.IsEmpty)
        {
            AnalysisDiagnostic first = analysis.Diagnostics[0];
            string prefix =
                analysis.Diagnostics.Length == 1
                    ? "Method Analysis is incomplete"
                    : $"Method Analysis is incomplete with "
                        + $"{analysis.Diagnostics.Length} diagnostics; first";
            return Failed<T>(
                resolution with
                {
                    Detail = $"{prefix}: {first.Method}: {first.Message}",
                },
                subject,
                descriptor);
        }

        return new(
            resolution,
            new FindingInspection<T>.Complete(
                project(analysis, subject)));
    }

    static MatchedApiMemberAnalysisResult<T> Failed<T>(
        MatchedApiMemberBodyResolutionEvidence resolution,
        FindingSubject subject,
        FindingDescriptor descriptor)
        where T : notnull =>
        new(
            resolution,
            new FindingInspection<T>.Failed(
                new InspectionError(
                    subject,
                    descriptor,
                    resolution.Detail
                    ?? "Matched API Member Analysis did not complete.")));

    static bool ValidMethod(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0
            && row <= reader.GetTableRowCount(TableIndex.MethodDef);
    }

    internal static MatchedApiMemberBodyResolutionStatus NonExactStatus(
        MethodCorrespondenceStatus status) =>
        status switch
        {
            MethodCorrespondenceStatus.Absent =>
                MatchedApiMemberBodyResolutionStatus.MethodAbsent,
            MethodCorrespondenceStatus.Ambiguous =>
                MatchedApiMemberBodyResolutionStatus.MethodAmbiguous,
            MethodCorrespondenceStatus.Failed =>
                MatchedApiMemberBodyResolutionStatus.MethodFailed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                "An exact method correspondence is not a non-success."),
        };

    readonly record struct BodyResolution(
        MatchedApiMemberBodyResolutionEvidence Evidence,
        int? MethodToken);
}
