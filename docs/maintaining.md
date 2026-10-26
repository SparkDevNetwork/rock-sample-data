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

### The coordinator

With more than two or three sessions running, following each one is hard. The person can name one session the coordinator and talk only to it. Every other session, the integrator included, sends the coordinator its questions and status (rule 19 in [AGENTS.md](../AGENTS.md#the-coordinator)).

In the Claude desktop app, the coordinator starts the other sessions itself: the integrator first, then a feature session for each piece of work, each briefed with the README's fallback prompt and told who its coordinator is. The person pastes one prompt, the README's coordinator prompt. Sessions the coordinator starts report to it automatically when they finish a turn. A session started some other way can still report to the coordinator by name. A terminal session cannot start other sessions, so in a terminal the person starts the integrator and the feature sessions with the fallback prompts.

The coordinator:

1. **Answers what is already settled.** If the decision log, AGENTS.md, or the person's earlier direction covers a question, it answers the session itself. Technical calls with clear evidence, such as how a release fix should work, are settled the same way.
2. **Brings real choices to the person.** Product choices and changes to the release plan go to the person, numbered, each with one recommendation. The coordinator forwards the answer and quotes the person's words.
3. **Keeps the plan.** It tracks what is in flight, what each release waits on, and what is waiting on the person. It tells sessions when something they wait on lands.
4. **Restarts stalled work.** It nudges sessions that stop on errors or usage limits.

The coordinator cannot:

- approve tool permission prompts in another session's window
- approve a GitHub deployment, such as a release's Publish job
- stand in for the person's yes on a tag, a push to another repository, a squash, or a history change. The session doing it asks the person directly, after telling the coordinator.

Keep the coordinator and the integrator in separate sessions. The integrator spends long stretches in verify runs, and the coordinator must stay free to answer. On a quiet day, one session can be both.

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
- If packages start to need loader code that older loaders lack, such as a new part, a new configuration file, or a new field a part reads, raise `SampleDataPackage.MinimumLoaderVersion` to the release that will ship the change, and the loader project's `Version` with it (decision 158). A Rock keeps the loader it shipped with, so without this an older Rock would download a package it cannot load.
- A newer loader must still read every published package (decision 168). Make a new field in a model the loader reads nullable or give it a default, and treat a missing value as "off". `loader-compat-check` opens the configuration of every published release saved in `loader-compat-check/published/`, so a change that breaks one fails there before a release.
- Run validation on Full 7 before and after your change. A change should not lower the share of values within tolerance without a written reason. See [Validation](../validation/README.md).

## Publishing a release

The release workflow, `.github/workflows/release.yml`, publishes everything (decisions 127 to 130). It has four jobs:

| Job | Where | What it does |
|---|---|---|
| Plan | Linux | Reads the version from the tag or the run's inputs, and the Rock releases from `release/rock-releases.json`, with each one's major release. Refuses two releases for one major, and, for a run that publishes, a version below the packages' minimum loader version (`release/check-minimum-loader.js`). |
| Packages for Rock X | Windows, once per Rock major release | Checks out that major's Rock release, builds it with Visual Studio's MSBuild, starts SQL Server, writes every dataset's package, runs the verify tool, and runs the publishing gate. Saves the packages and receipts as an artifact. |
| Loader NuGet package | Windows | Runs `loader-compat-check` for both builds, then packs the loader (`SparkDevNetwork.Rock.SampleData.Loader`) with the release version, Source Link, symbols, the license, and the README. |
| Publish | Windows, in the `release` environment | Runs the gate again, pushes the loader to nuget.org, uploads the packages and receipts, then merges and uploads the index last. |

A run without "publish" stops after the first three jobs. That is a dry run.

### Release a version

1. Make sure `main` has what you want to ship, and that `release/rock-releases.json` lists every Rock major release to support, each at its newest dot release.
2. Do a dry run first: in GitHub, open Actions > Release > Run workflow, pick `main`, and leave "publish" off.
3. Tag `main` and push the tag:

   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. Approve the Publish job when GitHub asks.
5. When the run published packages, save that release's configuration for the loader compatibility check, on a branch: make `loader-compat-check/published/<tag>/configuration/`, fill it with `git show <tag>:configuration/<file>` for each JSON file there, and add a check for it in `loader-compat-check/Program.cs` like the one for `v0.1.0-alpha.10`.

A tag with a label, such as `v1.1.0-beta.1`, is a prerelease. NuGet lists the loader as a prerelease, and only `packages/index-prerelease.json` changes (decision 129). To try one in Rock, point the installer block at `https://storage.rockrms.com/sampledata/packages/index-prerelease.json`.

A manual run can also publish: run it on `main`, give a version, and tick "publish". Use this when a tag's run failed for a reason outside the repository. A run that failed partway can be run again: files already uploaded with the same bytes are skipped, and a version already on nuget.org is skipped.

A tag never moves. If a run fails for a reason in the repository, fix it on a branch, land it on `main`, and tag the next version. Delete the failed tag only when nothing was published under it: check nuget.org and `storage.rockrms.com/sampledata/packages/v{version}/` first.

Before a stable tag, read what it will ship: the cards in `datasets/*.json` become the index entries Rock shows, every card field included (decision 144), so their text must be current and their numbers must match what each package loads.

### Releases so far

Add a row for every tag, including failed ones.

| Tag | Date | Kind | What happened |
|---|---|---|---|
| `v0.1.0-alpha.9` | 2026-10-23 | Prerelease | Nothing published. The Loader job's symbols check failed at random (see "When a release fails"). The tag was deleted. |
| `v0.1.0-alpha.10` | 2026-10-23 | Prerelease | Published `SparkDevNetwork.Rock.SampleData.Loader` 0.1.0-alpha.10 to nuget.org, and Core 1, Core 3, and Full 7 for Rock 21.0.2 with `index-prerelease.json`. The first Publish attempt failed at the Azure sign-in (AADSTS700213); the re-run after the trust fix passed. Full 7 was still the Cast only. |
| `v0.1.0-alpha.11` | 2026-10-23 | Prerelease | Nothing published. Verify failed rock-cleanup on all three packages because the run crossed midnight UTC mid-verify (see "When a release fails"); the re-run was cancelled so the next tag would carry the fix and later work. |

`packages/index.json`, which Rock's installer block reads by default, stays empty until the first stable tag. The next one planned is `v1.0.0`, the first release with Full 7's Crowd (decision 136).

### When a release fails

Open the failed run in Actions, find the first failed step, and match it here.

| Failed step | What it means | What to do |
|---|---|---|
| Any job, before it starts: "recent account payments have failed or your spending limit needs to be increased" | The organization's Actions billing is blocked. This only matters if the repository is private. | An organization owner fixes billing. Then re-run. |
| Build Rock: App_Code errors such as `The type or namespace name 'Ical' could not be found` | RockWeb is a website, so MSBuild's restore and a solution restore both skip its `packages.config`, and a command-line build cannot resolve its netstandard2.0 DLLs (MSB3268). The workflow's "Restore RockWeb's packages" and "Copy RockWeb's referenced DLLs into Bin" steps handle both. | Check that those two steps ran. A new Rock may add a `.refresh` file the copy step skips as "not restored"; restore that package too. |
| Check SQL Server sign-in, or the trusted base | SQL Server did not start, or the runner's account is not a sysadmin. | Re-run once. If it fails again, read the "Start SQL Server" step's log. |
| Verify the packages: a load "Could not finish" with "Could not allocate a new page ... the 'PRIMARY' filegroup is full due to lack of storage space" | The runner's disk filled up. A verify run holds the trusted base, its backup, each loaded copy (Full 7 loads in one transaction, so its log grows large), and the Rock Cleanup snapshot. Full 7 outgrew C: in the v0.1.0-alpha.13 dry run, so the "Keep SQL Server's files on D" step moves new databases, backups, and tempdb to `D:\sqldata`. | Read the "Free space after verify" step. If D: is near full too, the datasets have outgrown a standard runner: use a larger runner or a self-hosted one (see "A self-hosted runner instead"). |
| Verify the packages, or the publishing gate | A package failed a check on this Rock. | Treat it like any verify failure: see [Verification](verification.md) and the [verify README](../verify/README.md#when-a-check-fails). Fix it on a branch and tag the next version. |
| Verify the packages: rock-cleanup changed `ScheduleDate`, one row more than in a fresh database (for example 11,080 against 11,079) | Before 2026-10-26, a schedule occurrence at the edge of Rock Cleanup's moving window (now plus or minus two years) fell inside one run and outside the other. The referee now leaves out a day at each edge. | If it comes back, check that `MovingWindowEdges` in `verify/CleanupReferee.cs` still covers the table, then re-run. |
| Verify the packages, or the trusted base: "Rock's update job N failed" | One of Rock's one-time update jobs failed while the trusted base was built (decision 170). The verify tool saves the whole error, with Rock's stack trace, in a `rock-sample-data-runner-*-startup.log` in the temp folder. | Read the stack trace. A job that needs something only a web server provides (as the Lava engine and the `.ascx` block types did) needs the runner to set it up first; see `verify/rock-runner/Program.cs`. |
| Verify the packages: rock-cleanup changed `Person.Age`, `AgeBracket`, daily counts, or a tithe metric on every package | The run crossed midnight on the database server's clock between the loads and Rock Cleanup (v0.1.0-alpha.11, which started after 5pm Arizona time on a UTC runner). Since decision 152, the runner keeps Arizona time and verify says "Rock Cleanup was not judged" instead. | "Re-run failed jobs" once the date has settled. Avoid starting a release in the hour before midnight Arizona time. |
| Check the package: "symbols have no Source Link to this commit" | Before 2026-10-23, piping `tar` into `grep -q` failed at random. The check now extracts each `.pdb` to a file first. | If it comes back, check that the step still extracts to a file, then re-run. |
| Sign in to nuget.org | The trusted publishing policy does not match: owner `SparkDevelopmentNetwork`, repository `SparkDevNetwork/rock-sample-data`, workflow `release.yml`, environment `release`, package `SparkDevNetwork.Rock.SampleData.Loader`, and `NUGET_USER` the user who made the policy. | Fix the policy or the variable, then "Re-run failed jobs". |
| Sign in to Azure: `AADSTS700213: No matching federated identity record` | The app's trust record does not match GitHub's token subject. This repository uses the immutable subject with IDs. | Set the trust as in step 2 of the one-time setup, then "Re-run failed jobs". |
| Upload the packages and receipts: "already published with other bytes" | This version is already on storage with different files, so the run would change a published package. | Never overwrite it. Tag the next version. |
| Read the version and the Rock releases: "is below SampleDataPackage.MinimumLoaderVersion" | The tag is older than the loader the packages need, so its loader would refuse its own packages (decision 158). Nothing was built. | Delete the tag, since nothing was published, and tag the minimum version or later. |
| Read the version and the Rock releases: "two Rock releases for one major release" | `release/rock-releases.json` lists two dot releases of one major. Each major gets one package set (decision 52). | Keep only that major's newest dot release, on a branch, and run again. |

A re-run skips what is already published, so "Re-run failed jobs" is safe after any fix outside the repository. It needs one approval again.

### Add or change a Rock release

A package serves every dot release of one Rock major release (decision 52), so `release/rock-releases.json` has one entry per major. Each entry is that major's newest dot release, a Rock version such as `21.0.2`, and the `ref` to check out from SparkDevNetwork/Rock: a release tag, or a full commit hash before Rock tags the release. The packages are built and verified on it and published under `rock-{major}`. The workflow stops if the ref's `Directory.Build.props` names a different version. When Rock publishes the tag for a pinned commit, change the `ref` to the tag.

Today it lists Rock 21 (21.0.2, a develop commit) and Rock 20 (20.0.10, a release-20.0 commit), so each release has two Packages jobs, run side by side.

When Rock ships a new dot release of a supported major:

1. Change that major's entry to the new version and ref, on a branch.
2. Build the new Rock and run the verify tool against it, as the dry run does.
3. If every package passes, land the change. Churches on the new dot release already get the published packages, so no release is needed. The next release builds on the new dot release.
4. If a check fails, fix it on the branch and release a new version. Its packages replace that major's in the index.

To support a new major release, add an entry for it. Its packages appear in the next release beside the other majors'.

### Where files go

Everything goes in the `sampledata` container, which `storage.rockrms.com` serves through Azure Front Door.

| Path | What it is | Cache |
|---|---|---|
| `packages/v1.0.0/rock-21/core-1.zip` | A package, for every Rock 21 release. Never changes. Releases up to `v0.1.0-alpha.12` used the exact Rock version, such as `rock-21.0.2`. | One year |
| `packages/v1.0.0/rock-21/core-1.verify.json` | Its receipt. Never changes. | One year |
| `packages/index.json` | The index Rock reads (format in the [loader README](../loader/README.md#the-package-index)). | 5 minutes |
| `packages/index-prerelease.json` | The stable index plus the newest prerelease. | 5 minutes |

`release/merge-index.js` builds the index from the receipts and `datasets/*.json`. You can run it yourself on a folder of packages to see what a release would publish.

### One-time setup

The workflow needs the following before it can publish. A dry run needs only the first item.

#### 1. GitHub Actions minutes

The repository has been public since 2026-10-22, so jobs on GitHub-hosted runners cost no Actions minutes. While it was private, each job used the organization's minutes, and GitHub refused to start any job on 2026-10-21 until an organization owner fixed billing in Settings > Billing and plans. If the repository becomes private again, it needs a budget for Actions: a dry run uses about 70 minutes of Windows time for each Rock release, plus a few minutes for the loader.

#### 2. The Azure identity

This was done on 2026-10-22, as the app registration "rock-sample-data release". Run it again only to rebuild the identity. Use the Bash version in Azure Cloud Shell or Git Bash, or the PowerShell version below it in a Windows terminal. Fill in the first four values.

The trust must match the subject in GitHub's token exactly. This repository uses GitHub's immutable subject, which carries the organization's and the repository's numeric IDs: `repo:SparkDevNetwork@548191/rock-sample-data@1422820354:environment:release`. The IDs never change, even if the repository is renamed. A trust for the older `repo:SparkDevNetwork/rock-sample-data:environment:release` fails with `AADSTS700213: No matching federated identity record`, which is what stopped the first Publish job on 2026-10-23. Read the prefix with `gh api repos/SparkDevNetwork/rock-sample-data/actions/oidc/customization/sub -q .sub_claim_prefix`, as both versions below do.

```bash
TENANT=<tenant id>
SUBSCRIPTION=<subscription id>
RESOURCE_GROUP=<resource group of the storage account>
ACCOUNT=<storage account behind storage.rockrms.com>
SUBJECT_PREFIX=$(gh api repos/SparkDevNetwork/rock-sample-data/actions/oidc/customization/sub -q .sub_claim_prefix)

az login --tenant "$TENANT"
az account set --subscription "$SUBSCRIPTION"

# The app registration the workflow signs in as.
APP_ID=$(az ad app create --display-name "rock-sample-data release" --query appId -o tsv)
PRINCIPAL_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)

# Trust GitHub's tokens only for jobs in the release environment of this repository.
cat > credential.json <<EOF
{
  "name": "rock-sample-data-release-environment-ids",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "$SUBJECT_PREFIX:environment:release",
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

The same steps in PowerShell. PowerShell cannot run the Bash lines above: `NAME=$(...)` is not PowerShell, so nothing is created.

```powershell
$tenant = "<tenant id>"
$subscription = "<subscription id>"
$resourceGroup = "<resource group of the storage account>"
$account = "<storage account behind storage.rockrms.com>"
$subjectPrefix = gh api repos/SparkDevNetwork/rock-sample-data/actions/oidc/customization/sub -q .sub_claim_prefix

az login --tenant $tenant
az account set --subscription $subscription

$appId = az ad app create --display-name "rock-sample-data release" --query appId -o tsv
$principalId = az ad sp create --id $appId --query id -o tsv

@"
{
  "name": "rock-sample-data-release-environment-ids",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "${subjectPrefix}:environment:release",
  "description": "The Publish job of .github/workflows/release.yml",
  "audiences": ["api://AzureADTokenExchange"]
}
"@ | Set-Content -Encoding ascii credential.json
az ad app federated-credential create --id $appId --parameters credential.json

$scope = (az storage account show -n $account -g $resourceGroup --query id -o tsv) + "/blobServices/default/containers/sampledata"
az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role "Storage Blob Data Contributor" --scope $scope

"AZURE_CLIENT_ID = $appId"
```

To check the identity without changing anything, list its trust and its roles. Expect one credential, for `repo:SparkDevNetwork@548191/rock-sample-data@1422820354:environment:release`, and one role on the `sampledata` container:

```bash
APP_ID=$(az ad app list --display-name "rock-sample-data release" --query "[0].appId" -o tsv)
az ad app federated-credential list --id "$APP_ID" --query "[].subject" -o tsv
az role assignment list --assignee "$(az ad sp show --id "$APP_ID" --query id -o tsv)" --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

To check the container: `az storage container-rm show --storage-account <account> -g <resource group> -n sampledata`. It reads through Azure's management API, so it needs no blob role. The identity has no other role, so it cannot read the account's keys, change its settings, or purge Front Door.

Front Door must pass on the blobs' own `Cache-Control` headers, or the index stays cached longer than 5 minutes. If its caching rule overrides them, add a rule for `/sampledata/packages/index*.json` that honors the origin's header.

#### 3. nuget.org trusted publishing

The loader is published as `SparkDevNetwork.Rock.SampleData.Loader`, owned by the `SparkDevelopmentNetwork` organization on nuget.org. The ID comes from `PackageId` in `loader/RockSampleData.Loader.csproj`, and the workflow reads it from there. The assembly and namespace stay `RockSampleData.Loader`.

Sign in to nuget.org as `jonedmiston` (an administrator of `SparkDevelopmentNetwork`), open your user name > Trusted Publishing, and add a GitHub Actions policy:

| Field | Value |
|---|---|
| Package owner | `SparkDevelopmentNetwork` |
| Repository owner | `SparkDevNetwork` |
| Repository | `rock-sample-data` |
| Workflow file | `release.yml` |
| Environment | `release` |

The workflow signs in as the nuget.org user named in `NUGET_USER`, which is `jonedmiston`: the profile name of the user who made the policy, not an email address. If nuget.org marks the policy as temporarily active, publish once before it expires, or make it again.

The organization's verified prefix is `RockRMS.`, so this package shows no verified check mark until `SparkDevNetwork.` is reserved too. To reserve it, ask nuget.org support for a package ID prefix reservation for the organization.

#### 4. The release environment and its variables

This was done on 2026-10-22. The approvers are `jonedmiston`, `cabal95`, and `kylehenning`, and any one of them can approve a publish. An approver must have access to the repository, and an environment takes at most six. To change them, use Settings > Environments > release, or run these again.

An administrator of the repository runs these in Bash with the GitHub CLI and Node. Fill in the approvers' GitHub user names.

```bash
REPOSITORY=SparkDevNetwork/rock-sample-data
APPROVERS="<github-user> <github-user>"

# The environment, its approvers, and a rule that only the deployment policies below may use it.
ids=$(for user in $APPROVERS; do gh api "users/$user" -q .id; done)
node -e 'const ids = process.argv.slice(1).map(Number); console.log(JSON.stringify({
  reviewers: ids.map(id => ({ type: "User", id })),
  prevent_self_review: false,
  deployment_branch_policy: { protected_branches: false, custom_branch_policies: true } }))' $ids \
  | gh api -X PUT "repos/$REPOSITORY/environments/release" --input -

# Only main and version tags may deploy to it.
gh api -X POST "repos/$REPOSITORY/environments/release/deployment-branch-policies" -f name=main -f type=branch
gh api -X POST "repos/$REPOSITORY/environments/release/deployment-branch-policies" -f name='v*' -f type=tag

# The values the Publish job reads. They are not secrets, so they are environment variables.
gh variable set AZURE_CLIENT_ID       -R "$REPOSITORY" --env release -b "<APP_ID from step 2>"
gh variable set AZURE_TENANT_ID       -R "$REPOSITORY" --env release -b "<tenant id>"
gh variable set AZURE_SUBSCRIPTION_ID -R "$REPOSITORY" --env release -b "<subscription id>"
gh variable set AZURE_STORAGE_ACCOUNT -R "$REPOSITORY" --env release -b "<storage account>"
gh variable set NUGET_USER            -R "$REPOSITORY" --env release -b "jonedmiston"
```

Self-review is allowed, so the person who tags a release can approve it too. Set `prevent_self_review` to `true` (or tick "Prevent self-review" in the environment settings) to require a second person.

### A self-hosted runner instead

The Packages job runs on GitHub's `windows-2025` image by default. To run it on your own Windows machine instead, for example to keep the trusted base between runs or when the hosted image cannot build Rock:

1. Install Visual Studio 2022 or later (or its Build Tools) with the ASP.NET and web development workload, the .NET 10 SDK, Node.js 24, and Git. Run `git config --system core.longpaths true`.
2. Install SQL Server (Developer edition is fine) on the default instance, with the account the runner runs as in the sysadmin role.
3. In the repository's Settings > Actions > Runners, add a self-hosted Windows runner with the label `rock-build`, and install it as a service under that account.
4. Set the repository variable that moves the job: `gh variable set PACKAGES_RUNNER -R SparkDevNetwork/rock-sample-data -b '["self-hosted","windows","rock-build"]'`. Delete it to move back.

On a self-hosted runner the workflow skips installing SQL Server. The repository is public, so put the runner in a runner group that only this repository and workflow may use. A self-hosted runner runs whatever code a job gives it, and the workflow never runs on pull requests, but another workflow could.

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
