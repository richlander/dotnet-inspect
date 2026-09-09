namespace DotnetInspector.Fixtures;

public class TypeDependencyPair<TFirst, TSecond>
{
}

public class TypeDependencyOuter<TOuter>
{
    public class Inner<TInner> :
        TypeDependencyPair<TOuter, TInner>
    {
    }
}

public sealed class TypeDependencyNestedRoot :
    TypeDependencyOuter<int>.Inner<string>
{
}
