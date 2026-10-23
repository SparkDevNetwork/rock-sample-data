# Datasets

One manifest per dataset: [core-1.json](core-1.json), [core-3.json](core-3.json), and [full-7.json](full-7.json). The builder reads its settings from the manifest, and command-line options override them.

| Dataset | People | Campuses | Photos ([decision 30](../docs/decisions.md)) | Check images |
|---|---|---|---|---|
| Core 1 | Cast | 1 | Every Cast person | Every check |
| Core 3 | Cast plus 75 companion households each at Peoria and Gilbert | 3 | Cast, plus about half of companions | Every check |
| Full 7 | Cast plus the Crowd: 16,800 generated households across all seven campuses, the main campus included ([decision D-full7-crowd-1](../docs/decisions.md)) | 7 | Cast only | Last 12 months |

## Manifest settings

| Setting | Meaning |
|---|---|
| `key` | The dataset key, as passed to `--dataset`. |
| `name` | The display name. |
| `generatedHouseholds.perCampus` | Generated households to add at each campus, keyed by campus key. A campus not listed gets none, and every key must be one of the dataset's campuses. `--crowd` replaces it ([decision 49](../docs/decisions.md)). |
| `photos.generatedHouseholdPercent` | Percent of each campus's generated households that get faces from the photo pool. 0 or missing means none. |
| `checkImages.months` | Months of check gifts, counting back from the anchor date, that get scanned check images. 0 means every check. Used only when the builder runs with `--check-images`; `--check-image-months` overrides it. |

The manifest grows as the builder reads more settings from it. Campuses are listed per dataset in `../configuration/campuses.json`.

## The dataset card

Rock's installer shows a card for each dataset, so a church admin can choose one (decision 124). The loader embeds these fields and serves them as `SampleDataDatasets` (see the [loader README](../loader/README.md#the-dataset-catalog)). Every field is required, and the builder refuses a manifest that is missing one.

| Field | Meaning |
|---|---|
| `summary` | One or two plain sentences: who is in the dataset. |
| `bestFor` | One plain sentence: what the dataset is best for. |
| `highlights` | Two to four short phrases. |
| `people`, `households` | People and family households (businesses included) a load adds, measured from a real load at the default seed and 3 years of history. Verify fails a load more than 2% away from either. |
| `campuses` | The dataset's campuses. Must match `../configuration/campuses.json`. |
| `typicalInstallSeconds` | The median of three measured loads of every part, check images included, in whole seconds. |

Write the text for a church admin choosing what to install. Follow the [content standards](../docs/content-standards.md) and the writing rules in [AGENTS.md](../AGENTS.md): plain, short sentences and no em dashes. Leave out numbers the number fields already give, so the text never goes stale.
