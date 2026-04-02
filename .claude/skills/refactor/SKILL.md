---
name: refactor
description: Restructure API code without changing external behavior
user-invocable: true
---

# Refactor — API

## Process

1. **Check for existing branch:** `git branch -a | grep -E "feature/|fix/"`. Create `feature/<desc>` if needed.
2. **Understand scope** — Read files and dependents, identify public API surface, check test coverage.
3. **Plan** — List changes in order. Confirm no architectural boundary violations (Core stays dependency-free).
4. **Execute** — Incremental changes. After each step:
   - `dotnet build patient-care-api.sln`
   - `dotnet test patient-care-api.sln`
5. **Verify:**
   - [ ] Build succeeds, no new warnings
   - [ ] All tests pass
   - [ ] No changes to API contracts unless intentional
   - [ ] DI registrations updated if interfaces/implementations renamed
   - [ ] EF Core mappings updated if entity names changed

## Common Refactors

| Refactor | Watch out for |
|---|---|
| Extract interface | Register in `Core/Configurations/Dependencies.cs` |
| Move entity | Update `ApplicationDbContext.OnModelCreating` |
| Rename DTO | Update controller `[ProducesResponseType]` |
| Split service | Update DI; check injection sites |
