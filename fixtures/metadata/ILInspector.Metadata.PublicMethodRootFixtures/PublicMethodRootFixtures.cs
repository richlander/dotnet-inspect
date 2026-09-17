using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ILInspector.Metadata.PublicMethodRootFixtures;

public class PublicTopLevel
{
    public PublicTopLevel()
    {
    }

    public void PublicMethod()
    {
    }

    protected void ProtectedMethod()
    {
    }

    internal void InternalMethod()
    {
    }

    private void PrivateMethod()
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void HiddenByPresentation()
    {
    }

    [CompilerGenerated]
    public void CompilerGeneratedPublic()
    {
    }

    public int Value { get; private set; }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public static PublicTopLevel operator +(
        PublicTopLevel left,
        PublicTopLevel right) =>
        left;

    public sealed class PublicNested
    {
        public PublicNested()
        {
        }

        public void PublicNestedMethod()
        {
        }
    }

    internal sealed class InternalNested
    {
        public void HiddenByInternalNestedType()
        {
        }
    }

    protected sealed class ProtectedNested
    {
        public void HiddenByProtectedNestedType()
        {
        }
    }

    private sealed class PrivateNested
    {
        public void HiddenByPrivateNestedType()
        {
        }
    }
}

internal sealed class InternalTopLevel
{
    public void HiddenByInternalTopLevelType()
    {
    }

    public sealed class PublicNested
    {
        public void HiddenByInternalEnclosingType()
        {
        }
    }
}

public abstract class PublicAbstract
{
    public abstract void Bodiless();
}

public interface IPublicContract
{
    void InterfaceMethod();
}
