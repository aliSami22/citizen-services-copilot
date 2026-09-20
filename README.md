# Citizen Services Copilot

An Agentic RAG platform that answers citizen questions about government services and regulations using only grounded evidence from an ingested regulatory corpus. When the corpus lacks sufficient evidence, the system refuses to answer rather than hallucinate.

**Assigned Variant:** D4 — Government Citizen Services & Regulations  
**Mandatory Twist:** T3 — Cost Governor

## Problem Statement

Citizens navigating government services (ID renewal, subsidies, licensing, etc.) need accurate, regulation-backed answers — not generic AI guesses. This platform:

1. **Ingests** official regulatory documents, extracting and chunking them with metadata preservation.
2. **Retrieves** relevant evidence using hybrid dense vector + keyword search with Reciprocal Rank Fusion (RRF).
3. **Refuses** when the best evidence score falls below a grounding threshold (0.40), preventing hallucination.
4. **Drafts** structured responses with source citations when sufficient evidence exists.
5. **Governs cost** by classifying query complexity, routing to cheap/expensive LLM tiers, and enforcing per-user budget hard cutoffs before any LLM call.

### Key Principles

| Principle | Status |
|---|---|
| Grounded answers with source citations | ✅ Implemented |
| Automatic refusal when evidence is insufficient | ✅ Implemented |
| Cost governance (T3 twist) | ✅ Implemented |
| Human approval for consequential actions | ⚠️ Service exists (`HumanReviewService`) but no API endpoint is wired yet |

## Architecture

Clean Architecture with strict inward dependency direction:

```
API → Application → Domain
       ↑
Infrastructure
```

| Layer | Purpose | External Dependencies |
|---|---|---|
| **Domain** | Entities, enums, value objects | None |
| **Application** | Interfaces, agents, orchestration, services | `Microsoft.Extensions.DependencyInjection.Abstractions` only |
| **Infrastructure** | EF Core, PostgreSQL/pgvector, LLM adapters, embedding adapters | Npgsql, Pgvector, HttpClient |
| **API** | Minimal API endpoints, DTOs | ASP.NET Core |

Domain and Application layers have **zero** dependencies on external AI SDKs, database drivers, or infrastructure packages.

## Implemented Capabilities (through PR #7)

### Document Ingestion (FR-1)

- **Plain text extraction** via `PlainTextExtractor` with section header detection (`^#{1,6}`, `^Article`, `^Section`, `^Chapter`)
- **Word-overlap chunking** via `WordOverlapChunker` (configurable `MaxWords`/`OverlapWords`, default 200/30)
- **Metadata preservation**: page number, section title, chunk index propagated to each `DocumentChunk`
- **SHA-256 idempotency**: duplicate documents detected by content hash are returned immediately without re-chunking or re-embedding
- **Ingestion status tracking**: `Processing` → `Completed` / `Failed` with failure reason capture
- **Embedding generation on ingest**: each chunk receives a vector embedding during ingestion

### Embeddings & Vector Storage (FR-1/FR-2)

- **Provider-agnostic abstraction**: `IEmbeddingGenerator` with `Dimensions`, `GenerateEmbeddingAsync`, `GenerateEmbeddingsBatchAsync`
- **Two adapters**: `OpenAiEmbeddingGenerator` (`text-embedding-3-small`) and `OllamaEmbeddingGenerator` (`nomic-embed-text`), both using raw `HttpClient` — no external AI SDK
- **Configurable provider** via `LlmSettings:Provider` in `appsettings.json`
- **PostgreSQL + pgvector**: `vector(1536)` column on `DocumentChunk.Embedding`, HNSW cosine similarity index
- **Deterministic test stub**: `StubEmbeddingGenerator` (1536 dimensions) for offline unit tests

### Hybrid Retrieval Pipeline (FR-2)

