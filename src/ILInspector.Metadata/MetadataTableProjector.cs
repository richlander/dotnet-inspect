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

    static readonly ImmutableArray<TableSpec> SupportedTables =
    [
        new(TableIndex.Module, "Module", ModuleColumns, ReadModuleRow),
        new(TableIndex.TypeRef, "TypeRef", TypeRefColumns, ReadTypeRefRow),
        new(TableIndex.TypeDef, "TypeDef", TypeDefColumns, ReadTypeDefRow),
        new(TableIndex.Field, "Field", FieldColumns, ReadFieldRow),
        new(TableIndex.MethodDef, "MethodDef", MethodDefColumns, ReadMethodDefRow),
        new(TableIndex.Param, "Param", ParamColumns, ReadParamRow),
        new(TableIndex.MemberRef, "MemberRef", MemberRefColumns, ReadMemberRefRow),
        new(TableIndex.CustomAttribute, "CustomAttribute", CustomAttributeColumns, ReadCustomAttributeRow),
        new(TableIndex.AssemblyRef, "AssemblyRef", AssemblyRefColumns, ReadAssemblyRefRow),
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

        var reader = peReader.GetMetadataReader();

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

    static MetadataTableView BuildView(
        MetadataReader reader,
        TableSpec spec,
        int rowCount,
        MetadataProjectionOptions options)
    {
        int limit = Math.Min(rowCount, Math.Max(0, options.MaxRowsPerTable));
        var rows = ImmutableArray.CreateBuilder<MetadataRow>(limit);

        for (int rid = 1; rid <= limit; rid++)
        {
            int token = ((int)spec.Index << 24) | rid;
            var cells = spec.ReadRow(reader, rid, options);
            rows.Add(new MetadataRow(rid, token, cells));
        }

        var truncation = limit < rowCount ? new MetadataTableTruncation(limit, rowCount) : null;
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

    // ---- Per-table row readers -------------------------------------------

    static ImmutableArray<MetadataValue> ReadModuleRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var module = reader.GetModuleDefinition();
        return
        [
            new MetadataValue.Scalar(module.Generation, module.Generation.ToString()),
            StringCell(reader, module.Name),
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
            HandleCell(reader, typeRef.ResolutionScope),
            StringCell(reader, typeRef.Name),
            StringCell(reader, typeRef.Namespace),
        ];
    }

    static ImmutableArray<MetadataValue> ReadTypeDefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var handle = MetadataTokens.TypeDefinitionHandle(rid);
        var typeDef = reader.GetTypeDefinition(handle);
        return
        [
            FlagsCell((long)typeDef.Attributes, typeDef.Attributes.ToString()),
            StringCell(reader, typeDef.Name),
            StringCell(reader, typeDef.Namespace),
            HandleCell(reader, typeDef.BaseType),
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
            StringCell(reader, field.Name),
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
            StringCell(reader, method.Name),
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
            StringCell(reader, param.Name),
        ];
    }

    static ImmutableArray<MetadataValue> ReadMemberRefRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var memberRef = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(rid));
        return
        [
            HandleCell(reader, memberRef.Parent),
            StringCell(reader, memberRef.Name),
            BlobCell(reader, memberRef.Signature, options),
        ];
    }

    static ImmutableArray<MetadataValue> ReadCustomAttributeRow(MetadataReader reader, int rid, MetadataProjectionOptions options)
    {
        var attribute = reader.GetCustomAttribute(MetadataTokens.CustomAttributeHandle(rid));
        return
        [
            HandleCell(reader, attribute.Parent),
            HandleCell(reader, attribute.Constructor),
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
            StringCell(reader, assemblyRef.Name),
            StringCell(reader, assemblyRef.Culture),
            BlobCell(reader, assemblyRef.HashValue, options),
        ];
    }

    // ---- Cell builders ----------------------------------------------------

    static MetadataValue StringCell(MetadataReader reader, StringHandle handle)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            string raw = reader.GetString(handle);
            string text = ILStringEscaper.ForDisplay(raw);
            return new MetadataValue.HeapReference(HeapKind.String, HeapOffset(handle), raw.Length, text, text);
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
            return new MetadataValue.HeapReference(HeapKind.Guid, HeapOffset(handle), 16, text, text);
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
            if (take < length)
                preview += "\u2026";

            return new MetadataValue.HeapReference(HeapKind.Blob, HeapOffset(handle), length, Text: null, preview);
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

    static MetadataValue HandleCell(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil)
            return new MetadataValue.Nil();

        try
        {
            if (!MetadataTokens.TryGetTableIndex(handle.Kind, out var table))
                return new MetadataValue.Malformed($"Handle kind {handle.Kind} does not map to a table.");

            int rid = MetadataTokens.GetRowNumber(handle);
            int token = MetadataTokens.GetToken(handle);
            string? display = ResolveHandleDisplay(reader, handle);
            return new MetadataValue.Handle(new HandleRef(table, rid, token, display));
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
                    return ILStringEscaper.ForDisplay(
                        reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)handle).Name));

                case HandleKind.ModuleReference:
                    return ILStringEscaper.ForDisplay(
                        reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name));

                case HandleKind.ModuleDefinition:
                    return ILStringEscaper.ForDisplay(reader.GetString(reader.GetModuleDefinition().Name));

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

    readonly record struct TableSpec(
        TableIndex Index,
        string Name,
        ImmutableArray<MetadataColumn> Columns,
        Func<MetadataReader, int, MetadataProjectionOptions, ImmutableArray<MetadataValue>> ReadRow);
}
