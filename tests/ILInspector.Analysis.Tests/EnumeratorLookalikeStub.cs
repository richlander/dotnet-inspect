// An UNTRUSTED enumerator lookalike for the enumerator-allocation trust gate (#1814): a type in
// the framework collections namespace whose name starts with "IEnumerator", but defined in this
// (unsigned) test assembly, so it carries no framework public-key-token. A foreach that binds to
// a GetEnumerator returning this type must NOT be flagged as an enumerator allocation —
// LibraryBodyIndex.IsInterfaceEnumeratorAllocation is trust-gated (#1708), so only the real
// framework IEnumerator/IEnumerator<T> counts. The MoveNext/Current members let it satisfy the
// foreach enumerator pattern.
namespace System.Collections.Generic;

public sealed class IEnumeratorLookalike
{
    public bool MoveNext() => false;

    public int Current => 0;
}
