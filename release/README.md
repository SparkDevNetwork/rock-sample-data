# Release

What the release workflow, `.github/workflows/release.yml`, reads and runs. How to publish, the one-time setup, every release so far, and what to do when a run fails are in [Publishing a release](../docs/maintaining.md#publishing-a-release). Agents also follow the release rules in [AGENTS.md](../AGENTS.md#releases).

| File | What it is |
|---|---|
| `rock-releases.json` | One Rock release for each supported Rock major release, its newest dot release, with the tag or commit to check out from SparkDevNetwork/Rock (decisions 52 and 127). |
| `merge-index.js` | Merges one release's verified packages into the hosted `index.json` (decision 128). Each new entry carries its dataset's card from `datasets/*.json` (decision 144), its Rock major release, and its minimum loader version (decision 158). It replaces the hosted entry for the same dataset and major, and refuses two packages for one dataset and major. It merges only a hosted index Spark signed, read from its signed bytes, and writes an unsigned index for `sign-index.js`. |
| `sign-index.js` | Signs the index with Spark's key in Azure Key Vault, or a local test key, and checks the signature before it writes the file (decision D-signing-1). `--self-test` checks signing with a throwaway key; the Plan job runs it on every run. |
| `index-signature.js` | The signed index format, shared by the two scripts above. The loader's `packages/IndexSignature.cs` reads the same format. |
| `index-signing-keys.json` | The public keys that may sign the index: the active key and a spare. The loader embeds this file and trusts no other key. See [Signing keys](../docs/maintaining.md#signing-keys). |
| `check-minimum-loader.js` | Refuses a release version below `SampleDataPackage.MinimumLoaderVersion`, since that release's loader would refuse its own packages (decision 158). The workflow runs it before a publishing run builds anything. |

## Trying the index merge

`merge-index.js` needs only Node. Give it a folder with one `rock-{major}` folder per Rock major release, such as `rock-21`, each holding packages and their receipts, the way the workflow lays them out:

```
node release/merge-index.js --packages <folder> --version 1.0.0 --out index.json
```

Add `--index <hosted index.json>` to merge into an index that is already published, and `--commit <hash>` to refuse receipts for packages built from another commit. It refuses a receipt that did not pass, a hosted index that is not signed by a key in `index-signing-keys.json`, and a version the hosted index already lists with other bytes.

## Trying the signature

`sign-index.js` needs only Node. Without the vault, sign with a local test key. The loader never trusts it:

```
node release/sign-index.js --self-test
node release/sign-index.js --new-test-key test-key.pem
node release/sign-index.js --in index.json --out signed.json --test-key test-key.pem
```

The format is in the [loader README](../loader/README.md#the-signature).
