# Original full-group photos

The first group photos showed every member of each Cast small group: 12 photos of 129 people, made on October 19, 2026 from the [original brief](brief.md) and reported in its [handback](handback.md) (decision 56).

Group photos now show only each group's leader and spouse (decision 196), from the [current group photo brief](../group-brief.md). These full-group photos are kept because they are good photos, but only as downloads on the public site's brand page:

- No package carries them. The builder reads group photos only from `photos/groups/manifest.json`.
- The loader never sets them as a group's photo in Rock.

| Path | What it holds |
|---|---|
| `{group-key}.jpg` | The 1536 by 1024 JPEGs |
| `originals/{group-key}.png` | The image model's full-size originals |
| `manifest.json` | Each photo's group, the members it shows (`people`), and the exact prompt |
