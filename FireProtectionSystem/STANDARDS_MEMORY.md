# STANDARDS_MEMORY.md — FireProtectionSystem

> **Compact, evidence-grounded cache of the two provided standards PDFs.** Read after [[PROJECT_MEMORY]].
> Purpose: so future sessions do **not** reopen the ~419 + ~682 page PDFs to re-establish what is (and is
> not) available. Snapshot date: **2026-08-25**.
>
> **HARD RULES (verbatim intent from the master prompt — always in effect):**
> 1. **NEVER invent a fire-protection rule.**
> 2. **NEVER extend a rule beyond what the PDFs actually state.**
> 3. **NEVER silently substitute general NFPA knowledge for the provided standards.**
> 4. Any rule not verified against the actual PDF text is marked **`UNVERIFIED — DO NOT IMPLEMENT AS A
>    DESIGN RULE`** and must not be encoded as a numeric constant, table, or default.
> 5. Only reopen a PDF when a **specific** rule is genuinely needed / ambiguous / conflicting / being
>    verified — then record the result **here** so it is never re-derived. Do **not** page-scan for context.

---

## 1. The two source documents

| # | File (repo root) | Identity (PROVEN from cache header / metadata) | Pages | Text? | Cache |
|---|---|---|---|---|---|
| **PDF 1** | `72-19-PDF 1.pdf` | **NFPA 72, 2019 edition — *National Fire Alarm and Signaling Code*.** Copyright 2019 NFPA; "Licensed by agreement to Carl Weaver FOR INDIVIDUAL USE ONLY", downloaded 11/05/2019. | ~419 | **Yes (extractable)** | `.analysis/pdf/pdf1_layout.txt` — **37,198 lines** |
| **PDF 2** | `Layout, Detail, And Calculations of Fire Sprinkler Systems (1).pdf` | Sprinkler-system **layout/design textbook** (NFPA-13-oriented content). | ~682 | **NO — image-only scan; no OCR available** | *(none — inaccessible)* |

**Evidence for PDF 1 identity** (`.analysis/pdf/pdf1_layout.txt`, lines 1–14): "NFPA 72 … National Fire Alarm
and Signaling Code … Copyright 2019 … Customer ID 35355929". Head of the extraction is front-matter
disclaimers ("NOTICE AND DISCLAIMER OF LIABILITY", revision-symbol legend) — **not** engineering tables.

## 2. What each standard governs (scope, so you use the right one)

- **NFPA 72 (PDF 1) = ALARM & SIGNALING**, *not* sprinkler layout. Governs fire-alarm/detection: initiating
  devices (smoke/heat detectors), notification appliances (horn/strobe spacing & candela), control units,
  circuits, survivability, inspection/testing. **Relevant to the not-yet-built Smoke-Detector /
  Notification-Appliance workflows.** Contains **NO sprinkler spacing/coverage tables.**
- **Sprinkler textbook (PDF 2) = SPRINKLER LAYOUT & HYDRAULICS** (NFPA-13-style): hazard classifications
  (Light / Ordinary Hazard 1&2 / Extra Hazard), **max coverage area per sprinkler, max spacing S,
  max distance to walls (S/2)**, obstruction/beam rules, hydraulic calcs. **This is the source the sprinkler
  placement engine actually needs — and it is the one we cannot read.**

## 3. Verified rules cache

> Format: **Rule ID · Standard · Section · Applies to · Value/Statement · Confidence**.
> A rule appears here ONLY after being read in the actual PDF text. Adding a row = you verified it.

### 3a. Sprinkler layout / spacing (from PDF 2)

| Rule ID | Standard | Section | Applies to | Value / statement | Confidence |
|---|---|---|---|---|---|
| SPR-ALL | Sprinkler textbook (PDF 2) / NFPA 13 | — | all hazard classes, coverage, spacing, wall distance, obstructions, hydraulics | **NONE EXTRACTED.** PDF 2 is an image-only scan with no OCR; **zero** numeric values have been read. | **UNVERIFIED — DO NOT IMPLEMENT AS A DESIGN RULE** |

**Consequence:** the calc engine's spacing values are **provisional placeholders**, not standards:
`MaxSpacingFt = 15`, `CoverageRadiusFt = 7.5`, wall/obstacle clearances = `1 ft`, `HasApprovedRules = false`,
every room emitted `ReviewRequired` (Decision 004). These are **engineering-safe stand-ins to exercise the
pipeline**, explicitly **not NFPA-13-compliant**. Replacing them requires **FPE-approved tables**, not model
knowledge (hard rules 1–4). Do **not** flip `HasApprovedRules` to `true` until real, sourced tables exist.

### 3b. Alarm / detection / notification (from NFPA 72, PDF 1)

| Rule ID | Standard | Section | Applies to | Value / statement | Confidence |
|---|---|---|---|---|---|
| NFPA72-ID | NFPA 72 (2019) | title/legal pages | document identity | Confirmed = *National Fire Alarm and Signaling Code*, 2019 ed. | **PROVEN** (cache header) |
| NFPA72-* | NFPA 72 (2019) | detector/notification chapters | smoke-detector & notification-appliance workflows | **NONE DIGESTED into rules yet.** Text is cached but chapters/tables have not been read into specific verified values. | **UNVERIFIED — DO NOT IMPLEMENT** (until read & cited) |

**Note:** No detector/notification workflow currently consumes NFPA 72 values (those ViewModels are empty
shells — [[PROJECT_MEMORY]] §15 P2). So there is no code path silently depending on unverified 72 values today.

## 4. How to verify a rule when one is genuinely needed

1. **Alarm/notification value?** → it's in PDF 1; grep `.analysis/pdf/pdf1_layout.txt` for the term
   (e.g. `strobe`, `candela`, `spacing`, `smoke detector`, the chapter/table number). The text is present
   (37k lines); the head is disclaimers, so search by section/table, not by page-from-top.
2. **Sprinkler spacing/coverage value?** → it's in PDF 2, which is **image-only**. Options, in order:
   (a) ask the user/FPE for the specific approved value or an OCR'd excerpt; (b) if OCR is later provisioned,
   OCR only the **specific pages/tables** needed — never the whole 682-page book.
3. Record the verified result as a **new row in §3** with exact section + value + `PROVEN`. Never carry a
   value only in code or chat — it must live here with its citation.
4. If a needed value cannot be verified, keep the placeholder, keep `HasApprovedRules=false`, and surface it
   as `ReviewRequired` — never guess to "complete" a feature (hard rule 20 in [[PROJECT_MEMORY]]).

## 5. Status summary (honest)

- **NFPA 72 (PDF 1):** identity **PROVEN**; full text **cached & extractable**; specific rules **not yet
  digested** → all specific alarm values currently **UNVERIFIED**. Low risk today (no consumer).
- **Sprinkler textbook (PDF 2):** **BLOCKED** — image-only, no OCR. **No sprinkler design value is
  verified or encoded.** All spacing is **provisional**. This is the primary standards gap and is
  **FPE-gated**, not solvable by AI knowledge.
- **Global invariant:** the shipping product must not present provisional spacing as code-compliant.
  `HasApprovedRules=false` + per-room `ReviewRequired` is the honesty guard — **keep it** until sourced
  NFPA-13 tables are supplied.

## Related
[[PROJECT_MEMORY]] · [[STANDARDS_COMPLIANCE_MATRIX]] · [[DECISIONS]] (004, 008)
