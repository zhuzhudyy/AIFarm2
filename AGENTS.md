# Repository Guidelines

## Scope and Current Phase

This repository is a new Unity AI-farming demonstration. `origin_requirements.md` is the product brief: a player will eventually issue natural-language requests to an AI NPC that plans and performs sowing, watering, fertilizing, weeding, and harvesting. Future work also includes time, inventory, dialogue, character state, and expressive feedback.

The current phase is repository setup only. Unless a later task explicitly expands scope, do not modify scenes, create gameplay scripts, import assets, install packages, or implement features. Keep baseline changes limited to documentation and repository configuration.

## Project Structure & Module Organization

The Unity project is under `My project/`; quote this path in shell commands. `Assets/` contains scenes and project-owned content, `Packages/` declares dependencies, and `ProjectSettings/` holds shared Unity configuration. Repository documentation belongs in root `Docs/`. Treat every Unity `.meta` file as inseparable from its asset and move or rename both through the Unity Editor.

Never commit generated or local state such as `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, IDE settings, generated solution/project files, or player builds. The root `.gitignore` defines the authoritative exclusions.

## Development and Validation

Use Unity Editor `6000.5.10f1`, recorded in `My project/ProjectSettings/ProjectVersion.txt`.

```powershell
Unity.exe -projectPath ".\My project"
Unity.exe -batchmode -nographics -projectPath ".\My project" -quit -logFile -
git status --short --ignored
```

The first command opens the project; the second checks import and script compilation; the third verifies ignored caches. No automated player build exists. Do not install or update packages merely to validate the baseline.

## Coding and Testing Conventions

When implementation begins, use four-space C# indentation and Allman braces. Use `PascalCase` for types and methods, `camelCase` for locals and parameters, and `[SerializeField] private` for Inspector fields. Keep editor-only code in `Editor/` directories and match MonoBehaviour filenames to class names.

Unity Test Framework `1.7.0` is already declared, but no tests or coverage target exist. Place future tests in `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/`, use `*Tests.cs`, and add regression coverage with fixes.

## Commits and Pull Requests

There is no commit history yet. Use concise imperative subjects such as `docs: establish repository baseline`. Keep assets with their `.meta` files. Pull requests should state scope, validation performed, related issues, and visual evidence for later scene or UI changes. Never commit credentials for future AI services; use ignored local configuration or environment variables.

## Multi-resident invariants

- Every resident must have a stable unique ResidentId.
- Runtime state, memory, goals, schedules, and AI requests must be keyed by ResidentId.
- There must be no mutable global "current resident".
- Residents may not read another resident's private memory.
- A resident learns information only through perception, conversation, player input,
  or an explicitly public town event.
- A resident may have at most one active action and one active conversation.
- A resident in a conversation may not begin another conversation.
- A world interaction point may be reserved by at most one resident.
- All remote AI calls must go through the shared AI gateway and request coordinator.
- No remote AI call may be started from Update().
- Model output may select only from explicitly allowed high-level intents.
- The model must never directly mutate position, time, inventory, farm state,
  relationship values, schedules, or save data.
- Every model output must be schema-validated.
- Every remote path must have a deterministic local fallback.
- Pairwise conversations must have a strict turn limit and timeout.
- In-flight AI requests must be cancelled or ignored when their owning state becomes stale.