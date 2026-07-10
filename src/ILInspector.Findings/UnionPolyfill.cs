// The native discriminated-union feature (used by PairFinding<T>) is purely compiler-lowered: the
// compiler recognizes [Union] by well-known name and generates the case conversions and pattern-
// matching itself. Only the marker types live in the runtime, and only on net11+. The official
// non-AOT "any" fallback package targets the net10.0 LTS floor (see Directory.Build.props), so on
// that floor we supply the marker shapes ourselves — the same polyfill pattern used for
// IsExternalInit/RequiredMember on older frameworks. Guarded to below-net11 so it never conflicts
// with the real System.Private.CoreLib types on net11+ (which would be a CS0436 under
// TreatWarningsAsErrors). Kept internal so it stays off the package's public API surface.
#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class UnionAttribute : Attribute;

internal interface IUnion;
#endif
