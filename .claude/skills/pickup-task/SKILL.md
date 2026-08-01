---
name: pickup-task
description: Accept and implement an API-layer plan from the coordinator
user-invocable: true
---

# Pickup Task from Coordinator

You are receiving a plan scoped to the **API layer**.

## Steps

1. **Read the plan.** User provides it inline or as a file path.
2. **Conventions** (Clean Architecture, DI, DTO naming) are in the auto-loaded `CLAUDE.md` — don't re-read it.
3. **Check contracts.** List `../patient-care-super/_contracts/` filenames, then read **only** the contract file(s) for the domain(s) the plan touches (e.g. `sessions-api.md` for session work) — never the whole directory.
4. **Check for existing branch:** `git branch -a | grep -E "feature/|fix/"` — reuse if one exists.
5. **Create branch** if needed: `feature/<desc>` or `fix/<desc>`. Never commit to main.
6. **Kanban** — Move related card to DOING:
   ```bash
   curl -s --max-time 2 -X POST http://openclaw:8082/api/move \
     -H "Content-Type: application/json" -d '{"cardId": <ID>, "columnId": 4}'
   ```
7. **Implement.** Only API-layer steps. Follow existing patterns when referenced.
8. **Verify:**
   ```bash
   dotnet build patient-care-api.sln
   dotnet test patient-care-api.sln
   ```
9. **Test artifact:** Produce curl commands for each new/modified endpoint.
10. **Commit** with a clear message.
11. **Record completion.** Append to the plan file in `../patient-care-super/planning/active/`:
    ```markdown
    ## Completion — API
    - **Date**: YYYY-MM-DD
    - **Branch**: `feature/branch-name`
    - **Verification**: build: PASS/FAIL | test: PASS/FAIL
    - **Files**: N created, M modified
    - **Test artifact**: [curl commands]
    - **Open items**: None
    ```
    If other layers pending, leave in `active/`. If all done, move to `completed/` + add to `archive.md` (uncommitted).
12. **Report back.** Summarize implementation, verification, and open questions.
