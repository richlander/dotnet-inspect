' VB.NET finalizer fixture for #3196.
'
' `Protected Overrides Sub Finalize()` compiles to a virtual, reuse-slot,
' parameterless `void Finalize()` with NO `.override` MethodImpl. These shapes
' are the compiler-produced canary that the implicit-object-Finalize-override
' detection must classify as finalizers.

' Single finalizer with a field write plus an explicit base call.
Public Class Handle
    Private _n As Integer

    Protected Overrides Sub Finalize()
        _n = 0
        MyBase.Finalize()
    End Sub
End Class

' A base/derived chain where both types override Finalize. The derived override
' reuses the slot introduced by the base override, so the base walk must climb
' past the reused slot to System.Object to confirm both are finalizers.
Public Class VbBase
    Protected Overrides Sub Finalize()
        MyBase.Finalize()
    End Sub
End Class

Public Class VbDerived
    Inherits VbBase

    Protected Overrides Sub Finalize()
        MyBase.Finalize()
    End Sub
End Class
