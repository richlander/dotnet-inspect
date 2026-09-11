namespace ILInspector.Decompiler.Tests;

// A value-type instance method whose `this` value is read directly: returning
// `this` by value compiles to `ldarg.0; ldobj` (a load-indirect of the `this`
// managed pointer), which must render as `this`, not the CS0193 `*this`.
public struct CfgSelf
{
    public int Value;
    public CfgSelf Identity() => this;
}
