---
name: code-review
description: Review C# API code changes for correctness and convention adherence
user-invocable: true
---

# Code Review — API

## Pre-check
- [ ] Work is on a feature branch, not main

## Architecture
- [ ] Dependencies flow inward: Web → Core ← Infrastructure
- [ ] Core has no references to Infrastructure or Web
- [ ] New services registered in `Configurations/Dependencies.cs`
- [ ] Interfaces in `Core/Interfaces/`, implementations in `Core/Services/` or `Infrastructure/Repositories/`

## API Design
- [ ] Controllers are thin — logic in services
- [ ] Route pattern: `api/[controller]`
- [ ] Appropriate HTTP status codes (200, 201, 204, 400, 404)
- [ ] Request/Response DTOs separate from entities

## Data Access
- [ ] Async/await used consistently
- [ ] New entities have Fluent API mapping in `ApplicationDbContext.OnModelCreating`
- [ ] Column names match MySQL schema (PascalCase)

## Testing
- [ ] New service logic has `Core.Tests` coverage
- [ ] New controllers have `Web.Tests` coverage
- [ ] Tests use Moq + FluentAssertions

## General
- [ ] No hardcoded credentials — env vars only
- [ ] Follows PascalCase naming, `{Resource}Controller` pattern
- [ ] DTOs match `_contracts/` definitions
- [ ] Curl test commands provided as test artifact
