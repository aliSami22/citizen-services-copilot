# Citizen Services Copilot

An agentic RAG copilot that answers citizen questions about government
services and regulations **only** from grounded, cited corpus evidence — and
refuses (instead of hallucinating) when evidence is insufficient. Variant D4,
twist T3 (cost governor). Includes a multi-agent workflow with an officer
approval gate and per-user budget enforcement.

## Prerequisites

- **Docker** + Docker Compose (PostgreSQL 16 + pgvector)
- **.NET 10 SDK**
- An LLM provider: **Ollama** (local, default, free) or an
  **OpenAI-compatible API key** (OpenAI, Google Gemini)

## Quick Start

```powershell
# 1. Clone and enter
git clone <repo-url> citizen-services-copilot
cd citizen-services-copilot

# 2. Start Postgres (pgvector enabled)
docker compose up -d

# 3. Apply EF Core migrations
dotnet ef database update --project src/CitizenServicesCopilot.Infrastructure --startup-project src/CitizenServicesCopilot.Api

# 4. Run the API (opens the browser at /swagger)
dotnet run --project src/CitizenServicesCopilot.Api
# API:        http://localhost:5177
# Swagger UI: http://localhost:5177/swagger
```

If you get a browser certificate warning on `https://localhost:7177`, trust the
dev cert once: `dotnet dev-certs https --trust`.

## Environment Variables

All settings come from `appsettings.json`; override any key with an env var
using the `:` → `__` convention (see `.env.example` for the full list).

| Key (env form) | Default | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=citizenservices;Username=postgres;Password=postgrespassword` | PostgreSQL (pgvector) connection string |
| `LlmSettings__Provider` | `Ollama` | `Ollama` or `OpenAI` |
| `LlmSettings__OpenAI__ApiKey` | *(empty)* | Required when Provider is `OpenAI` |
| `LlmSettings__OpenAI__BaseUrl` | `https://api.openai.com/v1` | Any OpenAI-compatible endpoint (e.g. Gemini) |
| `LlmSettings__OpenAI__EmbeddingModel` | `text-embedding-3-small` | Must output **768** dimensions (schema is `vector(768)`) |
| `LlmSettings__Ollama__BaseUrl` | `http://localhost:11434` | Ollama server |
| `Jwt__Key` | *(dev value)* | JWT signing key; **must be ≥ 32 bytes**. Never commit a real key. |
| `Orchestrator__ApprovalWaitTimeout` | `00:05:00` | How long the workflow waits for an officer decision |

## Getting a Free API Key OR Running with Ollama

- **Ollama (free, default):**
  ```powershell
  ollama pull nomic-embed-text && ollama pull llama3.2:1b
  dotnet run --project src/CitizenServicesCopilot.Api
  ```
- **OpenAI free tier:** set the key via env var (never in committed config):
  ```powershell
  $env:LlmSettings__Provider = "OpenAI"
  $env:LlmSettings__OpenAI__ApiKey = "sk-..."
  ```
  Budgets are enforced per user by the cost governor before any LLM call.
- **Google Gemini (free tier, OpenAI-compatible):** Gemini exposes an
  OpenAI-compatible endpoint, so the same `OpenAI` provider works with no code
  changes. Embeddings must match the schema's `vector(768)` (e.g.
  `text-embedding-004`):
  ```powershell
  $env:LlmSettings__Provider = "OpenAI"
  $env:LlmSettings__OpenAI__ApiKey = "<your-gemini-api-key>"
  $env:LlmSettings__OpenAI__BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai"
  $env:LlmSettings__OpenAI__CheapModel = "<gemini-flash-model>"
  $env:LlmSettings__OpenAI__ExpensiveModel = "<gemini-pro-model>"
  $env:LlmSettings__OpenAI__EmbeddingModel = "text-embedding-004"
  ```

## Running Tests

```powershell
dotnet test
```

## Running the Evaluation Harness

Offline, deterministic retrieval/refusal evaluation over the golden set
(32 cases, 7 adversarial); no LLM provider needed. Writes `docs/EVALUATION.md`.

```powershell
dotnet run --project tools/EvalHarness -- --set tests/eval/golden-set.yaml --output docs/EVALUATION.md
```

## 5-Minute Demo Path

Full workflow: **login → ingest → ask → refuse → run → approve → trace → spend**.

1. **Start stack:** `docker compose up -d`, apply migrations (Quick Start), `dotnet run`.
2. **Login** — get citizen and officer JWTs:
   `POST /api/auth/login` with `{ "userId": "citizen-1", "role": "Citizen" }` and then `{ "userId": "officer-1", "role": "Officer" }`. Send as `Authorization: Bearer <token>`.
3. **Ingest** a regulation via `POST /api/documents/text` (see Swagger body schema). Re-submitting shows `isDuplicate: true` (SHA-256 idempotency).
4. **Ask** a grounded question via `POST /api/inquiries` matching the ingested content → draft with `citations`.
5. **Refuse** — ask an off-corpus query → `isRefusal: true`, no citations.
6. **Run the workflow:** `POST /api/workflows/citizen-response` → `202` + `runId`; watch progress on `GET /api/workflows/stream` (SSE). The run waits for officer `POST /api/runs/{runId}/approve`.
7. **Trace:** `GET /api/runs/{runId}/trace` → the run's `correlationId` + per-step timing/tokens/cost + approval record.
8. **Spend:** `GET /api/users/{userId}/spend` → monthly aggregated tokens/cost vs. budget.

## Demo Video

[Watch the 5-8 minute product demo](https://www.youtube.com/watch?v=sTqs79Nq9jw)

## Teaching Video

[Watch the 10-minute teaching sample](TO_BE_FILLED)

## Teaching Pack

OWASP-LLM teaching materials built from this repo live in `teaching/`
(slides, hands-on lab, learning outcomes, common mistakes).

## Troubleshooting

- **`422 Vector embedding generation failed` on ingest** → Ollama not reachable.
  Run `ollama serve`, pull `nomic-embed-text`, or switch Provider to `OpenAI`
  (or Gemini's OpenAI-compatible endpoint, see above).
- **`column ... does not exist` (500)** → schema drift; re-run
  `dotnet ef database update --project src/CitizenServicesCopilot.Infrastructure --startup-project src/CitizenServicesCopilot.Api`.
- **Port 5177 in use** → `netstat -ano | findstr :5177`, `taskkill /PID <pid> /F`.
- **HTTPS cert warning** → `dotnet dev-certs https --trust` once, restart browser.

## Further Reading

Architecture, BRD, system design, security (OWASP Web + LLM Top 10 with
evidence), and evaluation baseline live in `docs/` (`docs/ARCHITECTURE.md`,
`docs/BRD.md`, `docs/SYSTEM-DESIGN.md`, `docs/SECURITY.md`, `docs/EVALUATION.md`,
`docs/CHECKLIST.md`).