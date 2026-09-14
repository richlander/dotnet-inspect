using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// One definition's structured name and declared Public/NestedPublic visibility.
/// This does not compute accessibility through enclosing types.
/// </summary>
public sealed record MetadataTypeDefinitionDeclaration(
    MetadataTypeDefinitionName Name,
    bool IsPublic);

/// <summary>
/// Reader-independent definition and forwarding declarations from one
/// image. Duplicate declarations retain their metadata row order.
/// </summary>
public sealed class AssemblyTypeDeclarationInventory
{
    internal AssemblyTypeDeclarationInventory(
        AssemblyReferenceIdentity identity,
        ImmutableArray<MetadataTypeDefinitionDeclaration> definitions,
        ImmutableArray<MetadataTypeDefinitionName> forwarders,
        ImmutableArray<MetadataTypeDefinitionName> moduleExports,
        int meaningfulPublicTypeCount)
    {
        Identity = identity;
        TypeDefinitions = definitions;
        Definitions = definitions.Select(definition => definition.Name).ToImmutableArray();
        Forwarders = forwarders;
        ModuleExports = moduleExports;
        MeaningfulPublicTypeCount = meaningfulPublicTypeCount;
    }

    public AssemblyReferenceIdentity Identity { get; }
    public ImmutableArray<MetadataTypeDefinitionDeclaration> TypeDefinitions { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Definitions { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Forwarders { get; }
    public ImmutableArray<MetadataTypeDefinitionName> ModuleExports { get; }
    public int MeaningfulPublicTypeCount { get; }
}

/// <summary>The typed result of reading one declaration inventory.</summary>
public abstract class AssemblyTypeDeclarationInventoryOutcome
{
    private protected AssemblyTypeDeclarationInventoryOutcome()
    {
    }

    public sealed class Read : AssemblyTypeDeclarationInventoryOutcome
    {
        internal Read(AssemblyTypeDeclarationInventory inventory) =>
            Inventory = inventory;

        public AssemblyTypeDeclarationInventory Inventory { get; }
    }

    public sealed class Rejected : AssemblyTypeDeclarationInventoryOutcome
    {
        internal Rejected(CandidateOpenFailure failure) => Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// Copies type declaration facts from an authoritative descriptor stream.
/// Acquisition and directory discovery remain outside Metadata.
/// </summary>
public static class AssemblyTypeDeclarationInventoryReader
{
    public static AssemblyTypeDeclarationInventoryOutcome Read(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Stream? stream = null;
        PEReader? peReader = null;
        bool rejectionEstablished = false;
        AssemblyTypeDeclarationInventoryOutcome Reject(
            CandidateOpenFailureKind kind,
            string detail,
            MetadataRootMalformedReason? metadataRootReason = null)
        {
            rejectionEstablished = true;
            return Rejected(kind, detail, metadataRootReason);
        }

        try
        {
            stream = assembly.OpenRead();
            peReader = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen);
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                return Reject(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image has no managed metadata.");
            }

            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            assembly.ValidateArtifactContent(peReader);
            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (actual != assembly.Identity)
            {
                return Reject(
                    CandidateOpenFailureKind.InvalidImage,
                    "The opened image identity does not match the acquisition descriptor.");
            }

            AssemblyTypeDeclarationInventoryOutcome outcome =
                ReadDeclarations(reader, actual, default);
            rejectionEstablished = outcome is AssemblyTypeDeclarationInventoryOutcome.Rejected;
            return outcome;
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            return Rejected(
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                "The selected image uses an unsupported metadata format.");
        }
        catch (MalformedMetadataRootException ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            return Rejected(
                CandidateOpenFailureKind.InvalidImage,
                $"The selected image has a malformed metadata root ({ex.Reason}).",
                ex.Reason);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            return Rejected(
                CandidateOpenFailureKind.Unreadable,
                "The selected image could not be read.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            return Rejected(
                CandidateOpenFailureKind.InvalidImage,
                "The selected image metadata is invalid.");
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(
                ref peReader,
                ref stream,
                ex);
            throw;
        }
        finally
        {
            if (rejectionEstablished)
            {
                OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                    ref peReader,
                    ref stream);
            }
            else
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
    }

    internal static AssemblyTypeDeclarationInventoryOutcome Read(
        PEReader peReader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image has no managed metadata.");
            }

            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image is not an assembly.");
            }

            return ReadDeclarations(
                reader,
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                cancellationToken);
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Rejected(
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                "The selected image uses an unsupported metadata format.");
        }
        catch (MalformedMetadataRootException ex)
        {
            return Rejected(
                CandidateOpenFailureKind.InvalidImage,
                $"The selected image has a malformed metadata root ({ex.Reason}).",
                ex.Reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Rejected(
                CandidateOpenFailureKind.Unreadable,
                "The selected image could not be read.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or ArgumentOutOfRangeException or OverflowException)
        {
            return Rejected(
                CandidateOpenFailureKind.InvalidImage,
                "The selected image metadata is invalid.");
        }
    }

    static AssemblyTypeDeclarationInventoryOutcome ReadDeclarations(
        MetadataReader reader,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken)
    {
        var definitions = ImmutableArray.CreateBuilder<MetadataTypeDefinitionDeclaration>();
        int meaningfulPublicTypeCount = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "A type definition name could not be decoded.");
            }

            definitions.Add(new(read.Name, definition.IsPublic));
            if (definition.IsPublic
                && !TypeFilters.IsCompilerGenerated(read.Name.Segments[^1]))
            {
                meaningfulPublicTypeCount++;
            }
        }

        var forwarders = ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
        var moduleExports = ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var traversal = MetadataRelationshipTraversal
                .WalkExportedTypeImplementationChain(reader, handle);
            if (traversal is not RelationshipTraversalResult<
                RelationshipChain<ExportedTypeHandle>>.Completed completed)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "An exported type relationship could not be decoded.");
            }

            RelationshipChain<ExportedTypeHandle> chain = completed.Value;
            int terminalRow = MetadataTokens.GetRowNumber(chain.Terminal);
            bool validTerminal = !chain.Terminal.IsNil && (chain.Terminal.Kind switch
            {
                HandleKind.AssemblyReference => terminalRow <= reader.AssemblyReferences.Count,
                HandleKind.AssemblyFile => terminalRow <= reader.AssemblyFiles.Count,
                _ => false,
            });
            if (!validTerminal)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "An exported type chain has no valid assembly or file target.");
            }

            ExportedType root = reader.GetExportedType(chain.Handles[0]);
            bool isForwarder = chain.Terminal.Kind == HandleKind.AssemblyReference;
            // SRM's IsForwarder also tests the target kind; check the stored bit itself.
            const int ForwarderFlag = 0x00200000;
            bool markedForwarder = ((int)root.Attributes & ForwarderFlag) != 0;
            if (isForwarder != markedForwarder)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "An exported type chain's target does not match its forwarder marking.");
            }

            if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "An exported type name could not be decoded.");
            }

            (isForwarder ? forwarders : moduleExports).Add(read.Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AssemblyTypeDeclarationInventoryOutcome.Read(
            new AssemblyTypeDeclarationInventory(
                identity,
                definitions.ToImmutable(),
                forwarders.ToImmutable(),
                moduleExports.ToImmutable(),
                meaningfulPublicTypeCount));
    }

    static AssemblyTypeDeclarationInventoryOutcome.Rejected Rejected(
        CandidateOpenFailureKind kind,
        string detail,
        MetadataRootMalformedReason? metadataRootReason = null) =>
        new(new CandidateOpenFailure(kind, detail)
        {
            MetadataRootReason = metadataRootReason,
        });
}

