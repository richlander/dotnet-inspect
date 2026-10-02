using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;

namespace ILInspector.Metadata;

/// <summary>
/// One TypeDef token authenticated to the immutable module that issued it.
/// </summary>
public sealed record MetadataTypeDefinitionBinding
{
    public MetadataTypeDefinitionBinding(
        Guid moduleVersionId,
        TypeDefinitionToken definition)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A TypeDef binding requires a module version identifier.",
                nameof(moduleVersionId));
        }

        ModuleVersionId = moduleVersionId;
        Definition = definition;
    }

    public Guid ModuleVersionId { get; }
    public TypeDefinitionToken Definition { get; }
}

public enum MetadataDeclaredMethodPopulationTerminal
{
    Count,
    Rows,
}

public sealed record MetadataDeclaredMethodPopulationRequest
{
    public MetadataDeclaredMethodPopulationRequest(
        MetadataTypeDefinitionBinding type,
        MetadataDeclaredMethodPopulationTerminal terminal,
        int maximumRows = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(terminal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Unknown declared-method population terminal.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);

        Type = type;
        Terminal = terminal;
        MaximumRows = maximumRows;
    }

    public MetadataTypeDefinitionBinding Type { get; }
    public MetadataDeclaredMethodPopulationTerminal Terminal { get; }
    public int MaximumRows { get; }
}

/// <summary>
/// Product-owned evidence for the data touched by one terminal execution.
/// </summary>
public sealed record MetadataDeclaredMethodPopulationReceipt(
    int TypeDefinitionRowsRead,
    int MethodDefinitionHandlesVisited,
    int MethodDefinitionRowsRead,
    int MethodNamesDecoded,
    int MethodSignaturesDecoded,
    int MethodAttributesDecoded,
    int ProjectedRows);

public enum MetadataDeclaredMethodPopulationRejection
{
    ModuleVersionIdMismatch,
    TypeDefinitionOutOfRange,
}

public abstract record MetadataDeclaredMethodPopulationOutcome
{
    private protected MetadataDeclaredMethodPopulationOutcome()
    {
    }

