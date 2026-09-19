using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Inspector.Artifacts;

namespace ILInspector.Metadata;

/// <summary>
/// Projects content-free assembly facts and validates one query against them,
/// entirely within artifact-owner-issued content callbacks.
/// </summary>
public static class ArtifactAssemblyInspection
{
    public static ArtifactAssemblyProjectionOutcome Project(
        scoped ArtifactAdmissionContentView view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
            return RejectProjection(ArtifactAssemblyProjectionFailureKind.MalformedMetadata);

        ArtifactGenerationIdentity generation = view.Generation;
        ArtifactIdentity artifact = view.Artifact;
        return view.UseReadStream(
            content => Project(
                content,
                generation,
                artifact,
                cancellationToken));
    }

    private static ArtifactAssemblyProjectionOutcome Project(
        Stream content,
        ArtifactGenerationIdentity generation,
        ArtifactIdentity artifact,
        CancellationToken cancellationToken)
    {
        using var peReader = new PEReader(content);
        try
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                if (MetadataFormatAdmission.HasDeclaredClrHeader(peReader))
                    return RejectProjection(ArtifactAssemblyProjectionFailureKind.MalformedMetadata);
                return new ArtifactAssemblyProjectionOutcome.NotAssembly(ArtifactNonAssemblyKind.NativeImage);
            }
            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
                return new ArtifactAssemblyProjectionOutcome.NotAssembly(ArtifactNonAssemblyKind.ManagedModule);

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (string.IsNullOrWhiteSpace(identity.Name))
                return RejectProjection(ArtifactAssemblyProjectionFailureKind.MalformedMetadata);
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (mvid == Guid.Empty)
                return RejectProjection(ArtifactAssemblyProjectionFailureKind.EmptyModuleVersionId);

            cancellationToken.ThrowIfCancellationRequested();
            return new ArtifactAssemblyProjectionOutcome.Projected(
                new ArtifactAssemblyProjection(
                    new AssemblyProjectionRegistration(
                        generation,
                        artifact,
                        mvid),
                    identity));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return RejectProjection(ArtifactAssemblyProjectionFailureKind.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or OverflowException)
        {
            return RejectProjection(ArtifactAssemblyProjectionFailureKind.MalformedMetadata);
        }
    }

    public static ArtifactAssemblyQueryOutcome<TResult> Execute<TResult>(
        scoped ArtifactQueryContentView view,
        ArtifactAssemblyProjection projection,
        Func<AssemblyInspectionSession, CancellationToken, TResult> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(producer);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(view.Generation, projection.Registration.Generation))
            return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.GenerationMismatch);
        if (!ReferenceEquals(view.Artifact, projection.Registration.Artifact))
            return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.ArtifactIdentityMismatch);
        if (view.Content.IsEmpty)
            return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.MalformedMetadata);

        return view.UseReadStream(
            content => Execute(
                content,
                projection,
                producer,
                cancellationToken));
    }

    private static ArtifactAssemblyQueryOutcome<TResult> Execute<TResult>(
        Stream content,
        ArtifactAssemblyProjection projection,
        Func<AssemblyInspectionSession, CancellationToken, TResult> producer,
        CancellationToken cancellationToken)
    {
        using var peReader = new PEReader(content);
        try
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                if (MetadataFormatAdmission.HasDeclaredClrHeader(peReader))
                    return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.MalformedMetadata);
                return new ArtifactAssemblyQueryOutcome<TResult>.NotAssembly(ArtifactNonAssemblyKind.NativeImage);
            }
            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
                return new ArtifactAssemblyQueryOutcome<TResult>.NotAssembly(ArtifactNonAssemblyKind.ManagedModule);

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (string.IsNullOrWhiteSpace(identity.Name))
                return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.MalformedMetadata);
            if (!identity.IsEquivalentTo(projection.Identity))
                return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.AssemblyIdentityMismatch);
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (mvid == Guid.Empty)
                return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.EmptyModuleVersionId);
            if (mvid != projection.Registration.ModuleVersionId)
                return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.ModuleVersionIdMismatch);
        }
        catch (UnsupportedMetadataFormatException)
        {
            return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or OverflowException)
        {
            return RejectQuery<TResult>(ArtifactAssemblyQueryFailureKind.MalformedMetadata);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(peReader);
        TResult result = producer(session, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new ArtifactAssemblyQueryOutcome<TResult>.Validated(result);
    }

    private static ArtifactAssemblyProjectionOutcome.Rejected RejectProjection(
        ArtifactAssemblyProjectionFailureKind kind) => new(new(kind));

    private static ArtifactAssemblyQueryOutcome<TResult>.Rejected RejectQuery<TResult>(
        ArtifactAssemblyQueryFailureKind kind) => new(new(kind));
}
