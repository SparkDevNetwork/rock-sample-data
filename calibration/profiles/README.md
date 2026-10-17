# Calibration Profiles

A profile is the set of target numbers the builder aims for. It has two layers.

## Sources (`sources/*.json`)

One file per calibration source, written by `../normalize.js`. Each holds ratios only (per giving unit, per family, per week, per 1,000 active adults). Sources use neutral labels (`source-1`, `source-2`) and are never named or described. The normalizer drops anything that could identify a source (group type names, custom statuses, URLs, campus counts, exact totals). Never edit a source file by hand; rerun the normalizer instead.

## Profiles (`*.json`)

A profile is a short recipe on top of the sources:

1. **blend**: which sources to use, and their weights.
   **sectionBlend** (optional): gives one section its own sources, for example:
   `"sectionBlend": { "distance": [ { "source": "source-2", "weight": 1 } ] }`
2. **excluded**: paths to leave out, each with a reason.
3. **overrides**: values to replace or add, each with a reason. Values with no source are marked "Assumption." An override can also hold coarse, rounded targets that record no source at all.
4. **notes**: anything a reader should know about the targets.

See the effective values with:

```
node ../resolve-profile.js sample-church.json
```

## Adding a source

1. Run the calibration scripts and save the output in `../raw/`.
2. Run `node ../normalize.js source-2 "Calibration source" ../raw/source-2-part-1.tsv ...`.
3. Add `source-2` to a profile's `blend`, and revisit its exclusions and overrides.
4. Rebuild the datasets and run validation.

Never record a source's name, location, size, or story anywhere in the repository.
