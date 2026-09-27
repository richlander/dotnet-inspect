using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning.Experiments;

// Experiment only: row windows as phases over one cursor, to test whether
// skipping work is the performance answer. An open query is split into a
// predicate (which units are selected) and a projection (the row for a
// selected unit), so skipped and counted units never pay for projection.

/// <summary>The selection half of an open query, with its type scope.</summary>
internal interface IRowPredicate
{
    bool TypeInScope(MetadataReader reader, TypeDefinition type);

    bool Test(ref MethodDefinitionUnit unit);
}

/// <summary>The projection half of an open query: the row for a selected unit.</summary>
internal interface IRowProjection<TRow>
{
    TRow Project(ref MethodDefinitionUnit unit);
}

/// <summary>A method row like the legacy classified rows: name, declaring type, and signature text.</summary>
public readonly record struct MethodTextRow(string Name, string DeclaringType, string Signature);

/// <summary>
/// A row request as phases: Discard until <see cref="Skip"/> selected units,
/// then Rows until <see cref="Take"/> (all when negative), with a Count until
/// the end alongside when <see cref="CountAll"/> is set.
/// </summary>
public readonly record struct RowWindowRequest(int Skip, int Take, bool CountAll)
{
    public static RowWindowRequest Head(int n) => new(0, n, false);

    public static RowWindowRequest Window(int first, int last) => new(first - 1, last - first + 1, false);

    public static RowWindowRequest All => new(0, -1, false);
}

/// <summary>
/// The result, with how the last phase ended. A strict window whose start does
/// not exist is reported as <see cref="WindowStartMissing"/> for the row
/// selection owner to turn into its failure.
/// </summary>
public sealed record RowWindowResult<TRow>(
    ImmutableArray<TRow> Rows,
    int? Total,
    bool Exhausted,
    bool WindowStartMissing,
    int UnitsVisited);

/// <summary>Public, non-accessor methods on types whose name does not start with '&lt;': a dense population.</summary>
internal struct PublicMethodPredicate : IRowPredicate
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        ClassifiedScope.IsScopedType(reader, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Test(ref MethodDefinitionUnit unit) =>
        ClassifiedScope.IsScopedMethod(unit.Reader, unit.MethodDefinition);
}

/// <summary>The async methods in the same scope: a sparse population.</summary>
internal struct AsyncMethodRowPredicate : IRowPredicate
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        AsyncMethodScope.IsCountedType(reader, type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Test(ref MethodDefinitionUnit unit) =>
        AsyncMethodScope.IsCountedMethod(unit.Reader, unit.MethodDefinition);
}

/// <summary>The legacy-shaped row: text a person reads, and the expensive part of a row.</summary>
internal struct MethodTextProjection : IRowProjection<MethodTextRow>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly MethodTextRow Project(ref MethodDefinitionUnit unit) =>
        MethodText.Row(unit.Reader, unit.TypeDefinition, unit.MethodDefinition);
}

public static class MethodText
{
    public static MethodTextRow Row(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        string ns = reader.GetString(type.Namespace);
        string declaring = ns.Length == 0 ? reader.GetString(type.Name) : ns + "." + reader.GetString(type.Name);
        string signature;
        try
        {
            MethodSignature<string> decoded = method.DecodeSignature(TextProvider.Instance, null);
            var text = new StringBuilder();
            text.Append(decoded.ReturnType).Append(' ').Append(name).Append('(');
            for (int i = 0; i < decoded.ParameterTypes.Length; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(decoded.ParameterTypes[i]);
            }

            signature = text.Append(')').ToString();
        }
        catch (BadImageFormatException)
        {
            signature = name + "(...)";
        }

        return new(name, declaring, signature);
    }

    sealed class TextProvider : ISignatureTypeProvider<string, object?>
    {
        public static TextProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

        public string GetByReferenceType(string elementType) => "ref " + elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? context, int index) => "!" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}