- **Query enhancement**: `QueryEnhancer` maps colloquial citizen phrasing (Arabic & English) to formal regulatory vocabulary via regex-based domain synonym expansion
- **Dense vector search**: cosine similarity computed between query embedding and all stored chunk embeddings
- **Weighted keyword search**: multi-field term matching across document title, section, and content with positional weighting
- **Reciprocal Rank Fusion (RRF)**: `HybridFusionEngine` combines both ranked lists using RRF (k=60) with normalization to [0,1]
- **Grounded refusal**: if max fused score < 0.40, `GroundedRetriever` returns `RetrievalResult.Refuse()` with zero chunks
- **Structured citations**: each surviving chunk yields a `Citation` record (document title, source, version, section, page, excerpt, relevance score)

### Cost Governor (T3 Twist)

- **Query complexity classification**: heuristic based on word count, sentence count, and domain-specific keywords
- **Dual model routing**: simple queries → cheap model (`gpt-4o-mini` / `llama3.2:1b`), complex queries → expensive model (`gpt-4o` / `llama3.1:8b`)
- **Pre-flight cost estimation**: token count estimated before LLM call
- **Hard budget cutoff**: `BudgetExceededException` thrown (HTTP 402) if estimated cost would exceed user's remaining budget
- **Post-call cost tracking**: actual tokens deducted from user budget after response generation

### Multi-Agent Orchestration

- **EligibilityIdentifierAgent**: extracts eligibility criteria from retrieved chunks
- **ProcedureResolverAgent**: resolves required documents, procedural steps, fees, and timelines
- **ResponseDrafterAgent**: synthesizes agent outputs into a structured JSON draft with citations; includes independent refusal detection
- **OrchestratorService**: chains cost governor → retrieval → agents → cost deduction → persistence

### Human Review

- `HumanReviewService` with `ApproveInquiryAsync` / `RejectInquiryAsync` and `AuditLog` persistence
- **Note:** No API endpoint is wired for human review yet. The service is registered in DI and functional at the application layer.

## Repository Structure

```
citizen-services-copilot/
├── src/
│   ├── CitizenServicesCopilot.Api/          # Minimal API endpoints, DTOs
│   ├── CitizenServicesCopilot.Application/  # Interfaces, agents, orchestration, services
│   │   ├── Agents/                          # EligibilityIdentifier, ProcedureResolver, ResponseDrafter
│   │   ├── Common/
│   │   │   ├── Exceptions/                  # BudgetExceededException, EntityNotFoundException
│   │   │   ├── Interfaces/                  # ICostGovernor, IEmbeddingGenerator, ILLMProvider, IRetrievalService
│   │   │   │   ├── Ingestion/               # IDocumentIngestionService, IDocumentExtractor, IDocumentChunker
│   │   │   │   └── Retrieval/               # IQueryEnhancer
│   │   │   └── Models/                      # LLM, Ingestion, Retrieval records
│   │   ├── Orchestration/                   # OrchestratorService
│   │   └── Services/
│   │       ├── CostGovernorService.cs
│   │       ├── HumanReviewService.cs
│   │       ├── Ingestion/                   # DocumentIngestionService, PlainTextExtractor, WordOverlapChunker
│   │       └── Retrieval/                   # QueryEnhancer, HybridFusionEngine
│   ├── CitizenServicesCopilot.Domain/       # Entities, enums, value objects
│   └── CitizenServicesCopilot.Infrastructure/
│       ├── Embeddings/                      # OpenAiEmbeddingGenerator, OllamaEmbeddingGenerator
│       ├── Llm/                             # LlmOptions, OpenAiLlmProvider, OllamaLlmProvider
│       ├── Persistence/                     # AppDbContext, Repositories
│       ├── Retrieval/                       # GroundedRetriever
│       └── Migrations/                      # EF Core migrations (3 total)
├── tests/
│   └── CitizenServicesCopilot.UnitTests/    # xUnit tests (70 passing)
│       ├── Common/                          # StubEmbeddingGenerator
│       ├── Ingestion/                       # DocumentIngestionService, PlainTextExtractor, WordOverlapChunker tests
│       └── Retrieval/                       # GroundedRetriever, HybridFusionEngine, QueryEnhancer tests
├── docs/
│   └── adr/                                 # ADR 0001: PostgreSQL + pgvector decision
├── .github/workflows/ci.yml                 # GitHub Actions CI (build + test)
├── docker-compose.yml                       # PostgreSQL 16 with pgvector
└── CitizenServicesCopilot.slnx              # .NET solution file
```

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0+ |
| Docker & Docker Compose | Latest |
| LLM Provider | Ollama (local, default) **or** OpenAI API key |

