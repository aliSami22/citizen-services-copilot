# Contributing

Thanks for contributing to Citizen Services Copilot. This project follows
conventional commits, a strict branch → PR flow, and a "never commit secrets"
rule. The CI pipeline enforces format, tests, dependency scans, and a gitleaks
full-history secret scan on every PR.

## Flow

1. **Create a branch** off `main` (never commit directly to `main`):
   `git checkout -b feat/your-feature` (or `fix/...`, `docs/...`, `chore/...`).
2. **Open an issue first** for anything non-trivial so the work is visible and
   can be traced to a decision. Reference it in the PR with `Closes #N`.
3. **Describe what and why** in the PR description — not just *what* changed
   but *why* it was the right change, and how it maps to the requirements
   (see `docs/BRD.md` for objective/business-rule IDs).
4. **Run the tests** before opening the PR:
   ```bash
   dotnet build CitizenServicesCopilot.slnx
   dotnet test CitizenServicesCopilot.slnx
   ```
   New behavior must ship with tests (regression tests for bug fixes).
5. **Self-review before merge** — re-read your diff as the reviewer. Check:
   - No secrets, real tokens, or personal data is in any file or in the git
     history.
   - No unrelated changes leaked into the branch.
   - `dotnet format` clean, no new warnings.

## Commit message convention

Use [Conventional Commits]: `<type>(<scope>): <description>`

Common types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`,
`perf`, `style`.

Examples:
- `fix(api): use dedicated DI scope for background workflow execution`
- `feat(rag): add hybrid retrieval with refusal gate`
- `docs(readme): document dotnet dev-certs --trust`

Writing messages on Windows PowerShell: pass the message via a file
(`git commit -F <file>`) — a literal backtick in `-m "..."` is silently
escaped by PowerShell and will corrupt the message.

## Secrets & personal data

- **Never** commit secrets: JWT keys, API keys, connection passwords, or any
  real personal data (names, government IDs, addresses).
- Configuration goes in `appsettings.json` (safe defaults), per-developer
  secrets go in user-secrets or environment variables (see `.env.example`).
- CI runs `gitleaks detect` against the **full history** — a leaked secret
  must be scrubbed, not just in a newer commit.
- Real corpus documents for evaluation are versioned as fixtures
  (`tests/fixtures/`); do not add real citizens' data.

## Local checks

```bash
# format + vulnerability + secrets checks that CI runs
dotnet format CitizenServicesCopilot.slnx --verify-no-changes --no-restore
dotnet list CitizenServicesCopilot.slnx package --vulnerable --include-transitive
gitleaks detect --source . --redact --verbose --exit-code 2
# retrieval evaluation (offline, deterministic)
dotnet run --project tools/EvalHarness -- --set tests/eval/golden-set.yaml --output docs/EVALUATION.md
```

## Definition of done

- [ ] Branch off `main`; PR targets `main`.
- [ ] Conventional-commit subject; issue linked (`Closes #N`).
- [ ] `dotnet build` + `dotnet test` green.
- [ ] Tests added for new behavior / regression.
- [ ] `dotnet format` clean.
- [ ] No secrets or personal data committed.
- [ ] Self-reviewed the full diff before requesting merge.