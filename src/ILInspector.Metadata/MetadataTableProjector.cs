using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Projects an assembly's ECMA-335 metadata tables into a
/// <see cref="MetadataTableProjection"/>: a structurally-lossless view of SRM's
/// logical table/heap graph. Handles become resolvable
/// <see cref="HandleRef"/>/<see cref="HandleRange"/> edges, heaps surface as
/// bounded <see cref="MetadataValue.HeapReference"/> cells, and friendly decodes
/// are always additive.
///
/// This producer is a sibling of the typed extractors, never a dependency of
/// them (see <c>docs/design/metadata-table-projection.md</c>). It is read-only,
/// SRM-only, and applies explicit per-table row and blob-preview budgets with
/// typed rejection rather than silent truncation.
/// </summary>
public static class MetadataTableProjector
{
    static readonly ImmutableArray<TableIndex> ResolutionScopeTargets =
        [TableIndex.Module, TableIndex.ModuleRef, TableIndex.AssemblyRef, TableIndex.TypeRef];

    static readonly ImmutableArray<TableIndex> TypeDefOrRefTargets =
        [TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.TypeSpec];

    static readonly ImmutableArray<TableIndex> MemberRefParentTargets =
        [TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.ModuleRef, TableIndex.MethodDef, TableIndex.TypeSpec];

    static readonly ImmutableArray<TableIndex> CustomAttributeTypeTargets =
        [TableIndex.MethodDef, TableIndex.MemberRef];

    static readonly ImmutableArray<TableIndex> HasCustomAttributeTargets =
    [
        TableIndex.MethodDef, TableIndex.Field, TableIndex.TypeRef, TableIndex.TypeDef,
        TableIndex.Param, TableIndex.InterfaceImpl, TableIndex.MemberRef, TableIndex.Module,
        TableIndex.DeclSecurity, TableIndex.Property, TableIndex.Event, TableIndex.StandAloneSig,
        TableIndex.ModuleRef, TableIndex.TypeSpec, TableIndex.Assembly, TableIndex.AssemblyRef,
        TableIndex.File, TableIndex.ExportedType, TableIndex.ManifestResource, TableIndex.GenericParam,
        TableIndex.GenericParamConstraint, TableIndex.MethodSpec,
    ];

    static readonly ImmutableArray<TableIndex> HasConstantTargets =
        [TableIndex.Field, TableIndex.Param, TableIndex.Property];

    static readonly ImmutableArray<TableIndex> MethodDefOrRefTargets =
        [TableIndex.MethodDef, TableIndex.MemberRef];

    static readonly ImmutableArray<TableIndex> ImplementationTargets =
        [TableIndex.File, TableIndex.ExportedType, TableIndex.AssemblyRef];

    static readonly ImmutableArray<TableIndex> TypeOrMethodDefTargets =
        [TableIndex.TypeDef, TableIndex.MethodDef];

    static readonly ImmutableArray<TableSpec> SupportedTables =
    [
        new(TableIndex.Module, "Module", ModuleColumns, ReadModuleRow),
        new(TableIndex.TypeRef, "TypeRef", TypeRefColumns, ReadTypeRefRow),
        new(TableIndex.TypeDef, "TypeDef", TypeDefColumns, ReadTypeDefRow),
        new(TableIndex.Field, "Field", FieldColumns, ReadFieldRow),
        new(TableIndex.MethodDef, "MethodDef", MethodDefColumns, ReadMethodDefRow),
        new(TableIndex.Param, "Param", ParamColumns, ReadParamRow),
        new(TableIndex.MemberRef, "MemberRef", MemberRefColumns, ReadMemberRefRow),
        new(TableIndex.Constant, "Constant", ConstantColumns, ReadConstantRow),
        new(TableIndex.CustomAttribute, "CustomAttribute", CustomAttributeColumns, ReadCustomAttributeRow),
        new(TableIndex.StandAloneSig, "StandAloneSig", StandAloneSigColumns, ReadStandAloneSigRow),
        new(TableIndex.MethodImpl, "MethodImpl", MethodImplColumns, ReadMethodImplRow),
        new(TableIndex.TypeSpec, "TypeSpec", TypeSpecColumns, ReadTypeSpecRow),
        new(TableIndex.Assembly, "Assembly", AssemblyColumns, ReadAssemblyRow),
        new(TableIndex.AssemblyRef, "AssemblyRef", AssemblyRefColumns, ReadAssemblyRefRow),
        new(TableIndex.ExportedType, "ExportedType", ExportedTypeColumns, ReadExportedTypeRow),
        new(TableIndex.GenericParam, "GenericParam", GenericParamColumns, ReadGenericParamRow),
        new(TableIndex.MethodSpec, "MethodSpec", MethodSpecColumns, ReadMethodSpecRow),
    ];

