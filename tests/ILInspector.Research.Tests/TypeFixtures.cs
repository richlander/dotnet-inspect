using System.Threading.Tasks;

namespace ILInspector.Research.Tests.TypeFixtures;

internal interface IResearchNamed
{
    string Name { get; }
}

internal interface IResearchAged
{
    int Age { get; }
}

internal abstract class ResearchAnimal : IResearchNamed, IResearchAged
{
    public abstract string Name { get; }
    public int Age { get; protected set; }
    public virtual void Speak() { }
}

internal sealed class ResearchDog : ResearchAnimal
{
    public override string Name => "Dog";
    public override void Speak() { }
}

internal sealed class ResearchCat : ResearchAnimal
{
    public override string Name => "Cat";
}

internal class ResearchRepository<T> where T : class, IResearchNamed, new()
{
    public T Create() => new();
}

internal enum ResearchColor : byte
{
    Red,
    Green,
    Blue,
}

internal readonly struct ResearchReadonlyPoint
{
    public readonly int X;
    public ResearchReadonlyPoint(int x) => X = x;
}

internal ref struct ResearchRefSpan
{
    public int Length { get; set; }
}

internal class ResearchComposite
{
    public int Field = 0;
    public int Value { get; set; }

    public ResearchComposite() { }

    public static async Task DoWorkAsync() => await Task.Yield();

    public async Task<int> ComputeAsync()
    {
        await Task.Yield();
        return Field;
    }

    public static unsafe void Poke(int* p)
    {
        if (p != null)
            *p = 0;
    }

    public virtual void Overridable() { }

    public void Plain() { }
}
