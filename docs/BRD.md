# Business Requirements Document — Citizen Services Copilot

**Variant:** D4 — Government Citizen Services & Regulations
**Twist:** T3 — Cost Governor
**Status:** Complete (MVP scope)
**Thread:** Checkpoint E (Documentation)

## Context

Citizens routinely need accurate, regulation-backed answers about government
services (ID renewal, subsidies, licensing, fees) and are poorly served by
generic AI assistants that either hallucinate or refuse. The organization —
a government digital-services unit — wants a copilot that:

1. Ingest official regulatory text (plain-text docs) and index it with vector
   embeddings for semantic search.
2. Answer citizen questions **only** from grounded evidence in that corpus,
   refusing when the evidence is insufficient.
3. Govern cost per query and per user before any LLM call runs.
4. Route consequential multi-agent outputs (procedure drafts, fee tables)
   through an officer approval gate before they are persisted as answers.
5. Keep every run auditable: correlation id, per-step tokens/cost/timing.

The product is a single-tenant demo/MVP delivered under a strict 12-day
wall-clock deadline, so scope is deliberately honest about deferred
production hardening (see `docs/SYSTEM-DESIGN.md` Part B gap table).

## Personas

| Persona | Description | Goals | Pain today |
|---|---|---|---|
| **Citizen** | Any member of the public asking about a government procedure (Arabic or English). | Fast, correct, sourced answer; know when the answer is "no evidence", not invented. | Long call centers, outdated websites, inconsistent answers between branches. |
| **Officer** | Government employee who approves consequential outputs (eligibility/draft/fees) and monitors spend. | Review drafts before they become official answers; block hallucinated or forged content; see cost by citizen. | No systematic review trail; no per-citizen cost visibility; no audit log. |
| **Admin** | IT/operations engineer who ingests regulations and keeps the system secure and reliable. | Reliable ingestion (idempotent), schema/migration handling, secrets hygiene, CI gates, HTTPS dev experience. | Manual document re-processing; cert/port friction on local dev; secret sprawl risk. |

## Objectives

Measurable, each with a status and its current measured value where one exists.

| ID | Objective | Target | Status |
|---|---|---|---|
| O1 | **Grounded answering with refusal**. Answers cite corpus evidence; refuse when evidence is below threshold. | Refusal accuracy ≥ 90%, zero false refusals on the golden set. | ✅ **Implemented.** Refusal accuracy 30/32 = 93.8%, false refusals 0 (docs/EVALUATION.md). |
| O2 | **Idempotent, reliable ingestion**. Re-submitting a document is a byte-identical no-op; no duplicate chunks/embeddings. | Re-submit returns `isDuplicate: true`, 200, zero re-embedding. | ✅ **Implemented.** SHA-256 content-hash idempotency (`POST /api/documents/text`). |
| O3 | **Auditability**. Every run has a correlation id and persisted per-step tokens/cost/timing retrievable post-hoc. | Every run traces to correlation id + step ledger in the DB. | ✅ **Implemented.** `GET /api/runs/{id}/trace`, `AgentStep` persistence, correlation middleware. |
| O4 | **Cost governance (T3)**. Complexity-tiered model routing, pre-flight budget checks, hard per-user cutoff. | Budget exceed returns HTTP 402 before any LLM call; per-user aggregated spend view. | ✅ **Implemented.** `GET /api/users/{id}/spend`, budget pre-flight + hard cutoff. |
| O5 | **Human approval gate**. Officer reviews/approves/rejects/edit-approves drafts before final. | No consequential draft persisted without an officer decision. | ✅ **Implemented** for the workflow path (`/approve`, `/reject`, `/edit-and-approve`). ⚠️ Inquiry-level `HumanReviewService` has **no endpoint** (deferred). |

## Out of Scope (this contract)

- Citizen registration, real identity, or SSO (the demo uses any `userId` under a role).
- Payments, fee collection, or transactional government integration.
- Mobile apps; the deliverable is a web API + CLI + Swagger UI.
- Production deployment, managed cloud infra, autoscaling, DR (MVP is
  self-hosted/in-process; see Part B gap table).
