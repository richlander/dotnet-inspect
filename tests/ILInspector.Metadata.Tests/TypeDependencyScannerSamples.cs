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

public interface TypeDependencyCaseLeaf
{
}

public interface TypeDependencyCaseleaf
{
}

public interface TypeDependencyCaseBranch :
    TypeDependencyCaseLeaf
{
}

public interface TypeDependencyCasebranch :
    TypeDependencyCaseleaf
{
}

public interface TypeDependencyCaseRoot :
    TypeDependencyCaseBranch,
    TypeDependencyCasebranch
{
}

public sealed class TypeDependencyCaseValue
{
}

public sealed class TypeDependencyCasevalue
{
}

public interface TypeDependencyCaseGenericLeft :
    TypeDependencyGenericShared<TypeDependencyCaseValue>
{
}

public interface TypeDependencyCaseGenericRight :
    TypeDependencyGenericShared<TypeDependencyCasevalue>
{
}

public interface TypeDependencyCaseGenericRoot :
    TypeDependencyCaseGenericLeft,
    TypeDependencyCaseGenericRight
{
}
