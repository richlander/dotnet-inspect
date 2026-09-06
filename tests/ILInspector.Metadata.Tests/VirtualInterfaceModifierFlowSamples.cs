namespace ILInspector.Metadata.Tests;

public class VirtualModifierFlowSamples
{
    public virtual void Ref(int value) { }
    public virtual void Ref(ref int value) { }
    public virtual void Out(int value) { }
    public virtual void Out(out int value) => value = 0;
    public virtual void In(int value) { }
    public virtual void In(in int value) { }
    public virtual void ReadOnly(int value) { }
    public virtual void ReadOnly(ref readonly int value) { }
}

public interface IInterfaceModifierFlowSamples
{
    void Ref(int value);
    void Ref(ref int value);
    void Out(int value);
    void Out(out int value);
    void In(int value);
    void In(in int value);
    void ReadOnly(int value);
    void ReadOnly(ref readonly int value);
}