If using Ollama locally:
```bash
ollama pull llama3.2:1b
ollama pull llama3.1:8b
ollama pull nomic-embed-text
```

## Obtaining an API Key (OpenAI) or Running Locally (Ollama)

Two ways to run the LLM and embedding providers. No API key is needed for the
local Ollama path.

### Option A — OpenAI free tier (managed)

1. Create an account at <https://platform.openai.com> and sign in.
2. Open **API keys** → **Create new secret key**, copy the `sk-...` value and
   store it somewhere safe (you will not be able to see it again).
3. Configure the app to use the key. Do **not** edit the key into
   `appsettings.json` (it is committed to git). Use an environment variable:

   ```powershell
   $env:LlmSettings__Provider = "OpenAI"
   $env:LlmSettings__OpenAI__ApiKey = "sk-..."
   ```

   Then start the API with those variables in the same shell:
   ```powershell
   dotnet run --project src/CitizenServicesCopilot.Api
   ```

4. Note: new OpenAI accounts include trial credit; usage is billed to your
   account when the credit runs out. Budgets are enforced per user by the
   cost governor (T3 twist) before any LLM call.

### Option B — local Ollama (default, no key)

The default `LlmSettings:Provider` is `Ollama`, so no key is required.

1. Install Ollama from <https://ollama.com>.
2. Start the server. On Windows/macOS the tray app keeps `ollama serve`
   running on `http://localhost:11434`. To start it manually:

   ```bash
   ollama serve
   ```

3. Pull the models the app reads from `LlmSettings:Ollama`:

   ```bash
   ollama pull nomic-embed-text
   ollama pull llama3.2:1b
   ollama pull llama3.1:8b
   ```

4. Verify the server is reachable:

   ```bash
   curl http://localhost:11434/api/tags
   ```

   This should return a JSON list containing the pulled models.

5. Run the API:
   ```bash
   dotnet run --project src/CitizenServicesCopilot.Api
   ```

## Quick Start

### 1. Start the database

```bash
docker compose up -d
```

This launches PostgreSQL 16 with pgvector on `localhost:5432`.

### 2. Apply EF Core migrations

```bash
dotnet ef database update \
  --project src/CitizenServicesCopilot.Infrastructure \
  --startup-project src/CitizenServicesCopilot.Api
```

### 3. Run the API

```bash
dotnet run --project src/CitizenServicesCopilot.Api
```

The API starts on `http://localhost:5177` (HTTP) by default.

### 4. Ingest a document

```bash
curl -X POST http://localhost:5177/api/documents/text \
  -H "Content-Type: application/json" \
  -d '{
    "title": "National ID Card Procedures",
    "source": "Civil Status Authority Decree 2024",
    "version": "1.0",
    "category": "Civil Status",
    "content": "## Article 1 - Eligibility\nAll citizens aged 16 and above are required to obtain a national ID card.\n\n## Article 2 - Required Documents\nApplicants must present: birth certificate, proof of residence, and two recent photographs.\n\n## Article 3 - Fees\nThe standard issuance fee is 50 EGP. Renewal fee is 30 EGP. Processing time is 7 business days."
  }'
```

### 5. Submit an inquiry

```bash
curl -X POST http://localhost:5177/api/inquiries \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "citizen-1",
    "question": "ما هي الأوراق المطلوبة لتجديد بطاقة الرقم القومي؟"
  }'
```

