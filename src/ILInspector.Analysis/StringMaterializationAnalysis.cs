using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// The statically identified operation that can produce a string value.
/// </summary>
public enum StringMaterializationKind
{
    Concatenation,
    Join,
    Format,
    Create,
    Constructor,
    InterpolatedStringHandlerFinalization,
    StringBuilderFinalization,
    EncodingDecode,
}

/// <summary>
/// One exact IL operation that can produce a string value.
/// </summary>
public sealed record StringMaterializationOccurrence(
    MethodIdentity Method,
    MethodIdentity EvidenceMethod,
    MemberRef Operation,
    StringMaterializationKind Kind,
    int ILOffset,
    int OperandToken,
    string Opcode,
    bool InLoop,
    AllocationMultiplicity Multiplicity);

/// <summary>
/// Classifies exact string-producing operations from the resolved direct-call
/// census without estimating runtime allocation, bytes, or frequency.
/// </summary>
public static class StringMaterializationAnalysis
{
    public static ImmutableArray<StringMaterializationOccurrence> Collect(
        IEnumerable<DirectCall> calls)
        => Collect(calls, receiverSources: null);

    internal static ImmutableArray<StringMaterializationOccurrence> Collect(
        IEnumerable<DirectCall> calls,
        IReadOnlyDictionary<int, CallReceiverSource>? receiverSources)
    {
        ArgumentNullException.ThrowIfNull(calls);

        ImmutableArray<DirectCall> callArray = [.. calls];
        var callsByCoordinate = callArray.ToDictionary(
            static call => (
                call.EvidenceMethod.MetadataToken,
                call.ILOffset));
        var occurrences =
            ImmutableArray.CreateBuilder<StringMaterializationOccurrence>();

        foreach (DirectCall call in callArray)
        {
            if (Classify(
                    call,
                    callsByCoordinate,
                    receiverSources)
                    is not { } kind)
            {
                continue;
            }

            occurrences.Add(new(
                call.Caller,
                call.EvidenceMethod,
                call.Callee,
                kind,
                call.ILOffset,
                call.OperandToken,
                call.Opcode,
                call.InLoop,
                call.Multiplicity));
        }

        return occurrences.ToImmutable();
    }

    private static StringMaterializationKind? Classify(
        DirectCall call,
        IReadOnlyDictionary<(int MethodToken, int ILOffset), DirectCall>
            callsByCoordinate,
        IReadOnlyDictionary<int, CallReceiverSource>? receiverSources)
    {
        if (call.Kind is not (
                CallKind.Call
                or CallKind.CallVirtual
                or CallKind.NewObject))
        {
            return null;
        }

        MemberRef member = call.Callee;
        if (FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System",
                "String"))
        {
            if (call.Kind == CallKind.NewObject
                && member.Name == ".ctor")
            {
                return StringMaterializationKind.Constructor;
            }

            if (!IsString(member.ReturnType))
                return null;

            return member.Name switch
            {
                "Concat" => StringMaterializationKind.Concatenation,
                "Join" => StringMaterializationKind.Join,
                "Format" => StringMaterializationKind.Format,
                "Create" => StringMaterializationKind.Create,
                _ => null,
            };
        }

        if (!IsString(member.ReturnType))
            return null;

        if (FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System.Runtime.CompilerServices",
                "DefaultInterpolatedStringHandler")
            && member.Name == "ToStringAndClear")
        {
            return StringMaterializationKind
                .InterpolatedStringHandlerFinalization;
        }

        if (FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System.Text",
                "StringBuilder")
            && member.Name == "ToString")
        {
            return StringMaterializationKind.StringBuilderFinalization;
        }

        if (FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System.Text",
                "Encoding")
            && member.Name == "GetString")
        {
            return StringMaterializationKind.EncodingDecode;
        }

        if (FrameworkIdentity.IsCoreLibraryType(
                member.DeclaringType,
                "System",
                "Object")
            && member.Name == "ToString"
            && HasStringBuilderReceiver(
                call,
                callsByCoordinate,
                receiverSources))
        {
            return StringMaterializationKind.StringBuilderFinalization;
        }

        return null;
    }

    private static bool HasStringBuilderReceiver(
        DirectCall call,
        IReadOnlyDictionary<(int MethodToken, int ILOffset), DirectCall>
            callsByCoordinate,
        IReadOnlyDictionary<int, CallReceiverSource>? receiverSources)
    {
        CallReceiverSource? receiver =
            receiverSources?.GetValueOrDefault(call.ILOffset)
            ?? call.ReceiverSource;
        if (receiver is not
                {
                    IsComplete: true,
                    SourceCallOffsets.Length: > 0,
                })
        {
            return false;
        }

        foreach (int sourceOffset in receiver.SourceCallOffsets)
        {
            if (!callsByCoordinate.TryGetValue(
                    (call.EvidenceMethod.MetadataToken, sourceOffset),
                    out DirectCall? source)
                || !ProducesStringBuilder(source))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ProducesStringBuilder(DirectCall call)
        => (call.Kind == CallKind.NewObject
                && FrameworkIdentity.IsCoreLibraryType(
                    call.Callee.DeclaringType,
                    "System.Text",
                    "StringBuilder"))
            || FrameworkIdentity.IsCoreLibraryType(
                call.Callee.ReturnType,
                "System.Text",
                "StringBuilder");

    private static bool IsString(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(
            type,
            "System",
            "String");
}
