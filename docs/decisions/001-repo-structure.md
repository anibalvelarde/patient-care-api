# ADR-001: Repository Structure for Claude Code

## Status

Accepted

## Date

2026-03-07

## Context

This C# REST API project was created before Claude Code existed. To enable effective AI-assisted development, the repository needs a structure that gives Claude Code the context it needs to make accurate, project-aware contributions — without requiring lengthy preamble in every conversation.

The companion `patient-care-db` repository was already restructured with this approach and proved the pattern works well.

## Decision

Add the following to the repository root:

1. **`CLAUDE.md`** — A thin index (~50 lines) that orients Claude Code to the project: architecture summary, key paths, build commands, and conventions.
2. **`.claude/`** — Claude Code configuration directory with `settings.json`, and placeholder subdirectories for hooks, tools, scripts, prompts, and skills.
3. **`.claude/skills/`** — Reusable skill definitions (code-review, refactor, release) that encode team-specific checklists and processes.
4. **`docs/`** — Human-and-AI-readable documentation: architecture overview, API reference, ADRs (this directory), and runbooks.

The existing `src/`, `tests/`, `build/`, and `.github/` directories remain unchanged.

## Consequences

- Claude Code can orient itself quickly via `CLAUDE.md` without reading the entire codebase.
- Skills encode repeatable processes, reducing drift between human and AI contributions.
- ADRs capture structural decisions, giving future conversations the "why" behind the "what."
- Docs serve double duty for onboarding both humans and AI.
- Small maintenance cost: docs and skills need occasional updates as the project evolves.