> **Note:** The inquiry endpoint requires a running LLM provider (Ollama or OpenAI). Without one, the request will fail at the LLM call stage.

## Environment Variables / Configuration

All settings are read from `appsettings.json` / `appsettings.Development.json`.
Every key below can also be supplied as an environment variable using the
ASP.NET Core convention (hierarchy separator `:` becomes `__`), which
overrides the JSON value.

| Configuration key | Environment variable | Default | Description |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=citizenservices;Username=postgres;Password=postgrespassword` | PostgreSQL (pgvector) connection string |
| `LlmSettings:Provider` | `LlmSettings__Provider` | `Ollama` | Active LLM/embedding provider. `OpenAI` or `Ollama` |
| `LlmSettings:OpenAI:ApiKey` | `LlmSettings__OpenAI__ApiKey` | *(empty)* | OpenAI API key. **Required** when Provider is `OpenAI` |
| `LlmSettings:OpenAI:BaseUrl` | `LlmSettings__OpenAI__BaseUrl` | `https://api.openai.com/v1` | OpenAI API base URL |
| `LlmSettings:OpenAI:CheapModel` | `LlmSettings__OpenAI__CheapModel` | `gpt-4o-mini` | Model for simple queries (OpenAI) |
| `LlmSettings:OpenAI:ExpensiveModel` | `LlmSettings__OpenAI__ExpensiveModel` | `gpt-4o` | Model for complex queries (OpenAI) |
| `LlmSettings:OpenAI:EmbeddingModel` | `LlmSettings__OpenAI__EmbeddingModel` | `text-embedding-3-small` | Embedding model (OpenAI) |
| `LlmSettings:Ollama:BaseUrl` | `LlmSettings__Ollama__BaseUrl` | `http://localhost:11434` | Ollama server URL |
| `LlmSettings:Ollama:CheapModel` | `LlmSettings__Ollama__CheapModel` | `llama3.2:1b` | Model for simple queries (Ollama) |
| `LlmSettings:Ollama:ExpensiveModel` | `LlmSettings__Ollama__ExpensiveModel` | `llama3.1:8b` | Model for complex queries (Ollama) |
| `LlmSettings:Ollama:EmbeddingModel` | `LlmSettings__Ollama__EmbeddingModel` | `nomic-embed-text` | Embedding model (Ollama) |
| `Logging:LogLevel:*` | `Logging__LogLevel__*` | `Information` (`Debug` in Development) | ASP.NET Core log levels |
| `AllowedHosts` | `AllowedHosts` | `*` | Host filter for the ASP.NET Core server |

Example override in PowerShell:
```powershell
$env:LlmSettings__Provider = "OpenAI"
$env:LlmSettings__OpenAI__ApiKey = "sk-..."
```

