# Pickup Task from Coordinator

You are receiving an implementation plan from the **coordinator instance** (`patient-care-super`). This plan was created after cross-project analysis and describes work scoped to the **API layer**.

## Instructions

1. **Read the plan.** The user will provide it — either pasted inline or as a file path. Read the full plan carefully.
2. **Review your conventions.** Re-read `CLAUDE.md` in this repo to ensure you follow API project conventions (Clean Architecture layers, namespace conventions, DTO patterns).
3. **Check contracts.** If the plan references `_contracts/` files in the coordinator repo, read them to understand the expected request/response shapes.
4. **Create a feature branch.** Use the branch name specified in the plan, or derive one from the task (e.g., `feature/<short-description>`). Never commit directly to main.
5. **Implement.** Execute only the API-layer steps from the plan. Do not attempt work designated for DB or UI layers.
6. **Verify.** Run the verification steps specified in the plan. At minimum:
   ```bash
   dotnet build patient-care-api.sln
   dotnet test patient-care-api.sln
   ```
7. **Commit.** Stage and commit your changes with a clear message describing what was done.
8. **Report back.** Summarize what you implemented, what you verified, and any issues or open questions for the coordinator.

## Important

- Only implement work scoped to this layer (API endpoints, services, repositories, DTOs).
- If the plan references DB or UI work, ignore those sections — other specialist instances handle them.
- If something in the plan is unclear or seems wrong, ask the user before proceeding.
