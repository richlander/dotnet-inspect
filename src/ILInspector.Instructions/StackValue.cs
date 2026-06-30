namespace ILInspector.Instructions;

/// <summary>
/// One abstract evaluation-stack slot: its <see cref="StackType"/> plus the IL offset of the
/// instruction that produced it (<see cref="ProducerOffset"/>). The producer offset is the
/// "stack value provenance" the gate-decision protocol calls out — it is purely structural,
/// needs no metadata, and lets a consumer answer "which instruction created the receiver of
/// this call?" without re-running an abstract interpreter.
/// </summary>
public readonly record struct StackValue(StackType Type, int ProducerOffset)
{
    /// <summary>A slot whose producer is unknown (e.g. an EH-injected exception, or a merge of disagreeing paths).</summary>
    public const int NoProducer = -1;

    public static StackValue Of(StackType type, int producerOffset) => new(type, producerOffset);

    /// <summary>Position-wise lattice join of two stack slots from different predecessors.</summary>
    public StackValue Merge(StackValue other)
        => new(Type.Merge(other.Type), ProducerOffset == other.ProducerOffset ? ProducerOffset : NoProducer);

    public override string ToString()
        => ProducerOffset == NoProducer ? Type.ToGlyph() : $"{Type.ToGlyph()}@IL_{ProducerOffset:X4}";
}

/// <summary>
/// Resolves the few stack effects that genuinely need metadata: local/argument types, field
/// and element types, and call/newobj/calli signatures (argument counts + return type). Kept
/// behind an interface so the interpreter stays metadata-light: the default resolver answers
/// <see cref="StackType.Unknown"/> for everything and reports unresolved calls, which degrades
/// the typed-stack to incomplete rather than guessing. Token-resolving consumers (Analysis,
/// later the decompiler) supply a real SRM-backed resolver — see <c>MetadataStackTypeResolver</c>.
/// </summary>
public interface IStackTypeResolver
{
    /// <summary>The declared stack type of local slot <paramref name="index"/>.</summary>
    StackType Local(int index) => StackType.Unknown;

    /// <summary>The declared stack type of argument slot <paramref name="index"/> (slot 0 is <c>this</c> for instance methods).</summary>
    StackType Argument(int index) => StackType.Unknown;

    /// <summary>The stack type of the field named by a field token.</summary>
    StackType Field(int fieldToken) => StackType.Unknown;

    /// <summary>The stack type named by a type token (for <c>newobj</c> value types, <c>ldobj</c>, <c>ldelem</c> token form).</summary>
    StackType TypeOfToken(int typeToken) => StackType.Unknown;

    /// <summary>
    /// Resolves a <c>call</c>/<c>callvirt</c>/<c>newobj</c> by method token: the number of values it pops
    /// and the stack type it pushes (or <see cref="StackType.Unknown"/> and <c>pushes=false</c> for void).
    /// For <c>newobj</c>, <paramref name="popCount"/> is the constructor's parameter count (no <c>this</c>)
    /// and it always pushes the constructed type.
    /// </summary>
    bool TryResolveCall(int methodToken, bool isNewObj, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;
        return false;
    }

    /// <summary>Resolves a <c>calli</c> by standalone-signature token (pop count excludes the function pointer).</summary>
    bool TryResolveCalli(int signatureToken, out int popCount, out bool pushes, out StackType pushType)
    {
        popCount = -1;
        pushes = false;
        pushType = StackType.Unknown;
        return false;
    }
}

/// <summary>A resolver that knows nothing; every token-dependent slot becomes <see cref="StackType.Unknown"/>.</summary>
public sealed class DefaultStackTypeResolver : IStackTypeResolver
{
    public static readonly DefaultStackTypeResolver Instance = new();
}