- PDF/HTML document extraction (plain text only).
- Multi-tenancy: single tenant, one corpus, admin-controlled ingestion.

## Business Rules

| ID | Rule | Owner |
|---|---|---|
| BR-01 | An answer decision must be grounded: if the top fused retrieval evidence is below the grounding floor, the system refuses instead of answering. | Citizen | 
| BR-02 | Document ingestion must be idempotent by full-content SHA-256 hash; identical re-submission must not duplicate chunks or embeddings. | Admin |
| BR-03 | Every workflow run must be re-playable from a persisted ledger: correlation id, ordered agent steps, tokens, cost, duration. | Officer/Admin |
| BR-04 | The multi-agent workflow is a finite state machine with a max-iteration breaker, per-step timeout, retry-with-backoff, graceful degradation, and a mandatory approval gate before persistence of consequential output. | Officer |
| BR-05 | Cost is governed before and during a run: query complexity routes between cheap/expensive models, a pre-flight estimate + hard cutoff protects each user's budget, and every step's actual cost is deducted and surfaced. | Admin |

## Assumptions

- Single-tenant deployment; one official corpus administered by the org.
- Corpus arrives as plain text (ingested via API); no bulk import UI needed.
- LLM provider is Ollama (local, default) or OpenAI; either works offline in
  tests via stubs — the evaluation harness never calls a real provider.
- PostgreSQL 16 + pgvector runs locally via Docker; CI uses an in-memory EF
  test database (offline).
- Demo authentication (JWT issued for any userId) is acceptable for the MVP;
  production identity is deferred (Part B).
- Wall-clock 12-day deadline: production-grade durability (durable queue) and
  managed infra are deferred and documented, not silently shipped.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| LLM hallucination despite grounding | Med | High | Grounding threshold + refusal logic; independently enforced by ResponseDrafterAgent; guarded by `docs/EVALUATION.md` gate. |
| Prompt injection in ingested/adversarial content | High | Med | Retrieval-level per-list evidence floors (fix `8dcdb14`); residual ADV-004/005 documented; response-layer enforcement. Resolved for indirect injection; direct-injection residual. |
| Credential leakage (JWT key, OpenAI key) | Low | High | Secrets via user-secrets/env; JWT key ≥ 32-byte guard; gitleaks full-history scan on every PR (`7573374`). |
| Background run lost on app restart | Med | Med | TODO-D durable queue (Part B gap); interim root-anchored DI scope (`a04eaa4`). |
| Unbounded LLM spend | Med | High | Cost governor pre-flight/hard cutoff/cutoff tests (`58ebea2` + budget scenarios). |
| Key rotation / operational cert friction | Low | Med | Env-driven config; `dotnet dev-certs https --trust` documented; HTTPS dev profile. |

## Traceability Matrix

`BR-xx → implemented/partial/deferred` with concrete evidence (file/commit).

| Rule | Status | Evidence |
|---|---|---|
| BR-01 | **Implemented** | `GroundedRetriever` refusal + per-list evidence floor; fix `8dcdb14`, root-cause `88fbca1`; measured in `docs/EVALUATION.md`; unit tests in `tests/.../Retrieval/`. |
| BR-02 | **Implemented** | `DocumentIngestionService` SHA-256 idempotency (`4848b04`, `93ad085`); ingestion tests (`a6f1c79`). |
| BR-03 | **Implemented** | Correlation middleware (`6eef480`), run trace endpoint (`b047690`), `AgentStep` ledger; `GET /api/runs/{id}/trace`. |
| BR-04 | **Partial** | State-machine orchestrator (`d8dc236`, ADR-005), approval gate (`8cb5937`, `4490a50`), breaker/timeout controls. Deferred: per-step timeout tuning and durable execution (Part B gap). |
| BR-05 | **Implemented** | Budget pre-flight + hard cutoff (`58ebea2`), model routing (`926459a`), spend endpoint (`724fb9b`), budget scenario tests (`c883c05`). |

## Related

- System design and deferrals: `docs/SYSTEM-DESIGN.md`.
- Security controls and evidence: `docs/SECURITY.md`.
- Agentic-workflow process post-mortem: `docs/AGENTIC-WORKFLOW.md`.