using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public abstract record AssemblyContextMethodCorrespondenceResult
{
    private AssemblyContextMethodCorrespondenceResult()
    {
    }

    public sealed record Exact(
        int MethodToken,
        Guid ModuleVersionId,
        MethodCorrespondenceStatus Status)
        : AssemblyContextMethodCorrespondenceResult;

    public sealed record Bodyless(
        Guid ModuleVersionId,
        int MethodToken)
        : AssemblyContextMethodCorrespondenceResult;

    public sealed record Unavailable(string Reason)
        : AssemblyContextMethodCorrespondenceResult;
}

/// <summary>
/// Resolves one surface MethodDef to its exact implementation MethodDef and
/// admits only a managed IL body.
/// </summary>
public static class AssemblyContextMethodCorrespondenceQuery
{
    public static AssemblyContextMethodCorrespondenceResult Execute(
        AssemblyContextGroup surfaceGroup,
        AssemblyContextParticipant surfaceParticipant,
        int surfaceMethodToken,
        AssemblyContextGroup implementationGroup,
        AssemblyContextParticipant implementationParticipant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surfaceGroup);
        ArgumentNullException.ThrowIfNull(surfaceParticipant);
        ArgumentNullException.ThrowIfNull(implementationGroup);
        ArgumentNullException.ThrowIfNull(implementationParticipant);
        cancellationToken.ThrowIfCancellationRequested();

        AssemblyImageAccessResult<
            AssemblyContextMethodCorrespondenceResult> surfaceAccess =
            surfaceGroup.UseSnapshot(
                surfaceParticipant,
                cancellationToken,
                surfaceSnapshot =>
                {
                    AssemblyImageAccessResult<
                        AssemblyContextMethodCorrespondenceResult>
                        implementationAccess =
                            implementationGroup.UseSnapshot(
                                implementationParticipant,
                                cancellationToken,
                                implementationSnapshot =>
                                    Resolve(
                                        surfaceSnapshot,
                                        surfaceMethodToken,
                                        implementationSnapshot,
                                        cancellationToken));
                    return implementationAccess switch
                    {
                        AssemblyImageAccessResult<
                            AssemblyContextMethodCorrespondenceResult>
                            .Available available =>
                                available.Value,
                        AssemblyImageAccessResult<
                            AssemblyContextMethodCorrespondenceResult>
                            .Rejected rejected =>
                                new AssemblyContextMethodCorrespondenceResult
                                    .Unavailable(
                                        "The implementation image was "
                                            + $"unavailable: {rejected.Failure}."),
                        _ => throw new InvalidOperationException(
                            "Unknown implementation image-access outcome."),
                    };
                });
        return surfaceAccess switch
        {
            AssemblyImageAccessResult<
                AssemblyContextMethodCorrespondenceResult>.Available
                available =>
                    available.Value,
            AssemblyImageAccessResult<
                AssemblyContextMethodCorrespondenceResult>.Rejected
                rejected =>
                    new AssemblyContextMethodCorrespondenceResult.Unavailable(
                        "The surface image was unavailable: "
                            + $"{rejected.Failure}."),
            _ => throw new InvalidOperationException(
                "Unknown surface image-access outcome."),
        };
    }

    static AssemblyContextMethodCorrespondenceResult Resolve(
        AssemblyImageSnapshot surfaceSnapshot,
        int surfaceMethodToken,
        AssemblyImageSnapshot implementationSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            EntityHandle entity =
                MetadataTokens.EntityHandle(surfaceMethodToken);
            if (entity.Kind != HandleKind.MethodDefinition)
            {
                return new AssemblyContextMethodCorrespondenceResult
                    .Unavailable(
                        $"Token 0x{surfaceMethodToken:X8} is not a MethodDef.");
            }

            var surfaceAddress = new MetadataMethodAddress(
                surfaceSnapshot.ModuleVersionId,
                (MethodDefinitionHandle)entity);
            using var surfaceImage =
                new PEReader(surfaceSnapshot.Content);
            using var implementationImage =
                new PEReader(implementationSnapshot.Content);
            MetadataReader surfaceReader =
                surfaceImage.GetMetadataReader();
            MetadataReader implementationReader =
                implementationImage.GetMetadataReader();
            cancellationToken.ThrowIfCancellationRequested();

            if (!surfaceAddress.BelongsTo(surfaceReader)
                || !ValidMethod(surfaceReader, surfaceAddress.Handle))
            {
                return new AssemblyContextMethodCorrespondenceResult
                    .Unavailable(
                        "The surface MethodDef token does not belong to the "
                            + "selected surface image.");
            }

            if (surfaceSnapshot.ModuleVersionId
                == implementationSnapshot.ModuleVersionId)
            {
                return Complete(
                    implementationReader,
                    surfaceAddress,
                    MethodCorrespondenceStatus.Exact);
            }

            MethodCorrespondenceResult correspondence =
                MethodCorrespondenceResolver.Resolve(
                    surfaceReader,
                    surfaceAddress,
                    implementationReader);
            cancellationToken.ThrowIfCancellationRequested();
            if (correspondence.Target is not { } target)
            {
                return new AssemblyContextMethodCorrespondenceResult
                    .Unavailable(
                        correspondence.Failure
                            ?? "Implementation MethodDef correspondence did "
                                + "not complete.");
            }

            return Complete(
                implementationReader,
                target,
                correspondence.Status);
        }
        catch (Exception ex)
            when (ex is
                BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return new AssemblyContextMethodCorrespondenceResult.Unavailable(
                $"MethodDef correspondence failed: "
                    + $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static AssemblyContextMethodCorrespondenceResult Complete(
        MetadataReader implementationReader,
        MetadataMethodAddress target,
        MethodCorrespondenceStatus status)
    {
        if (!target.BelongsTo(implementationReader)
            || !ValidMethod(
                implementationReader,
                target.Handle))
        {
            return new AssemblyContextMethodCorrespondenceResult.Unavailable(
                "The implementation MethodDef address does not belong to the "
                    + "selected implementation image.");
        }

        MethodDefinition definition =
            implementationReader.GetMethodDefinition(target.Handle);
        return definition.RelativeVirtualAddress == 0
            ? new AssemblyContextMethodCorrespondenceResult.Bodyless(
                target.ModuleVersionId,
                target.Token)
            : new AssemblyContextMethodCorrespondenceResult.Exact(
                target.Token,
                target.ModuleVersionId,
                status);
    }

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
}
