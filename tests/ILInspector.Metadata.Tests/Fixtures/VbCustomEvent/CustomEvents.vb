Namespace VbCustomEventFixture
    Public Class CustomEvents
        Public Custom Event Changed As EventHandler
            AddHandler(value As EventHandler)
            End AddHandler

            RemoveHandler(value As EventHandler)
            End RemoveHandler

            RaiseEvent(sender As Object, e As EventArgs)
            End RaiseEvent
        End Event
    End Class
End Namespace
