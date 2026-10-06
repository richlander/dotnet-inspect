import ProducerCapabilityDemand.Coverage

/-!
# Which validator checks soundness needs

`ProducerCapabilityPlanValidator` reports 22 rejection reasons. This module
proves which checks are implied by others and which can be loosened without
losing soundness, and gives a counterexample where a check is required.

Soundness of an accepted plan means:

- every requirement association receives exactly one satisfaction, and
  every satisfaction path realizes its requirement (`Coverage`);
- every selected provision's dependencies are selected and can execute
  before it; and
- the plan does not join facts across resources.
-/

namespace ProducerCapabilityDemand

namespace ValidatorChecks

/-! ## `DependencyCycle` is implied by `DependencyOrder` -/

section Dependencies

variable {P : Type}

/--
`ValidateDependencies` requires each dependency of a selected provision to be
selected (`MissingDependency`) at an earlier index (`DependencyOrder`).
-/
def DependencyOrdered (selected : P → Prop) (dependsOn : P → P → Prop)
    (index : P → Nat) : Prop :=
  ∀ a b, selected a → dependsOn a b → selected b ∧ index b < index a

theorem ordered_path_decreases {selected : P → Prop}
    {dependsOn : P → P → Prop} {index : P → Nat}
    (h : DependencyOrdered selected dependsOn index) {a b : P}
    (path : Relation.TransGen dependsOn a b) (ha : selected a) :
    selected b ∧ index b < index a := by
  induction path with
  | single hab => exact h _ _ ha hab
  | tail _ hbc ih =>
      obtain ⟨hb, hlt⟩ := ih
      obtain ⟨hc, hlt'⟩ := h _ _ hb hbc
      exact ⟨hc, Nat.lt_trans hlt' hlt⟩

/-- Ordered selected dependencies cannot contain a cycle. -/
theorem ordered_implies_acyclic {selected : P → Prop}
    {dependsOn : P → P → Prop} {index : P → Nat}
    (h : DependencyOrdered selected dependsOn index) {a : P}
    (ha : selected a) : ¬ Relation.TransGen dependsOn a a := by
  intro cycle
  exact Nat.lt_irrefl _ (ordered_path_decreases h cycle ha).2

/--
The converse fails: an acyclic dependency listed after its dependent is
rejected by `DependencyOrder`. Keeping the cycle check instead would require
the executor to sort provisions itself.
-/
example :
    ¬ DependencyOrdered (fun _ : Nat => True) (fun a b => a = 0 ∧ b = 1) id := by
  intro h
  exact absurd (h 0 1 trivial ⟨rfl, rfl⟩).2 (by decide)

end Dependencies

/-! ## Satisfaction checks imply known capabilities and one resource -/

section Paths

open Coverage

variable {K : Type} [DecidableEq K]

