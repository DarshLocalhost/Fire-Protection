# FireProtectionSystem — Risk Register

> **Deliverable E** of the production rewrite (master prompt §5 / §45 Step 9).
> Risks that could make placement *look* successful while being wrong, or that could destabilize the model.
> L = likelihood, I = impact (both L/M/H). "Runtime-verifiable only" risks **cannot** be closed in this
> environment (hard rule 18) — they stay **open/pending**.

Related: [[ARCHITECTURE_AUDIT]] · [[STANDARDS_COMPLIANCE_MATRIX]] · [[MIGRATION_PLAN]] · [[DECISIONS]] · [[TODO]]

---

| ID | Risk | L | I | Trigger / failure mode | Mitigation | Status |
|---|---|---|---|---|---|---|
| **R-01** | **Wrong Revit hosting semantics** — treating WorkPlaneBased as level-based | H | H | `Sprinkler - Pendent - Hosted` is WorkPlaneBased; level overload → instance at (0,0,0) (proven) | Decision 011: strategy keyed to proven `FamilyPlacementType`; WorkPlaneBased→ face host or `SketchPlane` overload; **never** level overload | **Mitigated in code (Phase 5)**; ⏸ runtime confirm |
| **R-02** | **Linked-ceiling face hosting unreliable** — `CreateLinkReference` face may not host across API builds | M | H | linked-only ceilings; face host returns null or throws | WorkPlaneBased no longer depends on a face (SketchPlane fallback honors XYZ); FaceBased fails explicitly with `REQUIRED_HOST_UNAVAILABLE` (no fake instance) | Partially mitigated; ⏸ runtime confirm link face hosting |
| **R-03** | **Created-but-wrong instance reported as success** (hard rule 8) | H | H | any strategy returns an ElementId at the wrong location | Post-placement validation: compare actual vs requested XYZ → `PLACED_BUT_INVALID`; origin-snap detection | **Mitigated in code (Phase 6)**; ⏸ runtime confirm deltas |
| **R-04** | **Double coordinate transform** (hard rules 10, 11) | L | H | a future caller re-transforms a host-space DTO | invariant documented; placement `xyz` passed unchanged; only search point → link space | Low; residual = convention-only (consider host-space wrapper later) |
| **R-05** | **Wrong-level ceiling association** (X-1) → wrong Z / wrong floor | M | H | stacked rooms; 20 ft height fallback; no level filter; first-not-nearest ceiling | Phase 3 fix: level/elevation filter + sort nearest-above; Phase 6 candidate elevation check | Open (Phase 3) |
| **R-06** | **Arbitrary FLAT-ceiling pick** (C-A) → sprinklers at a soffit height | M | H | >1 FLAT ceiling; unsorted `FirstOrDefault` | Phase 3/6: sort by elevation + room XY-overlap | Open |
| **R-07** | **Coverage gaps from min-separation packing** (C-B) | H | M | `MaxSpacing` used as minimum separation; coverage never verified | Phase 6/9 redesign to verify coverage — **needs approved coverage radius (FPE)** | Open / 🧑‍🔧 FPE |
| **R-08** | **Ceiling failure masked as success** (C-G) | H | M | `MissingCeiling`/`UnsupportedCeiling` overwritten by `ReviewRequired` | Phase 6: preserve ceiling-failure status; do not launder into success | Open |
| **R-09** | **Transaction partial failure leaves unknown state** (§22) | L | H | exception mid-batch | single batch transaction + rollback on exception; document policy; consider per-room subtransactions | Mitigated (batch); ⏸ runtime confirm rollback |
| **R-10** | **Visibility mistaken for placement failure** (§24; hard rule 9) | M | M | placed sprinkler not visible → tempted to move coordinates | Diagnose visibility separately (view range/discipline/phase/crop); **never move coordinates for visibility** | Open / ⏸ runtime only |
| **R-11** | **No idempotency** (I-1, §27) → duplicates on re-run | M | M | re-run; only a 0.25 ft proximity guard; no element tagging | Phase 8: `GenerationId` tagging; update/replace only app-generated instances | Open (Phase 8) |
| **R-12** | **Performance** — per-point geometry queries over all ceilings/links | M | L | large models; `get_Geometry` per ceiling per point | later: cache ceiling geometry per generation; spatial index | Open (later) |
| **R-13** | **Unsupported geometry** (sloped/stepped/degenerate) | M | M | non-planar ceilings; <3-vertex boundary | calc flags `UnsupportedCeiling`/`InvalidRoomGeometry`; keep honest (don't mask — see R-08) | Partially mitigated |
| **R-14** | **Missing engineering inputs** (NFPA-13) presented as final | H | H | provisional 15 ft shown as compliant | `HasApprovedRules=false`, `IsProvisional=true`, every room `ReviewRequired`; never flip without FPE tables | Mitigated (honest) / 🧑‍🔧 FPE |
| **R-15** | **SketchPlane clutter** — one `SketchPlane` per point pollutes the model | M | L | WorkPlaneBased no-face path creates many sketch planes | reuse a sketch plane per distinct Z where feasible; document; verify at runtime whether Revit auto-reuses | New (Phase 5); ⏸ runtime confirm |
| **R-16** | **Fabricated runtime validation** (hard rule 18) | L | H | pressure to claim "placed 26" without Revit | policy: all runtime acceptance marked **pending** until executed in Revit; no runtime claim without evidence | Controlled |
| **R-17** | **Silent obsolete-code removal breaks build** (§39; hard rule 15) | L | M | staged deletions of legacy `Services/Placement/*` | Phase 10: grep for references before committing deletions | Controlled |

---

## Top risks to close first
1. **R-01 / R-03** — the (0,0,0) hosting bug and the "success without validation" gap. **Addressed by Phase
   5 + Phase 6 this session** (static); runtime confirmation pending.
2. **R-05 / R-06 / R-08** — ceiling association & status masking (wrong Z, hidden failures). Phase 3/6.
3. **R-07 / R-14** — coverage correctness and NFPA compliance. **FPE-gated**; must not be closed by
   inventing values.

## Explicitly accepted (by design, not defects)
- Provisional 15 ft spacing and universal `ReviewRequired` (Decision 004) — accepted until FPE tables exist.
- Runtime-only criteria remaining open — accepted; this environment has no live Revit host.
