// Native discriminated unions are compiler-lowered; only these marker types require runtime
// support. Product projects that declare unions link this shared source for the net10.0 fallback.
// The guards avoid conflicts with the net11+ System.Private.CoreLib types, and internal visibility
// keeps the polyfill off every package's public API surface.
#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class UnionAttribute : Attribute;

internal interface IUnion;
#endif
