using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Instructions;

public enum IlDiffKind
{
    Context,
    Remove,
    Add,
}

public enum IlOperandIdentityKind
{
    Immediate,
    Token,
    Slot,
    BranchTarget,
    SwitchTargets,
}

public sealed record IlOperandIdentity(IlOperandIdentityKind Kind, string Value);

public sealed record CanonicalIlOperation(
    int Offset,
    string OpcodeFamily,
    IlOperandIdentity? Operand)
{
    public string Display => Operand is null ? OpcodeFamily : $"{OpcodeFamily} {Operand.Value}";
}

public sealed record IlDiffRow(
    int HunkId,
    IlDiffKind Kind,
    CanonicalIlOperation Operation,
    string Message);

public enum IlDiffFailureKind
{
    OldBodyMissing,
    NewBodyMissing,
    DecodeFailure,
    TokenResolutionFailure,
    UnsupportedBoundary,
}

public sealed record IlDiffFailureRow(
    IlDiffFailureKind Kind,
    string Message,
    string? Side = null,
    string? Detail = null);

public sealed record IlBodyDiffResult(
    bool IsExact,
    string? Failure,
    ImmutableArray<IlDiffRow> Rows,
    ImmutableArray<IlDiffFailureRow> FailureRows = default)
{
    public static IlBodyDiffResult OldBodyMissing(string? detail = null)
        => Failed(IlDiffFailureKind.OldBodyMissing, "old body missing", side: "old", detail);

    public static IlBodyDiffResult NewBodyMissing(string? detail = null)
        => Failed(IlDiffFailureKind.NewBodyMissing, "new body missing", side: "new", detail);

    public static IlBodyDiffResult UnsupportedBoundary(string message, string? detail = null)
        => Failed(IlDiffFailureKind.UnsupportedBoundary, message, side: null, detail);

    public static IlBodyDiffResult UnsupportedBoundary(string message, ImmutableArray<IlDiffRow> rows, string? detail = null)
        => new(false, message, rows, [new IlDiffFailureRow(IlDiffFailureKind.UnsupportedBoundary, message, null, detail)]);

    public static IlBodyDiffResult Failed(IlDiffFailureKind kind, string message, string? side = null, string? detail = null)
        => new(false, message, [], [new IlDiffFailureRow(kind, message, side, detail)]);
}

/// <summary>
/// Low-level IL body diff substrate over decoded instruction streams.
/// </summary>
public static class IlBodyDiff
{
    public static IlBodyDiffResult Compare(MethodInstructions oldBody, MethodInstructions newBody)
        => Compare(oldBody, newBody, oldResolver: null, newResolver: null);

    public static IlBodyDiffResult Compare(
        MetadataReader oldReader,
        MethodBodyBlock oldBody,
        MetadataReader newReader,
        MethodBodyBlock newBody)
    {
        ArgumentNullException.ThrowIfNull(oldReader);
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newReader);
        ArgumentNullException.ThrowIfNull(newBody);

