// Fixture namespace for MemberBodyFactsTests.ReferencedNamespaces_ReportsFunctionPointerComponentNamespaces.
//
// FunctionPointerNamespaceFixtures.ParameterMarker and .ReturnMarker are only
// referenced through a function-pointer parameter/return type
// (CfgSampleClass.TakesFunctionPointerWithCustomNamespaceTypes), never directly
// as a local, field, or ordinary parameter/return type. That isolates
// TypeRefKind.FunctionPointer as the only path MemberBodyFacts.ReferencedNamespaces
// can discover this namespace through, so the fixture is a close negative for
// issue #2847: before the fix, the extractor's Add() switch had no
// TypeRefKind.FunctionPointer case, so a namespace reachable only via a function
// pointer's return/parameter types was silently missed.

namespace FunctionPointerNamespaceFixtures
{
    public struct ParameterMarker
    {
    }

    public struct ReturnMarker
    {
    }
}
