using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>The public MethodDef roots of the subject image.</summary>
public abstract record PublicMethodRootsResult
{
    PublicMethodRootsResult()
    {
    }

    /// <summary>The inventory, complete or bounded with its retained prefix.</summary>
    public sealed record Read(PublicMethodRootInventory Inventory)
        : PublicMethodRootsResult;

    /// <summary>The metadata could not be read.</summary>
    public sealed record Failed(string Message) : PublicMethodRootsResult;
}

/// <summary>
/// Exact public-root support work in one source receipt, as
/// <c>docs/design/method-query-source.md#image-exposure-support</c> defines it.
/// </summary>
public sealed record PublicMethodRootCoverage(
    bool Constructed,
    PublicMethodRootInventoryReceipt? Receipt,
    bool Failed,
    int Reads)
{
    public static PublicMethodRootCoverage Empty { get; } =
        new(Constructed: false, Receipt: null, Failed: false, Reads: 0);
}

/// <summary>
/// Exact reference-assembly status work in one source receipt, as
/// <c>docs/design/method-query-source.md#image-exposure-support</c> defines it.
/// </summary>
public sealed record ReferenceAssemblyStatusCoverage(
    bool Constructed,
    int AssemblyAttributeRows,
    int Reads)
{
    public static ReferenceAssemblyStatusCoverage Empty { get; } =
        new(Constructed: false, AssemblyAttributeRows: 0, Reads: 0);
}

/// <summary>
/// Declared execution-scoped support that reads the subject's public MethodDef
/// roots at the first read, so a declaring execution that never reads them
/// computes nothing. Membership and bounds are the inventory reader's.
/// </summary>
internal sealed class PublicMethodRootSupport(MetadataReader? reader)
{
    PublicMethodRootsResult? _result;

    /// <summary>
    /// The roots, computed by this call when <paramref name="constructed"/> is
    /// true.
    /// </summary>
    public PublicMethodRootsResult Acquire(out bool constructed)
    {
        constructed = _result is null;
        return _result ??= Read();
    }

    PublicMethodRootsResult Read()
    {
        if (reader is null)
            return new PublicMethodRootsResult.Failed("The image has no metadata.");

        // The limits are the image's own row counts, so only malformed
        // metadata leaves the inventory incomplete.
        try
        {
            int methods = Math.Max(1, reader.MethodDefinitions.Count);
            return new PublicMethodRootsResult.Read(
                PublicMethodRootInventoryReader.Read(
                    reader,
                    new PublicMethodRootInventoryLimits(
                        Math.Max(1, reader.TypeDefinitions.Count),
                        methods,
                        methods)));
        }
        catch (BadImageFormatException ex)
        {
            return new PublicMethodRootsResult.Failed(ex.Message);
        }
    }
}

/// <summary>
/// Declared execution-scoped support that classifies the subject under the
/// reference-assembly rule at the first read. The rule is Library
/// enablements'.
/// </summary>
internal sealed class ReferenceAssemblyStatusSupport(MetadataReader? reader)
{
    ReferenceAssemblyState? _state;
    int _attributeRowsRead;

    /// <summary>The assembly CustomAttribute rows the classification read.</summary>
    public int AttributeRowsRead => _attributeRowsRead;

    /// <summary>
    /// The status, classified by this call when <paramref name="constructed"/>
    /// is true.
    /// </summary>
    public ReferenceAssemblyState Acquire(out bool constructed)
    {
        constructed = _state is null;
        return _state ??= Classify();
    }

    ReferenceAssemblyState Classify()
    {
        if (reader is null)
            return ReferenceAssemblyState.Undecidable;

        try
        {
            return LibraryEnablementFacts.ClassifyReferenceAssembly(
                reader,
                ref _attributeRowsRead);
        }
        catch (BadImageFormatException)
        {
            return ReferenceAssemblyState.Undecidable;
        }
    }
}
