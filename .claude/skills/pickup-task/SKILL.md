# Pickup Task from Coordinator

You are receiving an implementation plan from the **coordinator instance** (`patient-care-super`). This plan was created after cross-project analysis and describes work scoped to the **API layer**.

## Instructions

1. **Read the plan.** The user will provide it — either pasted inline or as a file path. Read the full plan carefully.
2. **Review your conventions.** Re-read `CLAUDE.md` in this repo to ensure you follow API project conventions (Clean Architecture layers, namespace patterns, DTO naming, etc.).
3. **Check contracts.** If the plan references `_contracts/` files in the coordinator repo, read them to understand the API request/response shapes your endpoints must match.
4. **Create a feature branch.** Use the branch name specified in the plan, or derive one from the task (e.g., `feature/<short-description>`). Never commit directly to main.
5. **Implement.** Execute only the API-layer steps from the plan. Do not attempt work designated for DB or UI layers.
6. **Follow existing patterns.** When the plan says "pattern reference: `SomeFile.cs`", read that file first and replicate its structure, naming, and conventions.
7. **Verify.** Run the verification steps specified in the plan. At minimum:
   ```bash
   dotnet build patient-care-api.sln
   dotnet test patient-care-api.sln
   ```
8. **Commit.** Stage and commit your changes with a clear message describing what was done.
9. **Record completion.** Append a `## Completion` section to the plan file in `../patient-care-super/planning/active/`:
   ```markdown
   ## Completion — API

   - **Date**: YYYY-MM-DD
   - **Layer**: API
   - **Branch**: `feature/branch-name`
   - **Verification**:
     - dotnet build: PASS/FAIL
     - dotnet test: PASS/FAIL
   - **Files**: N created, M modified
   - **Open items**: (any issues, caveats, or next-layer dependencies to note — or "None")
   ```
10. **Archive the plan.** Move the plan file from `../patient-care-super/planning/active/` to `../patient-care-super/planning/completed/` and add a one-line entry to `../patient-care-super/planning/archive.md`:
    ```
    | YYYY-MM-DD | Plan title | Outcome summary | `feature/branch-name` |
    ```
    Leave these changes uncommitted — the coordinator or user will review and commit them.
    If other layers in the plan still need implementation, leave the plan in `active/` instead.
11. **Report back.** Summarize what you implemented, what you verified, and any issues or open questions for the coordinator.

## Important

- Only implement work scoped to this layer (endpoints, services, repositories, DTOs, entities).
- If the plan references DB or UI work, ignore those sections — other specialist instances handle them.
- If something in the plan is unclear or seems wrong, ask the user before proceeding.