/-- An accepted walk ends at its provision's key or at some edge target. -/
theorem walk_final_key {o o' : Offer K} {path : List (Edge K)}
    (h : walkLastEdge o path = some o') :
    o'.1 = o.1 ∨ ∃ e ∈ path, o'.1 = e.target := by
  induction path generalizing o with
  | nil =>
      simp only [walkLastEdge, Option.some.injEq] at h
      exact .inl (by rw [h])
  | cons e es ih =>
      simp only [walkLastEdge] at h
      split at h
      · rcases ih h with h' | ⟨e', he', h'⟩
        · exact .inr ⟨e, List.mem_cons_self .., h'⟩
        · exact .inr ⟨e', List.mem_cons_of_mem _ he', h'⟩
      · cases h

theorem accepts_final_key {p : Provision K} {path : List (Edge K)}
    {r : Requirement K} (h : accepts walkLastEdge p path r = true) :
    r.key = p.key ∨ ∃ e ∈ path, r.key = e.target := by
  unfold accepts at h
  split at h
  · rename_i k ex hw
    simp only [Bool.and_eq_true, decide_eq_true_eq] at h
    rw [← h.1]
    exact walk_final_key hw
  · cases h

/--
Any property shared by the provision's key and every declared coverage target
holds for an accepted satisfaction's requirement key. With `Q` as "belongs to
the producer domain", the requirement-level `ProducerDomainMismatch` check on
scope, capability, completion, and outcome is implied by the declaration and
coverage domain checks.
-/
theorem accepted_key_inherits (Q : K → Prop)
    {coverages : List (Edge K)} {p : Provision K} {path : List (Edge K)}
    {r : Requirement K} (hp : Q p.key) (hcov : ∀ e ∈ coverages, Q e.target)
    (hpath : ∀ e ∈ path, e ∈ coverages)
    (h : accepts walkLastEdge p path r = true) : Q r.key := by
  rcases accepts_final_key h with hk | ⟨e, he, hk⟩
  · rw [hk]; exact hp
  · rw [hk]; exact hcov e (hpath e he)

/--
`UnknownCapability` is implied: an accepted satisfaction's requirement
capability is produced by a declared provision or is a declared coverage
target. `capability` projects the capability identity from a key.
-/
theorem accepted_capability_is_known {C : Type} (capability : K → C)
    {provisions : List (Provision K)} {coverages : List (Edge K)}
    {p : Provision K} {path : List (Edge K)} {r : Requirement K}
    (hp : p ∈ provisions) (hpath : ∀ e ∈ path, e ∈ coverages)
    (h : accepts walkLastEdge p path r = true) :
    capability r.key ∈ provisions.map (capability ·.key) ++
      coverages.map (capability ·.target) := by
  rcases accepts_final_key h with hk | ⟨e, he, hk⟩
  · exact List.mem_append_left _ (List.mem_map.mpr ⟨p, hp, by rw [hk]⟩)
  · exact List.mem_append_right _
      (List.mem_map.mpr ⟨e, hpath e he, by rw [hk]⟩)

/-- A non-empty accepted walk starts at its first edge's source. -/
theorem walk_starts_at_source {o o' : Offer K} {e : Edge K}
    {es : List (Edge K)} (h : walkLastEdge o (e :: es) = some o') :
    o.1 = e.source := by
  simp only [walkLastEdge] at h
  split at h
  · assumption
  · cases h

/--
Declaration-level `ResourceMismatch` is implied for every provision a plan
uses. The requirement check fixes the requirement's resource, and the
coverage check fixes every edge's source and target resources, so an
accepted satisfaction's provision is on the requirement's resource.
`resource` projects a key's scope resource.
-/
theorem accepted_provision_resource {R : Type} (resource : K → R)
    {coverages : List (Edge K)} {p : Provision K} {path : List (Edge K)}
    {r : Requirement K} {resource₀ : R}
    (hr : resource r.key = resource₀)
    (hcov : ∀ e ∈ coverages,
      resource e.source = resource₀ ∧ resource e.target = resource₀)
    (hpath : ∀ e ∈ path, e ∈ coverages)
    (h : accepts walkLastEdge p path r = true) :
    resource p.key = resource₀ := by
  cases path with
  | nil =>
      rcases accepts_final_key h with hk | ⟨e, he, _⟩
      · rw [← hk, hr]
      · cases he
  | cons e es =>
      unfold accepts at h
      split at h
      · rename_i k ex hw
        have hstart := walk_starts_at_source hw
        simp only at hstart
        rw [hstart]
        exact (hcov e (hpath e (List.mem_cons_self ..))).1
      · cases h

end Paths

/-! ## `UnknownAssociation` is ignored by construction -/

section Associations

variable {A S : Type} [DecidableEq A]

/--
`ValidateSatisfactions` builds the plan by visiting requirements in order and
looking up each association's satisfaction.
-/
def assemble (requirements : List A) (candidates : List (A × S)) :
    List (Option S) :=
  requirements.map fun a => (candidates.find? (·.1 = a)).map (·.2)

/--
Inserting a candidate anywhere among the recorded candidates leaves the plan
unchanged when its association is outside the requirement set or an earlier
candidate already holds it. `ValidateSatisfactions` keeps the first recorded
candidate per association, so the lemma is stated over recorded candidates.
-/
theorem assemble_ignores_at (requirements : List A)
    (before after : List (A × S)) (c : A × S)
    (hc : c.1 ∉ requirements ∨ ∃ c' ∈ before, c'.1 = c.1) :
    assemble requirements (before ++ c :: after) =
      assemble requirements (before ++ after) := by
  unfold assemble
  apply List.map_congr_left
  intro a ha
  rw [List.find?_append, List.find?_append]
  cases hfind : before.find? (·.1 = a) with
  | some _ => rfl
  | none =>
      have hca : ¬ c.1 = a := by
        intro heq
        rcases hc with hnot | ⟨c', hc', hsame⟩
        · exact hnot (heq ▸ ha)
        · rw [List.find?_eq_none] at hfind
          exact hfind c' hc' (by simp [hsame, heq])
      simp [hca]

/--
Satisfactions for associations outside the requirement set never reach the
plan, at any position, so `UnknownAssociation` is a diagnostic for a producer
planning bug.
-/
theorem assemble_ignores_unknown (requirements : List A)
    (before after : List (A × S)) (c : A × S)
    (hc : c.1 ∉ requirements) :
    assemble requirements (before ++ c :: after) =
      assemble requirements (before ++ after) :=
  assemble_ignores_at requirements before after c (.inl hc)

/--
A later satisfaction for an already recorded association never reaches the
plan, at any position, so satisfaction-site `DuplicateAssociation` is a
diagnostic.
-/
theorem assemble_ignores_duplicates (requirements : List A)
    (before after : List (A × S)) (c : A × S)
    (hc : ∃ c' ∈ before, c'.1 = c.1) :
    assemble requirements (before ++ c :: after) =
      assemble requirements (before ++ after) :=
  assemble_ignores_at requirements before after c (.inr hc)

/--
`ValidateSatisfactions` records a candidate only after it names a selected
provision and its covering path walks (`valid`). Every satisfaction in a plan
assembled from recorded candidates is therefore valid, and an association
left without one stays `none`, which `UnsatisfiedRequirement` rejects. The
satisfaction-site `UnknownProvision` and `InvalidCoveringPath` rejections are
therefore diagnostics; the skip is what soundness needs.
-/
theorem assemble_recorded_valid (requirements : List A)
    (candidates : List (A × S)) (valid : A × S → Bool) :
    ∀ x ∈ assemble requirements (candidates.filter valid), ∀ s, x = some s →
      ∃ c ∈ candidates, valid c = true ∧ c.2 = s := by
  intro x hx s hs
  unfold assemble at hx
  obtain ⟨a, _, rfl⟩ := List.mem_map.mp hx
  cases hfind : (candidates.filter valid).find? (·.1 = a) with
  | none => simp [hfind] at hs
  | some c =>
      simp only [hfind, Option.map_some, Option.some.injEq] at hs
      have hmem := List.mem_of_find?_eq_some hfind
      obtain ⟨hc, hv⟩ := List.mem_filter.mp hmem
      exact ⟨c, hc, hv, hs⟩

end Associations

/-! ## `DuplicateDeclaration` matters only outside the plan -/

/--
With duplicate provision identities, a validator that keeps the first
declaration and an executor that resolves identities itself and keeps the
last disagree. Current adopters do not re-resolve declaration identities after
validation, so they never see the duplicate and the check is diagnostic under
that assumption. A keyed-map input type would make it unrepresentable.
-/
example :
    let declarations : List (Nat × String) := [(1, "Names"), (1, "Signature")]
    (declarations.find? (·.1 = 1)).map (·.2) ≠
      (declarations.reverse.find? (·.1 = 1)).map (·.2) := by
  decide

/-! ## Completion "at least" is sound for monotone completion (#9486) -/

section Completion

variable {K C V : Type} [DecidableEq K] [DecidableEq C]

/-- A key whose completion is ordered separately from its other identities. -/
structure Offer (K C : Type) where
  key : K
  completion : C
  exact : Bool

structure Edge (K C : Type) where
  source : K
  sourceCompletion : C
  target : K
  targetCompletion : C
  exact : Bool

structure Requirement (K C : Type) where
  key : K
  completion : C
  needsExact : Bool

/-- A producer's meaning, with an owner-declared completion order `atLeast`. -/
structure Semantics (K C V : Type) where
  realizes : K → C → V → Prop
  exactCard : V → Prop
  project : Edge K C → V → V
  atLeast : C → C → Bool

variable (S : Semantics K C V)

/--
The owner certifies that a result complete at one level also satisfies every
level it is at least.
-/
def CompletionMonotone : Prop :=
  ∀ k c c' v, S.atLeast c c' = true → S.realizes k c v → S.realizes k c' v

def EdgeSound (e : Edge K C) : Prop :=
  ∀ v, S.realizes e.source e.sourceCompletion v →
    S.realizes e.target e.targetCompletion (S.project e v) ∧
    (e.exact = true → S.exactCard v → S.exactCard (S.project e v))

/--
Exact completion, as `ProducerCapabilityPlanValidator` checks today, with the
conjunctive exactness rule proven sound in `Coverage`.
-/
def walkExact : Offer K C → List (Edge K C) → Option (Offer K C)
  | o, [] => some o
  | o, e :: es =>
      if o.key = e.source ∧ o.completion = e.sourceCompletion then
        walkExact ⟨e.target, e.targetCompletion, o.exact && e.exact⟩ es
      else none

/-- Relaxed: an offer may feed an edge whose source completion it is at least. -/
def walkAtLeast : Offer K C → List (Edge K C) → Option (Offer K C)
  | o, [] => some o
  | o, e :: es =>
      if o.key = e.source ∧ S.atLeast o.completion e.sourceCompletion = true then
        walkAtLeast ⟨e.target, e.targetCompletion, o.exact && e.exact⟩ es
      else none

def acceptsAtLeast (o : Offer K C) (path : List (Edge K C))
    (r : Requirement K C) : Bool :=
  match walkAtLeast S o path with
  | some o' =>
      decide (o'.key = r.key) && S.atLeast o'.completion r.completion &&
        (!r.needsExact || o'.exact)
  | none => false

def evalPath (v : V) (path : List (Edge K C)) : V :=
  path.foldl (fun v e => S.project e v) v

omit [DecidableEq C] in
theorem walkAtLeast_sound (hmono : CompletionMonotone S)
    (path : List (Edge K C)) (hs : ∀ e ∈ path, EdgeSound S e) :
    ∀ (o o' : Offer K C) (v : V),
      S.realizes o.key o.completion v → (o.exact = true → S.exactCard v) →
      walkAtLeast S o path = some o' →
      S.realizes o'.key o'.completion (evalPath S v path) ∧
        (o'.exact = true → S.exactCard (evalPath S v path)) := by
  induction path with
  | nil =>
      intro o o' v hk hx hw
      simp only [walkAtLeast, Option.some.injEq] at hw
      subst hw
      exact ⟨hk, hx⟩
  | cons e es ih =>
      intro o o' v hk hx hw
      simp only [walkAtLeast] at hw
      split at hw
      · rename_i hmatch
        obtain ⟨hkey, hle⟩ := hmatch
        have hsrc : S.realizes e.source e.sourceCompletion v :=
          hmono _ _ _ _ hle (hkey ▸ hk)
        have hv := hs e (List.mem_cons_self ..) v hsrc
        refine ih (fun e' h => hs e' (List.mem_cons_of_mem _ h)) _ o'
          (S.project e v) hv.1 ?_ hw
        intro hboth
        simp only [Bool.and_eq_true] at hboth
        exact hv.2 hboth.2 (hx hboth.1)
      · cases hw

omit [DecidableEq C] in
/-- The "at least" rule is sound when the owner's completion order is monotone. -/
theorem atLeast_sound (hmono : CompletionMonotone S)
    {o : Offer K C} {path : List (Edge K C)} {r : Requirement K C} {v : V}
    (hp : S.realizes o.key o.completion v ∧ (o.exact = true → S.exactCard v))
    (hs : ∀ e ∈ path, EdgeSound S e)
    (hacc : acceptsAtLeast S o path r = true) :
    S.realizes r.key r.completion (evalPath S v path) ∧
      (r.needsExact = true → S.exactCard (evalPath S v path)) := by
  unfold acceptsAtLeast at hacc
  split at hacc
  · rename_i o' hw
    have hfin := walkAtLeast_sound S hmono path hs o o' v hp.1 hp.2 hw
    simp only [Bool.and_eq_true, decide_eq_true_eq, Bool.or_eq_true,
      Bool.not_eq_true'] at hacc
    obtain ⟨⟨hkey, hle⟩, hx⟩ := hacc
    refine ⟨hkey ▸ hmono _ _ _ _ hle hfin.1, fun hn => ?_⟩
    cases hx with
    | inl h => simp [h] at hn
    | inr h => exact hfin.2 h
  · cases hacc

/--
"At least" only loosens the check: every exact walk is an at-least walk. This
covers edge-source matching; the final requirement check loosens the same way
by reflexivity of `atLeast`.
-/
theorem walkExact_le_walkAtLeast
    (hrefl : ∀ c, S.atLeast c c = true) :
    ∀ (path : List (Edge K C)) (o : Offer K C),
      walkExact o path ≠ none → walkAtLeast S o path = walkExact o path
  | [], _, _ => rfl
  | e :: es, o, h => by
      simp only [walkExact] at h ⊢
      simp only [walkAtLeast]
      split at h
      · rename_i hmatch
        obtain ⟨hk, hc⟩ := hmatch
        have h₁ : o.key = e.source ∧
            S.atLeast o.completion e.sourceCompletion = true :=
          ⟨hk, hc ▸ hrefl _⟩
        have h₂ : o.key = e.source ∧ o.completion = e.sourceCompletion :=
          ⟨hk, hc⟩
        simp only [h₁, h₂, and_self, ↓reduceIte, hrefl]
        exact walkExact_le_walkAtLeast hrefl es _ h
      · exact absurd rfl h

end Completion

/-! ### Monotonicity is the new owner obligation -/

/--
Without monotonicity the relaxation is unsound: a result realized at
completion `1` that the order says is at least completion `0` need not realize
completion `0`.
-/
example :
    let S : Semantics Unit Nat Nat :=
      { realizes := fun _ c v => v = c
        exactCard := fun _ => True
        project := fun _ v => v
        atLeast := fun c c' => decide (c' ≤ c) }
    acceptsAtLeast S ⟨(), 1, false⟩ [] ⟨(), 0, false⟩ = true ∧
      S.realizes () 1 1 ∧ ¬ S.realizes () 0 (evalPath S 1 []) := by
  decide

end ValidatorChecks

end ProducerCapabilityDemand
