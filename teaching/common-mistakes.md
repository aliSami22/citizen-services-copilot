# Common Trainee Mistakes

Five misconceptions trainees typically bring to this material, each with the
correction that the repo demonstrates. Use as a pre/post matching exercise or a
discussion opener.

## 1. "More retrieval = better answers."

**The misconception.** Tune `top_k` up, raise floors down, retrieve more chunks
and the answer quality improves on its own.

**The correction.** Retrieval is a *filter*, not a fountain. Retrieving more
spreads the fused score across noise and erodes the signal; what actually helps
is refusing when the evidence is bad. Measure it: the shipped refusal gate sits
at `min_relevance_score = 0.40` with per-list floors; the golden set keeps 32
cases with 25 standard + 7 adversarial, and the *answer decisions* (hit-rate,
groundedness) are reported separately from *refusal decisions* precisely so a
"better retrieval" change cannot hide behind better refusal numbers and vice
versa.

**Repo proof:** `docs/EVALUATION.md` — hit-rate (16/26, 61.5%) and groundedness
(57.1%) either stay flat or fall when the refusal gates move, and throwing
`dense + keyword` at an out-of-corpus query still yields an isolated rank-1
~0.50 hit (no better than refusing).

## 2. "Prompt injection is a user-input problem."

**The misconception.** Filter/sanitize the user's *message* and prompt
injection is handled. The threat is direct injection in the query field.

**The correction.** In RAG the dangerous vector is **indirect** injection:
instructions planted inside an ingested document that the retriever later
hands to the model as "evidence". The user never typed it, so input filtering
never sees it, and the system can't tell "relevant document" from "hostile
document" because *they look identical* — both are on-topic text. The user's
message is just payload; the corpus is the attack surface.

**Repo proof:** `tests/eval/golden-set.yaml` ADV-004/005 (direct) vs ADV-006
(indirect via `emergency-benefit-adjustment` fixture); `docs/EVALUATION.md`
Injection Exposure Diagnostics — retrieval alone surfaces poisoned content in
top-K and cannot remove it.

## 3. "Groundedness = a citation is present."

**The misconception.** If the answer carries a citation (a `Citation` record
with title/section/page), it's grounded.

**The correction.** A citation proves only that *a chunk was retrieved* — not
that the *claim in the answer* is supported by that chunk. Groundedness is
**claim-to-chunk**: each assertion in the answer must be traceable to text the
cited chunk actually contains. The eval harness scores exactly that: an answer
with citations but claims absent from the cited chunks scores 0% groundedness.

**Repo proof:** `docs/EVALUATION.md` — GS-001, GS-004, GS-005, GS-017, GS-019…
returned citations yet list "claims not grounded in cited chunks" (e.g.
`16 years`, `350 EGP`). A citation is a promise; the harness checks the claim.

## 4. "Multi-agent = more agents."

**The misconception.** Adding more agents/roles makes the system smarter and
more capable, and capable is good.

**The correction.** More agents = more surface: more tool calls, more budget,
more routes for instructions to flow through. The orchestrator deliberately
limits agency: three typed agents in an explicit order (EligibilityIdentifier →
ProcedureResolver → ResponseDrafter), a **fail-fast** check that every agent's
`AllowedTools` are all registered, a write tool reachable only behind an
ApprovalAudit, and hard termination (max iterations, step timeout, approval
wait timeout). Explicit I/O and termination beat raw agent count.

**Repo proof:** `WorkflowOrchestrator.cs` (fail-fast tool validation +
state-machine stages), `PersistDraftTool.cs` (approval gate inside the tool),
`OrchestratorOptions` (bounds), ADR-005.

## 5. "Budgeting = a token cap."

**The misconception.** Budget enforcement is "don't send more than N tokens in
one request" (or worse, a soft limit that "alerts").

**The correction.** A cap on a single request is meaningless when a *workflow*
of several stages can leak spend across requests. The cost governor budgets at
three control points: **pre-flight** (estimate cost before *any* LLM call —
deny/402 if it would exceed), **mid-run** (re-check between agent stages, so a
long chain is cut the moment it can't afford the next stage), and **post-call**
(deduct actual tokens used so the ledger matches reality), plus a hard
cut-off that stops runs entirely. A single request cap gives you none of those;
it just changes the shape of the bill.

**Repo proof:** `BudgetPreFlightCheck.cs` (`Allowed`/`Denied`/`WouldExceed`),
`WorkflowOrchestrator.cs` budget gate between stages, `BudgetExceededException`
→ HTTP 402 in `Program.cs`, and the budget exhaust integration tests.