/-!
# Rank-first capped ReturnToSender target selection

`fact-planned-compile-back-harness.md#standalone-method-target-selection`
owns target-only capped selection. The rank-first plan stable-ranks the scoped
body-bearing population, performs the complete target decision in rank order,
and stops when the requested number of eligible targets has settled. "The
selected set must equal the prefix produced by the complete pre-cap plan, and
selected targets return in original metadata order." Its receipt "does not
publish all exclusions or an exact eligible Count unless it exhausts the
population."

`ReturnToSenderTargetSourceSession.SelectCappedTargets` implements the
rank-first loop. The complete pre-cap plan, used as the reference by
`ReturnToSenderTargetScorecard`, decides every scoped body, ranks only the
eligible bodies, and takes the cap.

This module proves, for every population, eligibility decision, and cap:

- the rank-first loop selects the cap-prefix of the eligible ranked bodies;
- its receipt counts exactly the evaluated ranked prefix, which ends at the
  cap-th eligible body, and either settles the cap or exhausts the population;
- it selects the same bodies as the complete pre-cap plan whenever both
  rankings order eligible bodies the same way; and
- the host's per-assembly loop under one global cap preserves that equality.

A ranking is any list that is a permutation of the scoped population and is
pairwise ordered by a strict order. LINQ `OrderBy(Hash).ThenBy(Key)` is stable,
so it orders by `(Hash, Key, metadata position)`, which is strict whenever
metadata positions are distinct.
-/

namespace RtsCappedSelection

variable {α : Type}

/-- The capped plan's work receipt and selection, in rank order. -/
structure Receipt (α : Type) where
  selected : List α
  evaluated : Nat
  declarationCandidates : Nat
  excludedDeclarationCandidates : Nat
  deriving DecidableEq

def Receipt.empty : Receipt α := ⟨[], 0, 0, 0⟩

def bit (b : Bool) : Nat := if b then 1 else 0

/--
`SelectCappedTargets`: evaluate each ranked body, count declaration
candidates, count excluded declaration candidates, add eligible bodies, and
stop once `cap` targets are selected.
-/
def capped (eligible declaration : α → Bool) : Nat → List α → Receipt α
  | 0, _ => .empty
  | _ + 1, [] => .empty
  | k + 1, x :: xs =>
      if eligible x then
        let r := capped eligible declaration k xs
        { r with
          selected := x :: r.selected
          evaluated := r.evaluated + 1
          declarationCandidates := r.declarationCandidates + bit (declaration x) }
      else
        let r := capped eligible declaration (k + 1) xs
        { r with
          evaluated := r.evaluated + 1
          declarationCandidates := r.declarationCandidates + bit (declaration x)
          excludedDeclarationCandidates :=
            r.excludedDeclarationCandidates + bit (declaration x) }

variable (eligible declaration : α → Bool)

/-! ## Selection and receipt -/

