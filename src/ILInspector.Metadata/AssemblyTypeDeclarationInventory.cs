using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Reader-independent definition and forwarding declarations from one
/// acquisition-issued assembly descriptor.
/// </summary>
public sealed class AssemblyTypeDeclarationInventory
{
    internal AssemblyTypeDeclarationInventory(
        AssemblyReferenceIdentity identity,
        ImmutableArray<MetadataTypeDefinitionName> definitions,
        ImmutableArray<MetadataTypeDefinitionName> forwarders,
        int meaningfulPublicTypeCount)
    {
        Identity = identity;
        Definitions = definitions;
        Forwarders = forwarders;
        MeaningfulPublicTypeCount = meaningfulPublicTypeCount;
    }

    public AssemblyReferenceIdentity Identity { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Definitions { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Forwarders { get; }
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

        try
        {
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image has no managed metadata.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (actual != assembly.Identity)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "The opened image identity does not match the acquisition descriptor.");
            }

            var definitions =
                ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
            int meaningfulPublicTypeCount = 0;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition definition = reader.GetTypeDefinition(handle);
                if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is not MetadataTypeDefinitionNameReadResult.Read read)
                {
                    return Rejected(
                        CandidateOpenFailureKind.InvalidImage,
                        "A type definition name could not be decoded.");
                }

                definitions.Add(read.Name);
                if (definition.IsPublic
                    && !TypeFilters.IsCompilerGenerated(
                        reader.GetString(definition.Name)))
                {
                    meaningfulPublicTypeCount++;
                }
            }

            var forwarders =
                ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
            foreach (ExportedTypeHandle handle in reader.ExportedTypes)
            {
                var traversal =
                    MetadataRelationshipTraversal
                        .WalkExportedTypeImplementationChain(reader, handle);
                if (traversal is
                    RelationshipTraversalResult<
                        RelationshipChain<ExportedTypeHandle>>.Rejected)
                {
                    return Rejected(
                        CandidateOpenFailureKind.InvalidImage,
                        "An exported type relationship could not be decoded.");
                }

                RelationshipChain<ExportedTypeHandle> chain =
                    ((RelationshipTraversalResult<
                        RelationshipChain<ExportedTypeHandle>>.Completed)
                            traversal).Value;
                if (chain.Terminal.Kind != HandleKind.AssemblyReference)
                    continue;

                // ECMA-335 marks the root as a forwarder; nested rows point to
                // that root without carrying tdForwarder themselves.
                ExportedType root =
                    reader.GetExportedType(chain.Handles[0]);
                if (!root.IsForwarder)
                {
                    return Rejected(
                        CandidateOpenFailureKind.InvalidImage,
                        "An assembly-forwarded type chain is not marked as a forwarder.");
                }

                if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is not MetadataTypeDefinitionNameReadResult.Read read)
                {
                    return Rejected(
                        CandidateOpenFailureKind.InvalidImage,
                        "A forwarded type name could not be decoded.");
                }

                forwarders.Add(read.Name);
            }

            return new AssemblyTypeDeclarationInventoryOutcome.Read(
                new AssemblyTypeDeclarationInventory(
                    actual,
                    definitions.ToImmutable(),
                    forwarders.ToImmutable(),
                    meaningfulPublicTypeCount));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return Rejected(
                CandidateOpenFailureKind.Unreadable,
                "The selected image could not be read.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return Rejected(
                CandidateOpenFailureKind.InvalidImage,
                "The selected image metadata is invalid.");
        }
    }

    static AssemblyTypeDeclarationInventoryOutcome.Rejected Rejected(
        CandidateOpenFailureKind kind,
        string detail) =>
        new(new CandidateOpenFailure(kind, detail));
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
        catch (BadImageFormatException)
        {
            return new AssemblySurfaceClassificationOutcome.Rejected(
                new CandidateOpenFailure(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected assembly is not a valid managed image."));
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
