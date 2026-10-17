# Datasets

One manifest per dataset: [core-1.json](core-1.json), [core-3.json](core-3.json), and [full-7.json](full-7.json). The builder reads its settings from the manifest, and command-line options override them.

| Dataset | People | Campuses | Photos ([decision 30](../docs/decisions.md)) | Check images |
|---|---|---|---|---|
| Core 1 | Cast | 1 | Every Cast person | Every check |
| Core 3 | Cast plus 75 companion households each at Peoria and Gilbert | 3 | Cast, plus about half of companions | Every check |
| Full 7 | Cast plus Crowd (about 25,000) | 7 | Cast only | Last 12 months |

## Manifest settings

| Setting | Meaning |
|---|---|
| `key` | The dataset key, as passed to `--dataset`. |
| `name` | The display name. |
| `generatedHouseholds.perCampus` | Generated households to add at each campus, keyed by campus key. A campus not listed gets none, and every key must be one of the dataset's campuses. `--crowd` replaces it ([decision 49](../docs/decisions.md)). |
| `photos.generatedHouseholdPercent` | Percent of each campus's generated households that get faces from the photo pool. 0 or missing means none. |
| `checkImages.months` | Months of check gifts, counting back from the anchor date, that get scanned check images. 0 means every check. Used only when the builder runs with `--check-images`; `--check-image-months` overrides it. |

The manifest grows as the builder reads more settings from it. Campuses are listed per dataset in `../configuration/campuses.json`.