> ⚠️ Never commit real API keys. Use environment variables or user secrets for production credentials.

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/documents/text` | Ingest a raw text document (chunking + embedding + persistence) |
| `POST` | `/api/inquiries` | Submit a citizen question (cost check → retrieval → agents → draft) |

### `POST /api/documents/text`

**Request:**
```json
{
  "title": "string (required)",
  "source": "string (required)",
  "version": "string (optional, default: '1.0')",
  "category": "string (optional, default: 'General')",
  "content": "string (required)"
}
```

**Response (201 Created):**
```json
{
  "documentId": "guid",
  "status": "Completed",
  "chunkCount": 3,
  "contentHash": "sha256-hex-string",
  "isDuplicate": false,
  "failureReason": null
}
```

Duplicate submissions return `200 OK` with `isDuplicate: true`. Failed ingestion returns `422 Unprocessable Entity`.

### `POST /api/inquiries`

**Request:**
```json
{
  "userId": "string (required)",
  "question": "string (required)"
}
```

**Response (200 OK):**
```json
{
  "id": "guid",
  "userId": "citizen-1",
  "question": "...",
  "status": "Drafted | Refused",
  "routedModel": "gpt-4o-mini",
  "estimatedCostUsd": 0.00024,
  "actualCostUsd": 0.00018,
  "createdAtUtc": "...",
  "draft": {
    "eligibilitySummary": "...",
    "procedureSteps": "...",
    "requiredDocuments": "...",
    "feesAndTimeline": "...",
    "citations": [
      { "documentTitle": "...", "source": "...", "pageNumber": 1, "section": "...", "quoteSnippet": "..." }
    ],
    "isRefusal": false,
    "refusalReason": null
  }
}
```

Budget exceeded returns `402 Payment Required`.

### `GET /api/runs/{runId}`, `GET /api/users/{userId}/spend`

> Added in **Checkpoint D** (auth) and **PR #11** (workflow). Requires a bearer
> token; citizens may access only their own resources, officers any.

- `GET /api/runs/{runId}` → run summary with the persisted `steps` array.
- `GET /api/users/{userId}/spend` → aggregated token/cost accounting for a user
  (summed from persisted agent steps).

### `GET /api/runs/{runId}/trace`

> Added in **Checkpoint D** (diagnostics). Same ownership policy as the run
> view. Returns the full audit trail of one workflow run, including the
> request correlation id and per-step timing/token/cost breakdown:

```json
{
  "runId": "guid",
  "correlationId": "guid",
  "status": "Failed",
  "startedAtUtc": "...",
  "completedAtUtc": "...",
  "steps": [
    {
      "order": 0,
      "role": "EligibilityIdentifier",
      "status": "Succeeded",
      "toolName": "GroundedRetriever",
      "durationMs": 812,
      "tokensIn": 120,
      "tokensOut": 40,
      "costUsd": 0.00006,
      "outputSummary": "...",
      "errorMessage": null
    }
  ]
}
```

### `GET /health`, `GET /ready`

> Added in **Checkpoint D** (observability).

- `GET /health` → liveness, always `200 {"status": "Healthy", ...}`.
- `GET /ready` → readiness, probes the EF store with `CanConnectAsync`;
  `200 {"status": "Ready", ...}` when reachable, `503` otherwise.

## Running Tests

```bash
dotnet test
```

**Current status:** 191 tests passing (0 failed, 0 skipped).

Test coverage areas:
- Document ingestion service (idempotency, chunking, failure handling)
- Plain text extractor (section detection, metadata)
- Word overlap chunker (boundary handling, overlap, edge cases)
- Grounded retriever (dense search, keyword search, refusal, citation binding)
- Hybrid fusion engine (RRF scoring, normalization, edge cases)
- Query enhancer (domain synonym expansion, Arabic + English)

## Troubleshooting

### Local DB schema drift after pulling new migrations

If `POST /api/documents/text` returns 500 with "column does not exist", run:

```bash
dotnet ef database update \
  --project src/CitizenServicesCopilot.Infrastructure \
  --startup-project src/CitizenServicesCopilot.Api
```

### Ollama not reachable -> 422 on POST /api/documents/text

Symptom: `POST /api/documents/text` returns `422` with
`"failureReason": "Vector embedding generation failed during document
ingestion."`

Cause: the default `LlmSettings:Provider` is `Ollama`, and the embedding
generator (default model `nomic-embed-text`) cannot reach the server at
`LlmSettings:Ollama:BaseUrl` (`http://localhost:11434`).

Fix:
```bash
ollama serve
ollama pull nomic-embed-text
```

Or switch to OpenAI and supply a key (see "Obtaining an API Key" above);
embedding generation will use `LlmSettings:OpenAI:EmbeddingModel`.

### Port 5177 already in use

Symptom: `dotnet run` fails to start because `http://localhost:5177` is
already bound (e.g. a previous API instance is still running).

Fix — kill the process holding the port (Windows):
```powershell
netstat -ano | findstr :5177
taskkill /PID <pid> /F
```
Linux/macOS:
```bash
lsof -ti:5177 | xargs kill
```