    public sealed record Counted(
        int Count,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : MetadataDeclaredMethodPopulationOutcome;

    public sealed record Read(
        int Count,
        ImmutableArray<int> Rows,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : MetadataDeclaredMethodPopulationOutcome;

    public sealed record Incomplete(
        int Count,
        int MaximumRows,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : MetadataDeclaredMethodPopulationOutcome;

    public sealed record Rejected(
        MetadataDeclaredMethodPopulationRejection Reason)
        : MetadataDeclaredMethodPopulationOutcome;

    public sealed record Failed(string Detail)
        : MetadataDeclaredMethodPopulationOutcome;
}

public enum MetadataDeclaredMethodPopulationResultKind
{
    Counted = 1,
    Read,
    Incomplete,
}

/// <summary>
/// Allocation-free execution result from one prepared declared-MethodDef
/// source.
/// </summary>
public readonly struct MetadataDeclaredMethodPopulationResult
{
    private MetadataDeclaredMethodPopulationResult(
        MetadataDeclaredMethodPopulationResultKind kind,
        int count,
        int maximumRows,
        ImmutableArray<int> rows,
        MetadataDeclaredMethodPopulationReceipt receipt)
    {
        Kind = kind;
        Count = count;
        MaximumRows = maximumRows;
        Rows = rows;
        Receipt = receipt;
    }

    public MetadataDeclaredMethodPopulationResultKind Kind { get; }

    public int Count { get; }

    public int MaximumRows { get; }

    public ImmutableArray<int> Rows { get; }

    public MetadataDeclaredMethodPopulationReceipt Receipt { get; }

    internal static MetadataDeclaredMethodPopulationResult Counted(
        int count,
        MetadataDeclaredMethodPopulationReceipt receipt) =>
        new(
            MetadataDeclaredMethodPopulationResultKind.Counted,
            count,
            maximumRows: int.MaxValue,
            rows: default,
            receipt);

    internal static MetadataDeclaredMethodPopulationResult Read(
        ImmutableArray<int> rows,
        MetadataDeclaredMethodPopulationReceipt receipt) =>
        new(
            MetadataDeclaredMethodPopulationResultKind.Read,
            rows.Length,
            maximumRows: int.MaxValue,
            rows,
            receipt);

    internal static MetadataDeclaredMethodPopulationResult Incomplete(
        int count,
        int maximumRows,
        MetadataDeclaredMethodPopulationReceipt receipt) =>
        new(
            MetadataDeclaredMethodPopulationResultKind.Incomplete,
            count,
            maximumRows,
            rows: default,
            receipt);
}

public abstract record MetadataDeclaredMethodPopulationPreparation
{
    private protected MetadataDeclaredMethodPopulationPreparation()
    {
    }

    public sealed record Ready(
        MetadataDeclaredMethodPopulationSource Source)
        : MetadataDeclaredMethodPopulationPreparation;

    public sealed record Rejected(
        MetadataDeclaredMethodPopulationRejection Reason)
        : MetadataDeclaredMethodPopulationPreparation;

    public sealed record Failed(string Detail)
        : MetadataDeclaredMethodPopulationPreparation;
}

/// <summary>
/// Authenticated declared-MethodDef source prepared for repeated terminal
/// execution while its issuing session remains alive.
/// </summary>
public sealed class MetadataDeclaredMethodPopulationSource
{
    private readonly AssemblyInspectionSession _session;
    private readonly MethodDefinitionHandleCollection _methods;
    private readonly MetadataDeclaredMethodPopulationReceipt _rowsReceipt;

    internal MetadataDeclaredMethodPopulationSource(
        AssemblyInspectionSession session,
        MethodDefinitionHandleCollection methods)
    {
        _session = session;
        _methods = methods;
        _rowsReceipt =
            MetadataDeclaredMethodPopulationInspection.RowsReceipt(
                methods.Count);
    }

    public MetadataDeclaredMethodPopulationResult Count() =>
        _session.SnapshotOperation(
            this,
            static access =>
                MetadataDeclaredMethodPopulationInspection.Count(
                    access.Operation._methods));

    public MetadataDeclaredMethodPopulationResult Rows(
        int maximumRows = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        return _session.SnapshotOperation(
            this,
            maximumRows,
            static (access, limit) =>
                MetadataDeclaredMethodPopulationInspection.Rows(
                    access.Operation._methods,
                    limit,
                    access.Operation._rowsReceipt));
    }
}

internal static class MetadataDeclaredMethodPopulationInspection
{
    private static readonly MetadataDeclaredMethodPopulationReceipt
        s_countReceipt = new(
            TypeDefinitionRowsRead: 1,
            MethodDefinitionHandlesVisited: 0,
            MethodDefinitionRowsRead: 0,
            MethodNamesDecoded: 0,
            MethodSignaturesDecoded: 0,
            MethodAttributesDecoded: 0,
            ProjectedRows: 0);

    internal static MetadataDeclaredMethodPopulationOutcome Inspect(
        MetadataReader reader,
        MetadataDeclaredMethodPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryPrepare(
                reader,
                request.Type,
                out MethodDefinitionHandleCollection methods,
                out MetadataDeclaredMethodPopulationRejection rejection,
                out string? failure))
        {
            return failure is null
                ? new MetadataDeclaredMethodPopulationOutcome.Rejected(
                    rejection)
                : new MetadataDeclaredMethodPopulationOutcome.Failed(
                    failure);
        }

        MetadataDeclaredMethodPopulationResult result =
            request.Terminal
                == MetadataDeclaredMethodPopulationTerminal.Count
                    ? Count(methods)
                    : Rows(
                        methods,
                        request.MaximumRows,
                        RowsReceipt(methods.Count));
        return result.Kind switch
        {
            MetadataDeclaredMethodPopulationResultKind.Counted =>
                new MetadataDeclaredMethodPopulationOutcome.Counted(
                    result.Count,
                    result.Receipt),
            MetadataDeclaredMethodPopulationResultKind.Read =>
                new MetadataDeclaredMethodPopulationOutcome.Read(
                    result.Count,
                    result.Rows,
                    result.Receipt),
            MetadataDeclaredMethodPopulationResultKind.Incomplete =>
                new MetadataDeclaredMethodPopulationOutcome.Incomplete(
                    result.Count,
                    result.MaximumRows,
                    result.Receipt),
            _ => throw new InvalidOperationException(
                "Unknown prepared declared-method result."),
        };
    }

    internal static MetadataDeclaredMethodPopulationPreparation Prepare(
        AssemblyInspectionSession session,
        MetadataReader reader,
        MetadataTypeDefinitionBinding type)
    {
        if (!TryPrepare(
                reader,
                type,
                out MethodDefinitionHandleCollection methods,
                out MetadataDeclaredMethodPopulationRejection rejection,
                out string? failure))
        {
            return failure is null
                ? new MetadataDeclaredMethodPopulationPreparation.Rejected(
                    rejection)
                : new MetadataDeclaredMethodPopulationPreparation.Failed(
                    failure);
        }

        return new MetadataDeclaredMethodPopulationPreparation.Ready(
            new MetadataDeclaredMethodPopulationSource(
                session,
                methods));
    }

    internal static MetadataDeclaredMethodPopulationResult Count(
        MethodDefinitionHandleCollection methods) =>
        MetadataDeclaredMethodPopulationResult.Counted(
            methods.Count,
            s_countReceipt);

    internal static MetadataDeclaredMethodPopulationResult Rows(
        MethodDefinitionHandleCollection methods,
        int maximumRows,
        MetadataDeclaredMethodPopulationReceipt rowsReceipt)
    {
        int count = methods.Count;
        if (count > maximumRows)
        {
            return MetadataDeclaredMethodPopulationResult.Incomplete(
                count,
                maximumRows,
                s_countReceipt);
        }

        int[] tokens = GC.AllocateUninitializedArray<int>(count);
        int index = 0;
        foreach (MethodDefinitionHandle method in methods)
            tokens[index++] = MetadataTokens.GetToken(method);
        return MetadataDeclaredMethodPopulationResult.Read(
            ImmutableCollectionsMarshal.AsImmutableArray(tokens),
            rowsReceipt);
    }

    internal static MetadataDeclaredMethodPopulationReceipt RowsReceipt(
        int count) =>
        new(
            TypeDefinitionRowsRead: 1,
            MethodDefinitionHandlesVisited: count,
            MethodDefinitionRowsRead: 0,
            MethodNamesDecoded: 0,
            MethodSignaturesDecoded: 0,
            MethodAttributesDecoded: 0,
            ProjectedRows: count);

    private static bool TryPrepare(
        MetadataReader reader,
        MetadataTypeDefinitionBinding type,
        out MethodDefinitionHandleCollection methods,
        out MetadataDeclaredMethodPopulationRejection rejection,
        out string? failure)
    {
        methods = default;
        rejection = default;
        failure = null;
        try
        {
            Guid moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (moduleVersionId != type.ModuleVersionId)
            {
                rejection =
                    MetadataDeclaredMethodPopulationRejection
                        .ModuleVersionIdMismatch;
                return false;
            }

            EntityHandle entity =
                MetadataTokens.EntityHandle(type.Definition.Value);
            int rowNumber = MetadataTokens.GetRowNumber(entity);
            if (entity.Kind != HandleKind.TypeDefinition
                || rowNumber <= 0
                || rowNumber
                    > reader.GetTableRowCount(TableIndex.TypeDef))
            {
                rejection =
                    MetadataDeclaredMethodPopulationRejection
                        .TypeDefinitionOutOfRange;
                return false;
            }

            methods = reader.GetTypeDefinition(
                    (TypeDefinitionHandle)entity)
                .GetMethods();
            return true;
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            failure = exception.Message;
            return false;
        }
    }
}
