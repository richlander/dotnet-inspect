// Fixture namespaces for MemberBodyFactsTests.ReferencedNamespaces_ResultIsOrdinalSorted.
//
// "NamespaceOrderFixtures.HPack" and "NamespaceOrderFixtures.Headers" share the
// prefix "NamespaceOrderFixtures.H" and then diverge on 'P' (0x50) vs 'e' (0x65):
// StringComparer.Ordinal ranks HPack before Headers (uppercase 'P' < lowercase
// 'e'), while StringComparer.CurrentCulture/InvariantCulture and
// StringComparer.OrdinalIgnoreCase rank Headers before HPack (letter 'e' < 'p').
// Referencing both from one method pins ReferencedNamespaces to ordinal ordering
// specifically, rather than to an ordering shared by ordinal and culture-aware
// comparers alike.

namespace NamespaceOrderFixtures.HPack
{
    public sealed class Marker
    {
    }
}

namespace NamespaceOrderFixtures.Headers
{
    public sealed class Marker
    {
    }
}
