using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

internal static class FieldMemorySafetyContract
{
    internal static bool RequiresUnsafe(
        FieldRef field,
        bool callerUsesUpdatedRules,
        bool legacyShapeRequiresUnsafe)
    {
        if (!EvidenceRequired(
                callerUsesUpdatedRules,
                legacyShapeRequiresUnsafe)
            || HasInvalidEvidence(field)
            || !field.HasNormalizedMemorySafetyContract)
        {
            return false;
        }
        if (field.RequiresUnsafe)
            return callerUsesUpdatedRules;
        return field.RequiresUnsafeFact switch
        {
            MetadataFactState.Yes => true,
            MetadataFactState.No => false,
            _ => field.MemorySafetyRulesState
                    == MemorySafetyRulesState.Legacy
                && legacyShapeRequiresUnsafe,
        };
    }

    internal static bool EvidenceRequired(
        bool callerUsesUpdatedRules,
        bool legacyShapeRequiresUnsafe)
        => callerUsesUpdatedRules || legacyShapeRequiresUnsafe;

    internal static bool HasInvalidEvidence(FieldRef field)
        => field.MemorySafetyRulesState is
                MemorySafetyRulesState.Unsupported
                    or MemorySafetyRulesState.Malformed
                    or MemorySafetyRulesState.Conflicting
            || field.MemorySafetyRulesUnavailable
            || field.MemorySafetyContractUnavailable;
}
