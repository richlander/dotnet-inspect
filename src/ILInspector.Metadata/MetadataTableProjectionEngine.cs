using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// Owns the supported table declarations, per-table column schemas and row readers, cell
/// builders, handle display resolution, and heap offset lookups that turn one SRM
/// <see cref="MetadataReader"/> row into a projected <see cref="MetadataTableView"/>.
///
/// This is the shared substrate <see cref="MetadataTableProjector"/>'s facade,
/// <see cref="MetadataRowReferenceFinder"/>, and <see cref="MetadataHeapProjector"/> all read
/// directly, so the table declarations and cell decode logic exist exactly once rather than
/// forking a second row/cell decoder.
/// </summary>
internal static class MetadataTableProjectionEngine
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

    /// <summary>
    /// The tables this projector models, in ECMA-335 table order, together with their column
    /// schema and row reader. A table absent from this set is not projected at all.
    /// </summary>
    internal static readonly ImmutableArray<TableSpec> SupportedTables =
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

    internal static bool TryGetTableSpec(TableIndex table, out TableSpec spec)
    {
        foreach (var candidate in SupportedTables)
        {
            if (candidate.Index == table)
            {
                spec = candidate;
                return true;
            }
        }

        spec = default;
        return false;
    }

    /// <summary>
    /// Projects the supported metadata tables of an already-open <paramref name="reader"/>.
    /// Assumes the caller has already validated the source image has metadata.
    /// </summary>
    internal static MetadataTableProjection Project(MetadataReader reader, MetadataProjectionOptions options)
    {
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
    /// Projects a single row of one table on demand, independent of any row window applied to a
    /// wider projection. Assumes the caller has already validated the source image has metadata
    /// and that <paramref name="rowId"/> is at least 1.
    ///
    /// Returns <see langword="null"/> when <paramref name="rowId"/> is past the table's last row.
    /// </summary>
    internal static MetadataTableView? ProjectRow(
        MetadataReader reader,
        TableSpec spec,
        int rowId,
        MetadataProjectionOptions options)
    {
        int rowCount = reader.GetTableRowCount(spec.Index);
        if (rowId > rowCount)
            return null;

        // Reuse the windowed reader rather than a second row path: a one-row
        // window at rowId is exactly this lookup, including malformed-row containment.
        return BuildView(reader, spec, rowCount, options with { StartRowId = rowId, MaxRowsPerTable = 1 });
    }

    internal static MetadataTableView BuildView(
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
                    malformed.Add(new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Row read failed: {ex.Message}")));

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

    internal static MetadataValue StringCell(MetadataReader reader, StringHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetString(handle);
            int offset = HeapOffset(handle);
            InertString text = ContainCellText(
                raw,
                options.MaxStringChars,
                options.UntrustedText,
                TextOrigin.At(HeapKind.String, offset));
            return new MetadataValue.HeapReference(
                HeapKind.String, offset, raw.Length, text, text, text.IsTruncated);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"String heap read failed: {ex.Message}"));
        }
    }

    internal static MetadataValue UserStringCell(MetadataReader reader, UserStringHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetUserString(handle);
            int offset = MetadataTokens.GetHeapOffset(handle);
            InertString text = ContainCellText(
                raw,
                options.MaxStringChars,
                options.UntrustedText,
                TextOrigin.At(HeapKind.UserString, offset));
            return new MetadataValue.HeapReference(
                HeapKind.UserString, offset, raw.Length, text, text, text.IsTruncated);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"UserString heap read failed: {ex.Message}"));
        }
    }

    internal static MetadataValue GuidCell(MetadataReader reader, GuidHandle handle)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            // A GUID's own spelling is hex and dashes, so containment cannot change it;
            // it goes through the same policy anyway so that no heap value reaches the
            // projection as raw text and the type carries that uniformly.
            InertString text = ContainCellText(reader.GetGuid(handle).ToString(), int.MaxValue);
            return new MetadataValue.HeapReference(
                HeapKind.Guid, HeapOffset(handle), 16, text, text, Truncated: false);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Guid heap read failed: {ex.Message}"));
        }
    }

    internal static MetadataValue BlobCell(MetadataReader reader, BlobHandle handle, MetadataProjectionOptions options)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            var blobReader = reader.GetBlobReader(handle);
            int length = blobReader.Length;
            int take = Math.Min(length, Math.Max(0, options.MaxPreviewBytes));
            byte[] bytes = blobReader.ReadBytes(take);
            InertString preview = ContainCellText(Convert.ToHexString(bytes), int.MaxValue);

            // The blob's partiality is a byte-level fact established before there was any
            // text to bound, so it cannot be read back off the hex: that hex is a complete
            // spelling of the bytes that were read.
            return new MetadataValue.HeapReference(
                HeapKind.Blob, HeapOffset(handle), length, Text: null, preview, Truncated: take < length);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Blob heap read failed: {ex.Message}"));
        }
    }

    internal static int HeapOffset(StringHandle handle)
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

    internal static int HeapOffset(GuidHandle handle)
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

    internal static int HeapOffset(BlobHandle handle)
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
                return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Handle kind {handle.Kind} does not map to a table."));

            int rid = MetadataTokens.GetRowNumber(handle);
            int token = MetadataTokens.GetToken(handle);

            // A coded index can decode to a row that does not exist in the target
            // table; a dangling edge is a visible failure, not a resolvable handle.
            int targetRows = reader.GetTableRowCount(table);
            if (rid < 1 || rid > targetRows)
                return new MetadataValue.Malformed(
                    InertString.Format(TextPolicy.Field, $"Handle 0x{token:X8} targets {table} row {rid}, outside [1, {targetRows}]."));

            string? resolved = ResolveHandleDisplay(reader, handle);
            InertString? display = resolved is null
                ? null
                : ContainCellText(
                    resolved,
                    options.MaxStringChars,
                    options.UntrustedText,
                    TextOrigin.Named("a resolved handle display"));

            return new MetadataValue.Handle(new HandleRef(table, rid, token, display));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"Handle resolution failed: {ex.Message}"));
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
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"List column read failed: {ex.Message}"));
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
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"List column read failed: {ex.Message}"));
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
            return new MetadataValue.Malformed(InertString.Format(TextPolicy.Field, $"List column read failed: {ex.Message}"));
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

    internal static InertString ContainCellText(
        string value,
        int maxChars,
        UntrustedTextMode mode = UntrustedTextMode.Contain,
        TextOrigin origin = default)
    {
        if (mode is UntrustedTextMode.Refuse
            && !InertString.IsPermitted(TextPolicy.Field, value, out ScalarViolation? violation))
        {
            throw new UntrustedTextException(
                origin, violation.Value.Index, violation.Value.Scalar, violation.Value.Category);
        }

        return new InertString(TextPolicy.Field, value, maxChars);
    }

    /// <summary>
    /// One supported table's identity, column schema, and row reader, kept together so a
    /// consumer cannot read a table's columns out of step with the reader that fills them.
    /// </summary>
    internal readonly record struct TableSpec(
        TableIndex Index,
        string Name,
        ImmutableArray<MetadataColumn> Columns,
        Func<MetadataReader, int, MetadataProjectionOptions, ImmutableArray<MetadataValue>> ReadRow);
}
