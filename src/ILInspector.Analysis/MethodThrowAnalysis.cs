using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal static class MethodThrowAnalysis
{
    internal static void Collect(
        MethodBodyAnalysisContext context,
        IReadOnlyDictionary<int, DirectCall> callsByOffset,
        Func<int, ResolvedValueSet> resolveValue,
        Func<TypeRef, ExceptionTypeQualification> qualifyExceptionType,
        ImmutableArray<LocalThrowSite>.Builder sites)
    {
        foreach (var instruction in context.Instructions.Instructions)
        {
            if (instruction.OpCode == ILOpCode.Rethrow)
            {
                sites.Add(new(
                    instruction.Offset,
                    LocalThrowInstructionKind.Rethrow,
                    new LocalThrowTypeEvidence.Unresolved(
                        LocalThrowUnresolvedReason.Rethrow)));
            }
            else if (instruction.OpCode == ILOpCode.Throw)
            {
                sites.Add(new(
                    instruction.Offset,
                    LocalThrowInstructionKind.Throw,
                    ResolveType(resolveValue(instruction.Offset))));
            }
        }

        LocalThrowTypeEvidence ResolveType(ResolvedValueSet value)
        {
            if (!value.IsResolved)
                return Unresolved(LocalThrowUnresolvedReason.UnresolvedValue);
            if (value.Sources.Length != 1)
                return Unresolved(LocalThrowUnresolvedReason.MultipleSources);
            ResolvedValueSource source = value.Sources[0];
            if (source.Kind != ResolvedValueSourceKind.NewObjectResult)
                return Unresolved(LocalThrowUnresolvedReason.UnsupportedValue);
            if (!callsByOffset.TryGetValue(source.ILOffset, out DirectCall? call)
                || call.Kind != CallKind.NewObject
                || call.Callee.Kind != MemberKind.Constructor)
            {
                return Unresolved(LocalThrowUnresolvedReason.UnresolvedValue);
            }

            TypeRef type = call.Callee.DeclaringType;
            ExceptionTypeQualification qualification = qualifyExceptionType(type);
            return qualification.IsException switch
            {
                true => new LocalThrowTypeEvidence.Known(
                    type,
                    call.ILOffset,
                    call.OperandToken,
                    qualification.Definition),
                false => Unresolved(LocalThrowUnresolvedReason.NonExceptionType),
                null => Unresolved(LocalThrowUnresolvedReason.UnresolvedType),
            };
        }
    }

    static LocalThrowTypeEvidence.Unresolved Unresolved(
        LocalThrowUnresolvedReason reason) => new(reason);
}
