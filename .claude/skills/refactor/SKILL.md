# Skill: Refactor

## When to use

When restructuring or improving existing code without changing external behavior.

## Process

### 1. Understand scope

- Read the files involved and their dependents
- Identify the public API surface that must not change
- Check for existing tests that cover the code

### 2. Plan the refactor

- List the specific changes and the order they should happen
- Confirm no architectural boundary violations (Core must stay dependency-free)
- If renaming, check all references across `src/` and `tests/`

### 3. Execute

- Make changes incrementally — one logical change per step
- After each step, verify the solution builds: `dotnet build patient-care-api.sln`
- Run tests after each meaningful change: `dotnet test patient-care-api.sln`

### 4. Verify

- [ ] `dotnet build patient-care-api.sln` succeeds with no warnings in changed files
- [ ] `dotnet test patient-care-api.sln` passes — all existing tests still green
- [ ] No changes to public API contracts (controller routes, request/response shapes) unless intentional
- [ ] DI registrations updated if interfaces or implementations were renamed/moved
- [ ] EF Core model mappings updated if entity names changed

### Common refactors in this project

| Refactor | Watch out for |
|---|---|
| Extract interface | Register new interface in `Core/Configurations/Dependencies.cs` |
| Move entity | Update `ApplicationDbContext.OnModelCreating` mapping |
| Rename DTO | Update controller `[ProducesResponseType]` attributes |
| Split service | Update DI registration; check all injection sites |
| Add new layer/project | Update `patient-care-api.sln` and project references |
