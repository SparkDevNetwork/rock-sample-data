# Release

What the release workflow, `.github/workflows/release.yml`, reads and runs. How to publish, the one-time setup, every release so far, and what to do when a run fails are in [Publishing a release](../docs/maintaining.md#publishing-a-release). Agents also follow the release rules in [AGENTS.md](../AGENTS.md#releases).

| File | What it is |
|---|---|
| `rock-releases.json` | The Rock releases each release builds packages for, each with the tag or commit to check out from SparkDevNetwork/Rock (decision 127). |
| `merge-index.js` | Merges one release's verified packages into the hosted `index.json` (decision 128). Each new entry carries its dataset's card from `datasets/*.json` (decision D-index-cards-1). |

## Trying the index merge

`merge-index.js` needs only Node. Give it a folder with one `rock-{version}` folder per Rock release, each holding packages and their receipts, the way the workflow lays them out:

```
node release/merge-index.js --packages <folder> --version 1.0.0 --out index.json
```

Add `--index <hosted index.json>` to merge into an index that is already published, and `--commit <hash>` to refuse receipts for packages built from another commit. It refuses a receipt that did not pass, and a version the hosted index already lists with other bytes.
