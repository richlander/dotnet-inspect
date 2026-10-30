import BodyUseTerminalFolding

open BodyUseTerminalFolding

def source : List (Operand Nat String) :=
  [
    .rejected,
    .admitted [10, 11],
    .failed "trailing failure",
    .admitted [12]
  ]

def main : IO Unit := do
  IO.println s!"discrete Exists: {repr (runExists source)}"
  IO.println s!"discrete Count:  {repr (runCount source)}"
  IO.println s!"discrete Rows:   {repr (runRows source)}"
  IO.println s!"fused:           {repr (runFused source)}"

