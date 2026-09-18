# Working notes

## Shell footguns (Windows PowerShell 5.1)

- Backtick is PowerShell's escape character. Passing a commit message via
  `git commit -m "... `gitleaks ...` ..."` silently converts literal backticks
  to something else (observed: `` ` `` -> `\`). To preserve backticks, write
  the message to a file and use `git commit --amend -F <file>` (or a single-
  quoted here-string into `git commit -F -`).
- Do NOT chain git commands with `if ($?)` when a native command writes to
  stderr: PS 5.1 reports `$? = False` after `cmd 2>&1 | Select-Object` because
  stderr becomes ErrorRecords. Prefer running commands one per tool call, or
  check the real `$LASTEXITCODE`.