using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Reader-independent declarations from one admitted assembly image.
/// </summary>
public sealed class AssemblyTypeDeclarationInventory
{
    internal AssemblyTypeDeclarationInventory(
        AssemblyReferenceIdentity identity,
        ImmutableArray<MetadataTypeDefinitionName> definitions,
        ImmutableArray<MetadataTypeDefinitionName> forwarders,
        ImmutableArray<AssemblyTypeDeclaration> declarations,
        int meaningfulPublicTypeCount,
        long retainedTextCharacters)
    {
        Identity = identity;
        Definitions = definitions;
        Forwarders = forwarders;
        Declarations = declarations;
        MeaningfulPublicTypeCount = meaningfulPublicTypeCount;
        RetainedTextCharacters = retainedTextCharacters;
    }

    public AssemblyReferenceIdentity Identity { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Definitions { get; }
    public ImmutableArray<MetadataTypeDefinitionName> Forwarders { get; }
    public ImmutableArray<AssemblyTypeDeclaration> Declarations { get; }
    public int MeaningfulPublicTypeCount { get; }
    public long RetainedTextCharacters { get; }

    /// <summary>
    /// Selects public discovery declarations by default, or all declarations
    /// without rereading the image. Module exports retain their distinct kind.
    /// </summary>
    public IEnumerable<AssemblyTypeDeclaration> GetDeclarations(bool includeAll = false) =>
        includeAll ? Declarations : Declarations.Where(static declaration => declaration.IsPublicSurface);
}

public enum AssemblyTypeDeclarationKind
{
    Definition,
    Forwarder,
    ModuleExport,
}

/// <summary>High-level type category carried by a type definition.</summary>
public enum AssemblyTypeDefinitionKind
{
    Class,
    Interface,
    ValueType,
    Enum,
    Delegate,
}

/// <summary>One detached declaration; public discovery is not target binding.</summary>
public sealed class AssemblyTypeDeclaration
{
    internal AssemblyTypeDeclaration(
        MetadataTypeDefinitionName name,
        AssemblyTypeDeclarationKind kind,
        AssemblyTypeDefinitionKind? definitionKind,
        bool? isDefinitionPublic,
        bool isPublicSurface,
        TypeDeclarationDiscoveryAttributes? discoveryAttributes = null)
    {
        Name = name;
        Kind = kind;
        DefinitionKind = definitionKind;
        IsDefinitionPublic = isDefinitionPublic;
        IsPublicSurface = isPublicSurface;
        DiscoveryAttributes = discoveryAttributes;
    }

    public MetadataTypeDefinitionName Name { get; }
    public AssemblyTypeDeclarationKind Kind { get; }
    public AssemblyTypeDefinitionKind? DefinitionKind { get; }

    /// <summary>
    /// Whether this definition's own visibility is Public or NestedPublic,
    /// without evaluating an enclosing definition chain. Null for exports.
    /// </summary>
    public bool? IsDefinitionPublic { get; }

    public bool IsPublicSurface { get; }

    /// <summary>
    /// Attributes declared on this definition, without inheritance or filtering.
    /// Null for exports: their target definition has not been inspected.
    /// </summary>
    public TypeDeclarationDiscoveryAttributes? DiscoveryAttributes { get; }
}

/// <summary>
/// Detached attribute facts for type discovery. Compiler-compatibility obsolete
/// markers recognized by Metadata are not deprecation.
/// </summary>
public sealed record TypeDeclarationDiscoveryAttributes(
    bool IsEditorBrowsableNever,
    bool IsObsolete);

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

    public sealed class Incomplete : AssemblyTypeDeclarationInventoryOutcome
    {
        internal Incomplete(
            AssemblyTypeDeclarationInventoryBound bound,
            long measuredDeclarations,
            long measuredRetainedTextCharacters)
        {
            Bound = bound;
            MeasuredDeclarations = measuredDeclarations;
            MeasuredRetainedTextCharacters =
                measuredRetainedTextCharacters;
        }

