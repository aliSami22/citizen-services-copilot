# Architecture — Citizen Services Copilot

Clean Architecture (API → Application → Domain; Infrastructure owned by
Application). C4 diagrams below are committed as Mermaid sources.

## C4 L1 — Context

```mermaid
flowchart TD
    Citizen["Citizen\n(asks a question)"] -->|HTTPS REST/SSE + JWT| API["Citizen Services Copilot\n(single deployable)"]
    Officer["Officer\n(reviews & approves)"] -->|HTTPS REST + JWT| API
    Admin["Admin\n(ingests regulations)"] -->|HTTPS REST| API
    API -->|pgvector SQL / cosine ANN| PG[("PostgreSQL 16 + pgvector")]
    API -->|LLM completions| LLM["LLM provider\n(OpenAI or local Ollama)"]
```

## C4 L2 — Containers

```mermaid
flowchart TD
    subgraph Browser["Browser"]
        SW["Swagger UI / manual REST"]
    end
    subgraph Process["CitizenServicesCopilot.Api"]
        KW["Kestrel"]
        EW["WorkflowEndpoints\n(citizen-response, stream, run, trace, approve)"]
        OE["/api/inquiries, /api/documents/text\n(legacy OrchestratorService + ingestion)"]
        AE["AuthEndpoints (/api/auth/login)"]
        MW["CorrelationIdMiddleware"]
        KW --> MW
        MW --> EW
        MW --> OE
        MW --> AE
    end
    EW --> APP["Application layer"]
    APP -->|"AppDbContext\n(repositories)"| PG[("PostgreSQL + pgvector")]
    APP -->|"ILLMProvider"| LLM["LLM provider"]
    APP -->|"IRetrievalService"| PG
    Browser -->|HTTPS| KW
```

## C4 L3 — Components (workflow path)

```mermaid
flowchart LR
    WE["WorkflowEndpoints\n(Minimal API static route definitions)"] --> ORCH["WorkflowOrchestrator\n(state machine)"]
    ORCH --> RET["GroundedRetriever\n(refusal gate)"]
    ORCH --> AGENTS["3 typed agents\nEligibilityIdentifier\nProcedureResolver\nResponseDrafter"]
    ORCH --> TOOL["IToolExecutor + ToolRegistry\n(write-gated persist)"]
    ORCH --> APPROVAL["ApprovalService\n(approve/reject/edit-and-approve)"]
    ORCH --> BUDGET["IBudgetPreFlightCheck\n+ IModelRouter"]
    TOOL --> REPO["EF repositories\n(IWorkflowRunRepository\nIAgentStepRepository)"]
    APPROVAL --> REPO
    RET --> EMBD["IEmbeddingGenerator"]
    AGENTS --> LM["ILLMProvider"]
    we("IWorkflowProgressSink\n→ Channel{T} (SSE)") -.-> ORCH
```

## Sequence — Full agentic workflow incl. approval gate + streaming

Mermaid source (committed; rendered by any Mermaid renderer, e.g. GitHub).

```mermaid
sequenceDiagram
    participant C as Citizen (browser)
    participant API as API (RunAsync path)<br/>WorkflowEndpoints
    participant OR as WorkflowOrchestrator
    participant RET as GroundedRetriever
    participant AG as Agents (3x)
    participant LM as LLM provider
    participant DB as Postgres/pgvector
    participant O as Officer
    participant AV as ApprovalService
    participant DB2 as Audit/Approval rows

    C->>API: POST /api/workflows/citizen-response {question} + JWT
    API->>API: resolve userId from JWT sub; persist runId
    API-->>C: 202 Accepted {runId}
    Note over API,OR: background task in dedicated DI scope (a04eaa4)
    API->>OR: RunAsync(userId, question, model, runId)
    OR->>RET: RetrieveAsync(query)
    RET->>DB: vector cosine search (HNSW)
    RET-->>OR: RetrievalResult (or Refusal)
    alt insufficient evidence
        OR-->>API: run → Failed (refused)
    else evidence present
        OR->>AG: run EligibilityIdentifier
        AG->>LM: completion(prompt, model)
        LM-->>AG: output
        OR->>AG: run ProcedureResolver
        AG->>LM: completion(...)
        LM-->>AG: output
        OR->>AG: run ResponseDrafter (JSON draft)
        OR->>AV: stage AwaitApproval
        OR-->>API: run → WaitingApproval
    end
    O->>AV: POST /api/runs/{id}/approve (+ officer JWT)
    AV->>DB2: write ApprovalAudit (approverId from JWT sub)
    AV-->>O: 200 {decision: Approved, approverId}
    O->>AV: (optional) edit-and-approve
    AV-->>OR: proceed → persist draft
    OR->>DB: PersistDraft tool (write-gated)
    OR-->>API: run → Completed
    Note over C,API: streaming variant
    C->>API: GET /api/workflows/stream?question=... (SSE)
    API->>OR: RunAsync(..., progress: Channel sink)
    OR-->>API: stage/step/done events
    API-->>C: SSE data: {stage|step|done} events
```

## Data-Flow Diagram with Trust Boundaries