/-- The loop selects the cap-prefix of the eligible ranked bodies. -/
theorem capped_selected (k : Nat) (ranked : List α) :
    (capped eligible declaration k ranked).selected =
      (ranked.filter eligible).take k := by
  induction ranked generalizing k with
  | nil => cases k <;> simp [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => simp [capped, Receipt.empty]
      | succ k =>
          by_cases h : eligible x = true
          · simp [capped, h, ih]
          · simp [capped, h, ih]

/-- The plan never evaluates more bodies than the population contains. -/
theorem capped_evaluated_le (k : Nat) (ranked : List α) :
    (capped eligible declaration k ranked).evaluated ≤ ranked.length := by
  induction ranked generalizing k with
  | nil => cases k <;> simp [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => simp [capped, Receipt.empty]
      | succ k =>
          by_cases h : eligible x = true
          · simp only [capped, h, ↓reduceIte, List.length_cons]
            have := ih k
            omega
          · simp only [capped, h, Bool.false_eq_true, ↓reduceIte,
              List.length_cons]
            have := ih (k + 1)
            omega

/--
Every receipt counter describes exactly the evaluated ranked prefix, and the
selection is that prefix's eligible bodies.
-/
theorem capped_receipt_prefix (k : Nat) (ranked : List α) :
    let r := capped eligible declaration k ranked
    let evaluated := ranked.take r.evaluated
    r.selected = evaluated.filter eligible ∧
    r.declarationCandidates = evaluated.countP declaration ∧
    r.excludedDeclarationCandidates =
      evaluated.countP (fun x => declaration x && !eligible x) := by
  induction ranked generalizing k with
  | nil => cases k <;> simp [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => simp [capped, Receipt.empty]
      | succ k =>
          by_cases h : eligible x = true
          · obtain ⟨h₁, h₂, h₃⟩ := ih k
            simp only [capped, h, ↓reduceIte, List.take_succ_cons]
            refine ⟨?_, ?_, ?_⟩
            · simp [h, h₁]
            · simp only [List.countP_cons, bit, h₂]
              try (split <;> simp_all)
            · simp [h, h₃]
          · obtain ⟨h₁, h₂, h₃⟩ := ih (k + 1)
            simp only [capped, h, Bool.false_eq_true, ↓reduceIte,
              List.take_succ_cons]
            refine ⟨?_, ?_, ?_⟩
            · simp [h, h₁]
            · simp only [List.countP_cons, bit, h₂]
              try (split <;> simp_all)
            · simp only [List.countP_cons, bit, h₃]
              split <;> simp_all

/--
The plan either settles the cap or exhausts the population, and an exhausted
run has selected every eligible body. A settled run may also have selected
every eligible body, but its receipt cannot tell; see
`published_settled_ignores_unevaluated`.
-/
theorem capped_settles_or_exhausts (k : Nat) (ranked : List α) :
    let r := capped eligible declaration k ranked
    r.selected.length = k ∨
      (r.evaluated = ranked.length ∧ r.selected = ranked.filter eligible) := by
  induction ranked generalizing k with
  | nil => cases k <;> simp [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => simp [capped, Receipt.empty]
      | succ k =>
          by_cases h : eligible x = true
          · rcases ih k with h' | ⟨he, hs⟩
            · left; simp [capped, h, h']
            · right; simp [capped, h, he, hs]
          · rcases ih (k + 1) with h' | ⟨he, hs⟩
            · left; simp [capped, h, h']
            · right; simp [capped, h, he, hs]

/--
When the cap settles, the last evaluated body is the cap-th eligible one: the
plan performs no decision after the cap is reached.
-/
theorem capped_stops_at_cap (k : Nat) (ranked : List α) (hk : 0 < k)
    (hsettled : (capped eligible declaration k ranked).selected.length = k) :
    ∃ before last,
      ranked.take (capped eligible declaration k ranked).evaluated =
        before ++ [last] ∧ eligible last = true := by
  induction ranked generalizing k with
  | nil => cases k <;> simp_all [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => omega
      | succ k =>
          by_cases h : eligible x = true
          · cases k with
            | zero =>
                refine ⟨[], x, ?_, h⟩
                simp [capped, h, Receipt.empty]
            | succ k =>
                have hs : (capped eligible declaration (k + 1) xs).selected.length
                    = k + 1 := by
                  simpa [capped, h] using hsettled
                obtain ⟨before, last, htake, hlast⟩ := ih (k + 1) (by omega) hs
                refine ⟨x :: before, last, ?_, hlast⟩
                simp [capped, h, htake]
          · have hs : (capped eligible declaration (k + 1) xs).selected.length
                = k + 1 := by
              simpa [capped, h] using hsettled
            obtain ⟨before, last, htake, hlast⟩ := ih (k + 1) hk hs
            refine ⟨x :: before, last, ?_, hlast⟩
            simp [capped, h, htake]

/--
The receipt published by `SelectCappedTargets`: the loop's receipt plus
`RankedBodyCount`, the size of the ranked population.
-/
def published (k : Nat) (ranked : List α) : Receipt α × Nat :=
  (capped eligible declaration k ranked, ranked.length)

/--
The loop's receipt after a settled run is determined by the evaluated prefix:
replacing the unevaluated ranked tail with any other bodies leaves it
unchanged.
-/
theorem capped_settled_ignores_unevaluated (k : Nat) (ranked rest : List α)
    (hsettled : (capped eligible declaration k ranked).selected.length = k) :
    capped eligible declaration k
        (ranked.take (capped eligible declaration k ranked).evaluated ++ rest) =
      capped eligible declaration k ranked := by
  induction ranked generalizing k with
  | nil => cases k <;> simp_all [capped, Receipt.empty]
  | cons x xs ih =>
      cases k with
      | zero => simp [capped]
      | succ k =>
          by_cases h : eligible x = true
          · have hs : (capped eligible declaration k xs).selected.length = k := by
              simpa [capped, h] using hsettled
            simp [capped, h, ih k hs]
          · have hs : (capped eligible declaration (k + 1) xs).selected.length
                = k + 1 := by
              simpa [capped, h] using hsettled
            simp [capped, h, ih (k + 1) hs]

/--
After a settled run, the published receipt depends only on the evaluated
prefix and the population size: every same-length replacement of the
unevaluated tail publishes the same receipt. When the run did not exhaust the
population, that tail is non-empty and its eligible bodies are invisible to the
receipt.
-/
theorem published_settled_ignores_unevaluated (k : Nat) (ranked rest : List α)
    (hsettled : (capped eligible declaration k ranked).selected.length = k)
    (hlength : rest.length =
      (ranked.drop (capped eligible declaration k ranked).evaluated).length) :
    published eligible declaration k
        (ranked.take (capped eligible declaration k ranked).evaluated ++ rest) =
      published eligible declaration k ranked := by
  have hle := capped_evaluated_le eligible declaration k ranked
  unfold published
  rw [capped_settled_ignores_unevaluated eligible declaration k ranked rest
    hsettled]
  simp only [List.length_append, List.length_take, List.length_drop,
    Prod.mk.injEq, true_and] at hlength ⊢
  omega

/-- Two equal-size populations that differ only after a settled run's
evaluated prefix can have different eligible Counts and still publish the
same receipt. -/
example :
    let e := fun n : Nat => n % 2 == 0
    let d := fun _ : Nat => false
    published e d 1 [0, 1] = published e d 1 [0, 2] ∧
      ([0, 1].filter e).length ≠ ([0, 2].filter e).length := by
  decide

/-! ## Equality with the complete pre-cap plan -/

/-- Lists strictly ordered by an asymmetric relation are determined by content. -/
theorem eq_of_perm_of_pairwise {lt : α → α → Prop}
    (asymm : ∀ a b, lt a b → ¬ lt b a) :
    ∀ {l₁ l₂ : List α}, l₁.Perm l₂ →
      l₁.Pairwise lt → l₂.Pairwise lt → l₁ = l₂
  | [], l₂, hp, _, _ => List.Perm.nil_eq hp
  | a :: as, [], hp, _, _ => absurd hp.length_eq (by simp)
  | a :: as, b :: bs, hp, h₁, h₂ => by
      rw [List.pairwise_cons] at h₁ h₂
      by_cases hab : a = b
      · subst hab
        rw [eq_of_perm_of_pairwise asymm (hp.cons_inv) h₁.2 h₂.2]
      · have ha : a ∈ bs := by
          have := hp.subset (List.mem_cons_self ..)
          simpa [hab] using this
        have hb : b ∈ as := by
          have := hp.symm.subset (List.mem_cons_self ..)
          simpa [Ne.symm hab] using this
        exact absurd (h₂.1 a ha) (asymm _ _ (h₁.1 b hb))

/--
Rank-first selection equals the complete pre-cap plan.

* `population` is the scoped body-bearing population.
* `rankFirst` ranks the whole scoped population by `ltRankFirst`.
* `eager` ranks only the eligible bodies by `ltEager`.
* The two orders agree on eligible bodies.
-/
theorem rankFirst_eq_eager
    (population rankFirst eager : List α)
    (ltRankFirst ltEager : α → α → Prop)
    (asymm : ∀ a b, ltEager a b → ¬ ltEager b a)
    (hRankFirst : rankFirst.Perm population)
    (hRankFirstSorted : rankFirst.Pairwise ltRankFirst)
    (hEager : eager.Perm (population.filter eligible))
    (hEagerSorted : eager.Pairwise ltEager)
    (agree : ∀ a b, eligible a = true → eligible b = true →
      (ltRankFirst a b ↔ ltEager a b))
    (cap : Nat) :
    (capped eligible declaration cap rankFirst).selected = eager.take cap := by
  rw [capped_selected]
  congr 1
  apply eq_of_perm_of_pairwise asymm
  · exact (hRankFirst.filter eligible).trans hEager.symm
  · have := hRankFirstSorted.filter eligible
    refine List.Pairwise.imp_of_mem ?_ this
    intro a b ha hb hlt
    exact (agree a b (List.mem_filter.mp ha).2 (List.mem_filter.mp hb).2).mp hlt
  · exact hEagerSorted

/-! ## The host's global cap across assemblies -/

/--
The host visits assemblies in caller order and gives each one the remaining
global cap.
-/
def acrossAssemblies {β : Type} (select : β → Nat → List α) :
    Nat → List β → List α
  | _, [] => []
  | cap, a :: as =>
      let s := select a cap
      s ++ acrossAssemblies select (cap - s.length) as

/-- Per-assembly equality extends to the whole corpus under one global cap. -/
theorem acrossAssemblies_congr {β : Type} {f g : β → Nat → List α}
    (h : ∀ a cap, f a cap = g a cap) (cap : Nat) (assemblies : List β) :
    acrossAssemblies f cap assemblies = acrossAssemblies g cap assemblies := by
  induction assemblies generalizing cap with
  | nil => rfl
  | cons a as ih => simp [acrossAssemblies, h, ih]

/-! ## Example -/

/-- Bodies `0..5` in rank order; even bodies are eligible; `0` and `3` are
declaration candidates. Cap `2` evaluates `0, 1, 2` and excludes nothing. -/
example :
    let r := capped (fun n : Nat => n % 2 == 0) (fun n => n == 0 || n == 3)
      2 [0, 1, 2, 3, 4, 5]
    (r.selected, r.evaluated, r.declarationCandidates,
      r.excludedDeclarationCandidates) = ([0, 2], 3, 1, 0) := by
  decide

end RtsCappedSelection
