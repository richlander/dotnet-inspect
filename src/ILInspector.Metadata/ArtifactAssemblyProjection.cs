using DotnetInspector.Artifacts;

namespace ILInspector.Metadata;

public sealed record AssemblyProjectionRegistration(
    ArtifactGenerationIdentity Generation,
    ArtifactIdentity Artifact,
    Guid ModuleVersionId);

public sealed record ArtifactAssemblyProjection(
    AssemblyProjectionRegistration Registration,
    AssemblyReferenceIdentity Identity);

public enum ArtifactNonAssemblyKind
{
    NativeImage,
    ManagedModule,
}

public enum ArtifactAssemblyProjectionFailureKind
{
    AdmissionUnauthorized,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public sealed record ArtifactAssemblyProjectionFailure(
    ArtifactAssemblyProjectionFailureKind Kind);

public abstract record ArtifactAssemblyProjectionOutcome
{
    private protected ArtifactAssemblyProjectionOutcome()
    {
    }

    public sealed record Projected(ArtifactAssemblyProjection Value)
        : ArtifactAssemblyProjectionOutcome;

    public sealed record NotAssembly(ArtifactNonAssemblyKind Kind)
        : ArtifactAssemblyProjectionOutcome;

    public sealed record Rejected(ArtifactAssemblyProjectionFailure Failure)
        : ArtifactAssemblyProjectionOutcome;

    public static ArtifactAssemblyProjectionOutcome FromAccess(
        ArtifactContentAccessOutcome<ArtifactAssemblyProjectionOutcome> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch
        {
            ArtifactContentAccessOutcome<ArtifactAssemblyProjectionOutcome>.Accessed accessed =>
                accessed.Value,
            ArtifactContentAccessOutcome<ArtifactAssemblyProjectionOutcome>.Unauthorized =>
                new Rejected(new(ArtifactAssemblyProjectionFailureKind.AdmissionUnauthorized)),
            _ => throw new InvalidOperationException("Unknown artifact content access outcome."),
        };
    }
}

public enum ArtifactAssemblyQueryFailureKind
{
    QueryUnauthorized,
    GenerationMismatch,
    ArtifactIdentityMismatch,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    AssemblyIdentityMismatch,
    ModuleVersionIdMismatch,
}

public sealed record ArtifactAssemblyQueryFailure(
    ArtifactAssemblyQueryFailureKind Kind);

public abstract record ArtifactAssemblyQueryOutcome<TResult>
{
    private protected ArtifactAssemblyQueryOutcome()
    {
    }

    public sealed record Validated(TResult Value)
        : ArtifactAssemblyQueryOutcome<TResult>;

    public sealed record NotAssembly(ArtifactNonAssemblyKind Kind)
        : ArtifactAssemblyQueryOutcome<TResult>;

    public sealed record Rejected(ArtifactAssemblyQueryFailure Failure)
        : ArtifactAssemblyQueryOutcome<TResult>;

    public static ArtifactAssemblyQueryOutcome<TResult> FromAccess(
        ArtifactContentAccessOutcome<ArtifactAssemblyQueryOutcome<TResult>> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch
        {
            ArtifactContentAccessOutcome<ArtifactAssemblyQueryOutcome<TResult>>.Accessed accessed =>
                accessed.Value,
            ArtifactContentAccessOutcome<ArtifactAssemblyQueryOutcome<TResult>>.Unauthorized =>
                new Rejected(new(ArtifactAssemblyQueryFailureKind.QueryUnauthorized)),
            _ => throw new InvalidOperationException("Unknown artifact content access outcome."),
        };
    }
}
