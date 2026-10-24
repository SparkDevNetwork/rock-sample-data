# Release

What the release workflow, `.github/workflows/release.yml`, reads and runs. How to publish, the one-time setup, every release so far, and what to do when a run fails are in [Publishing a release](../docs/maintaining.md#publishing-a-release). Agents also follow the release rules in [AGENTS.md](../AGENTS.md#releases).

| File | What it is |
|---|---|
| `rock-releases.json` | One Rock release for each supported Rock major release, its newest dot release, with the tag or commit to check out from SparkDevNetwork/Rock (decisions 52 and 127). |
| `merge-index.js` | Merges one release's verified packages into the hosted `index.json` (decision 128). Each new entry carries its dataset's card from `datasets/*.json` (decision 144), its Rock major release, and its minimum loader version (decision D-rock-major-1). It replaces the hosted entry for the same dataset and major, and refuses two packages for one dataset and major. |
| `check-minimum-loader.js` | Refuses a release version below `SampleDataPackage.MinimumLoaderVersion`, since that release's loader would refuse its own packages (decision D-rock-major-1). The workflow runs it before a publishing run builds anything. |

## Trying the index merge

`merge-index.js` needs only Node. Give it a folder with one `rock-{major}` folder per Rock major release, such as `rock-21`, each holding packages and their receipts, the way the workflow lays them out:

```
node release/merge-index.js --packages <folder> --version 1.0.0 --out index.json
```

Add `--index <hosted index.json>` to merge into an index that is already published, and `--commit <hash>` to refuse receipts for packages built from another commit. It refuses a receipt that did not pass, and a version the hosted index already lists with other bytes.
