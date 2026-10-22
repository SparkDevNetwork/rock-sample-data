# Maintaining

How the team changes the sample data. To suggest a change from outside the team, see [Contributing](contributing.md).

Before you start, read the [content standards](content-standards.md). Every change must follow them.

## Where changes go

| You want to... | Start here |
|---|---|
| Add a Cast family or person | See "Add a Cast household." |
| Change the church setup (campuses, funds, rooms, times) | Edit `configuration/` and explain why in the decision log. |
| Add a calibration source | Run the calibration scripts on a database you may use. See [Calibration](../calibration/README.md). |
| Change how data is generated | Change the builder, then show the validation result did not get worse. |
| Make a decision | Add a row to [decisions.md](decisions.md) with a placeholder number. See "Working in parallel." |
| Publish packages and the loader | Tag a version. See "Publishing a release." |

## Working in parallel

Several people and AI agents often change this repository at the same time. The short rules are in [AGENTS.md](../AGENTS.md), which agents read automatically. This section explains how the pieces fit.

### One worktree per piece of work

Each piece of work gets its own branch in its own folder (a git worktree), so no one edits another's files:

```
git worktree add -b notes ../rock-sample-data-notes main
```

The main checkout, `rock-sample-data`, only takes merges. The hooks in `.githooks` enforce it: `pre-commit` refuses an ordinary commit on `main`, and `pre-merge-commit` refuses a merge into `main` that still has a placeholder decision. Turn them on once per clone (see [AGENTS.md](../AGENTS.md#setup-once-per-clone)). A person who truly needs a direct commit on `main` can set `ALLOW_MAIN_COMMIT=1` for that one command.

Merge `main` into your branch often: when you start, before each verify run, and before you hand off. Small pieces that land the same day rarely conflict.

### Decision numbers

Two branches that each take "the next number" collide. So a branch never takes one. A new decision gets a placeholder: `D-`, a slug for the branch, and a counter.

```
| D-example-1 | 2026-10-21 | The example decision. ... | ... |
```

Cite it the same way everywhere, for example "(decision D-example-1)". Just before the branch merges, the integrator runs:

```
node docs/number-decisions.js --dry-run
node docs/number-decisions.js
```

It numbers the placeholders from the next free number, in the order they appear in the log, and rewrites every mention in the repository's tracked text files. `--check` exits with 1 when a placeholder is left; the merge hook runs it.

### Generated files

Some files are written by tools, never by hand. Two branches each write a correct version, and git sees a conflict. Never merge them line by line. Take either side, finish the merge, then rebuild:

| File | Rebuild with |
|---|---|
| `validation/rock-contract.json` | The verify tool with `--record-contract` |
| `cast/families/*.md` | `node cast/build-pages.js` |
| `packages/` (ignored by git) | The builder |

The contract is the list of every Rock table, column, Guid, and stored procedure the loader writes or depends on, recorded from real loads. The verify tool checks it against each Rock release, so a Rock change that would break the loader is caught before a package ships.

### Shared machine resources

The verify tool names its own databases after the worktree it runs from (`RockSampleVerify-notes-core-1`, or `RockSampleVerify-main-core-1` from the main checkout), so runs from different worktrees never touch each other. They all copy from one trusted base, `RockSampleVerify-base`. A run holds a shared lock on it while it runs, and takes an exclusive lock only to rebuild a stale base, so a rebuild waits for every running verification. `--run <tag>` sets the tag by hand.

Scratch databases for your own loads follow the same idea: name them after your branch, and never touch one another session made.

### The integrator

One session or person, the integrator, lands work on `main`. Feature work ends with a message to the integrator: the branch, what changed, and the checks that ran. The integrator lands one branch at a time:

1. Merge `main` into the branch, and resolve conflicts. Take either side of a generated file.
2. Run `node docs/number-decisions.js` on the branch.
3. Rebuild the packages, run the verify tool with `--record-contract`, then run it again without it for a clean pass.
4. Commit on the branch, and merge it into `main`. Push once the person running the integrator agrees.
5. Tell the other sessions that `main` moved, so they merge it.

Start an integrator whenever two or more sessions will change the repository at once. It handles the steps above, but a person still makes the judgment calls, such as dropping a branch that `main` already covers.

When only one session is working, it can be its own integrator (solo mode, rule 12 in [AGENTS.md](../AGENTS.md#handing-off)). The person turns it on by saying so. A session never assumes it, because it cannot see other sessions. A branch that changes only documentation (`.md` files outside `cast/` and `content/`) can skip the package build and verify, since docs cannot break a package.

## Add a Cast household

1. **Copy a similar household** in `cast/households/` and give it a new file name. The file name is the household `key` (lowercase, hyphens).
2. **Make new Guids** for the household and every person. Guids are uppercase and never change once committed.
3. **Write the story and test purposes.** The first test purpose is the main reason the family exists. It shows on the family pages.
4. **Fill in each person:** names, gender, family role, age (`birth.age` plus month and day), marital status, connection status, and, for featured people, a short `bio`.
5. **Pick names first.** Then set `photo.ethnicity` for each adult so the Cast stays close to the mix in `photos/ethnicities.json`, with about 15% of couples mixed. Children inherit from their parents. Set a child's value only when the blend would be wrong, for example an adoption.
6. **Use only reserved contact data.** Phones from 555-0100 to 555-0199 that no one else in the Cast uses, and emails at `example.org`.
7. **Set attendance and giving patterns** in words the builder understands. See `cast/household.schema.json` for every option. Use `daysAgo` for anything recent.
8. **Leave the address out.** Run `node addresses/build-pool.js --cast`, and it adds an address near the main campus.
9. **Check and publish:**

   ```
   node cast/validate-cast.js
   node cast/build-pages.js
   ```

   The validator must show 0 errors. Read your new family page in `cast/families/`.

10. **Load it.** Build the Core 1 package with the builder (`--dataset core-1`), then load it with the loader CLI and `--dry-run` against a fresh Rock database, and confirm it loads.
11. **Photos.** New people need photos. The validator warns about each person without one. Give an image model the [photo brief](../photos/brief.md), then deliver the results with the builder's `--deliver-photos` command.

## Change a Cast person

Tests and documentation rely on Cast people. Treat a change like a code change:

- Never change a Guid.
- Do not change a name, age, or relationship that a test or help article may rely on, unless you also fix what depends on it.
- Keep the status rules: a Leading person leads a group; an Engaged person gives, serves, or is in a group (a child counts when an adult at home does).

## Change the builder or loader

- Keep the load to one transaction, and keep `--dry-run` working.
- Keep every random choice seeded, so the same seed and anchor give the same data.
- Keep packages data only. A file the loader needs goes into the package through the builder, and the loader reads it through the package, never from the repository.
- If a change alters the package layout in a way an older loader cannot read, raise the package format version.
- Run validation on Full 7 before and after your change. A change should not lower the share of values within tolerance without a written reason. See [Validation](../validation/README.md).

## Publishing a release

The release workflow, `.github/workflows/release.yml`, publishes everything (decisions D-release-1 to D-release-4). It has four jobs:

| Job | Where | What it does |
|---|---|---|
| Plan | Linux | Reads the version from the tag or the run's inputs, and the Rock releases from `release/rock-releases.json`. |
| Packages for Rock X | Windows, once per Rock release | Checks out that Rock, builds it with Visual Studio's MSBuild, starts SQL Server, writes every dataset's package, runs the verify tool, and runs the publishing gate. Saves the packages and receipts as an artifact. |
| Loader NuGet package | Windows | Runs `loader-compat-check` for both builds, then packs `RockSampleData.Loader` with the release version, Source Link, symbols, the license, and the README. |
| Publish | Windows, in the `release` environment | Runs the gate again, pushes the loader to nuget.org, uploads the packages and receipts, then merges and uploads the index last. |

A run without "publish" stops after the first three jobs. That is a dry run.

### Release a version

1. Make sure `main` has what you want to ship, and that `release/rock-releases.json` lists every Rock release to support.
2. Do a dry run first: in GitHub, open Actions > Release > Run workflow, pick `main`, and leave "publish" off.
3. Tag `main` and push the tag:

   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. Approve the Publish job when GitHub asks.

A tag with a label, such as `v1.1.0-beta.1`, is a prerelease. NuGet lists the loader as a prerelease, and only `packages/index-prerelease.json` changes (decision D-release-3). To try one in Rock, point the installer block at `https://storage.rockrms.com/sampledata/packages/index-prerelease.json`.

A manual run can also publish: run it on `main`, give a version, and tick "publish". Use this when a tag's run failed for a reason outside the repository. A run that failed partway can be run again: files already uploaded with the same bytes are skipped, and a version already on nuget.org is skipped.

### Add or change a Rock release

Each entry in `release/rock-releases.json` is a Rock version and the `ref` to check out from SparkDevNetwork/Rock: a release tag, or a full commit hash before Rock tags the release. The workflow stops if the ref's `Directory.Build.props` names a different version. When Rock publishes the tag for a pinned commit, change the `ref` to the tag.

### Where files go

Everything goes in the `sampledata` container, which `storage.rockrms.com` serves through Azure Front Door.

| Path | What it is | Cache |
|---|---|---|
| `packages/v1.0.0/rock-21.0.2/core-1.zip` | A package. Never changes. | One year |
| `packages/v1.0.0/rock-21.0.2/core-1.verify.json` | Its receipt. Never changes. | One year |
| `packages/index.json` | The index Rock reads (format in the [loader README](../loader/README.md#the-package-index)). | 5 minutes |
| `packages/index-prerelease.json` | The stable index plus the newest prerelease. | 5 minutes |

`release/merge-index.js` builds the index from the receipts and `datasets/*.json`. You can run it yourself on a folder of packages to see what a release would publish.

### One-time setup

The workflow needs the following before it can publish. A dry run needs only the first item.

#### 1. GitHub Actions minutes

The repository is private, so every job uses the organization's Actions minutes, and Windows minutes cost more than Linux ones. If GitHub refuses to start jobs with "recent account payments have failed or your spending limit needs to be increased," an organization owner must fix it in the organization's Settings > Billing and plans, with a budget for Actions. This happened on 2026-10-21 and was fixed the same day. A dry run uses about 70 minutes of Windows time for each Rock release, plus a few minutes for the loader.

#### 2. The Azure identity

Run these in Azure Cloud Shell (Bash), or any shell with the Azure CLI. Fill in the first four values.

```
TENANT=<tenant id>
SUBSCRIPTION=<subscription id>
RESOURCE_GROUP=<resource group of the storage account>
ACCOUNT=<storage account behind storage.rockrms.com>
REPOSITORY=SparkDevNetwork/rock-sample-data

az login --tenant "$TENANT"
az account set --subscription "$SUBSCRIPTION"

# The app registration the workflow signs in as.
APP_ID=$(az ad app create --display-name "rock-sample-data release" --query appId -o tsv)
PRINCIPAL_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)

# Trust GitHub's tokens only for jobs in the release environment of this repository.
cat > credential.json <<EOF
{
  "name": "rock-sample-data-release-environment",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:$REPOSITORY:environment:release",
  "description": "The Publish job of .github/workflows/release.yml",
  "audiences": ["api://AzureADTokenExchange"]
}
EOF
az ad app federated-credential create --id "$APP_ID" --parameters credential.json

# Let it write blobs in the sampledata container, and nowhere else.
SCOPE="$(az storage account show -n "$ACCOUNT" -g "$RESOURCE_GROUP" --query id -o tsv)/blobServices/default/containers/sampledata"
az role assignment create --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" --scope "$SCOPE"

echo "AZURE_CLIENT_ID=$APP_ID"
```

Check that the container exists before the first release: `az storage container show --account-name "$ACCOUNT" -n sampledata --auth-mode login`. The identity has no other role, so it cannot read the account's keys, change its settings, or purge Front Door.

Front Door must pass on the blobs' own `Cache-Control` headers, or the index stays cached longer than 5 minutes. If its caching rule overrides them, add a rule for `/sampledata/packages/index*.json` that honors the origin's header.

#### 3. nuget.org trusted publishing

Sign in to nuget.org as an owner of `RockSampleData.Loader` (or of the account that will own it), open your user name > Trusted Publishing, and add a GitHub Actions policy:

| Field | Value |
|---|---|
| Package owner | The nuget.org account or organization that owns the package |
| Repository owner | `SparkDevNetwork` |
| Repository | `rock-sample-data` |
| Workflow file | `release.yml` |
| Environment | `release` |

The workflow signs in as the nuget.org user named in `NUGET_USER`: the profile name of the user who made the policy, not an email address. If nuget.org marks the policy as temporarily active, publish once before it expires, or make it again.

#### 4. The release environment and its variables

An administrator of the repository runs these with the GitHub CLI. Fill in the approvers' GitHub user names.

```
REPOSITORY=SparkDevNetwork/rock-sample-data
APPROVERS="<github-user> <github-user>"

reviewers=$(for user in $APPROVERS; do gh api "users/$user" -q '{type: "User", id: .id}'; done | jq -s .)
jq -n --argjson reviewers "$reviewers" \
  '{reviewers: $reviewers, prevent_self_review: false, deployment_branch_policy: {protected_branches: false, custom_branch_policies: true}}' \
  | gh api -X PUT "repos/$REPOSITORY/environments/release" --input -

# Only main and version tags may deploy to it.
gh api -X POST "repos/$REPOSITORY/environments/release/deployment-branch-policies" -f name=main -f type=branch
gh api -X POST "repos/$REPOSITORY/environments/release/deployment-branch-policies" -f name='v*' -f type=tag

# The values the Publish job reads. They are not secrets, so they are environment variables.
gh variable set AZURE_CLIENT_ID       -R "$REPOSITORY" --env release -b "<APP_ID from step 2>"
gh variable set AZURE_TENANT_ID       -R "$REPOSITORY" --env release -b "<tenant id>"
gh variable set AZURE_SUBSCRIPTION_ID -R "$REPOSITORY" --env release -b "<subscription id>"
gh variable set AZURE_STORAGE_ACCOUNT -R "$REPOSITORY" --env release -b "<storage account>"
gh variable set NUGET_USER            -R "$REPOSITORY" --env release -b "<nuget.org user name>"
```

Set `prevent_self_review` to `true` if the person who tags a release should not approve it too.

### A self-hosted runner instead

The Packages job runs on GitHub's `windows-2025` image by default. To run it on your own Windows machine instead, for example to keep the trusted base between runs or when the hosted image cannot build Rock:

1. Install Visual Studio 2022 or later (or its Build Tools) with the ASP.NET and web development workload, the .NET 10 SDK, Node.js 24, and Git. Run `git config --system core.longpaths true`.
2. Install SQL Server (Developer edition is fine) on the default instance, with the account the runner runs as in the sysadmin role.
3. In the repository's Settings > Actions > Runners, add a self-hosted Windows runner with the label `rock-build`, and install it as a service under that account.
4. Set the repository variable that moves the job: `gh variable set PACKAGES_RUNNER -R SparkDevNetwork/rock-sample-data -b '["self-hosted","windows","rock-build"]'`. Delete it to move back.

On a self-hosted runner the workflow skips installing SQL Server. Use a runner only for this private repository: once the repository is public, a self-hosted runner must sit in a runner group limited to this workflow, because it runs whatever code a job gives it.

## Commits

Write commit messages that start with a past-tense verb and say what changed, for example "Added a blended family with half siblings." Keep each commit to one idea.

## Review checklist

- [ ] Follows the [content standards](content-standards.md).
- [ ] `node cast/validate-cast.js` and `node validation/check-standards.js` show 0 errors.
- [ ] For a builder or loader change, the [verify tool](../verify/README.md) passes, and any change to `validation/rock-contract.json` is explained. See [Verification](verification.md).
- [ ] Family pages rebuilt if a Cast file changed.
- [ ] The Core 1 package builds and loads with `--dry-run`.
- [ ] New decisions added to [decisions.md](decisions.md), with placeholder numbers on a branch.
- [ ] [Roadmap](roadmap.md) and [cast/INVENTORY.md](../cast/INVENTORY.md) updated if a gap opened or closed.
