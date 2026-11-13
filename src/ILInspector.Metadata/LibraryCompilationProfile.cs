using System.Reflection;
using System.Reflection.Metadata;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// The Library's compilation profile, as docs/design/notable-findings.md reads
/// it: the Debug fact from the assembly's <c>DebuggableAttribute</c> arguments,
/// and the whole-population async facts, counted over every method definition
/// with a CIL body at every accessibility. The public presence flags in
/// <see cref="PresenceFlags"/> are not these facts: they summarize public
/// non-accessor methods only.
/// </summary>
/// <param name="IsDebug">
/// True when the assembly's <c>DebuggableAttribute</c> disables JIT
/// optimizations, through <c>DebuggingModes.DisableOptimizations</c> or the
/// two-argument constructor's <c>isJITOptimizerDisabled</c>. An absent
/// attribute is an optimized build.
/// </param>
/// <param name="MethodsWithBody">Method definitions with a non-zero RVA.</param>
/// <param name="RuntimeAsyncMethods">Bodies carrying the runtime-async implementation flag.</param>
/// <param name="StateMachineAsyncMethods">
/// Bodies without the runtime-async flag that carry
/// <c>AsyncStateMachineAttribute</c> or <c>AsyncIteratorStateMachineAttribute</c>.
/// </param>
/// <param name="AsyncIteratorMethods">The <see cref="StateMachineAsyncMethods"/> that are async iterators.</param>
/// <param name="IteratorMethods">Bodies carrying <c>IteratorStateMachineAttribute</c>.</param>
public readonly record struct LibraryCompilationProfile(
    bool IsDebug,
    int MethodsWithBody,
    int RuntimeAsyncMethods,
    int StateMachineAsyncMethods,
    int AsyncIteratorMethods,
    int IteratorMethods)
{
    /// <summary>At least one method with a body is runtime async, at any accessibility.</summary>
    public bool HasRuntimeAsync => RuntimeAsyncMethods > 0;

    /// <summary>At least one method with a body carries a compiler async state machine.</summary>
    public bool HasStateMachineAsync => StateMachineAsyncMethods > 0;

    /// <summary>
    /// The Expectation count: in a runtime-async Library, the state-machine
    /// async methods that contradict the profile. Zero when the Library has no
    /// runtime async, because state-machine async is then the norm.
    /// </summary>
    public int UnexpectedStateMachineAsyncMethods => HasRuntimeAsync ? StateMachineAsyncMethods : 0;

    /// <summary>
    /// The Library-explained rule: a Debug build explains a classic async
    /// method's class state machine. Iterators and async iterators are never
    /// explained, because their state machines are classes in every build.
    /// </summary>
    public bool ExplainsStateMachine(StateMachineClaimKind? kind) =>
        IsDebug && kind == StateMachineClaimKind.ClassicAsync;
}

/// <summary>
/// Reads the <see cref="LibraryCompilationProfile"/> from one metadata reader.
/// The async facts reuse the per-method classification in
/// <see cref="MethodClassificationScanner"/>; this type adds the population and
/// the Debug fact, not a second classifier.
/// </summary>
public static class LibraryCompilationProfileScanner
{
    /// <summary><c>DebuggingModes.DisableOptimizations</c>.</summary>
    const int DisableOptimizations = 0x100;

    static readonly MetadataTypeNameTarget DebuggableAttribute =
        new("System.Diagnostics.DebuggableAttribute");

    public static LibraryCompilationProfile Scan(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int bodies = 0, runtimeAsync = 0, stateMachineAsync = 0, asyncIterators = 0, iterators = 0;
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            bodies++;

            if ((method.ImplAttributes & StateMachineClaims.RuntimeAsyncFlag) != 0)
            {
                runtimeAsync++;
                continue;
            }

            switch (MethodClassificationScanner.ClassifyStateMachine(reader, method))
            {
                case StateMachineClaimKind.ClassicAsync:
                    stateMachineAsync++;
                    break;
                case StateMachineClaimKind.AsyncIterator:
                    stateMachineAsync++;
                    asyncIterators++;
                    break;
                case StateMachineClaimKind.Iterator:
                    iterators++;
                    break;
            }
        }

        return new LibraryCompilationProfile(
            IsDebug(reader),
            bodies,
            runtimeAsync,
            stateMachineAsync,
            asyncIterators,
            iterators);
    }

    /// <summary>
    /// Whether the assembly's <c>DebuggableAttribute</c> disables JIT
    /// optimizations. The attribute type is matched in place; only its
    /// arguments are decoded, through <see cref="AttributeDecoder"/>. An
    /// argument blob the decoder refuses reads as optimized.
    /// </summary>
    public static bool IsDebug(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.IsAssembly)
            return false;

        foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (!MetadataTypeNameMatch.AttributeHasType(reader, attribute, DebuggableAttribute))
                continue;

            return AttributeDecoder.TryDecode(reader, attribute)?.FixedArguments switch
            {
                // DebuggableAttribute(DebuggingModes modes)
                [{ Value: int modes }] => (modes & DisableOptimizations) != 0,
                // DebuggableAttribute(bool isJITTrackingEnabled, bool isJITOptimizerDisabled)
                [{ Value: bool }, { Value: bool optimizerDisabled }] => optimizerDisabled,
                _ => false,
            };
        }

        return false;
    }
}