        public AssemblyTypeDeclarationInventoryBound Bound { get; }
        public long MeasuredDeclarations { get; }
        public long MeasuredRetainedTextCharacters { get; }
    }
}

public enum AssemblyTypeDeclarationInventoryBound
{
    RetainedDeclarations,
    RetainedTextCharacters,
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
                ReadDeclarations(
                    reader,
                    actual,
                    maximumRetainedDeclarations: int.MaxValue,
                    maximumRetainedTextCharacters: int.MaxValue);
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
        PEReader peReader)
        => Read(
            peReader,
            maximumRetainedDeclarations: int.MaxValue,
            maximumRetainedTextCharacters: int.MaxValue);

    internal static AssemblyTypeDeclarationInventoryOutcome Read(
        PEReader peReader,
        int maximumRetainedDeclarations,
        int maximumRetainedTextCharacters)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        try
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "The selected image has no managed metadata.");
            }

            MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);
            return ReadDeclarations(
                reader,
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                maximumRetainedDeclarations,
                maximumRetainedTextCharacters);
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
        int maximumRetainedDeclarations,
        int maximumRetainedTextCharacters)
    {
        var definitions = ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
        var forwarders = ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
        var declarations = ImmutableArray.CreateBuilder<AssemblyTypeDeclaration>();
        var names = new HashSet<MetadataTypeDefinitionName>();
        int meaningfulPublicTypeCount = 0;
        long retainedTextCharacters = 0;
        MetadataVisibilityClassification visibility =
            MetadataVisibility.ClassifyAll(reader);
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
            bool isModule =
                read.Name.Namespace.Length == 0
                && read.Name.Segments is ["<Module>"];
            if (!isModule
                && declarations.Count >= maximumRetainedDeclarations)
            {
                return new AssemblyTypeDeclarationInventoryOutcome.Incomplete(
                    AssemblyTypeDeclarationInventoryBound
                        .RetainedDeclarations,
                    (long)declarations.Count + 1,
                    retainedTextCharacters);
            }
            long measuredTextCharacters = checked(
                retainedTextCharacters + TextCharacters(read.Name));
            if (measuredTextCharacters
                > maximumRetainedTextCharacters)
            {
                return new AssemblyTypeDeclarationInventoryOutcome.Incomplete(
                    AssemblyTypeDeclarationInventoryBound
                        .RetainedTextCharacters,
                    declarations.Count + (isModule ? 0L : 1L),
                    measuredTextCharacters);
            }
            if (!names.Add(read.Name))
                return DuplicateDeclaration();
            retainedTextCharacters = measuredTextCharacters;

            definitions.Add(read.Name);
            if (definition.IsPublic
                && !TypeFilters.IsCompilerGenerated(reader.GetString(definition.Name)))
            {
                meaningfulPublicTypeCount++;
            }

            // Keep the legacy name projection intact; the module row is not
            // a discoverable type, even in the all-declaration view.
            if (isModule)
            {
                continue;
            }
            declarations.Add(new AssemblyTypeDeclaration(
                read.Name, AssemblyTypeDeclarationKind.Definition,
                GetDefinitionKind(reader, definition),
                definition.IsPublic,
                visibility.IsExternallyVisible(handle),
                AttributeReader.ReadTypeDiscoveryAttributes(
                    reader, definition.GetCustomAttributes())));
        }

        var referenceProjection = new AssemblyReferenceProjectionCache(reader);
        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            if (MetadataTypeDefinitionNameReader.Read(reader, handle)
                is not MetadataTypeDefinitionNameReadResult.Read read)
            {
                return Rejected(
                    CandidateOpenFailureKind.InvalidImage,
                    "An exported type name could not be decoded.");
            }
            if (declarations.Count >= maximumRetainedDeclarations)
            {
                return new AssemblyTypeDeclarationInventoryOutcome.Incomplete(
                    AssemblyTypeDeclarationInventoryBound
                        .RetainedDeclarations,
                    (long)declarations.Count + 1,
                    retainedTextCharacters);
            }
            long measuredTextCharacters = checked(
                retainedTextCharacters + TextCharacters(read.Name));
            if (measuredTextCharacters
                > maximumRetainedTextCharacters)
            {
                return new AssemblyTypeDeclarationInventoryOutcome.Incomplete(
                    AssemblyTypeDeclarationInventoryBound
                        .RetainedTextCharacters,
                    (long)declarations.Count + 1,
                    measuredTextCharacters);
            }
            if (!names.Add(read.Name))
                return DuplicateDeclaration();
            retainedTextCharacters = measuredTextCharacters;
            if (!MetadataTypeDeclarationProbe.TryReadExportedCandidate(
                    reader, handle, referenceProjection,
                    out TypeDeclarationCandidate? candidate,
                    out MetadataTypeNameFailure? failure))
            {
                return Rejected(CandidateOpenFailureKind.InvalidImage, failure!.Detail);
            }

            switch (candidate)
            {
                case TypeDeclarationCandidate.Forwarder:
                    forwarders.Add(read.Name);
                    // Forwarder visibility bits are zero in real reference
                    // facades. This is an advertised export, not target access.
                    declarations.Add(new AssemblyTypeDeclaration(
                        read.Name, AssemblyTypeDeclarationKind.Forwarder,
                        definitionKind: null,
                        isDefinitionPublic: null,
                        isPublicSurface: true));
                    break;
                case TypeDeclarationCandidate.ModuleExport module:
                    declarations.Add(new AssemblyTypeDeclaration(
                        read.Name, AssemblyTypeDeclarationKind.ModuleExport,
                        definitionKind: null,
                        isDefinitionPublic: null,
                        isPublicSurface: module.Declarations.All(token =>
                            (reader.GetExportedType(
                                (ExportedTypeHandle)MetadataTokens.EntityHandle(token.Value))
                                .Attributes & TypeAttributes.VisibilityMask)
                            is TypeAttributes.Public or TypeAttributes.NestedPublic)));
                    break;
                default:
                    throw new InvalidOperationException("Unknown exported declaration candidate.");
            }
        }

        return new AssemblyTypeDeclarationInventoryOutcome.Read(
            new AssemblyTypeDeclarationInventory(
                identity, definitions.ToImmutable(), forwarders.ToImmutable(),
                declarations.ToImmutable(), meaningfulPublicTypeCount,
                retainedTextCharacters));
    }

    static long TextCharacters(MetadataTypeDefinitionName name)
    {
        long characters = name.Namespace.Length;
        foreach (string segment in name.Segments)
            characters = checked(characters + segment.Length);
        return characters;
    }

    static AssemblyTypeDefinitionKind GetDefinitionKind(
        MetadataReader reader,
        TypeDefinition definition)
    {
        if ((definition.Attributes & TypeAttributes.Interface) != 0)
            return AssemblyTypeDefinitionKind.Interface;

        string? baseType = definition.BaseType.IsNil
            ? null
            : TypeResolver.GetTypeName(reader, definition.BaseType);
        return baseType switch
        {
            "System.Enum" => AssemblyTypeDefinitionKind.Enum,
            "System.ValueType" => AssemblyTypeDefinitionKind.ValueType,
            "System.Delegate" or "System.MulticastDelegate" =>
                AssemblyTypeDefinitionKind.Delegate,
            _ => AssemblyTypeDefinitionKind.Class,
        };
    }

    static AssemblyTypeDeclarationInventoryOutcome.Rejected DuplicateDeclaration() =>
        Rejected(
            CandidateOpenFailureKind.InvalidImage,
            "More than one declaration has the same exact metadata type name.");

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
