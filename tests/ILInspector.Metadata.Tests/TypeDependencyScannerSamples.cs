namespace ILInspector.Metadata.TypeDependencyFixtures;

public interface TypeDependencyGenericBase<T>
{
}

public interface TypeDependencyGenericShared<T> :
    TypeDependencyGenericBase<T>
{
}

public interface TypeDependencyGenericLeft :
    TypeDependencyGenericShared<int>
{
}

public interface TypeDependencyGenericRight :
    TypeDependencyGenericShared<string>
{
}

public interface TypeDependencyConstructedRoot :
    TypeDependencyGenericLeft,
    TypeDependencyGenericRight
{
}