    /// <summary>
    /// Projects the supported metadata tables of <paramref name="peReader"/>.
    /// Returns an empty projection when the image carries no metadata.
    /// </summary>
    public static MetadataTableProjection Project(PEReader peReader, MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        options ??= new MetadataProjectionOptions();

        if (!peReader.HasMetadata)
            return new MetadataTableProjection(ImmutableArray<MetadataTableView>.Empty);

        // MetadataReaderOptions.None keeps the projection raw: the default enables
        // Windows-Runtime projection, which would replace real table/heap values
        // with synthesized aliases and defeat structural losslessness.
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        var selected = options.Tables.IsDefaultOrEmpty
            ? (IReadOnlyCollection<TableIndex>?)null
            : options.Tables.ToImmutableHashSet();

        var views = ImmutableArray.CreateBuilder<MetadataTableView>();
        foreach (var spec in SupportedTables)
        {
            if (selected is not null && !selected.Contains(spec.Index))
                continue;

            int rowCount = reader.GetTableRowCount(spec.Index);
            if (rowCount == 0)
                continue;

            views.Add(BuildView(reader, spec, rowCount, options));
        }

        return new MetadataTableProjection(views.ToImmutable());
    }

    /// <summary>
    /// Projects a single row of one table on demand, independent of any row
    /// window applied to a wider projection. This is the handle click-through
    /// primitive: a <see cref="HandleRef"/> whose target lies outside the current
    /// window is still reachable without re-projecting the target table.
    ///
    /// The row is returned inside its table's <see cref="MetadataTableView"/> so
    /// the caller also gets the column schema and the table's physical
    /// <see cref="MetadataTableView.RowCount"/>, which a single row cannot carry.
    ///
    /// <paramref name="table"/> names the target directly, so
    /// <see cref="MetadataProjectionOptions.Tables"/> and
    /// <see cref="MetadataProjectionOptions.StartRowId"/> are deliberately
    /// ignored here; only the cell budgets apply. Honouring the table selection
    /// would dead-end the very edges this method exists to follow — a caller
    /// browsing TypeRef could not follow a TypeRef row into TypeDef.
    ///
    /// Returns <see langword="null"/> when the image has no metadata, when
    /// <paramref name="table"/> is not one this projector supports, or when
    /// <paramref name="rowId"/> is past the table's last row.
    /// </summary>
    public static MetadataTableView? ProjectRow(
        PEReader peReader,
        TableIndex table,
        int rowId,
        MetadataProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowId, 1);
        options ??= new MetadataProjectionOptions();

        if (!peReader.HasMetadata)
            return null;

        TableSpec? match = null;
        foreach (var candidate in SupportedTables)
        {
            if (candidate.Index == table)
            {
                match = candidate;
                break;
            }
        }

