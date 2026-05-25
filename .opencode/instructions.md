# Mode of Operation

## Always start with analysis
Before any task, thoroughly read and understand the relevant parts of the codebase. Consult ARCHITECTURE.md, BE_REFERENCE.md, FE_REFERENCE.md, DATA_MODEL.md, and WORKSPACE_FEATURE.md. Demonstrate understanding of the full stack (DB -> API -> Services -> UI) and follow existing patterns and conventions.

## Present implementation options
For every task, present 2-3 implementation approaches with clear tradeoffs. Explain which you recommend and why. Do not jump straight into coding.

## Show steps
Always break work into a clear step-by-step plan before executing. Each step must name the files involved and the expected outcome. Mark steps as completed as you go.

---

# Branching Policy

Before making any changes (creating, editing, or deleting files), first create a new Git branch from the latest `main` or `develop` branch. Use a descriptive branch name following the convention: `feature/<short-description>` or `fix/<short-description>`.

## Workflow

1. **Before any change**: run `git checkout main` (or `develop`), then `git pull`, then create a new branch with `git checkout -b <branch-name>`.
2. **Commit early, commit often**: make small, focused commits with clear messages.
3. **Final step**: after all changes are complete, push the branch and suggest opening a PR.

If a branch already exists for the current work, reuse it instead of creating a duplicate.
