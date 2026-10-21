# Agent rules

Rules for every AI agent and person working in this repository. Several agents often work here at the same time, so these rules keep their work from colliding. The longer how-to is in [Maintaining](docs/maintaining.md#working-in-parallel). Read the [README](README.md) first for what the project is.

## Setup, once per clone

```
git config core.hooksPath .githooks
git config branch.main.mergeoptions --no-ff
```

The first line turns on the hooks that guard `main`. The second makes every merge into `main` a real merge commit, so the hooks run.

## Where to work

1. **Work in your own worktree, on your own branch.** Never edit files in the main checkout (`rock-sample-data`). It only takes merges. Make a worktree with `git worktree add -b <branch> ../rock-sample-data-<branch> main`.
2. **Never commit to `main`.** The pre-commit hook refuses it. Do not bypass it with `--no-verify`.
3. **Merge `main` into your branch** when you start, before every verify run, and before you hand work off. Other agents land work all day, and a stale branch is where conflicts grow.
4. **Land small pieces.** A piece that works and passes its checks goes to the integrator the same day. Do not hold a large branch for days.

## Decisions

5. **Never take a decision number on a branch.** Give a new decision a placeholder in the # column of `docs/decisions.md`: `D-`, a short slug for your branch, and a counter, such as `D-notes-1`. Cite it by the placeholder everywhere ("decision D-notes-1").
6. **Numbers are assigned at merge.** The integrator runs `node docs/number-decisions.js` on the branch just before merging. It numbers each placeholder from the next free number and rewrites every mention. The merge hook refuses a merge into `main` that still has a placeholder.

## Generated files

7. **Never hand-merge a generated file.** `validation/rock-contract.json` is recorded by the verify tool from real loads. On a conflict, take either side, then re-record it with the verify tool's `--record-contract` after the merge. The same goes for packages, receipts, and `cast/families/` pages: rebuild them, never merge them.

## Shared machine resources

8. **Verify runs are isolated by worktree.** The verify tool names its databases after your worktree (for example `RockSampleVerify-notes-core-1`), and it locks the shared trusted base while it uses it. You may run it whenever you need to. If it says it is waiting for another run, let it wait.
9. **Name your own scratch databases after your branch,** such as `RockSample-notes-core-3`. Never load into, drop, or restore a database another session made. Never load into `RockSample-base`; copy it.

## Handing off

10. **Hand finished work to the integrator.** When your branch is ready, message the integrator session with the branch name, what changed, and the checks you ran. The integrator merges `main` into the branch, numbers decisions, re-records the contract, runs verify, merges into `main`, and pushes once the person running it agrees. Feature sessions do not merge into `main`, except in solo mode (rule 12).
11. **Before you hand off,** run `node cast/validate-cast.js`, `node validation/check-standards.js`, any checker for content you changed, and the verify tool on your branch. Update `docs/decisions.md` (with placeholders), `docs/roadmap.md`, and any README your change affects.
12. **Solo mode.** When only one session is working in the repository, the person may tell it to land its own branch, for example: "You are the only session working. Land it yourself." The session then follows the integrator's steps itself. Never assume solo mode. A session cannot see other sessions, so only the person can turn it on. A branch that changes only documentation (`.md` files outside `cast/` and `content/`) may skip the package build and verify.

## Writing

13. Follow the [content standards](docs/content-standards.md) for data, and plain, short sentences for docs: no em dashes, no jargon without a definition.
14. Never name or describe a calibration source anywhere. Use neutral labels such as `source-1`.