        if (match is not { } spec)
            return null;

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);
        int rowCount = reader.GetTableRowCount(spec.Index);
        if (rowId > rowCount)
            return null;

        // Reuse the windowed reader rather than a second row path: a one-row
        // window at rowId is exactly this lookup, including malformed-row containment.
        return BuildView(reader, spec, rowCount, options with { StartRowId = rowId, MaxRowsPerTable = 1 });
    }

    /// <summary>
    /// Finds every row that points at <paramref name="targetTable"/> row
    /// <paramref name="targetRowId"/> — the reverse of the forward
    /// <see cref="HandleRef"/>/<see cref="HandleRange"/> edges a projection
    /// exposes, and the "who references this?" gesture an explorer needs.
    ///
    /// Both edge shapes are searched. A handle column matches when it names the
    /// row directly; a list column matches when the target falls inside its run,
    /// which is how ECMA-335 encodes ownership — so this answers which
    /// <c>TypeDef</c> declares a given <c>Field</c>, <c>MethodDef</c>, or which
    /// <c>MethodDef</c> owns a given <c>Param</c>.
    ///
    /// The scan always covers every supported table. It takes no
    /// <see cref="MetadataProjectionOptions"/> at all, and in particular offers
    /// no equivalent of <see cref="MetadataProjectionOptions.Tables"/>, because
    /// a reverse search narrowed to part of the image could report "nothing
    /// points here" while a pointer sat in an unsearched table. Its two blind
    /// spots are reported instead of hidden:
    /// <see cref="MetadataRowReferenceSet.Truncated"/> when
    /// <paramref name="maxReferences"/> stopped the scan, and
    /// <see cref="MetadataRowReferenceSet.UnreadableRows"/> for rows whose edges
    /// could not be fully determined.
    ///
    /// A dangling edge is not a reference: a handle whose row lies outside its
    /// target table projects as <see cref="MetadataValue.Malformed"/>, so it
    /// cannot match. It does, however, make its row an
    /// <see cref="MetadataRowReferenceSet.UnreadableRows"/> entry, because an
    /// edge column the projection could not resolve is an edge this search
    /// cannot account for.
    /// </summary>
    public static MetadataRowReferenceSet FindReferences(
        PEReader peReader,
        TableIndex targetTable,
        int targetRowId,
        int maxReferences = MetadataRowReferenceSet.DefaultMaxReferences)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetRowId, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferences);

        var target = new MetadataRowLocation(targetTable, targetRowId);
        var references = ImmutableArray.CreateBuilder<MetadataRowReference>();
        var unreadable = ImmutableArray.CreateBuilder<MetadataRowLocation>();

        if (!peReader.HasMetadata)
            return new MetadataRowReferenceSet(target, references.ToImmutable(), unreadable.ToImmutable(), Truncated: false);

        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        // The scan needs edges, not text. Handle and range cells carry their
        // target table and row id independently of these budgets, so trimming the
        // heap previews cannot change which rows match — it only avoids decoding
        // strings and blobs the result never shows.
        var scan = new MetadataProjectionOptions { MaxStringChars = 1, MaxPreviewBytes = 0 };

        bool truncated = false;
        foreach (var spec in SupportedTables)
        {
            if (truncated)
                break;

            int rowCount = reader.GetTableRowCount(spec.Index);
            for (int rid = 1; rid <= rowCount && !truncated; rid++)
            {
                ImmutableArray<MetadataValue> cells;
                try
                {
                    cells = spec.ReadRow(reader, rid, scan);
                }
                catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
                {
                    // The row's edges are unknowable, so record the blind spot
                    // rather than letting a missed reference look like an absent one.
                    unreadable.Add(new MetadataRowLocation(spec.Index, rid));
                    continue;
                }

                bool blind = false;
                for (int column = 0; column < cells.Length; column++)
                {
                    // A cell that failed to decode in an *edge* column may have
                    // been an edge onto the target. The cell-level readers
                    // contain such failures as Malformed rather than throwing, so
                    // ReadRow succeeds and the row would otherwise pass as fully
                    // searched. Record the blind spot instead, or a missed
                    // reference reads as an absent one.
                    //
                    // The column's declared kind decides, not the cell: a
                    // Malformed heap, scalar, or flags cell was never an edge and
                    // cannot hide a reference.
                    if (cells[column] is MetadataValue.Malformed
                        && spec.Columns[column].Kind is MetadataColumnKind.Handle or MetadataColumnKind.HandleRange)
                    {
                        blind = true;
                        continue;
                    }

                    if (!PointsAt(cells[column], targetTable, targetRowId, out var kind))
                        continue;

                    if (references.Count >= maxReferences)
                    {
                        truncated = true;
                        break;
                    }

                    references.Add(new MetadataRowReference(
                        new MetadataRowLocation(spec.Index, rid),
                        column,
                        spec.Columns[column].Name,
                        kind));
                }

                // Recorded once per row, and after the column loop, so a row with
                // one broken edge still reports the good edges it does have.
                if (blind)
                    unreadable.Add(new MetadataRowLocation(spec.Index, rid));
            }
        }

        return new MetadataRowReferenceSet(target, references.ToImmutable(), unreadable.ToImmutable(), truncated);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is an edge onto the target row. A handle
    /// names one row; a list column covers the half-open run
    /// <c>[StartRowId, EndRowId)</c>, so membership — not equality — decides.
    /// </summary>
    static bool PointsAt(MetadataValue value, TableIndex table, int rowId, out MetadataRowReferenceKind kind)
    {
        switch (value)
        {
            case MetadataValue.Handle handle
                when handle.Reference.TargetTable == table && handle.Reference.TargetRowId == rowId:
                kind = MetadataRowReferenceKind.Handle;
                return true;

            case MetadataValue.Range range
                when range.Reference.TargetTable == table
                    && rowId >= range.Reference.StartRowId
                    && rowId < range.Reference.EndRowId:
                kind = MetadataRowReferenceKind.Range;
                return true;

            default:
                kind = default;
                return false;
        }
    }

    static MetadataTableView BuildView(
        MetadataReader reader,
        TableSpec spec,
        int rowCount,
        MetadataProjectionOptions options)
    {
        int start = Math.Max(1, options.StartRowId);
        int budget = Math.Max(0, options.MaxRowsPerTable);

        // Widen before adding: a caller-supplied start near int.MaxValue would
        // otherwise overflow into a window that wrongly overlaps the table.
        long inclusiveEnd = (long)start + budget - 1;
        int end = (int)Math.Min(rowCount, inclusiveEnd);
        int projected = end < start ? 0 : end - start + 1;

        var rows = ImmutableArray.CreateBuilder<MetadataRow>(projected);

        for (int rid = start; rid <= end; rid++)
        {
            int token = ((int)spec.Index << 24) | rid;

            // A single malformed row must not abort the whole projection: contain
            // SRM's rejection as a typed Malformed row aligned to the columns.
            try
            {
                rows.Add(new MetadataRow(rid, token, spec.ReadRow(reader, rid, options)));
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                var malformed = ImmutableArray.CreateBuilder<MetadataValue>(spec.Columns.Length);
                for (int column = 0; column < spec.Columns.Length; column++)
                    malformed.Add(new MetadataValue.Malformed($"Row read failed: {ex.Message}"));

                rows.Add(new MetadataRow(rid, token, malformed.MoveToImmutable()));
            }
        }

        var truncation = projected < rowCount ? new MetadataTableTruncation(projected, rowCount) : null;
        return new MetadataTableView(spec.Index, spec.Name, rowCount, spec.Columns, rows.ToImmutable(), truncation);
    }

    // ---- Per-table column schemas ----------------------------------------

    static ImmutableArray<MetadataColumn> ModuleColumns =>
    [
        new("Generation", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
        new("Mvid", MetadataColumnKind.Heap),
        new("EncId", MetadataColumnKind.Heap),
        new("EncBaseId", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> TypeRefColumns =>
    [
        new("ResolutionScope", MetadataColumnKind.Handle, ResolutionScopeTargets),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> TypeDefColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
        new("Extends", MetadataColumnKind.Handle, TypeDefOrRefTargets),
        new("FieldList", MetadataColumnKind.HandleRange, [TableIndex.Field]),
        new("MethodList", MetadataColumnKind.HandleRange, [TableIndex.MethodDef]),
    ];

    static ImmutableArray<MetadataColumn> FieldColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodDefColumns =>
    [
        new("Rva", MetadataColumnKind.Scalar),
        new("ImplAttributes", MetadataColumnKind.Flags),
        new("Attributes", MetadataColumnKind.Flags),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
        new("ParamList", MetadataColumnKind.HandleRange, [TableIndex.Param]),
    ];

    static ImmutableArray<MetadataColumn> ParamColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("Sequence", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MemberRefColumns =>
    [
        new("Class", MetadataColumnKind.Handle, MemberRefParentTargets),
        new("Name", MetadataColumnKind.Heap),
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> ConstantColumns =>
    [
        new("Type", MetadataColumnKind.Scalar),
        new("Parent", MetadataColumnKind.Handle, HasConstantTargets),
        new("Value", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> CustomAttributeColumns =>
    [
        new("Parent", MetadataColumnKind.Handle, HasCustomAttributeTargets),
        new("Type", MetadataColumnKind.Handle, CustomAttributeTypeTargets),
        new("Value", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> AssemblyRefColumns =>
    [
        new("MajorVersion", MetadataColumnKind.Scalar),
        new("MinorVersion", MetadataColumnKind.Scalar),
        new("BuildNumber", MetadataColumnKind.Scalar),
        new("RevisionNumber", MetadataColumnKind.Scalar),
        new("Flags", MetadataColumnKind.Flags),
        new("PublicKeyOrToken", MetadataColumnKind.Heap),
        new("Name", MetadataColumnKind.Heap),
        new("Culture", MetadataColumnKind.Heap),
        new("HashValue", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> StandAloneSigColumns =>
    [
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodImplColumns =>
    [
        new("Class", MetadataColumnKind.Handle, [TableIndex.TypeDef]),
        new("MethodBody", MetadataColumnKind.Handle, MethodDefOrRefTargets),
        new("MethodDeclaration", MetadataColumnKind.Handle, MethodDefOrRefTargets),
    ];

    static ImmutableArray<MetadataColumn> TypeSpecColumns =>
    [
        new("Signature", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> AssemblyColumns =>
    [
        new("HashAlgId", MetadataColumnKind.Scalar),
        new("MajorVersion", MetadataColumnKind.Scalar),
        new("MinorVersion", MetadataColumnKind.Scalar),
        new("BuildNumber", MetadataColumnKind.Scalar),
        new("RevisionNumber", MetadataColumnKind.Scalar),
        new("Flags", MetadataColumnKind.Flags),
        new("PublicKey", MetadataColumnKind.Heap),
        new("Name", MetadataColumnKind.Heap),
        new("Culture", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> ExportedTypeColumns =>
    [
        new("Attributes", MetadataColumnKind.Flags),
        new("TypeDefId", MetadataColumnKind.Scalar),
        new("Name", MetadataColumnKind.Heap),
        new("Namespace", MetadataColumnKind.Heap),
        new("Implementation", MetadataColumnKind.Handle, ImplementationTargets),
    ];

    static ImmutableArray<MetadataColumn> GenericParamColumns =>
    [
        new("Number", MetadataColumnKind.Scalar),
        new("Attributes", MetadataColumnKind.Flags),
        new("Owner", MetadataColumnKind.Handle, TypeOrMethodDefTargets),
        new("Name", MetadataColumnKind.Heap),
    ];

    static ImmutableArray<MetadataColumn> MethodSpecColumns =>
    [
        new("Method", MetadataColumnKind.Handle, MethodDefOrRefTargets),
        new("Instantiation", MetadataColumnKind.Heap),
    ];

    // ---- Per-table row readers -------------------------------------------

    static ImmutableArray<MetadataValue> ReadModuleRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var module = reader.GetModuleDefinition();
        return
        [
            new MetadataValue.Scalar(module.Generation, module.Generation.ToString()),
            StringCell(reader, module.Name, options),
            GuidCell(reader, module.Mvid),
            GuidCell(reader, module.GenerationId),
            GuidCell(reader, module.BaseGenerationId),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var typeRef = reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(rid));
        return
        [
            HandleCell(reader, typeRef.ResolutionScope, options),
            StringCell(reader, typeRef.Name, options),
            StringCell(reader, typeRef.Namespace, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeDefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var handle = MetadataTokens.TypeDefinitionHandle(rid);
        var typeDef = reader.GetTypeDefinition(handle);
        return
        [
            FlagsCell((long)typeDef.Attributes, typeDef.Attributes.ToString()),
            StringCell(reader, typeDef.Name, options),
            StringCell(reader, typeDef.Namespace, options),
            HandleCell(reader, typeDef.BaseType, options),
            RangeCell(TableIndex.Field, typeDef.GetFields()),
            RangeCell(TableIndex.MethodDef, typeDef.GetMethods()),
        ];
    }

    static ImmutableArray<MetadataValue> ReadFieldRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var field = reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(rid));
        return
        [
            FlagsCell((long)field.Attributes, field.Attributes.ToString()),
            StringCell(reader, field.Name, options),
            BlobCell(reader, field.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodDefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(rid));
        return
        [
            new MetadataValue.Scalar(method.RelativeVirtualAddress, $"0x{method.RelativeVirtualAddress:X8}"),
            FlagsCell((long)method.ImplAttributes, method.ImplAttributes.ToString()),
            FlagsCell((long)method.Attributes, method.Attributes.ToString()),
            StringCell(reader, method.Name, options),
            BlobCell(reader, method.Signature, options),
            RangeCell(TableIndex.Param, method.GetParameters()),
        ];
    }

    static ImmutableArray<MetadataValue> ReadParamRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var param = reader.GetParameter(MetadataTokens.ParameterHandle(rid));
        return
        [
            FlagsCell((long)param.Attributes, param.Attributes.ToString()),
            new MetadataValue.Scalar(param.SequenceNumber, param.SequenceNumber.ToString()),
            StringCell(reader, param.Name, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMemberRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var memberRef = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(rid));
        return
        [
            HandleCell(reader, memberRef.Parent, options),
            StringCell(reader, memberRef.Name, options),
            BlobCell(reader, memberRef.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadConstantRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var constant = reader.GetConstant(MetadataTokens.ConstantHandle(rid));
        return
        [
            new MetadataValue.Scalar((long)constant.TypeCode, constant.TypeCode.ToString()),
            HandleCell(reader, constant.Parent, options),
            BlobCell(reader, constant.Value, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadCustomAttributeRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var attribute = reader.GetCustomAttribute(MetadataTokens.CustomAttributeHandle(rid));
        return
        [
            HandleCell(reader, attribute.Parent, options),
            HandleCell(reader, attribute.Constructor, options),
            BlobCell(reader, attribute.Value, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadAssemblyRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var assemblyRef = reader.GetAssemblyReference(MetadataTokens.AssemblyReferenceHandle(rid));
        var version = assemblyRef.Version;
        return
        [
            new MetadataValue.Scalar(version.Major, version.Major.ToString()),
            new MetadataValue.Scalar(version.Minor, version.Minor.ToString()),
            new MetadataValue.Scalar(version.Build, version.Build.ToString()),
            new MetadataValue.Scalar(version.Revision, version.Revision.ToString()),
            FlagsCell((long)assemblyRef.Flags, assemblyRef.Flags.ToString()),
            BlobCell(reader, assemblyRef.PublicKeyOrToken, options),
            StringCell(reader, assemblyRef.Name, options),
            StringCell(reader, assemblyRef.Culture, options),
            BlobCell(reader, assemblyRef.HashValue, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadStandAloneSigRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var signature = reader.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(rid));
        return
        [
            BlobCell(reader, signature.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodImplRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var methodImpl = reader.GetMethodImplementation(MetadataTokens.MethodImplementationHandle(rid));
        return
        [
            HandleCell(reader, methodImpl.Type, options),
            HandleCell(reader, methodImpl.MethodBody, options),
            HandleCell(reader, methodImpl.MethodDeclaration, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeSpecRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var typeSpec = reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(rid));
        return
        [
            BlobCell(reader, typeSpec.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadAssemblyRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var assembly = reader.GetAssemblyDefinition();
        var version = assembly.Version;
        return
        [
            new MetadataValue.Scalar((long)assembly.HashAlgorithm, assembly.HashAlgorithm.ToString()),
            new MetadataValue.Scalar(version.Major, version.Major.ToString()),
            new MetadataValue.Scalar(version.Minor, version.Minor.ToString()),
            new MetadataValue.Scalar(version.Build, version.Build.ToString()),
            new MetadataValue.Scalar(version.Revision, version.Revision.ToString()),
            FlagsCell((long)assembly.Flags, assembly.Flags.ToString()),
            BlobCell(reader, assembly.PublicKey, options),
            StringCell(reader, assembly.Name, options),
            StringCell(reader, assembly.Culture, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadExportedTypeRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var exportedType = reader.GetExportedType(MetadataTokens.ExportedTypeHandle(rid));
        int typeDefId = exportedType.GetTypeDefinitionId();
        return
        [
            FlagsCell((long)exportedType.Attributes, exportedType.Attributes.ToString()),
            new MetadataValue.Scalar(typeDefId, $"0x{typeDefId:X8}"),
            StringCell(reader, exportedType.Name, options),
            StringCell(reader, exportedType.Namespace, options),
            HandleCell(reader, exportedType.Implementation, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadGenericParamRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var genericParam = reader.GetGenericParameter(MetadataTokens.GenericParameterHandle(rid));
        return
        [
            new MetadataValue.Scalar(genericParam.Index, genericParam.Index.ToString()),
            FlagsCell((long)genericParam.Attributes, genericParam.Attributes.ToString()),
            HandleCell(reader, genericParam.Parent, options),
            StringCell(reader, genericParam.Name, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMethodSpecRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var methodSpec = reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(rid));
        return
        [
            HandleCell(reader, methodSpec.Method, options),
            BlobCell(reader, methodSpec.Signature, options),
        ];
    }

    // ---- Cell builders ----------------------------------------------------

    static MetadataValue StringCell(MetadataReader reader, StringHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetString(handle);
            string text = EscapeText(raw, options.MaxStringChars, out bool truncated);
            return new MetadataValue.HeapReference(
                HeapKind.String, HeapOffset(handle), raw.Length, text, text, truncated);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"String heap read failed: {ex.Message}");
        }
    }

    static MetadataValue GuidCell(MetadataReader reader, GuidHandle handle)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string text = reader.GetGuid(handle).ToString();
            return new MetadataValue.HeapReference(
                HeapKind.Guid, HeapOffset(handle), 16, text, text, Truncated: false);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Guid heap read failed: {ex.Message}");
        }
    }

    static MetadataValue BlobCell(MetadataReader reader, BlobHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            var blobReader = reader.GetBlobReader(handle);
            int length = blobReader.Length;
            int take = Math.Min(length, Math.Max(0, options.MaxPreviewBytes));
            byte[] bytes = blobReader.ReadBytes(take);
            string preview = Convert.ToHexString(bytes);

            return new MetadataValue.HeapReference(
                HeapKind.Blob, HeapOffset(handle), length, Text: null, preview, Truncated: take < length);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Blob heap read failed: {ex.Message}");
        }
    }

    static int HeapOffset(StringHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static int HeapOffset(GuidHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static int HeapOffset(BlobHandle handle)
    {
        try
        {
            return MetadataTokens.GetHeapOffset(handle);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    static MetadataValue HandleCell(MetadataReader reader, EntityHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            if (!MetadataTokens.TryGetTableIndex(handle.Kind, out var table))
                return new MetadataValue.Malformed($"Handle kind {handle.Kind} does not map to a table.");

            int rid = MetadataTokens.GetRowNumber(handle);
            int token = MetadataTokens.GetToken(handle);

            // A coded index can decode to a row that does not exist in the target
            // table; a dangling edge is a visible failure, not a resolvable handle.
            int targetRows = reader.GetTableRowCount(table);
            if (rid < 1 || rid > targetRows)
                return new MetadataValue.Malformed(
                    $"Handle 0x{token:X8} targets {table} row {rid}, outside [1, {targetRows}].");

            string? display = ResolveHandleDisplay(reader, handle);
            bool displayTruncated = false;
            if (display is not null)
                display = NeutralizeControls(display, options.MaxStringChars, out displayTruncated);

            return new MetadataValue.Handle(new HandleRef(table, rid, token, display, displayTruncated));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"Handle resolution failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, FieldDefinitionHandleCollection fields)
    {
        if (fields.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in fields)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + fields.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, MethodDefinitionHandleCollection methods)
    {
        if (methods.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in methods)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + methods.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static MetadataValue RangeCell(TableIndex target, ParameterHandleCollection parameters)
    {
        if (parameters.Count == 0)
            return new MetadataValue.Nil();

        try
        {
            int start = 0;
            foreach (var handle in parameters)
            {
                start = MetadataTokens.GetRowNumber(handle);
                break;
            }

            return new MetadataValue.Range(new HandleRange(target, start, start + parameters.Count));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed($"List column read failed: {ex.Message}");
        }
    }

    static string? ResolveHandleDisplay(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                case HandleKind.MethodDefinition:
                case HandleKind.MemberReference:
                case HandleKind.FieldDefinition:
                    string text = ILTokenResolver.ResolveToken(reader, MetadataTokens.GetToken(handle));
                    return text.StartsWith("0x", StringComparison.Ordinal) ? null : text;

                case HandleKind.AssemblyReference:
                    return reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)handle).Name);

                case HandleKind.ModuleReference:
                    return reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name);

                case HandleKind.ModuleDefinition:
                    return reader.GetString(reader.GetModuleDefinition().Name);

                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    static MetadataValue FlagsCell(long raw, string decoded)
        => new MetadataValue.Flags(raw, decoded);

    /// <summary>
    /// Escapes a decoded heap string for use as data and bounds the EMITTED
    /// preview to <paramref name="maxChars"/> characters. Backslash and quote
    /// are escaped, and every control character (including ESC) is rendered as
    /// <c>\uXXXX</c> so the value cannot inject terminal control sequences or
    /// break structured output. Because escaping can expand a single character,
    /// the budget is enforced on the output length, not the input length;
    /// <paramref name="truncated"/> reports whether any input was dropped to keep
    /// the preview within budget.
    /// </summary>
    static string EscapeText(string value, int maxChars, out bool truncated)
        => EscapeCore(value, maxChars, escapeStructural: true, out truncated);

    /// <summary>
    /// Renders every control character in a display string as <c>\uXXXX</c>,
    /// leaving all other characters (including the structural <c>::</c>, quotes,
    /// and generic-arity marks in resolved names) intact, and bounds the EMITTED
    /// text to <paramref name="maxChars"/> characters so a large resolved name
    /// cannot be re-materialized across every referencing row. The budget is
    /// enforced on the output length, not the input length;
    /// <paramref name="truncated"/> reports whether any input was dropped to keep
    /// the text within budget.
    /// </summary>
    static string NeutralizeControls(string value, int maxChars, out bool truncated)
        => EscapeCore(value, maxChars, escapeStructural: false, out truncated);

    /// <summary>
    /// Shared budget-bounded escaper. Walks the UTF-16 value one scalar at a
    /// time so a well-formed surrogate pair is kept atomic — the budget boundary
    /// can never retain a lone (malformed) surrogate — while an unpaired
    /// surrogate is rendered as <c>\uXXXX</c> so it cannot corrupt the output on
    /// UTF-8 conversion. When <paramref name="escapeStructural"/> is set,
    /// <c>\ " \n \r \t</c> are escaped for use as data; either way every control
    /// character is rendered as <c>\uXXXX</c>. The <paramref name="maxChars"/>
    /// budget is enforced on the emitted length; <paramref name="truncated"/>
    /// reports whether any input was dropped to stay within it.
    /// </summary>
    static string EscapeCore(string value, int maxChars, bool escapeStructural, out bool truncated)
    {
        int limit = Math.Max(0, maxChars);
        var builder = new System.Text.StringBuilder(Math.Min(value.Length, limit));
        truncated = false;

        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];

            // A well-formed surrogate pair is one scalar; emit it atomically.
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (builder.Length + 2 > limit)
                {
                    truncated = true;
                    break;
                }

                builder.Append(c).Append(value[i + 1]);
                i += 2;
                continue;
            }

            // A lone surrogate is ill-formed text; escape it rather than emit it.
            if (char.IsSurrogate(c))
            {
                if (builder.Length + 6 > limit)
                {
                    truncated = true;
                    break;
                }

                builder.Append("\\u").Append(((int)c).ToString("X4"));
                i++;
                continue;
            }

            string? structural = escapeStructural
                ? c switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => null,
                }
                : null;

            int width = structural is not null ? structural.Length : (IsControl(c) ? 6 : 1);
            if (builder.Length + width > limit)
            {
                truncated = true;
                break;
            }

            if (structural is not null)
                builder.Append(structural);
            else if (IsControl(c))
                builder.Append("\\u").Append(((int)c).ToString("X4"));
            else
                builder.Append(c);

            i++;
        }

        return builder.ToString();
    }

    // C0 controls, DEL, and the C1 control range — none of which are safe to
    // emit verbatim into a terminal or a structured record.
    static bool IsControl(char c) => c < ' ' || c == '\x7f' || (c >= '\x80' && c <= '\x9f');

    readonly record struct TableSpec(
        TableIndex Index,
        string Name,
        ImmutableArray<MetadataColumn> Columns,
        Func<MetadataReader, int, MetadataProjectionOptions, ImmutableArray<MetadataValue>> ReadRow);
}
