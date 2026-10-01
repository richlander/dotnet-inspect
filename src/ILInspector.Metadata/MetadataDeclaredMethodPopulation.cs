using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

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

        Guid moduleVersionId;
        try
        {
            moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return new MetadataDeclaredMethodPopulationOutcome.Failed(
                exception.Message);
        }
        if (moduleVersionId != request.Type.ModuleVersionId)
        {
            return new MetadataDeclaredMethodPopulationOutcome.Rejected(
                MetadataDeclaredMethodPopulationRejection
                    .ModuleVersionIdMismatch);
        }

        EntityHandle entity =
            MetadataTokens.EntityHandle(request.Type.Definition.Value);
        int rowNumber = MetadataTokens.GetRowNumber(entity);
        if (entity.Kind != HandleKind.TypeDefinition
            || rowNumber <= 0
            || rowNumber > reader.GetTableRowCount(TableIndex.TypeDef))
        {
            return new MetadataDeclaredMethodPopulationOutcome.Rejected(
                MetadataDeclaredMethodPopulationRejection
                    .TypeDefinitionOutOfRange);
        }

        try
        {
            TypeDefinition definition =
                reader.GetTypeDefinition((TypeDefinitionHandle)entity);
            MethodDefinitionHandleCollection methods =
                definition.GetMethods();
            int count = methods.Count;
            if (request.Terminal
                == MetadataDeclaredMethodPopulationTerminal.Count)
            {
                return new MetadataDeclaredMethodPopulationOutcome.Counted(
                    count,
                    s_countReceipt);
            }
            if (count > request.MaximumRows)
            {
                return new MetadataDeclaredMethodPopulationOutcome.Incomplete(
                    count,
                    request.MaximumRows,
                    s_countReceipt);
            }

            var rows = ImmutableArray.CreateBuilder<int>(count);
            int visited = 0;
            foreach (MethodDefinitionHandle method in methods)
            {
                visited++;
                rows.Add(MetadataTokens.GetToken(method));
            }
            ImmutableArray<int> projectedRows = rows.MoveToImmutable();

            return new MetadataDeclaredMethodPopulationOutcome.Read(
                count,
                projectedRows,
                new(
                    TypeDefinitionRowsRead: 1,
                    MethodDefinitionHandlesVisited: visited,
                    MethodDefinitionRowsRead: 0,
                    MethodNamesDecoded: 0,
                    MethodSignaturesDecoded: 0,
                    MethodAttributesDecoded: 0,
                    ProjectedRows: projectedRows.Length));
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return new MetadataDeclaredMethodPopulationOutcome.Failed(
                exception.Message);
        }
    }
}
