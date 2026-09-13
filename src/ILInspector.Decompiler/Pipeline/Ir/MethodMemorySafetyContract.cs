using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

internal static class MethodMemorySafetyContract
{
    internal static bool RequiresUnsafe(
        MethodRef method,
        bool callerUsesUpdatedRules,
        bool legacyShapeRequiresUnsafe)
    {
        if (HasInvalidEvidence(method))
            return false;
        if (method.RequiresUnsafe)
            return callerUsesUpdatedRules;
        return method.RequiresUnsafeFact switch
        {
            MetadataFactState.Yes => true,
            MetadataFactState.No => false,
            _ => (method.MemorySafetyRulesState is
                    null or MemorySafetyRulesState.Legacy)
                && legacyShapeRequiresUnsafe,
        };
    }

    internal static bool HasInvalidEvidence(MethodRef method)
        => method.MemorySafetyRulesState is
                MemorySafetyRulesState.Unsupported
                    or MemorySafetyRulesState.Malformed
                    or MemorySafetyRulesState.Conflicting
            || method.MemorySafetyRulesUnavailable
            || method.MemorySafetyContractUnavailable;
}
