// The runtime's IntrinsicAttribute is internal to System.Private.CoreLib, so
// the fixture declares one with the same namespace and name.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Field, Inherited = false)]
internal sealed class IntrinsicAttribute : Attribute
{
}
