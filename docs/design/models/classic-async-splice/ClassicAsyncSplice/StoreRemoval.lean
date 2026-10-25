/-!
# Store removal

The housekeeping-clear role of `classic-async-reconstruction.md` (General
inverse) removes a store only when it is provably unobservable. This module
states the two laws the role rests on over one path of memory operations, and
the composition statement that a sequence of such removals stays sound when
each is justified against the body the earlier removals produced.

- `removeDeadStore_observe`: a store from which no read of its location is
  reachable before another store to it changes no observed value (the
  compiler-temporary arm).
- `removeRedundantStore_observe`: a store of the value the location already
  holds changes nothing (the adjacent-clear arm; same-block adjacency is what
  establishes the premise that the location holds the value).
- `removals_compose`: observational equality is transitive, so removals
  justified one at a time compose.
-/

namespace StoreRemoval

/-- One operation on a path: a store to a location, a read of a location, or an opaque effect. -/
inductive Op where
  | store (loc : Nat) (value : Nat)
  | read (loc : Nat)
  | effect (e : Nat)
  deriving DecidableEq

/-- Memory as a total function from locations to values. -/
def Memory := Nat → Nat

def Memory.write (mem : Memory) (loc value : Nat) : Memory :=
  fun l => if l = loc then value else mem l

/-- What a path observes: the values its reads return and the effects it performs, in order. -/
inductive Observation where
  | readValue (loc value : Nat)
  | effect (e : Nat)
  deriving DecidableEq

/-- Run a path from a memory, collecting the observations. -/
def observe (mem : Memory) : List Op → List Observation
  | [] => []
  | .store loc value :: rest => observe (mem.write loc value) rest
  | .read loc :: rest => .readValue loc (mem loc) :: observe mem rest
  | .effect e :: rest => .effect e :: observe mem rest

/--
No read of `loc` is reachable on the path before another store to `loc`: the
dead-store premise for the first store on this path.
-/
def NoReadBeforeStore (loc : Nat) : List Op → Prop
  | [] => True
  | .store l _ :: rest => l = loc ∨ NoReadBeforeStore loc rest
  | .read l :: rest => l ≠ loc ∧ NoReadBeforeStore loc rest
  | .effect _ :: rest => NoReadBeforeStore loc rest

/-- Two memories that agree everywhere except at `loc` observe the same path when the path is dead for `loc`. -/
theorem observe_agree_except (loc : Nat) (path : List Op) (dead : NoReadBeforeStore loc path)
    (m1 m2 : Memory) (agree : ∀ l, l ≠ loc → m1 l = m2 l) :
    observe m1 path = observe m2 path := by
  induction path generalizing m1 m2 with
  | nil => rfl
  | cons op rest ih =>
    cases op with
    | store l v =>
      simp only [NoReadBeforeStore] at dead
      simp only [observe]
      rcases dead with hl | hrest
      · subst l
        have : m1.write loc v = m2.write loc v := by
          funext x
          simp only [Memory.write]
          by_cases hx : x = loc
          · simp [hx]
          · simp [hx, agree x hx]
        rw [this]
      · apply ih hrest
        intro x hx
        simp only [Memory.write]
        by_cases hxl : x = l
        · simp [hxl]
        · simp [hxl, agree x hx]
    | read l =>
      simp only [NoReadBeforeStore] at dead
      simp only [observe]
      rw [ih dead.2 m1 m2 agree, agree l dead.1]
    | effect e =>
      simp only [NoReadBeforeStore] at dead
      simp only [observe]
      rw [ih dead m1 m2 agree]

/-- **Dead store.** Removing a store from which no read of its location is reachable before another store changes no observation. -/
theorem removeDeadStore_observe (mem : Memory) (loc value : Nat) (rest : List Op)
    (dead : NoReadBeforeStore loc rest) :
    observe mem (.store loc value :: rest) = observe mem rest := by
  simp only [observe]
  apply observe_agree_except loc rest dead
  intro l hl
  simp [Memory.write, hl]

/-- **Redundant store.** A store of the value the location already holds changes no observation. -/
theorem removeRedundantStore_observe (mem : Memory) (loc value : Nat) (rest : List Op)
    (held : mem loc = value) :
    observe mem (.store loc value :: rest) = observe mem rest := by
  simp only [observe]
  have : mem.write loc value = mem := by
    funext x
    simp only [Memory.write]
    by_cases hx : x = loc
    · subst hx; simp [held]
    · simp [hx]
  rw [this]

/-- After a store, the location holds the stored value: the premise an adjacent second store inherits. -/
theorem write_holds (mem : Memory) (loc value : Nat) : (mem.write loc value) loc = value := by
  simp [Memory.write]

/-- **Adjacent clear.** The second of two adjacent stores of one value to one location is removable. -/
theorem removeAdjacentStore_observe (mem : Memory) (loc value : Nat) (rest : List Op) :
    observe mem (.store loc value :: .store loc value :: rest) = observe mem (.store loc value :: rest) := by
  simp only [observe]
  exact removeRedundantStore_observe _ loc value rest (write_holds mem loc value)

/-- One removal step is sound when the path before and after it observe alike from the memory at hand. -/
def Sound (mem : Memory) (before after : List Op) : Prop :=
  observe mem before = observe mem after

/-- A chain of removals, each justified against the path the earlier removals produced. -/
inductive SoundChain (mem : Memory) : List Op → List Op → Prop where
  | refl (path : List Op) : SoundChain mem path path
  | step {path reduced further : List Op} :
      SoundChain mem path reduced → Sound mem reduced further → SoundChain mem path further

/-- **Composition.** A sequence of removals, each sound against the path the earlier removals produced, is sound as a whole. -/
theorem removals_compose (mem : Memory) {path reduced : List Op}
    (chain : SoundChain mem path reduced) : Sound mem path reduced := by
  induction chain with
  | refl => rfl
  | step _ hs ih =>
    unfold Sound at hs ih ⊢
    rw [ih, hs]

end StoreRemoval
