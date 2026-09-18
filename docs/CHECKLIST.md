# ITI Assessment Checklist — Status Audit
_Last updated: 2026-09-18 (as of PR #8; this doc ships as PR #9)_

## Legend
✅ Done (evidence: PR #, file path, test name)
🟡 Partial (what's done, what's missing)
⬜ Not started
❌ Blocked / at risk

## 1. Functional Requirements (FR-1 .. FR-9)
| ID | Requirement (1-line) | Status | Evidence | Gap / Next action |
|----|----------------------|--------|----------|-------------------|
| FR-1 | Ingestion (2 formats, stages, idempotent, per-doc status) | 🟡 | `PlainTextExtractor`, `WordOverlapChunker`, `DocumentIngestionService` (PR #4/#5); SHA-256 idempotency + status+hash migration; `POST /api/documents/text`; tests `DocumentIngestionServiceTests`, `PlainTextExtractorTests`, `WordOverlapChunkerTests` | Only 1 of 2 formats (plain text). PDF/HTML extractor deferred; `DocumentSourceInput.StreamContent` already models it |
| FR-2 | Retrieval (hybrid, fusion, enhancement, citations, refusal) | ✅ | PR #7: `GroundedRetriever`, `HybridFusionEngine`, `QueryEnhancer`, `IRetrievalService`; tests `GroundedRetrieverTests`, `HybridFusionEngineTests`, `QueryEnhancerTests` | HNSW index + `vector(1536)` migration present; dense cosine + keyword + RRF + 0.40 refusal gate |
| FR-3 | Evaluation (≥25 Q/A, ≥5 adversarial, harness, baseline) | ⬜ | — (README lists it under Deferred Work) | PR #10: `golden-set.yaml`, `tools/EvalHarness`, `docs/EVALUATION.md` |
| FR-4 | Multi-Agent (≥3 + orch, ≥4 tools, ≥1 write gated, typed I/O) | 🟡 | Scaffold only: `EligibilityIdentifierAgent`, `ProcedureResolverAgent`, `ResponseDrafterAgent`, `OrchestratorService` (PR #1/#2) — LLM calls, tuple returns | No tool abstraction, no allow-lists, no gated write tool, no typed I/O records → PR #11 |
| FR-5 | Orchestration (pattern, breaker, timeout, retry, degradation, trace, approval) | 🟡 | `OrchestratorService` chains cost→retrieval→3 agents→persist; `HumanReviewService` (approve/reject + `AuditLog`) exists | No state machine, breaker, timeout, retry, degradation path, step trace, approval/edit endpoints → PR #11 |
| FR-6 | Real-time (SSE/WS, progress events, client cancel) | ⬜ | — | Not started → PR #13 |
| FR-7 | Surface (OpenAPI, minimal UI/CLI, session history) | 🟡 | `AddOpenApi`/`MapOpenApi` + 2 minimal endpoints (`Program.cs`); inquiries persisted in DB (`IInquiryRepository`) | No CLI, no session-history view, no seeded demo account → PR #13 |
| FR-8 | Access (auth, ≥2 roles, server-side enforced) | ⬜ | — (README: "No authentication or authorization on any endpoint") | Not started → PR #13 |
| FR-9 | Observability (correlation ID, cost/token per request, tracing, health) | 🟡 | `Inquiry.EstimatedCostUsd/ActualCostUsd/RoutedModel`; `UserBudget.TotalTokensUsed`; `ILogger` in `GroundedRetriever`/providers | No CorrelationId, no `/health`,`/ready`, no per-request token/cost endpoint → PR #13 |

## 2. Architecture & Engineering
- Clean Architecture (name + justify) ........ ✅ Layers: Domain→Application→Infrastructure→Api; `Domain.csproj` 0 deps, `Application.csproj` only DI Abstractions; ports in Application, adapters in Infrastructure (verified all 5 csproj files; README §Architecture)
- Provider abstraction (≥2 impls, config-switch) ✅ `ILLMProvider` (OpenAI/Ollama) + `IEmbeddingGenerator` (OpenAI/Ollama), switch via `LlmSettings:Provider` (`Infrastructure/DependencyInjection.cs`)
- DI everywhere ............................... ✅ `AddApplicationServices` + `AddInfrastructureServices`; all repos/services registered; config-bound `LlmOptions`
- Externalised config + versioned prompts ....... 🟡 Config in `appsettings.json` ✅; agent prompts hard-coded in `Agents/*.cs` — no externalized/versioned prompt assets ⬜
- Domain errors modelled ...................... ✅ `BudgetExceededException`, `EntityNotFoundException` (`Common/Exceptions/ApplicationExceptions.cs`); `BudgetExceededException` → HTTP 402 (`Program.cs`)
- Relational + vector store with migrations .... ✅ 3 EF migrations (InitialCreate, IngestionStatus+Hash, HNSW vector); `vector(1536)` + `vector_cosine_ops` HNSW (`AppDbContext`)
- ≥4 ADRs (chunking, orchestration, vector, twist) ⬜ Only ADR 0001 (pgvector). Need chunking + orchestration + T3 + ≥1 more → PR #11 (ADR-005), #12 (ADR-006), #14 (#002–004)
- Testing (unit / integration / contract) ...... 🟡 Test count **verified live** `dotnet test` 2026-09-18: **70 passed, 0 failed, 0 skipped** (1 project, `CitizenServicesCopilot.UnitTests`); offline-integration `GroundedRetrieverTests` (in-memory EF, no pgvector) ✅; no contract tests (no tool schemas yet), no dedicated integration project, no coverage/CI gate
- `docker compose up` + seed command .......... 🟡 `docker-compose.yml` boots pgvector/pg16 only; no seed command/script
- `.env.example` complete, no secrets .......... ⬜ No `.env.example`; `.env` ignored in `.gitignore`; dev-only localhost creds live in `appsettings*.json`

## 3. Security
### OWASP Web Top 10
| Control | Addressed? | Evidence (file/middleware) | Gap |
|---------|-----------|---------------------------|-----|
| Broken access control (object ownership) | ❌ | None | No auth; all endpoints anonymous (`Program.cs`) |
| Cryptographic failures | 🟡 | `UseHttpsRedirection()` | No TLS transport model, no key management, dev creds in appsettings |
| Injection (param. queries, validated upload) | 🟡 | Repos use LINQ/EF (parameterized); text-only ingest validated for required fields | No file-type/mime validation, no upload size limits |
| Rate limiting / abuse | ❌ | None | No throttling anywhere |
| Security misconfig (headers, CORS) | ❌ | None | `AllowedHosts: "*"`, no CORS/headers policy |
| Dependency scanning in CI | 🟡 | Planned for PR #10: `dotnet list package --vulnerable --include-transitive` (CI step) + `.github/dependabot.yml` (nuget + github-actions) | Not yet in CI |
| Security logging (no secrets) | 🟡 | Structured `ILogger`; no secrets in code | No security-event audit channel; audit logs not exposed |

### OWASP LLM Top 10
| Risk | Mitigation in repo | Evidence | Gap |
|------|-------------------|----------|-----|
| Prompt injection (direct + INDIRECT via docs) | 🟡 RAG refusal gate blocks ungroundable answers | `GroundedRetriever` 0.40 threshold; drafter refusal phrase | No explicit injection detection; indirect case is PR #10 adversarial (exposure will be measured, not yet defended) |
| Insecure output handling | 🟡 | `ResponseDrafterAgent` schema-validates JSON (safe parse + fallback) | No output encoding policy for downstream consumers |
| Sensitive disclosure (PII) | 🟡 | No PII in logs (grep-verified calls log excerpts only) | No PII policy; `Inquiry.Question` persists citizen text as-is → PR #14 SECURITY.md |
| Excessive agency (tool allow-lists) | ⬜ | Agent prompts are fixed LLM roles only | No tool layer exists yet → PR #11 |
| Unbounded consumption (token caps) | ✅ | `CostGovernorService` pre-flight estimate + `UserBudget.CanAfford` + 402 | Mid-run per-step check missing → PR #12 |
| Supply chain (pinned deps, lockfiles) | 🟡 | csproj versions pinned; CI `dotnet-version: '10.0.x'` | No `packages.lock.json`; PR #10 adds `dotnet list package --vulnerable --include-transitive` gate + Dependabot |

### Secrets
- [ ] `gitleaks detect --source . --log-opts="--all"` clean  → ⬜ not run yet — PR #10 adds it as a CI gate (full-history scan); also run locally pre-submission. Manual review so far: no real keys in code/docs/appsettings (dev-only localhost creds).

## 4. Engineering Process
- ≥30 commits across ≥6 distinct days ......... 39 commits, 4 distinct days (2026-09-15 … 09-18) → 6-day floor is a KNOWN UNFIXABLE GAP (see §8)
- Conventional Commits convention documented .... ✅ in use (see log); documented in repo ⬜ (no CONTRIBUTING)
- Atomic commits (no dumps) .................... ✅ one logical change per commit (feat/test/docs/chore/ci visible in log)
- .gitignore from commit one ................... ✅ present & comprehensive; "from commit one" unverified from local history
- ≥8 PRs with real descriptions + self-review ... ✅ 9 PRs after this doc (#1–#9); descriptions/self-review external to repo — verify on GitHub
- Issues linked to PRs (`Closes #N`) ........... ⬜ no `Closes #N` in commit messages; issue linking unverifiable from repo
- Board / milestones showing plan .............. ⬜ no repo evidence (GitHub project board)
- CI on every PR (build/lint/test/scan) ........ 🟡 `ci.yml` on push+PR currently does build+test only; PR #10 adds lint (`dotnet format --verify-no-changes`), dependency scan (`dotnet list package --vulnerable --include-transitive`), and secret scan (gitleaks, full history) gates
- Branch protection on main .................... ⬜ no repo evidence — must verify in GitHub settings
- Release tags ................................. ⬜ zero tags
- Repo hygiene (README, LICENSE, CONTRIBUTING, templates, CODEOWNERS) 🟡 README only; no LICENSE, CONTRIBUTING, PR/issue templates, CODEOWNERS
- Agentic coding workflow (≥5 of the 8 listed) . ⬜ none in repo (no AGENTS.md, versioned prompts, subagents, hooks, custom commands/MCP) → PR #14 AGENTIC-WORKFLOW.md

## 5. Deliverables
| # | Deliverable | Path | Status | Gap |
|---|-------------|------|--------|-----|
| 1 | Public GitHub repo | — | ✅ exists (origin/main, 8 PRs merged) | verify visibility/public settings |
| 2 | BRD | docs/BRD.md | ⬜ | PR #14 |
| 3 | System Design (Part A + Part B gap table) | docs/SYSTEM-DESIGN.md | ⬜ | PR #14, incl. KNOWN UNFIXABLE GAPS (§8) |
| 4 | Architecture (C4 L1-3, seq, DFD, ER, ADRs) | docs/ARCHITECTURE.md | ⬜ | PR #14; ADRs #002–006 outstanding |
| 5 | README (5-min demo path, env vars, etc.) | README.md | ✅ demo path + env vars present (PR #8) | add troubleshooting + seeded accounts at F |
| 6 | Security + Evaluation reports | docs/SECURITY.md, docs/EVALUATION.md | ⬜ | EVALUATION.md → PR #10; SECURITY.md → PR #14 |
| 7 | Teaching pack (slides, lab, outcomes, mistakes) | teaching/ | ⬜ | PR #15 |
| 8 | 2 videos (demo + teaching) | unlisted links | ⬜ | human task — remind candidate |
| 9 | Deployment (optional) | — | ⬜ | deferred (SDD Part B) |

## 6. Variant-specific
- D4: Eligibility Identifier / Procedure Resolver / Response Drafter, officer approves ... 🟡 three agent classes + `HumanReviewService` exist as LLM scaffolds; officer approve/reject is app-layer only (no HTTP endpoints, no edit path) → PR #11
- D4 risk guarded: "Stating an obligation or entitlement that doesn't exist — escalate under ambiguity" ... 🟡 refusal gate prevents unsupported answers; no explicit **ambiguity-escalate** path (approval only handles approve/reject) → PR #11
- T3: Per-user budgets, budget-aware routing, pre-flight estimation, hard cut-off, spend view ... 🟡 per-user budget ✅, pre-flight estimate + 402 ✅, post-call deduction ✅; routing is hardcoded complexity binary (not config-driven, not per-agent) ⬜; mid-run cut-off ⬜; spend view endpoint ⬜ → PR #12

## 7. Critical Path to Submission (24h)
> PR numbering shifted +1 from the original plan: this docs audit is PR #9, so Checkpoint A starts at PR #10.
| Checkpoint | PR | Est. hours | Depends on | Risk |
|-----------|----|-----------:|-----------|------|
| A. Evaluation Harness + CI gates | #10 | 2.5 | — | Low |
| B. Multi-Agent + Approval | #11 | 4 | A | Medium |
| C. T3 Cost Governor | #12 | 3 | B | Medium |
| D. Streaming + Surface + Auth + Obs | #13 | 3 | C | High |
| E. Docs (BRD, SDD, Arch, Sec, Agentic) | #14 | 3 | D | Low |
| F. Teaching + Docker verify | #15 | 4 | E | Low |

## 8. Gaps Accepted (to be closed post-deadline or documented in SDD Part B)
### KNOWN UNFIXABLE GAPS
- "Git-history distribution: brief requires ≥30 commits across ≥6 distinct days. Actual: 39 commits across 4 days (09-15 .. 09-18). The 6-day floor is unreachable within the 12-day wall-clock deadline given actual work compression. History is honest and will NOT be back-dated. Documented in SDD Part B gap table as a constraint, not a choice."
### Other accepted gaps
- Second ingestion format (PDF/HTML) — single text extractor ships; `StreamContent` seams already laid.
- Managed/cloud vector DB, rate limiting, secrets manager, autoscaling, observability stack — all self-hosted in-process for MVP; full target model in SDD Part A, honest deferrals in Part B.
- Real auth + SSE streaming delayed to PR #13 — no auth on any endpoint until then is a known submission-gate risk (flagged, not silent).
- Lockfiles not generated yet — mitigated by pinned csproj versions + the PR #10 vulnerability gate + immediate Dependabot wiring at Checkpoint A.
- Indirect prompt-injection exposure measured in PR #10 will be reported as a known gap; explicit defense deferred to PR #11/#14.
- Cost routing policy still code-defined (checkpoint #12 moves it to appsettings).