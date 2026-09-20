# Learning Outcomes & Assessment Map

Six outcomes this teaching pack targets, each mapped to the assessment
instrument that proves it. The "assessment" is the lab (`teaching/lab.md`) —
questions and stretch challenges — plus a short answers section in the deck.

## Outcomes

### LO1 — Diagnose why a grounded retriever refuses or fails to refuse

The trainee can read an evaluation report and trace a refusal/missed-refusal
back to a concrete mechanism (RRF normalization, per-list floors, relative
margin) and tell you why the model behaved as it did.

**Assessed by:** Lab Task 1 (record numbers) and Lab Task 4 (RRF explanation).

### LO2 — Classify and reason about prompt-injection cases

The trainee distinguishes direct vs indirect injection, explains why
indirect-via-document is the high-risk case in RAG, and can say *why a
particular query must be refused* in a government context.

**Assessed by:** Lab Task 2 (ADV-001..005 rationale); stretch S1 incident.

### LO3 — Design and add an adversarial evaluation case

The trainee can extend a golden set with a correctly-specified new case
(correct `should_refuse` expectation, category, fixture wiring) and interpret
the resulting exposure/refusal diagnostics honestly.

**Assessed by:** Lab Task 3 (add ADV-008, re-run, interpret).

### LO4 — Explain the boundary of type-level vs semantic validation

The trainee understands what `ToolSchemaValidator` can and cannot catch
(shape vs meaning) and where the semantic checks live, and can demonstrate a
malformed tool call failing at the right layer.

**Assessed by:** Stretch S2 (negative `compute_fee` amount).

### LO5 — Operate the budget governor as a safety control

The trainee can set a budget, run a workflow, and verify that the hard
cut-off fires between stages rather than silently accumulating spend.

**Assessed by:** Stretch S3 (mid-flight cut-off demonstration);
deck slides 9–11.

### LO6 — Evaluate a fix against its own numbers

The trainee can compare a proposed retrieval fix to a shipped fix, re-run the
harness on the fixed code, and check that accuracy improved *without* flipping
answer decisions (false refusals still 0).

**Assessed by:** Lab Task 5 (proposed fix vs `8dcdb14`, re-run, 30/32).

## Assessment Map

| Outcome | Lab Task | Stretch | Passing signal |
|---|---|---|---|
| LO1 | T1, T4 | — | Report matches shipped baseline; RRF paragraph shows rank-1-in-one-list ≈ 0.50 and pinpoints the normalization lines |
| LO2 | T2 | S1 | Every ADV-001..005 explained with correct category + refusal reason; S1 names the QueryEnhancer cross-lingual path |
| LO3 | T3 | — | ADV-008 is well-formed, refused, and the exposure/refusal interpretation is correct |
| LO4 | — | S2 | Explains validator = shape-only; semantic check in `FeeEngine`; failing `ToolResult` captured |
| LO5 | — | S3 | Cut-off fires between stages; stage identified; no post-cutoff spend |
| LO6 | T5 | — | Comparison table of proposed vs shipped fix; re-run shows 30/32 and 0 false refusals |

## Assessment rubric (per outcome)

- **Not met:** can't reproduce the result or misattributes mechanism.
- **Met:** correct mechanism, correct numbers, using repo evidence (file/line or
  commit).
- **Exceeds:** finds the residual we already disclose (ADV-004/005) and
  proposes a layered production mitigation consistent with the Part B gap table.

## Deck support

Each learning outcome is previewed in the slide deck (`teaching/slides.md`):
LO1 ↔ slides 18–20, LO2 ↔ slides 3–5, LO3 ↔ slide 4 + lab pointer, LO4 ↔
slides 6–8, LO5 ↔ slides 9–11, LO6 ↔ slide 19.