# Security — Citizen Services Copilot

Security posture, controls, and **in-repo evidence** for the MVP. Every control
row cites a real file, test, or commit that demonstrates it. Where a class of
risk is accepted post-MVP, it is called out (see Known Gaps) and mirrored in
`docs/SYSTEM-DESIGN.md` Part B.

## OWASP Web Top 10

| # | Risk | Control | Evidence |
|---|---|---|---|
| A01 | Broken Access Control | JWT bearer authentication on every business endpoint; role policy `Officer` for approval/spend; per-resource ownership check (citizens read own runs/spend, officers any). | `Program.cs:58-64` (policy), `WorkflowEndpoints.cs` `IsOfficer`/`GetUserId` ownership guards (`910a962`); tests: `Approve_AsCitizen_Returns403`, `Get_Run_AsDifferentCitizen_Returns403` in `tests/.../WorkflowEndpointTests.cs`. |
| A02 | Cryptographic Failures | Symmetric JWT signing key must be ≥32 bytes or startup throws; key only from user-secrets/env, never committed; DPAPI at-rest key protection for ASP.NET data-protection. | `Program.cs:35-56` key-length guard; `docs/SECURITY.md` Secrets section; gitleaks full-history scan (`7573374`; `.github/workflows/ci.yml:68`). |
| A03 | Injection (prompt/RAG) | Grounded refusal gate (evidence floor + relative-margin guard); retrieval-level per-list floors; tool registry validates agent tool schemas; write-gated persist stage. | `GroundedRetriever`, fix `8dcdb14`, root-cause `88fbca1`; measured in `docs/EVALUATION.md` (refusal accuracy 30/32, false refusals 0). Residual direct-injection cases ADV-004/005 documented as Known Gaps. |
| A04 | Insecure Design | Consequential outputs pass an officer approval gate before persistence; approval identity is taken from JWT `sub` — a forged body `approverId` is ignored. | `POST /api/runs/{id}/approve|reject|edit-and-approve` (`4490a50`, `0edec2b`); test `Approve_RecordsApproverFromJwt_NotBody`. |
| A05 | Security Misconfiguration | Swagger/OpenAPI only in `Development`; HTTPS enforced outside Development (`UseHttpsRedirection`); dev-certs trust documented. | `Program.cs:75-94`; `docs/BRD.md`; README Quick Start (`dotnet dev-certs https --trust`). |
| A06 | Vulnerable & Outdated Components | Dependabot enabled (weekly, nuget + GitHub Actions); CI fails on any vulnerable transitive package. | `.github/dependabot.yml`; `.github/workflows/ci.yml:54-58` (`dotnet list ... --vulnerable --include-transitive`); dependabot bump commits (`d037b49` etc.). |
| A07 | Identification & Authentication Failures | Short-lived tokens (1 h expiry, `AuthEndpoints.cs:33`); clock skew ≤ 1 min; lifetime validation on. | `AuthEndpoints.cs`; `Program.cs:46-55` `TokenValidationParameters`. |
| A08 | Software & Data Integrity | Build reproducibility via CI; gitleaks blocks leaked-secret regressions; deterministic offline eval harness gates retrieval correctness. | `.github/workflows/ci.yml` `quality-gates` job; `tools/EvalHarness`, `docs/EVALUATION.md`. |
| A09 | Logging & Monitoring Failures | Correlation id middleware scopes every log line; per-run trace endpoint exposes an audit ledger (order, tokens, cost, duration, correlation id). | `CorrelationIdMiddleware.cs` (`6eef480`); `GET /api/runs/{id}/trace` (`b047690`); test `Trace_ReturnsRunCorrelation_WhenHeaderPropagates`. |
| A10 | SSRF (external fetch) | The MVP only calls configured LLM/embedding endpoints over HTTPS; ingestion is server-supplied text, not URL fetch. | `ILLMProvider` adapters (`OpenAiLlmProvider`/`OllamaLlmProvider`); no ingress URL-acceptance API in the surface. |

## OWASP LLM Top 10

