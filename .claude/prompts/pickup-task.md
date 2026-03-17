# Pick Up Task from Coordinator

Read the active plan files from the coordinator to pick up your portion of a cross-cutting task.

## Steps

1. List available plans:
   ```
   ls ../patient-care-super/planning/active/
   ```

2. Read the plan file(s) to understand:
   - What is the overall goal?
   - What is the **API layer's** responsibility in this plan?
   - What has already been completed in other layers (especially DB)?

3. Read the coordinator's CLAUDE.md if you need broader context:
   ```
   ../patient-care-super/CLAUDE.md
   ```

4. Check relevant API contracts for the expected endpoint shapes:
   ```
   ../patient-care-super/_contracts/
   ```

5. Summarize what you need to do and confirm with the user before implementing.

## Reminder

DB schema changes must land before yours. After implementing, run `dotnet build && dotnet test` to verify. The UI layer depends on your output — update `../patient-care-super/_contracts/` if you change endpoint shapes.