        return Compare(
            MethodInstructions.Decode(oldBody),
            MethodInstructions.Decode(newBody),
            new MetadataOperandResolver(oldReader),
            new MetadataOperandResolver(newReader));
    }

    static IlBodyDiffResult Compare(
        MethodInstructions oldBody,
        MethodInstructions newBody,
        MetadataOperandResolver? oldResolver,
        MetadataOperandResolver? newResolver)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);

        if (!oldBody.IsComplete)
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.DecodeFailure,
                oldBody.Blocks.IncompleteReason ?? "old body decode failed",
                side: "old");
        if (!newBody.IsComplete)
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.DecodeFailure,
                newBody.Blocks.IncompleteReason ?? "new body decode failed",
                side: "new");

        var oldInstructions = oldBody.Instructions;
        var newInstructions = newBody.Instructions;
        if (!TryBuildOperations(oldInstructions, oldResolver, "old", out var oldOperations, out var oldFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                oldFailure ?? "old body token resolution failed",
                side: "old");
        if (!TryBuildOperations(newInstructions, newResolver, "new", out var newOperations, out var newFailure))
            return IlBodyDiffResult.Failed(
                IlDiffFailureKind.TokenResolutionFailure,
                newFailure ?? "new body token resolution failed",
                side: "new");
        var lcs = LongestCommonSubsequence(oldOperations, newOperations);
        var oldToNew = BuildAlignmentMap(lcs, oldOperations.Length, newOperations.Length);
        var rows = ImmutableArray.CreateBuilder<IlDiffRow>();
        int oldIndex = 0;
        int newIndex = 0;
        int hunkId = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddUnmatched(oldOperations, oldIndex, nextOld, newOperations, newIndex, nextNew, rows, ref hunkId);
            if (!BranchTargetsMatch(oldInstructions, nextOld, newInstructions, nextNew, oldToNew))
            {
                int hunk = hunkId++;
                rows.Add(Row(hunk, IlDiffKind.Remove, oldOperations[nextOld]));
                rows.Add(Row(hunk, IlDiffKind.Add, newOperations[nextNew]));
            }
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddUnmatched(oldOperations, oldIndex, oldOperations.Length, newOperations, newIndex, newOperations.Length, rows, ref hunkId);

        var diffRows = rows.ToImmutable();
        return new IlBodyDiffResult(diffRows.Length == 0, Failure: null, diffRows);
    }

    static bool TryBuildOperations(
        ImmutableArray<DecodedInstruction> instructions,
        MetadataOperandResolver? resolver,
        string side,
        out ImmutableArray<CanonicalIlOperation> operations,
        out string? failure)
    {
        var builder = ImmutableArray.CreateBuilder<CanonicalIlOperation>(instructions.Length);
        foreach (var instruction in instructions)
        {
            if (!TryToOperation(instruction, resolver, out var operation, out var operationFailure))
            {
                operations = [];
                failure = $"{side} body {operationFailure}";
                return false;
            }

            builder.Add(operation);
        }

        operations = builder.MoveToImmutable();
        failure = null;
        return true;
    }

    static void AddUnmatched(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        int oldStart,
        int oldEnd,
        ImmutableArray<CanonicalIlOperation> newOperations,
        int newStart,
        int newEnd,
        ImmutableArray<IlDiffRow>.Builder rows,
        ref int hunkId)
    {
        if (oldStart == oldEnd && newStart == newEnd)
            return;

        int hunk = hunkId++;
        int oldIndex = oldStart;
        int newIndex = newStart;
        while (oldIndex < oldEnd)
            rows.Add(Row(hunk, IlDiffKind.Remove, oldOperations[oldIndex++]));
        while (newIndex < newEnd)
            rows.Add(Row(hunk, IlDiffKind.Add, newOperations[newIndex++]));
    }

    static IlDiffRow Row(int hunkId, IlDiffKind kind, CanonicalIlOperation operation)
        => new(hunkId, kind, operation, Message(kind, operation));

    static string Message(IlDiffKind kind, CanonicalIlOperation operation)
    {
        string action = kind switch
        {
            IlDiffKind.Add => "Added",
            IlDiffKind.Remove => "Removed",
            IlDiffKind.Context => "Unchanged",
            _ => kind.ToString(),
        };
        string subject = operation.Operand?.Kind is IlOperandIdentityKind.BranchTarget or IlOperandIdentityKind.SwitchTargets
            ? "IL branch"
            : "IL operation";
        return $"{action} {subject} '{operation.Display}'";
    }

    static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        ImmutableArray<CanonicalIlOperation> oldOperations,
        ImmutableArray<CanonicalIlOperation> newOperations)
    {
        var lengths = new int[oldOperations.Length + 1, newOperations.Length + 1];
        for (int oldIndex = oldOperations.Length - 1; oldIndex >= 0; oldIndex--)
        {
            for (int newIndex = newOperations.Length - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = CanonicalEquals(oldOperations[oldIndex], newOperations[newIndex])
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var pairs = new List<(int OldIndex, int NewIndex)>();
        int i = 0;
        int j = 0;
        while (i < oldOperations.Length && j < newOperations.Length)
        {
            if (CanonicalEquals(oldOperations[i], newOperations[j]))
            {
                pairs.Add((i, j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }

    static IReadOnlyDictionary<int, int> BuildAlignmentMap(
        IReadOnlyList<(int OldIndex, int NewIndex)> lcs,
        int oldLength,
        int newLength)
    {
        var map = new Dictionary<int, int>();
        int oldIndex = 0;
        int newIndex = 0;
        foreach (var (nextOld, nextNew) in lcs)
        {
            AddGapPairs(map, oldIndex, nextOld, newIndex, nextNew);
            map[nextOld] = nextNew;
            oldIndex = nextOld + 1;
            newIndex = nextNew + 1;
        }

        AddGapPairs(map, oldIndex, oldLength, newIndex, newLength);
        return map;
    }

    static void AddGapPairs(
        Dictionary<int, int> map,
        int oldStart,
        int oldEnd,
        int newStart,
        int newEnd)
    {
        int oldCount = oldEnd - oldStart;
        int newCount = newEnd - newStart;
        if (oldCount != newCount)
            return;

        for (int i = 0; i < oldCount; i++)
            map[oldStart + i] = newStart + i;
    }

    static bool CanonicalEquals(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
    {
        if (oldOperation.OpcodeFamily != newOperation.OpcodeFamily)
            return false;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.BranchTarget)
            return true;
        if (oldOperation.Operand?.Kind is IlOperandIdentityKind.SwitchTargets)
            return oldOperation.Operand.Value.Split(',').Length == newOperation.Operand?.Value.Split(',').Length;
        return oldOperation.Operand == newOperation.Operand;
    }

    static bool TryToOperation(
        DecodedInstruction instruction,
        MetadataOperandResolver? resolver,
        out CanonicalIlOperation operation,
        out string? failure)
    {
        if (!TryOperandIdentity(instruction, resolver, out var operand, out failure))
        {
            operation = new CanonicalIlOperation(instruction.Offset, OpcodeFamily(instruction), Operand: null);
            return false;
        }

        operation = new CanonicalIlOperation(
            instruction.Offset,
            OpcodeFamily(instruction),
            operand);
        return true;
    }

    static string OpcodeFamily(DecodedInstruction instruction)
    {
        var opcode = instruction.OpCode;
        if (opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1
            or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4
            or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7
            or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4)
        {
            return "ldc.i4";
        }

        return opcode.IsShortBranch()
            ? opcode.ToString()[..^"_s".Length].Replace('_', '.').ToLowerInvariant()
            : opcode.ToString().Replace('_', '.').ToLowerInvariant();
    }

    static bool TryOperandIdentity(
        DecodedInstruction instruction,
        MetadataOperandResolver? resolver,
        out IlOperandIdentity? operand,
        out string? failure)
    {
        if (IsMetadataTokenOperand(instruction.Operand))
        {
            if (resolver is null)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} requires a MetadataReader-backed comparison";
                return false;
            }

            return resolver.TryResolve(instruction, out operand, out failure);
        }

        operand = ImmediateOperandIdentity(instruction) ?? OperandIdentityByKind(instruction);
        failure = null;
        return true;
    }

    static IlOperandIdentity? ImmediateOperandIdentity(DecodedInstruction instruction)
        => instruction.OpCode switch
        {
            ILOpCode.Ldc_i4_m1 => new(IlOperandIdentityKind.Immediate, "-1"),
            ILOpCode.Ldc_i4_0 => new(IlOperandIdentityKind.Immediate, "0"),
            ILOpCode.Ldc_i4_1 => new(IlOperandIdentityKind.Immediate, "1"),
            ILOpCode.Ldc_i4_2 => new(IlOperandIdentityKind.Immediate, "2"),
            ILOpCode.Ldc_i4_3 => new(IlOperandIdentityKind.Immediate, "3"),
            ILOpCode.Ldc_i4_4 => new(IlOperandIdentityKind.Immediate, "4"),
            ILOpCode.Ldc_i4_5 => new(IlOperandIdentityKind.Immediate, "5"),
            ILOpCode.Ldc_i4_6 => new(IlOperandIdentityKind.Immediate, "6"),
            ILOpCode.Ldc_i4_7 => new(IlOperandIdentityKind.Immediate, "7"),
            ILOpCode.Ldc_i4_8 => new(IlOperandIdentityKind.Immediate, "8"),
            _ => null,
        };

    static IlOperandIdentity? OperandIdentityByKind(DecodedInstruction instruction)
        => instruction.Operand switch
        {
            OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8
                or OperandKind.ShortInlineR or OperandKind.InlineR
                => new(IlOperandIdentityKind.Immediate, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineVar or OperandKind.InlineVar
                => new(IlOperandIdentityKind.Slot, instruction.OperandValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget
                => new(IlOperandIdentityKind.BranchTarget, $"IL_{instruction.BranchTargets[0]:X4}"),
            OperandKind.InlineSwitch
                => new(IlOperandIdentityKind.SwitchTargets, string.Join(", ", instruction.BranchTargets.Select(target => $"IL_{target:X4}"))),
            _ => null,
        };

    static bool IsMetadataTokenOperand(OperandKind kind)
        => kind is OperandKind.InlineString or OperandKind.InlineMethod or OperandKind.InlineField
            or OperandKind.InlineType or OperandKind.InlineSig or OperandKind.InlineTok;

    static bool BranchTargetsMatch(
        ImmutableArray<DecodedInstruction> oldInstructions,
        int oldIndex,
        ImmutableArray<DecodedInstruction> newInstructions,
        int newIndex,
        IReadOnlyDictionary<int, int> oldToNew)
    {
        var oldInstruction = oldInstructions[oldIndex];
        var newInstruction = newInstructions[newIndex];
        if (oldInstruction.Operand is not (OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget or OperandKind.InlineSwitch))
            return true;
        if (oldInstruction.BranchTargets.Length != newInstruction.BranchTargets.Length)
            return false;

        for (int i = 0; i < oldInstruction.BranchTargets.Length; i++)
        {
            int oldTargetIndex = InstructionIndexAt(oldInstructions, oldInstruction.BranchTargets[i]);
            int newTargetIndex = InstructionIndexAt(newInstructions, newInstruction.BranchTargets[i]);
            if (oldTargetIndex < 0 || newTargetIndex < 0)
                return oldInstruction.BranchTargets[i] == newInstruction.BranchTargets[i];
            if (!oldToNew.TryGetValue(oldTargetIndex, out int mappedNewTarget))
                return false;
            if (mappedNewTarget != newTargetIndex)
                return false;
        }

        return true;
    }

    static int InstructionIndexAt(ImmutableArray<DecodedInstruction> instructions, int offset)
    {
        for (int i = 0; i < instructions.Length; i++)
            if (instructions[i].Offset == offset)
                return i;
        return -1;
    }

    sealed class MetadataOperandResolver(MetadataReader reader)
    {
        public bool TryResolve(DecodedInstruction instruction, out IlOperandIdentity? operand, out string? failure)
        {
            try
            {
                string value = instruction.Operand switch
                {
                    OperandKind.InlineString => ResolveString((int)instruction.OperandValue),
                    OperandKind.InlineMethod => ResolveMethod((int)instruction.OperandValue),
                    OperandKind.InlineField => ResolveField((int)instruction.OperandValue),
                    OperandKind.InlineType => ResolveType((int)instruction.OperandValue),
                    OperandKind.InlineTok => ResolveToken((int)instruction.OperandValue),
                    OperandKind.InlineSig => ResolveSignature((int)instruction.OperandValue),
                    _ => throw new InvalidOperationException($"Operand kind {instruction.Operand} is not a metadata token."),
                };
                operand = new IlOperandIdentity(IlOperandIdentityKind.Token, value);
                failure = null;
                return true;
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
            {
                operand = null;
                failure = $"metadata token operand at IL_{instruction.Offset:X4} could not be resolved: {ex.Message}";
                return false;
            }
        }

        string ResolveString(int token)
        {
            var handle = MetadataTokens.UserStringHandle(token);
            return $"string \"{Escape(reader.GetUserString(handle))}\"";
        }

        string ResolveMethod(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodDefinition((MethodDefinitionHandle)handle),
                HandleKind.MemberReference => FormatMemberReference((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => FormatMethodSpecification((MethodSpecificationHandle)handle),
                _ => throw new BadImageFormatException($"Expected method token, got {handle.Kind}."),
            };
        }

        string ResolveField(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.FieldDefinition => FormatFieldDefinition((FieldDefinitionHandle)handle),
                HandleKind.MemberReference => FormatFieldMemberReference((MemberReferenceHandle)handle),
                _ => throw new BadImageFormatException($"Expected field token, got {handle.Kind}."),
            };
        }

        string ResolveType(int token)
            => FormatType(MetadataTokens.EntityHandle(token));

        string ResolveToken(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => $"type {FormatType(handle)}",
                HandleKind.MethodDefinition => $"method {FormatMethodDefinition((MethodDefinitionHandle)handle)}",
                HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)handle).GetKind() == MemberReferenceKind.Method
                    => $"method {FormatMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MemberReference => $"field {FormatFieldMemberReference((MemberReferenceHandle)handle)}",
                HandleKind.MethodSpecification => $"method {FormatMethodSpecification((MethodSpecificationHandle)handle)}",
                HandleKind.FieldDefinition => $"field {FormatFieldDefinition((FieldDefinitionHandle)handle)}",
                _ => throw new BadImageFormatException($"Unsupported ldtoken handle kind {handle.Kind}."),
            };
        }

        string ResolveSignature(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                throw new BadImageFormatException($"Expected standalone signature token, got {handle.Kind}.");

            var signature = reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            return $"signature {Convert.ToHexString(reader.GetBlobBytes(signature.Signature))}";
        }

        string FormatMethodDefinition(MethodDefinitionHandle handle)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = method.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return FormatCall(signature, FormatType(method.GetDeclaringType()), reader.GetString(method.Name), genericArgs: null);
        }

        string FormatMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Method)
                throw new BadImageFormatException("Expected method member reference.");

            var signature = member.DecodeMethodSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return FormatCall(signature, FormatMemberParent(member.Parent), reader.GetString(member.Name), genericArgs: null);
        }

        string FormatMethodSpecification(MethodSpecificationHandle handle)
        {
            var spec = reader.GetMethodSpecification(handle);
            var typeArguments = spec.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
            string genericArgs = $"<{string.Join(", ", typeArguments)}>";

            return spec.Method.Kind switch
            {
                HandleKind.MethodDefinition => FormatMethodSpecificationDefinition((MethodDefinitionHandle)spec.Method, genericArgs),
                HandleKind.MemberReference => FormatMethodSpecificationReference((MemberReferenceHandle)spec.Method, genericArgs),
                _ => throw new BadImageFormatException($"Unsupported method specification target {spec.Method.Kind}."),
            };
        }

        string FormatMethodSpecificationDefinition(MethodDefinitionHandle handle, string genericArgs)
        {
            var method = reader.GetMethodDefinition(handle);
            var signature = method.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return FormatCall(signature, FormatType(method.GetDeclaringType()), reader.GetString(method.Name), genericArgs);
        }

        string FormatMethodSpecificationReference(MemberReferenceHandle handle, string genericArgs)
        {
            var member = reader.GetMemberReference(handle);
            var signature = member.DecodeMethodSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return FormatCall(signature, FormatMemberParent(member.Parent), reader.GetString(member.Name), genericArgs);
        }

        string FormatFieldDefinition(FieldDefinitionHandle handle)
        {
            var field = reader.GetFieldDefinition(handle);
            string fieldType = field.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return $"{fieldType} {FormatType(field.GetDeclaringType())}::{reader.GetString(field.Name)}";
        }

        string FormatFieldMemberReference(MemberReferenceHandle handle)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Field)
                throw new BadImageFormatException("Expected field member reference.");

            string fieldType = member.DecodeFieldSignature(SignatureIdentityProvider.Instance, genericContext: null);
            return $"{fieldType} {FormatMemberParent(member.Parent)}::{reader.GetString(member.Name)}";
        }

        string FormatCall(MethodSignature<string> signature, string parent, string name, string? genericArgs)
        {
            string instance = signature.Header.IsInstance ? "instance " : "";
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            string arity = signature.GenericParameterCount > 0
                ? $"`{signature.GenericParameterCount}"
                : "";
            return $"{instance}{convention}{signature.ReturnType} {parent}::{name}{arity}{genericArgs}({FormatParameterList(signature)})";
        }

        string FormatMemberParent(EntityHandle parent)
            => parent.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification
                    => FormatType(parent),
                _ => throw new BadImageFormatException($"Unsupported member parent {parent.Kind}."),
            };

        string FormatType(EntityHandle handle)
            => handle.Kind switch
            {
                HandleKind.TypeDefinition => FormatTypeDefinition((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => FormatTypeReference((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(SignatureIdentityProvider.Instance, null),
                _ => throw new BadImageFormatException($"Unsupported type handle {handle.Kind}."),
            };

        string FormatTypeDefinition(TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            string fullName = declaring.IsNil
                ? Dotted(reader.GetString(type.Namespace), name)
                : $"{FormatTypeDefinition(declaring)}+{name}";
            return $"[{CurrentAssemblyName()}]{fullName}";
        }

        string FormatTypeReference(TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string fullName = Dotted(reader.GetString(type.Namespace), name);
            return type.ResolutionScope.Kind switch
            {
                HandleKind.AssemblyReference =>
                    $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}",
                HandleKind.TypeReference =>
                    $"{FormatTypeReference((TypeReferenceHandle)type.ResolutionScope)}+{fullName}",
                _ => $"[{CurrentAssemblyName()}]{fullName}",
            };
        }

        string CurrentAssemblyName()
            => reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";

        static string Dotted(string ns, string name)
            => ns.Length == 0 ? name : $"{ns}.{name}";

        static string Escape(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, object?>
    {
        public static SignatureIdentityProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Void => "void",
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.SByte => "int8",
                PrimitiveTypeCode.Byte => "uint8",
                PrimitiveTypeCode.Int16 => "int16",
                PrimitiveTypeCode.UInt16 => "uint16",
                PrimitiveTypeCode.Int32 => "int32",
                PrimitiveTypeCode.UInt32 => "uint32",
                PrimitiveTypeCode.Int64 => "int64",
                PrimitiveTypeCode.UInt64 => "uint64",
                PrimitiveTypeCode.Single => "float32",
                PrimitiveTypeCode.Double => "float64",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.IntPtr => "native int",
                PrimitiveTypeCode.UIntPtr => "native uint",
                PrimitiveTypeCode.TypedReference => "typedref",
                _ => typeCode.ToString(),
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeName(reader, handle);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => $"{elementType}[]";
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        public string GetByReferenceType(string elementType) => $"{elementType}&";
        public string GetPointerType(string elementType) => $"{elementType}*";
        public string GetPinnedType(string elementType) => $"{elementType} pinned";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(", ", typeArguments)}>";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            string convention = CallingConventionPrefix(signature.Header.CallingConvention);
            return $"method {convention}{signature.ReturnType} *({FormatParameterList(signature)})";
        }

        static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
                return $"{TypeName(reader, declaring)}+{name}";
            string ns = reader.GetString(type.Namespace);
            string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
            return $"[{assembly}]{(ns.Length == 0 ? name : $"{ns}.{name}")}";
        }

        static string TypeName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            string ns = reader.GetString(type.Namespace);
            string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
            return type.ResolutionScope.Kind switch
            {
                HandleKind.AssemblyReference =>
                    $"[{AssemblyReferenceIdentity(reader, (AssemblyReferenceHandle)type.ResolutionScope)}]{fullName}",
                HandleKind.TypeReference =>
                    $"{TypeName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{fullName}",
                _ => fullName,
            };
        }
    }

    static string FormatParameterList(MethodSignature<string> signature)
    {
        if (signature.Header.CallingConvention != SignatureCallingConvention.VarArgs)
            return string.Join(", ", signature.ParameterTypes);

        var builder = ImmutableArray.CreateBuilder<string>(signature.ParameterTypes.Length + 1);
        int requiredCount = Math.Clamp(signature.RequiredParameterCount, 0, signature.ParameterTypes.Length);
        for (int i = 0; i < requiredCount; i++)
            builder.Add(signature.ParameterTypes[i]);
        builder.Add("...");
        for (int i = requiredCount; i < signature.ParameterTypes.Length; i++)
            builder.Add(signature.ParameterTypes[i]);
        return string.Join(", ", builder);
    }

    static string CallingConventionPrefix(SignatureCallingConvention convention)
    {
        string text = convention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.VarArgs => "vararg",
            SignatureCallingConvention.CDecl => "unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => "unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => "unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => "unmanaged[Fastcall]",
            _ => convention.ToString(),
        };
        return text.Length == 0 ? "" : $"{text} ";
    }

    static string AssemblyReferenceIdentity(MetadataReader reader, AssemblyReferenceHandle handle)
    {
        var reference = reader.GetAssemblyReference(handle);
        string name = reader.GetString(reference.Name);
        string culture = reference.Culture.IsNil ? "neutral" : reader.GetString(reference.Culture);
        string token = reference.PublicKeyOrToken.IsNil
            ? "null"
            : Convert.ToHexString(reader.GetBlobBytes(reference.PublicKeyOrToken)).ToLowerInvariant();
        string flags = reference.Flags == default ? "" : $", Flags={reference.Flags}";
        return $"{name}, Version={reference.Version}, Culture={culture}, PublicKeyToken={token}{flags}";
    }
}
