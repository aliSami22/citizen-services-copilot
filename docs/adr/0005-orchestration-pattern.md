# ADR-005: Orchestration Pattern — State Machine

## Status
Accepted

## Context
Checkpoint B requires a named, justified orchestration pattern for the D4
multi-agent workflow (EligibilityIdentifier → ProcedureResolver →
ResponseDrafter → AwaitApproval → Persist). The brief (§3 R-5) mandates:
max-iteration breaker, per-step timeout, retry with backoff, graceful
degradation, and step-by-step run inspection.

## Decision
We use an explicit State Machine. The workflow has a small, finite set of
named stages with well-defined transitions and terminal states (Completed,
Rejected, Failed).

## Alternatives Considered
1. Supervisor pattern — a coordinator LLM decides the next agent dynamically.
   Rejected: introduces non-determinism in a domain (government benefits)
   where reproducibility and auditability are first-class.
2. Planner-Executor — a planner emits a multi-step plan, an executor runs it.
   Rejected: overkill for a 3-agent linear flow; the plan is known a priori.
3. Pipeline — pure function composition. Rejected: cannot express the
   AwaitApproval stage (external event) or the terminal Rejected state
   without leaking control flow into the pipeline itself.

## Consequences
+ Deterministic, replayable, inspectable per-run.
+ Natural home for the breaker, timeout, retry, and degradation controls.
+ Approval gate is a first-class stage, not an afterthought.
- Less flexible if the workflow later needs dynamic branching; would require
  explicit new states/transitions rather than free-form re-planning.
- State transitions must be enumerated carefully; the enum + switch is the
  source of truth.

## References
- WorkflowOrchestrator.cs (Application/Orchestration)
- Checkpoint B3 commit d8dc236
- ITI brief §3 R-5