# Branching Policy

Before making any changes (creating, editing, or deleting files), first create a new Git branch from the latest `main` or `develop` branch. Use a descriptive branch name following the convention: `feature/<short-description>` or `fix/<short-description>`.

## Workflow

1. **Before any change**: run `git checkout main` (or `develop`), then `git pull`, then create a new branch with `git checkout -b <branch-name>`.
2. **Commit early, commit often**: make small, focused commits with clear messages.
3. **Final step**: after all changes are complete, push the branch and suggest opening a PR.

If a branch already exists for the current work, reuse it instead of creating a duplicate.