```mermaid
flowchart TD
    subgraph T1["Trust zone 1 — untrusted: public client"]
        C["Citizen / browser\n(userId, question, JWT)"]
        O["Officer\n(role JWT)"]
    end
    subgraph T2["Trust zone 2 — platform (self-hosted MVP)"]
        API["API (Kestrel)\n- JWT validation\n- role/ownership policy\n- userId FROM JWT sub,\n  never from request body"]
        ORCH["WorkflowOrchestrator"]
        RET["GroundedRetriever\nrefusal gate"]
        REPO["EF + Postgres 16/pgvector"]
        AUDIT["Approval + audit ledger"]
    end
    subgraph T3["Trust zone 3 — external LLM provider"]
        LM["OpenAI / Ollama\n(sees ONLY:\n query text, agent prompt,\n model name — NOT keys,\n NOT userId, NOT audit,\n NOT correlation IDs)"]
    end

    C -->|POST citizen-response| API
    O -->|approve/reject/edit-and-approve| API
    API -->|userId from sub claim| ORCH
    ORCH --> RET
    RET --> REPO
    ORCH --> AUDIT
    ORCH -->|ILLMProvider: prompt + model only| LM
```

**What the LLM provider sees — boundary note:**
The provider receives only (a) the agent prompt built from the corpus query
and retrieved chunks, and (b) the resolved model name. It never receives the
user's JWT, the `userId` (`sub`), correlation ids, API keys, the budget ledger,
or the approval audit trail. Grep evidence: `ILLMProvider.GenerateCompletionAsync(LlmPrompt, ...)` —
the prompt carries no identity claims; identity is resolved server-side from
`http.User` (`GetUserId`/`IsOfficer` in `WorkflowEndpoints.cs`).

## ER Diagram

```mermaid
erDiagram
    Document ||--o{ DocumentChunk : "has chunks"
    Document {
        guid Id PK
        string Title
        string Source
        string ContentHash "SHA-256"
        string Status "Completed|Failed"
    }
    DocumentChunk {
        guid Id PK
        float[] Embedding "vector(1536)"
        int PageNumber
        string Section
        string Content
    }
    UserBudget {
        string UserId PK
        decimal AllocatedBudgetUsd
        decimal SpentUsd
        int TotalTokensUsed
        bool IsBlocked
    }
    WorkflowRun ||--o{ AgentStep : "records steps"
    WorkflowRun ||--o{ ApprovalAudit : "is decided by"
    WorkflowRun ||--o| PersistedDraft : "ultimately writes"
    WorkflowRun {
        guid Id PK
        string UserId FK "runs owned by user"
        string Status "NotStarted..Cancelled"
        Guid? CorrelationId
        decimal TotalCostUsd
        string? ErrorMessage
    }
    AgentStep {
        guid Id PK
        guid RunId FK
        int Order
        string Role
        string Status
        int? TokensIn
        int? TokensOut
        decimal CostUsd
        int DurationMs
        string? OutputSummary
    }
    ApprovalAudit {
        guid Id PK
        guid RunId FK
        string ApproverId
        string Decision "Approved|Rejected"
        string? Reason
        string? ModifiedDraftJson
    }
    PersistedDraft {
        guid Id PK
        guid RunId FK
        string DraftJson
    }
    Inquiry ||--o{ InquiryDraft : "drafts"
    Inquiry {
        guid Id PK
        string UserId
        string Status "Drafted|Refused"
    }
```

## Layer Dependency Diagram

```mermaid
flowchart TD
    API["API\n(minimal endpoints, DTOs, middleware)"]
    APP["Application\n(interfaces, orchestrator, agents, services)"]
    INFRA["Infrastructure\n(EF, pgvector, providers, repos)"]
    DOM["Domain\n(entities, enums, value objects)"]

    API --> APP
    API --> INFRA
    APP --> DOM
    INFRA --> DOM
    INFRA -.->|"implements Application interfaces"| APP
    %% API depends on Infrastructure only for DI composition root
```

Invariant (CI-enforced by `dotnet format` only — dependency *direction* is
reviewed manually per PR): `Application` references **no** persistence or
provider SDKs; the only NuGet dependency is
`Microsoft.Extensions.DependencyInjection.Abstractions`.

## ADR Index (ADR-001 .. ADR-006)

| ADR | Title | Status | File / Evidence |
|---|---|---|---|
| ADR-001 | Use PostgreSQL + pgvector for vector storage | Accepted | `docs/adr/0001-use-postgresql-pgvector-for-vector-store.md` (commit `9d79bb3`) |
| ADR-002 | **Deferred write-up**: JWT bearer + role policies | Decision recorded | Implicit in commit `910a962` (feat: JWT auth + role-based authorization); recommended ADR 00x extraction |
| ADR-003 | **Deferred write-up**: SSE streaming over WebSockets | Decision recorded | Implicit in commit `84aa16e` (SSE + client cancellation) |
| ADR-004 | **Deferred write-up**: cost governor (pre-flight + hard cutoff + tiered routing) | Decision recorded | Implicit in commits `58ebea2`, `926459a`, `724fb9b` |
| ADR-005 | Orchestration pattern — state machine | Accepted | `docs/adr/0005-orchestration-pattern.md` (commit `af08522`) |
| ADR-006 | **Deferred write-up**: background execution via dedicated DI scope | Decision recorded | Implicit in commit `a04eaa4` (root-anchored scope; durable-queue alternative in Part B) |

## Endpoint → Source Map

| Endpoint | File |
|---|---|
| `POST /api/inquiries`, `POST /api/documents/text`, `/health`, `/ready` | `src/CitizenServicesCopilot.Api/Program.cs` |
| `POST /api/workflows/citizen-response`, `GET /api/workflows/stream`, runs/trace/spend/approve/reject/edit-and-approve | `src/CitizenServicesCopilot.Api/Endpoints/WorkflowEndpoints.cs` |
| `POST /api/auth/login` | `src/CitizenServicesCopilot.Api/Endpoints/AuthEndpoints.cs` |
| Correlation middleware | `src/CitizenServicesCopilot.Api/Middleware/CorrelationIdMiddleware.cs` |