# ADR 0001: Use PostgreSQL and pgvector for Vector Storage

## Status
Accepted

## Context
The Citizen Services Copilot requires persistent storage for citizen-facing regulatory documents, segmented chunks, and dense vector embeddings to power retrieval-augmented generation (RAG).

We evaluated whether to:
1. Adopt a dedicated standalone vector database (e.g., Pinecone, Qdrant, Milvus, Weaviate), or
2. Co-locate relational data, metadata, audit trails, and vector embeddings within **PostgreSQL** using the **pgvector** extension.

Key assessment requirements include:
* Strict Clean Architecture isolation.
* Transactional integrity across documents, chunks, and audit logs.
* Operational simplicity for deployment and automated evaluation.
* Robust support for cosine similarity vector indexing.

## Decision
We decided to adopt **PostgreSQL with the pgvector extension** as the unified persistence and vector storage engine for the following reasons:

1. **Transactional Co-Location & ACID Guarantees**:
   * Storing documents, chunks, content hashes, and embeddings in the same relational database ensures atomic transactions (`SaveChangesAsync`).
   * Eliminates dual-write anomalies, synchronization lag, and distributed transaction complexity between an external vector DB and a relational database.

2. **Index Performance (HNSW Cosine Operator)**:
   * `pgvector` provides Hierarchical Navigable Small World (HNSW) indexing (`vector_cosine_ops`), enabling sub-linear approximate nearest neighbor (ANN) retrieval for dense vector similarity.

3. **Operational Simplicity & Cost Efficiency**:
   * Zero additional infrastructure or third-party cloud vector DB services required.
   * Runs locally and in CI via a standard Docker container (`pgvector/pgvector:pg16`).

4. **Clean Architecture Compliance**:
   * Vector column definitions (`vector(1536)`) and EF Core value converters are completely encapsulated within the `Infrastructure` layer (`AppDbContext`).
   * The `Domain` layer simply holds `float[]? Embedding` on `DocumentChunk`, and the `Application` layer interacts purely through abstractions (`IEmbeddingGenerator`, `IDocumentRepository`).

## Consequences

### Positive
* Single source of truth for all relational, audit, and vector data.
* Deterministic testing and local evaluation using standard database tooling.
* Seamless migration management via EF Core Migrations.

### Negative / Trade-offs
* Extremely large multi-million scale datasets may eventually require specialized vector partitioning or dedicated memory allocation, but for domain-specific copilot workloads (tens to hundreds of thousands of chunks), pgvector with HNSW indexing provides performance and precision.