/// <summary>
/// The phase kernel: Discard, then Rows, with a Count alongside, over one
/// cursor, specialized to the predicate and projection. It stops before
/// advancing the cursor once every phase is done.
/// </summary>
internal static class RowPhaseKernel
{
    public static RowWindowResult<TRow> Run<TPredicate, TProjection, TRow>(
        PEReader peReader,
        RowWindowRequest request)
        where TPredicate : struct, IRowPredicate
        where TProjection : struct, IRowProjection<TRow>
    {
        MetadataReader reader = peReader.GetMetadataReader();
        TPredicate predicate = default;
        TProjection projection = default;
        var unit = new MethodDefinitionUnit(reader, peReader, lookup: null);
        ImmutableArray<TRow>.Builder rows = ImmutableArray.CreateBuilder<TRow>(request.Take > 0 ? request.Take : 16);
        int selected = 0;
        int visited = 0;
        bool exhausted = true;
        bool rowsOpen = request.Take != 0;

        try
        {
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition = reader.GetTypeDefinition(typeHandle);
                if (!predicate.TypeInScope(reader, typeDefinition))
                    continue;

                foreach (MethodDefinitionHandle methodHandle in typeDefinition.GetMethods())
                {
                    unit.MoveTo(typeHandle, typeDefinition, methodHandle);
                    visited++;
                    if (!predicate.Test(ref unit))
                        continue;

                    // Discard phase: only the predicate ran.
                    if (++selected <= request.Skip || !rowsOpen)
                        continue;

                    // Rows phase.
                    rows.Add(projection.Project(ref unit));
                    if (request.Take > 0 && rows.Count == request.Take)
                    {
                        rowsOpen = false;
                        if (!request.CountAll)
                        {
                            exhausted = false;
                            goto Done;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
        {
            throw new InvalidDataException($"{unit.Label} could not be analyzed: {ex.Message}", ex);
        }

    Done:
        return new(
            rows.DrainToImmutable(),
            request.CountAll ? selected : null,
            exhausted,
            request.Skip > 0 && exhausted && selected <= request.Skip,
            visited);
    }
}

public static class RowWindowExperiment
{
    public static RowWindowResult<MethodTextRow> Kernel(bool dense, PEReader peReader, RowWindowRequest request) =>
        dense
            ? RowPhaseKernel.Run<PublicMethodPredicate, MethodTextProjection, MethodTextRow>(peReader, request)
            : RowPhaseKernel.Run<AsyncMethodRowPredicate, MethodTextProjection, MethodTextRow>(peReader, request);

    /// <summary>The hand-written loop for one request: the same cursor, no generics.</summary>
    public static RowWindowResult<MethodTextRow> Hand(bool dense, PEReader peReader, RowWindowRequest request)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        ImmutableArray<MethodTextRow>.Builder rows = ImmutableArray.CreateBuilder<MethodTextRow>(request.Take > 0 ? request.Take : 16);
        int selected = 0;
        int visited = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (dense ? !ClassifiedScope.IsScopedType(reader, type) : !AsyncMethodScope.IsCountedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                visited++;
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (dense ? !ClassifiedScope.IsScopedMethod(reader, method) : !AsyncMethodScope.IsCountedMethod(reader, method))
                    continue;
                if (++selected <= request.Skip || (request.Take >= 0 && rows.Count >= request.Take))
                    continue;
                rows.Add(MethodText.Row(reader, type, method));
                if (request.Take > 0 && rows.Count == request.Take && !request.CountAll)
                    return new(rows.DrainToImmutable(), null, false, false, visited);
            }
        }

        return new(rows.DrainToImmutable(), request.CountAll ? selected : null, true, request.Skip > 0 && selected <= request.Skip, visited);
    }

    /// <summary>Today's shape: build every row, then select the window.</summary>
    public static RowWindowResult<MethodTextRow> Materialize(bool dense, PEReader peReader, RowWindowRequest request)
    {
        RowWindowResult<MethodTextRow> all = Hand(dense, peReader, RowWindowRequest.All);
        return Trim(all.Rows, request, all.UnitsVisited);
    }

    /// <summary>The legacy rows for the sparse population: classify every public method, then filter and select.</summary>
    public static RowWindowResult<MethodTextRow> Legacy(PEReader peReader, RowWindowRequest request)
    {
        ImmutableArray<MethodTextRow> rows =
        [
            .. MethodClassificationScanner.Scan(peReader)
                .Where(static m => m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync)
                .Select(static m => new MethodTextRow(m.MethodName, m.DeclaringType, m.Signature)),
        ];
        return Trim(rows, request, 0);
    }

    static RowWindowResult<MethodTextRow> Trim(ImmutableArray<MethodTextRow> all, RowWindowRequest request, int visited)
    {
        int start = Math.Min(request.Skip, all.Length);
        int length = request.Take < 0 ? all.Length - start : Math.Min(request.Take, all.Length - start);
        return new(
            all.Slice(start, length),
            request.CountAll ? all.Length : null,
            true,
            request.Skip > 0 && all.Length <= request.Skip,
            visited);
    }
}