| # | Risk | Control | Evidence |
|---|---|---|---|
| LLM01 | Prompt Injection | Indirect injection blocked at retrieval (per-list evidence floors) and independently by the ResponseDrafterAgent refusal check; direct-injection residual documented. | `8dcdb14`, `88fbca1`, `docs/EVALUATION.md` ADV-004/005; `ResponseDrafterAgent.cs`. |
| LLM02 | Insecure Output Handling | Agent output must parse as the strict JSON draft schema; officer `edit-and-approve` rejects invalid JSON with 400. | `InvalidEditedContentException` → 400 in `WorkflowEndpoints.cs`; test `EditAndApprove_InvalidJson_Returns400_WhenOfficer`. |
| LLM03 | Training Data Poisoning | Corpus is admin-supplied plain text only; ingestion is idempotent (SHA-256) so no duplicate/skewed content; no user-supplied docs. | `DocumentIngestionService` (`4848b04`); ingestion tests (`a6f1c79`). |
| LLM04 | Model DoS (excessive cost) | Cost governor: complexity-tiered routing, pre-flight estimate, hard per-user cutoff (HTTP 402) *before* any LLM call; post-call deduction. | `58ebea2`, `926459a`; `BudgetExceededException` → 402 in `Program.cs:111-118`; budget scenario tests (`c883c05`). |
| LLM05 | Supply-Chain | Language model agnostic via `ILLMProvider`; dependencies pinned by lockfiles and Dependabot; `--vulnerable --include-transitive` gate in CI. | `.github/workflows/ci.yml:54-58`; `.github/dependabot.yml`. |
| LLM06 | Sensitive Information Disclosure | The LLM provider sees only prompt text + model name (see `docs/ARCHITECTURE.md` "What the LLM provider sees"); JWT identity, correlation ids, budgets, audit live server-side. | `WorkflowEndpoints.cs` `GetUserId` from claims only; prompts under `Application/prompts/` contain no identity fields. |
| LLM07 | Insecure Plugin/Insecure Tool Design | Tools are registry-checked: an agent may only declare tools actually registered, and the persist tool is write-gated for the approval stage. | `WorkflowOrchestrator.cs:88-99` fail-fast tool validation; `ToolRegistry`/`ToolCatalog` (`b8300b2`). |
| LLM08 | Excessive Agency | Explicit state machine constrains the agent chain to 3 fixed stages + approval gate + persist; no dynamic self-directed tooling. | ADR-005 `docs/adr/0005-orchestration-pattern.md`; `WorkflowOrchestrator.cs` (`d8dc236`). |
| LLM09 | Misinformation | Grounding threshold (min_relevance_score=0.40) + refusal; measured on the golden set (refusal accuracy 93.8%, 0 false refusals). | `docs/EVALUATION.md`; `GroundedRetriever` (`fff1b7f`). |
| LLM10 | Unbounded Consumption | Per-stage tokens/cost persisted and surfaced per run; hard budget cutoff; single in-flight run per request is bounded by SSE/disconnect cancellation. | `AgentStep` ledger, `GET /api/runs/{id}/trace`; cost governor tests. |

## Secrets

- **gitleaks full-history scan is clean.** CI runs `gitleaks detect --source .
  --redact --verbose --exit-code 2` on every push/PR (`7573374`,
  `.github/workflows/ci.yml:60-68`) — i.e. not just the patch, the full
  history. The repo contains **no** committed secrets: verify with the same
  command locally:
  ```powershell
  gitleaks detect --source . --redact --verbose --exit-code 2
  ```
- **JWT signing key** (`Jwt:Key`): never in `appsettings.json` (committed).
  Reads from `appsettings.Development.json`/user-secrets/env `JWT__KEY`
  (`docs/BRD.md`, README). Startup throws if shorter than 32 bytes
  (`Program.cs:39-44`).
- **OpenAI API key** (`LlmSettings:OpenAI:ApiKey`): environment variable only;
  default provider is Ollama so no key ships.

## Known Gaps (MVP)

Accepted for the MVP, each with a documented deferral in Part B:

1. **No production IdP/SSO** — demo JWT issuer (`/api/auth/login`) accepts any
   `userId` + role. Effective for the brief, unacceptable for production.
2. **Direct prompt-injection residual** — ADV-004/005 (content-bearing direct
   injection) still retrieve; real embedding separation is layered in
   production (see `docs/EVALUATION.md`).
3. **No rate limiting at the API layer** — a single instance with no gateway
   (Part B row: managed gateway).
4. **No secrets manager** — all secrets are env/user-secrets (Part B row).
5. **No managed vector DB / no TLS-terminating gateway** — pgvector single node,
   Kestrel direct (Part B rows).
6. **Inquiry-level human-review endpoint** — `HumanReviewService` exists but is
   not exposed over HTTP (BRD O5 partial).