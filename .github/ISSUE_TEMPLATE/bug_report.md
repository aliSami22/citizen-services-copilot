---
name: Bug report
about: Report a defect so it can be reproduced and fixed
title: "[bug] short description"
labels: bug
assignees: ''
---

## Describe the bug

<!-- Clear, concise description of what happened. -->

## Steps to reproduce

1. ...
2. ...
3. ...

## Expected behavior

<!-- What should have happened instead? -->

## Actual behavior

<!-- Exact output/logs. If it is a workflow run, paste the run trace from GET /api/runs/{id}/trace (redact personal data). -->

## Environment

- OS:
- .NET SDK version:
- Provider: `Ollama` / `OpenAI` / `stub` (tests)
- Database: local Postgres / in-memory tests

## Evidence

- Relevant files/tests: <!-- e.g. test name or file path -->
- Optional: commit hash where it reproduces

<!-- Note: do NOT paste real JWT keys, API keys, passwords, or personal data. -->