public enum AssemblySurfaceKind
{
    Implementation,
    Facade,
}

/// <summary>
/// Metadata-owned surface classification derived from a complete typed
/// declaration inventory.
/// </summary>
public sealed record AssemblySurfaceClassification(
    AssemblySurfaceKind Kind,
    int ForwarderCount,
    int MeaningfulPublicTypeCount);

/// <summary>The typed result of classifying one assembly surface.</summary>
public abstract class AssemblySurfaceClassificationOutcome
{
    private protected AssemblySurfaceClassificationOutcome()
    {
    }

    public sealed class Classified : AssemblySurfaceClassificationOutcome
    {
        internal Classified(AssemblySurfaceClassification classification) =>
            Classification = classification;

        public AssemblySurfaceClassification Classification { get; }
    }

    public sealed class Rejected : AssemblySurfaceClassificationOutcome
    {
        internal Rejected(CandidateOpenFailure failure) => Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }
}

public static class AssemblySurfaceClassifier
{
    public static AssemblySurfaceClassificationOutcome Classify(
        string assemblyPath,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(provenance);
        try
        {
            return Classify(
                ResolvedAssemblyReference.CreateFromPath(
                    assemblyPath,
                    provenance));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.Unreadable,
                    "The selected assembly could not be read."));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.UnsupportedMetadataFormat,
                    "The selected assembly uses an unsupported metadata format."));
        }
        catch (MalformedMetadataRootException ex)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    $"The selected assembly has a malformed metadata root ({ex.Reason}).")
                {
                    MetadataRootReason = ex.Reason,
                });
        }
        catch (BadImageFormatException)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected assembly is not a valid managed image."));
        }
        catch (OverflowException)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected assembly metadata is invalid."));
        }
    }

    public static AssemblySurfaceClassificationOutcome Classify(
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return AssemblyTypeDeclarationInventoryReader.Read(assembly) switch
        {
            AssemblyTypeDeclarationInventoryOutcome.Read read =>
                Classify(read.Inventory),
            AssemblyTypeDeclarationInventoryOutcome.Rejected rejected =>
                new AssemblySurfaceClassificationOutcome.Rejected(
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown declaration inventory outcome."),
        };
    }

    public static AssemblySurfaceClassificationOutcome.Classified Classify(
        AssemblyTypeDeclarationInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        bool isFacade = inventory.Forwarders.Length > 0
            && inventory.MeaningfulPublicTypeCount == 0;
        return new AssemblySurfaceClassificationOutcome.Classified(
            new AssemblySurfaceClassification(
                isFacade
                    ? AssemblySurfaceKind.Facade
                    : AssemblySurfaceKind.Implementation,
                inventory.Forwarders.Length,
                inventory.MeaningfulPublicTypeCount));
    }
}
