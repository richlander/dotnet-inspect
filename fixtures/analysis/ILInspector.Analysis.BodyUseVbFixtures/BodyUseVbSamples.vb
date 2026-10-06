Imports System
Imports System.Collections.Generic
Imports System.Threading.Tasks

Namespace AnalysisBodyUseVbFixtures

    Public Class VbTarget
    End Class

    Public Class VbSource
        Public Shared Async Function AsyncUse() As Task(Of VbTarget)
            Await Task.Yield()
            Return New VbTarget()
        End Function

        Public Shared Iterator Function IteratorUse() As IEnumerable(Of VbTarget)
            Yield New VbTarget()
        End Function

        Public Shared Function CapturingLambda(seed As Integer) As Func(Of VbTarget)
            Return Function()
                       Dim unused = seed
                       Return New VbTarget()
                   End Function
        End Function

        Public Shared Function AsyncLambda() As Func(Of Task(Of VbTarget))
            Return Async Function()
                       Await Task.Yield()
                       Return New VbTarget()
                   End Function
        End Function
    End Class

End Namespace
