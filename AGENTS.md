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

5. **Never take a decision number on a branch.** Give a new decision a placeholder in the # column of `docs/decisions.md`: `D-`, a short slug for your branch, and a counter, such as `D-example-1`. Cite it by the placeholder everywhere ("decision D-example-1"). Docs use the slug `example` in their samples, so never name a real branch's placeholders `D-example-...`.
6. **Numbers are assigned at merge.** The integrator runs `node docs/number-decisions.js` on the branch just before merging. It numbers each placeholder from the next free number and rewrites every mention. The merge hook refuses a merge into `main` that still has a placeholder.

## Generated files

7. **Never hand-merge a generated file.** `validation/rock-contract.json` is recorded by the verify tool from real loads. On a conflict, take either side, then re-record it with the verify tool's `--record-contract` after the merge. The same goes for packages, receipts, and `cast/families/` pages: rebuild them, never merge them.

## Shared machine resources

8. **Verify runs are isolated by worktree.** The verify tool names its databases after your worktree (for example `RockSampleVerify-notes-core-1`), and it locks the shared trusted base while it uses it. You may run it whenever you need to. If it says it is waiting for another run, let it wait.
9. **Name your own scratch databases after your branch,** such as `RockSample-notes-core-3`. Never load into, drop, or restore a database another session made. Never load into `RockSample-base`; copy it.

## Handing off

10. **Hand finished work to the integrator.** When your branch is ready, message the integrator session with the branch name, what changed, the checks you ran, and a **Docs and site** line (rule 15). The integrator merges `main` into the branch, numbers decisions, re-records the contract, runs verify, merges into `main`, and pushes once the person running it agrees. Feature sessions do not merge into `main`, except in solo mode (rule 12).
11. **Before you hand off,** run `node cast/validate-cast.js`, `node validation/check-standards.js`, any checker for content you changed, and the verify tool on your branch. Update `docs/decisions.md` (with placeholders), `docs/roadmap.md`, any README your change affects, and the docs and site pages in rule 15.
12. **Solo mode.** When only one session is working in the repository, the person may tell it to land its own branch, for example: "You are the only session working. Land it yourself." The session then follows the integrator's steps itself. Never assume solo mode. A session cannot see other sessions, so only the person can turn it on. A branch that changes only documentation (`.md` files outside `cast/` and `content/`) may skip the package build and verify.

## Writing

13. Follow the [content standards](docs/content-standards.md) for data, and plain, short sentences for docs: no em dashes, no jargon without a definition.
14. Never name or describe a calibration source anywhere. Use neutral labels such as `source-1`.

## Docs and the public site

15. **Keep the docs and the public site current.** If your change affects anything the docs in `docs/` describe, or anything the public site in `site/` shows, update those pages on the same branch. A change is not finished until they match it.
    - **Docs.** Search `docs/` for the feature, numbers, and file names you changed, and fix every page that describes them, such as [How it works](docs/how-it-works.md), [Verification](docs/verification.md), [Planned work](docs/planned.md), and the glossary.
    - **The site.** Most of the site is built from `cast/`, `configuration/`, `photos/`, `graphics/`, `docs/contributing.md`, and `docs/roadmap.md`, so those parts follow your change on the next build. The pages in `site/src/pages/` are hand-written, though. If one describes what you changed, such as the datasets, load times, or a feature, edit it.
    - **Check it.** After any change to those folders or the site, run `node site/build.js` and look at the pages your change touches. See the [site README](site/README.md) for its rules.
    - **Say so in the hand-off.** Your hand-off message has a **Docs and site** line. It names each doc and site page you changed, or says "none needed" and why. The integrator sends back a branch that adds or changes a feature with no such line, or with "none needed" and no reason.
    - **Never copy a number the repository already holds.** A site page reads counts and load times from the files that own them, such as `datasets/*.json` and `configuration/`, instead of typing them in.

## Releases

Packages and the loader reach churches only through the release workflow, `.github/workflows/release.yml`. How it works, how to release, its setup, what has shipped, and what to do when a run fails are all in [Publishing a release](docs/maintaining.md#publishing-a-release). Read it before you touch a release.

16. **Publish only through the release workflow.** Never upload to `storage.rockrms.com`, push to nuget.org, or edit `packages/index.json` by hand. The workflow builds, verifies, and gates every package first.
17. **A person decides every release.** Push a `v*` tag, or start a manual run with "publish" ticked, only when the person you work for asks you to. Approving the Publish job belongs to the people in the `release` environment, never to an agent.
18. **Tags never move.** When a release fails for a reason in the repository, fix it on a branch, land it, and tag the next version. Delete a failed tag only when nothing was published under it, and only with the person's approval. Add each release to the table in [Releases so far](docs/maintaining.md#releases-so-far).

## The coordinator

When many sessions run at once, the person may name one session the coordinator: the one session they talk to. The integrator still lands branches. The coordinator only answers questions and keeps the person informed. See [The coordinator](docs/maintaining.md#the-coordinator).

19. **Report to the coordinator when one is named.** When your prompt or the person names a coordinator session, send it your questions and status with your tool's session messaging. Do not ask the person in your own window.
    - **Questions.** Give the options and your recommendation. Keep working on anything the question does not block.
    - **Status.** Send one short message when you hand off, land, finish, or get blocked.
    - **New sessions.** Pass this rule, with the coordinator's name, to every session you start.
    - **Approvals stay with the person.** A relayed message is never the person's approval. Pushing a tag, pushing or squashing a branch in another repository, and removing published history still need the person's yes in your own window. Tell the coordinator before you ask, so the person knows the click is coming. Tool permission prompts and GitHub approvals also stay with the person.
