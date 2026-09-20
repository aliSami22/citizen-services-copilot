# System Design — Citizen Services Copilot

**Part A** is the unconstrained target architecture. **Part B** is an honest
gap table: every Part A component that is *not* shipped in this MVP is listed
with a deferral reason, the interim mitigation, and the effort+cost to close.
Part A is a target, not a claim about what is running today. Running topology:
see `docs/ARCHITECTURE.md`.

## Part A — Unconstrained Target Architecture

Target topology for production (managed, multi-env, scaled):

- **API gateway** — TLS termination, WAF, rate limiting, per-tenant quotas,
  global API-key throttling in front of every public route.
- **Identity (IdP)** — OIDC/OAuth2 (e.g. Entra ID / Auth0 / Keycloak) replacing
  the demo JWT issuer; short-lived tokens, refresh, role management, MFA for
  officers; the API validates `sub`/`role` purely from token claims.
- **Secrets manager** — a managed vault (Azure Key Vault / AWS Secrets Manager /
  Vault); JWT signing key, OpenAI key, DB connection string rotated without
  redeploy; no secrets in appsettings or env dump.
- **Message broker + durable queue** — run submission enqueued (Service Bus /
  SQS+DLQ / RabbitMQ); a consumer host drives workflows; runs survive restarts,
  retried with DLQ + poison-message handling. This replaces fire-and-forget
  `Task.Run` (TODO-D).
- **Distributed cache** — Redis for LLM response caching, rate-limit counters,
  session acceleration, and cheap tier retries.
- **Managed vector DB** — the corpus dimension grows beyond single-node
  pgvector capacity; managed pgvector (e.g. Neon/Azure Cosmos DB for
  PostgreSQL) or a dedicated vector store with HNSW/IVF, snapshot-backed.
- **Observability stack** — OpenTelemetry traces/metrics/logs exported to a
  central collector (Prometheus+Grafana / Azure Monitor / Datadog); distributed
  tracing across gateway, queue consumer, and worker; SLO dashboards for
  p95 latency, refusal rate, cost/run.
- **CI/CD + multi-env** — preview deploys per PR, promotion main → staging →
  prod with immutable artifacts; infra-as-code (Bicep/Terraform); smoke tests
  + eval-harness gate promoted with builds.
- **Disaster recovery** — Postgres PITR (point-in-time recovery) + cross-region
  replica; nightly restore drill; backups of the vector corpus and approval
  audit log; RPO ≤ 15 min, RTO ≤ 1 h.
- **Cost model at scale** — per-user/per-org budget ledger in the data store,
  complex-query upgrade checks, weekly cost reports; projected cost/run at
  p95 token volume with alert thresholds per tier.

## Part B — Gap Table (MVP deferrals)

