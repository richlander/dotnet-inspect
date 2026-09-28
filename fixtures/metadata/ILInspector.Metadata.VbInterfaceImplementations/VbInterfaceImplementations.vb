' VB.NET fixture for effective member access
' (docs/design/api-population-scope.md#spelling-within-api-visibility-scope).
'
' A private body that implements an interface member belongs to that
' interface's bucket. A non-private body keeps its own accessibility, wherever
' the interface is declared.

Public Interface IVbLocalPublic
    Sub LocalPublicMember()
    Sub LocalPublicMember2()
    Sub LocalPublicMember3()
End Interface

Friend Interface IVbLocalInternal
    Sub LocalInternalMember()
End Interface

Public Class VbImplementations
    Implements IVbLocalPublic, IVbLocalInternal, IDisposable

    ' Friend body, same-assembly public interface: internal.
    Friend Sub FriendImplementsLocalPublic() Implements IVbLocalPublic.LocalPublicMember
    End Sub

    ' Protected body, same-assembly public interface: protected.
    Protected Sub ProtectedImplementsLocalPublic() Implements IVbLocalPublic.LocalPublicMember2
    End Sub

    ' Private body, same-assembly public interface: public.
    Private Sub PrivateImplementsLocalPublic() Implements IVbLocalPublic.LocalPublicMember3
    End Sub

    ' Friend body, referenced public interface: internal.
    Friend Sub FriendImplementsReferenced() Implements IDisposable.Dispose
    End Sub

    ' Private body, same-assembly internal interface: internal.
    Private Sub PrivateImplementsLocalInternal() Implements IVbLocalInternal.LocalInternalMember
    End Sub
End Class