Alternative — change the port in
`src/CitizenServicesCopilot.Api/Properties/launchSettings.json`
(`http` profile → `applicationUrl`), then re-run.

## Seeded Demo Accounts

> Added in **Checkpoint D** (authentication & authorization).

Authentication is JWT bearer. Obtain a token with
`POST /api/auth/login` and a JSON body `{ "userId": "...", "role": "Citizen" | "Officer" }`,
then send it as `Authorization: Bearer <token>`. No user store is seeded: any
`userId` may log in under a role (the demo has no registration).

Roles and endpoint access:

| Endpoint | Policy |
| --- | --- |
| `POST /api/auth/login` | anonymous |
| `POST /api/workflows/citizen-response`, `GET /api/workflows/stream` | anonymous |
| `GET /api/runs/{runId}`, `GET /api/runs/{runId}/trace` | any authenticated user; citizens only their own runs, officers any |
| `GET /api/users/{userId}/spend` | any authenticated user; citizens only their own spend, officers any |
| `POST /api/runs/{runId}/approve`, `/reject`, `/edit-and-approve` | `Officer` role only |

The signing key comes from `Jwt:Key` (Development: `appsettings.Development.json`
or `dotnet user-secrets set Jwt:Key "..."`; production: `JWT__KEY` env var or a
secret store). It must be at least 32 bytes.

## Citizen Workflow CLI

> Added in **Checkpoint D**. A dependency-free console client that drives the
> API end-to-end: logs in as a citizen, submits a question, then polls the run
> trace until it terminates and prints it.

```bash
dotnet run --project src/CitizenServicesCopilot.Cli -- [baseUrl] [userId] [question]
# e.g.  dotnet run --project src/CitizenServicesCopilot.Cli -- http://localhost:5177 citizen-1 "Passport procedure?"
```

`baseUrl` defaults to `http://localhost:5177`, `userId` to `cli-user`; if the
question is omitted it is prompted on stdin. Exit code `0` for `Approved`/
`Rejected`, `1` otherwise (including `Failed`/`Cancelled`).

## 5-Minute Demo Path

> Based **only** on currently working capabilities.

1. **Start infrastructure:** `docker compose up -d`
2. **Apply migrations:** `dotnet ef database update --project src/CitizenServicesCopilot.Infrastructure --startup-project src/CitizenServicesCopilot.Api`
3. **Start Ollama** and pull models: `ollama pull nomic-embed-text && ollama pull llama3.2:1b`
4. **Run API:** `dotnet run --project src/CitizenServicesCopilot.Api`
5. **Ingest a regulation** via `POST /api/documents/text` (see Quick Start example)
6. **Re-submit the same document** → observe `isDuplicate: true` (SHA-256 idempotency)
7. **Ask a grounded question** via `POST /api/inquiries` with a question matching the ingested content
8. **Ask an unrelated question** → observe the grounded refusal response (`isRefusal: true`)
9. **Run the test suite:** `dotnet test` → 191/191 passing

## Current Limitations & Deferred Work

The following items are **not yet implemented** and will be addressed in upcoming PRs:

- **Human review API endpoint** — `HumanReviewService` exists but is not exposed via HTTP
- **Orchestrator does not consume `RetrievalResult` directly** — it uses a backwards-compatible method that drops `Citation` objects from the retrieval layer; citations are rebuilt by the `ResponseDrafterAgent` independently
- **No authentication or authorization** on any endpoint
- **No streaming response** support
- **No PDF/HTML document extraction** — only plain text is supported
- **No automated evaluation harness** (e.g., precision/recall over a labeled dataset)
- **No approval gate UI** for human-in-the-loop review
- **Cost governor unit tests** are not yet in the test suite

> This README will evolve as the remaining MVP requirements are implemented across subsequent PRs.

## Documentation

- [ADR 0001: PostgreSQL + pgvector for Vector Storage](docs/adr/0001-use-postgresql-pgvector-for-vector-store.md)