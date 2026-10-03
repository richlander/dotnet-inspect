using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace DotnetInspector.PerformanceOracles;

/// <summary>Which method-classification population a comparator answers for.</summary>
public enum ClassificationPopulation
{
    Async,
    PInvoke,
}

/// <summary>
/// The method-classification gate's population, exactly as
/// <c>MethodClassificationScope</c> applies it: types whose name does not
/// start with <c>&lt;</c>; public, non-accessor methods.
/// </summary>
public static class MethodClassificationGate
{
    public static bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        !reader.StringComparer.StartsWith(type.Name, "<");

    public static bool MethodInScope(MetadataReader reader, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;
        StringHandle name = method.Name;
        return !(reader.StringComparer.StartsWith(name, "get_")
            || reader.StringComparer.StartsWith(name, "set_")
            || reader.StringComparer.StartsWith(name, "add_")
            || reader.StringComparer.StartsWith(name, "remove_"));
    }

    public static bool IsPInvoke(MethodDefinition method) => (method.Attributes & MethodAttributes.PinvokeImpl) != 0;
}

/// <summary>
/// Per-call state: the identity budgets the Planner keeps per execution, and
/// the attribute-match memos the gate keeps per execution.
/// </summary>
public sealed class MethodClassificationCallState(MetadataReader reader)
{
    public readonly MetadataReader Reader = reader;
    public int IdentityDecodeFailures;
    public int IdentityWork = MetadataSafetyPolicy.MaxClassificationScanWorkChars;
    public readonly Dictionary<(EntityHandle, MetadataTypeNameTarget), bool> Constructors = [];
    public readonly Dictionary<(EntityHandle, MetadataTypeNameTarget), bool> Types = [];
}

/// <summary>
/// The analyzers' tests, as the Planner applies them after its gate: the
/// P/Invoke flag; the runtime-async flag; and the in-place attribute type
/// match through <see cref="MetadataTypeNameMatch"/>, one target at a time,
/// memoized per constructor and per attribute type.
/// </summary>
public static class MethodClassificationTests
{
    const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;
    static readonly MetadataTypeNameTarget AsyncStateMachine = new(KnownAttributeNames.AsyncStateMachineAttribute);
    static readonly MetadataTypeNameTarget AsyncIteratorStateMachine = new(KnownAttributeNames.AsyncIteratorStateMachineAttribute);

    public static bool IsRuntimeAsync(MethodDefinition method) => (method.ImplAttributes & RuntimeAsyncFlag) != 0;

    public static MethodClassification? Classify(ClassificationPopulation population, MethodClassificationCallState state, MethodDefinition method)
    {
        if (population == ClassificationPopulation.PInvoke)
            return MethodClassificationGate.IsPInvoke(method) ? MethodClassification.PInvoke : null;
        if (MethodClassificationGate.IsPInvoke(method))
            return null;
        if (IsRuntimeAsync(method))
            return MethodClassification.RuntimeAsync;
        return HasAttributeOfType(state, method, AsyncStateMachine) || HasAttributeOfType(state, method, AsyncIteratorStateMachine)
            ? MethodClassification.StateMachineAsync
            : null;
    }

    static bool HasAttributeOfType(MethodClassificationCallState state, MethodDefinition method, MetadataTypeNameTarget target)
    {
        MetadataReader reader = state.Reader;
        foreach (CustomAttributeHandle handle in method.GetCustomAttributes())
        {
            EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;
            if (!state.Constructors.TryGetValue((constructor, target), out bool matches))
            {
                EntityHandle parent = constructor.Kind switch
                {
                    HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                    HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                    _ => default,
                };
                matches = !parent.IsNil && TypeMatches(state, parent, target);
                state.Constructors[(constructor, target)] = matches;
            }

            if (matches)
                return true;
        }

        return false;
    }

    static bool TypeMatches(MethodClassificationCallState state, EntityHandle type, MetadataTypeNameTarget target)
    {
        if (state.Types.TryGetValue((type, target), out bool known))
            return known;
        bool matches = MetadataTypeNameMatch.Matches(state.Reader, type, target) switch
        {
            MetadataTypeNameMatchResult.Match => true,
            MetadataTypeNameMatchResult.NoMatch => false,
            _ => throw new BadImageFormatException("An attribute type's name could not be matched."),
        };
        state.Types[(type, target)] = matches;
        return matches;
    }
}
