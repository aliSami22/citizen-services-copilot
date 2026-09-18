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

All configuration is in `appsettings.json` / `appsettings.Development.json`:

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5432;Database=citizenservices;Username=postgres;Password=postgrespassword` | PostgreSQL connection string |
| `LlmSettings:Provider` | `Ollama` | Active LLM/embedding provider. Set to `OpenAI` or `Ollama` |
| `LlmSettings:OpenAI:ApiKey` | *(empty)* | OpenAI API key. **Required** if Provider is `OpenAI` |
| `LlmSettings:OpenAI:BaseUrl` | `https://api.openai.com/v1` | OpenAI API base URL |
| `LlmSettings:OpenAI:CheapModel` | `gpt-4o-mini` | Model for simple queries (OpenAI) |
| `LlmSettings:OpenAI:ExpensiveModel` | `gpt-4o` | Model for complex queries (OpenAI) |
| `LlmSettings:OpenAI:EmbeddingModel` | `text-embedding-3-small` | Embedding model (OpenAI) |
| `LlmSettings:Ollama:BaseUrl` | `http://localhost:11434` | Ollama server URL |
| `LlmSettings:Ollama:CheapModel` | `llama3.2:1b` | Model for simple queries (Ollama) |
| `LlmSettings:Ollama:ExpensiveModel` | `llama3.1:8b` | Model for complex queries (Ollama) |
| `LlmSettings:Ollama:EmbeddingModel` | `nomic-embed-text` | Embedding model (Ollama) |

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

## Running Tests

```bash
dotnet test
```

**Current status:** 70 tests passing (0 failed, 0 skipped).

Test coverage areas:
- Document ingestion service (idempotency, chunking, failure handling)
- Plain text extractor (section detection, metadata)
- Word overlap chunker (boundary handling, overlap, edge cases)
- Grounded retriever (dense search, keyword search, refusal, citation binding)
- Hybrid fusion engine (RRF scoring, normalization, edge cases)
- Query enhancer (domain synonym expansion, Arabic + English)

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
9. **Run the test suite:** `dotnet test` → 70/70 passing

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