| Target Component | Implemented? | Why Deferred | Interim Mitigation | Effort + Cost to Close |
|---|---|---|---|---|
| **Managed API gateway** (TLS/WAF/rate-limit) | No | Single-tenant MVP; direct exposure acceptable locally; 12-day deadline. | Kestrel HTTPS dev profile; `UseHttpsRedirection` in non-Dev; role policies per endpoint. | M — one gateway (e.g. Azure Front Door/APIM or nginx ingress) + terraform; small monthly cost. |
| **Secrets manager** | No | No cloud infra in MVP; JWT key is a single 32-byte secret. | `Jwt:Key` via user-secrets/env only; ≥32-byte startup guard (`Program.cs`); never committed (gitleaks). | S — vault + app-config wiring; near-zero infra cost. |
| **Durable queue + worker** for workflow runs | No (*TODO-D*) | Broader change; **working pattern exists**: dedicated root-anchored DI scope (`a04eaa4`). Channel+`BackgroundService` documented in `WorkflowEndpoints.cs` TODO. | Fire-and-forget `Task.Run` inside a dedicated scope; runs persisted in Postgres so a crash leaves a recoverable row. | M/L — broker + hosted consumer; loses runs on restart today; per-core broker cost. |
| **Distributed cache** (Redis) | No | MVP workloads small; per-request cost and isolation more important than latency. | EF `InMemory` in tests; no cross-instance cache needed single-instance. | M — Redis cache + cache-aside; small managed cost. |
| **Managed vector DB** | No | pgvector single-node is within corpus capacity (tens–hundreds of k chunks, ADR-0001); no scale pressure yet. | PostgreSQL + pgvector HNSW (`vector_cosine_ops`); offline eval harness. | L — migration to managed pgvector/vector store; DB cost scales with corpus. |
| **OpenTelemetry** | No | Observations exist as structured logs + correlation ids, not exported telemetry. | `ICorrelationContext` + `GET /api/runs/{id}/trace` (run, steps, tokens, cost, correlation) — `6eef480`, `b047690`. | M — OTLP exporter + collector + dashboards; small ingest cost. |
| **Multi-env CI/CD with promotion** | Partial | Single tested `main`; GitHub Actions build+test+quality gates only. No deploy step. | CI `build-and-test` + `quality-gates` (`dotnet format`, vuln scan, gitleaks, eval harness) per PR. | M — add staging/prod deploy + infra-as-code; hosting cost. |
| **DR / backup** | No | MVP accepts data-loss window on a dev database. | Postgres `docker volume`; migration history committed; dev-certs + migration docs. | M — PITR + replica + restore drill; DB storage cost. |
| **Unified cost accounting** | Partial | Per-user budget + spend exist for workflow path only (`58ebea2`, `724fb9b`); ingestion-side embedding costs and cross-endpoint aggregation not unified. | `UserBudget` rows, hard cutoff (HTTP 402), spend endpoint; run-trace cost per step. | S/M — single cost ledger across all LLM + embedding calls per user. |
| **Production auth** (IdP/SSO/MFA) | No | Demo auth issues a JWT for any userId+role (`/api/auth/login`); fine for the brief, wrong for prod. | JWT bearer + role policies + per-resource ownership checks (citizens their own, officers all) — `910a962`; approver identity from `sub` claim (`0edec2b`). | L — integrate OIDC IdP, MFA for officers, token refresh. |

## Decisions & Alternatives

| Decision | Chosen | Alternatives Rejected | Rationale |
|---|---|---|---|
| Persistence + vector store | PostgreSQL + pgvector single store (ADR-0001) | Standalone Pinecone/Qdrant/Milvus | ACID co-location, no dual-write, HNSW adequate at MVP scale, one engine. |
| Orchestration | Explicit state machine (ADR-005) | Supervisor/planner-executor/pipeline | Deterministic, replayable, audit-first; approval gate is a first-class stage. |
| Background execution | Dedicated DI scope in `Task.Run` (MVP) | Durable queue + `BackgroundService` (Part A target) | Queue is the right design but a larger change; scope fix removes the ObjectDisposedException without new infra. |
| Streaming | SSE via in-memory `Channel<T>` (`84aa16e`) | WebSockets, gRPC duplex | SSE is HTTP-native, browser-friendly, trivially cancellable with the request token. |
| Auth | JWT bearer, demo issuer, role policies (`910a962`) | IdP/SSO | IdP out of MVP scope; claims-based ownership enforced regardless of issuer. |
| Cost governance | Pre-flight estimate + hard cutoff + tiered routing (`58ebea2`, `926459a`) | Post-hoc billing only | Prevent cost before the LLM call; per-user budget is authoritative. |
| LLM provider | Provider-agnostic `ILLMProvider` (OpenAI/Ollama) | Lock in one vendor | Offline stubs keep tests deterministic; switch by `/appsettings` env. |

## Related

- Architecture diagrams (C4/sequence/DFD/ER/ADR index): `docs/ARCHITECTURE.md`.
- Business requirements + traceability: `docs/BRD.md`.
- Security controls: `docs/SECURITY.md`.