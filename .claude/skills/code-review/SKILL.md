# Skill: Code Review

## When to use

When reviewing C# code changes in this API project — PRs, new features, or refactors.

## Checklist

### Architecture

- [ ] Dependencies flow inward: Web -> Core <- Infrastructure
- [ ] Core has no references to Infrastructure or Web
- [ ] New services are registered in the appropriate `Configurations/Dependencies.cs`
- [ ] New interfaces are in `Core/Interfaces/`; implementations in `Core/Services/` or `Infrastructure/Repositories/`

### API Design

- [ ] Controllers are thin — business logic lives in services
- [ ] Route follows existing pattern: `api/[controller]`
- [ ] Appropriate HTTP status codes (200, 201, 204, 400, 404, 500)
- [ ] `[ProducesResponseType]` attributes on controller actions
- [ ] Request/Response DTOs are separate from entities

### Data Access

- [ ] Repository methods use async/await consistently
- [ ] New entities have Fluent API mapping in `ApplicationDbContext.OnModelCreating`
- [ ] Table and column names match the MySQL schema (e.g., `PatientID`, not `Id`)
- [ ] No direct DbContext usage in controllers or services

### Testing

- [ ] New service logic has corresponding tests in `Core.Tests`
- [ ] New controller actions have corresponding tests in `Web.Tests`
- [ ] Tests use Moq for dependencies and FluentAssertions for assertions
- [ ] No tests depend on external services (DB, network)

### Security & Configuration

- [ ] No hardcoded credentials or connection strings
- [ ] Database credentials come from environment variables only
- [ ] CORS policies are not overly permissive
- [ ] No sensitive data logged at Information level or below

### General

- [ ] No unused `using` statements in changed files
- [ ] Follows existing naming conventions (PascalCase, `{Resource}Controller`, etc.)
- [ ] No breaking changes to existing API contracts without discussion
