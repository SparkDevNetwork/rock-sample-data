# Roadmap

Where the project stands: the team's working list. Update this page whenever something finishes or a new need comes up. For the community-facing list of planned work, see [Planned work](planned.md); keep the two in step when an item starts or finishes.

## Done

- The design spec and the knowledge framework.
- Calibration scripts (parts 1 to 4) that run inside Rock's SQL editor, the normalizer, and profile v1.
- The church setup as data: 7 campuses, Sunday service times, funds with campus accounts, check-in rooms, and connection statuses.
- The Cast: 38 legacy families converted, plus 45 new families and 4 new businesses, for 87 households and 185 people. Featured families have a bio for each person, and every family has a page.
- The address pool: 18,000 real-street addresses with house numbers that do not exist, and the Cast moved onto it.
- The Phase 0 builder: configuration, people, Sunday attendance and kids check-in, giving history, a generated crowd, and validation. Core 1 loads in about 4 seconds. Full 7 loads in about 23 seconds (about 34 with check images) and passes 89% of measured values.
- Photo rules, photo ethnicity for every Cast adult, and the image agent's brief.
- All 45 legacy Cast portraits have been updated and approved. The temporary photo review is closed.
- Every Cast photo is delivered: 130 new people and the 45 legacy redos (175 photos), plus the 120-photo Core 3 pool. All are in `photos/manifest.json`.
- Photos load into Rock: every Cast person's delivered photo shows on their profile, and in Core 3 about half of the generated households get faces from the pool, one photo ethnicity per family, with no face repeated on a campus (decision 30).
- Small groups: the pattern in [Small groups](small-groups.md) (decisions 33 to 40). The Cast has 8 active groups, 2 inactive, and 2 archived, planned into `cast/groups`; generated households get groups per campus at load time. Groups load with members, meeting places, attributes, history, Group Finder test cases, and weekly attendance on the church year.
- Scanned check images for every check gift: five fictional banks (each with its own logo and paper color), a wallet per giver so card, bank, and check details agree, mailed business checks, bundled open fonts, and check scanner matching fields. Stored as Rock transaction images (decisions 25 to 27).
- Combined and separate giving: most couples give as one giving unit, about 10% give separately, and adult children at home give on their own (decision 42).
- Giving realism: check writers follow age and gift size, onsite givers catch up after a missed Sunday, amounts vary the way real gifts do, schedules change now and then, givers come and go, and the largest givers give stock now and then. Giving passes 191 of 196 measured values (decision 43).
- Giving analytics: Rock's giving attributes, bins and percentiles, journey stages with history, and three alert types with 12 months of alerts (decisions 44 and 45). Cast giving expectations are checked on every load (decision 46), and a version guard checks Rock on GitHub for changes to the code the loader ports (decision 47).
- The builder and loader split: the [builder](../builder/README.md) writes one package per dataset (format 1: data only, a checksum for every file, byte-identical when rebuilt), and the [loader](../loader/README.md) library and CLI install packages. Each package is for one exact Rock, checked by its newest migration (decision 52). Loads through packages matched the old direct loads exactly for all three datasets.
- Kids check-in streaks and achievements: one Kids Check-in streak type, seven achievement types from High Five to Century Club, and each child's streak and attempts the way Rock computes them. Next-gen check-in shows them (decisions 57 to 61). Cast achievement expectations are checked on every load, the version guard covers the ported code, and a parity test runs Rock's own code on loaded data. Core 1 and Full 7 match Rock exactly.
- Group photos: each of the Cast's 12 small groups has a photo of its real members, made from their profile photos from the [group photo brief](../photos/group-brief.md). Every dataset sets them as the groups' photos, and Group Finder shows them on its cards (decisions 56 and 62).
- The [verify tool](../verify/README.md) (decision 66): a trusted base made from the Rock release by its migrations and start-up steps, a contract recorded from real loads, invariants, Rock's own Rock Cleanup job as a referee, the content standards as checks, and a receipt per package with a publishing gate.
- Businesses at every campus: at least 6 on the main campus (all from the Cast) and at least 2 at each other campus, or the measured share of giving units when that is more. Every business has one or two contacts, loaded both ways as Rock known relationships, a work address, and gifts in the last 12 months. Every load checks all of it. Full 7 has 87 businesses, and both measured business values pass (decisions 63 to 65).
- Every package passes the verify tool. The loader now sets what Rock's save hooks and Rock Cleanup would: primary aliases, age classification, family salutations, inactive families, an alternate Id and name metaphones for every person, and a Known Relationship group for every person (see [How people load](../loader/README.md#how-people-load)). The trusted base also runs Rock's first-night post-install job, without which Rock Cleanup blanks every age.

## In progress

1. **Prayer requests (Phase 1).** The profile has prayer targets: volume and seasonality from source-1, categories, request length, timing, and who asks from source-2, and a designed prayer team (decision 67). Calibration part 5 measures prayer in detail. Next: weights for Rock's own prayer categories and prayer settings in configuration, text pools, the generator with prayer team activity, and Cast requests that cover each prayer screen state.

## Waiting on a decision

Nothing right now. (Tempe was confirmed as the seventh campus on 2026-10-16; see decision 19.)

## Next

1. **Serving (Phase 1).** Add serving teams (kids, guest services, worship, students) with schedules, and generate serving check-ins. This fills the adult check-in gap in validation.
2. **Close validation gaps.** First-time visitors run low and teens run low.
3. **Run the giving parity test** against a running Rock: load Full 7, run Rock's Giving Automation job, and compare (see [Validation](../validation/README.md#rock-version-guard)).
4. **Per-person attendance,** so a teen can attend more often than their parents.
5. **Real Crowd names** from public-domain frequency data by birth decade.

## To do (parked)

Ideas we agreed on but set aside for later. Move an item into "Next" when we pick it up.

### Small groups

1. **Women's groups.** Add women's small groups led by women, with a Women's Bible Study topic. Alisha Marble's existing group becomes one of them; until then it is not part of the small group design.
2. **Leader requirements.** Use Rock's group requirements (for example, a background check for leaders), with a few leaders whose check is missing or expired so Rock shows the warning.

### People

1. **The Cast's other known relationships.** Some Cast people list `can-check-in` and `allow-check-in-by` relationships. Only `business` loads so far.

### Packages and loaders

The builder and loader are split (see Done). What is left:

1. **The loader inside Rock.** A Rock block that calls the same library, saving configuration through Rock's services instead of SQL. It needs:
   - A build of the library for the .NET Framework version Rock runs on.
   - Progress reported through a logger instead of the console.
   - The server's own PasswordKey and DataEncryptionKey for the check scanner fields.
2. **Download a package by version** in the loader CLI (`--version 1.2.0 --dataset core-1`), once packages are hosted (see Publishing).
3. **Publish the loader to NuGet when we tag a release.** In the same tag-triggered GitHub Action as the Azure upload:
   - `dotnet pack` the loader library with the version taken from the tag, then `dotnet nuget push` to nuget.org (not GitHub Packages, which needs a sign-in even to download).
   - Sign in with nuget.org trusted publishing (OpenID Connect, no stored key), or a push-only API key limited to this package, with an expiry date, stored as an Actions secret.
   - Tags like `v1.3.0-beta.1` publish as NuGet prereleases. Include Source Link and symbols for debugging.
   - Dataset packages record the loader version that built them; the package format keeps its own version so a loader can tell what it can read.

### Publishing

1. **Publish packages to Azure Storage when we tag a release.** A GitHub Action runs on a version tag (or a manual run from `main`), builds one package per dataset, and uploads them to the sample data container behind `storage.rockrms.com`. Pieces:
   - The builder already writes the packages (`dotnet run -- --rock <Rock checkout>` in `builder/`). The Action runs it once per supported Rock release, since each package is for one exact Rock (decision 52).
   - Then it runs the [verify tool](../verify/README.md) on the same built Rock checkout, and `--check-receipts` before any upload (decision 66). The runner needs Windows, SQL Server, and a built Rock, so it is a self-hosted runner, or a Windows runner that builds Rock and starts SQL Server first. Upload each receipt beside its package.
   - Azure sign-in with OpenID Connect (preferred: the repository gets "Storage Blob Data Contributor" on that one container, so no secret is stored), or a SAS token limited to that container, write-only, with an expiry date, stored as an Actions secret.
   - Versioned paths (for example `sampledata/v1.2.0/core-1.zip`), plus an `index.json` uploaded last with a short cache time, listing each package, its checksum, and the Rock version and migration it is for.
   - Publish only from tags or a manual run on `main`, optionally behind a GitHub environment that needs approval.
   - First, confirm that `storage.rockrms.com` is an Azure blob container (or a CDN in front of one) and which container holds sample data.
2. **Get the repository ready to be public.** Squash the history to one commit before the first push. The code and data use the Rock Community License (decided 2026-10-16).

## Later (Phase 1 and beyond)

Registrations, prayer requests, communications, connection requests, steps, learning (LMS), benevolence, reminders, notes, and person attributes for the Crowd.

## Open questions

These come from the [design spec](design-spec.md).

1. Where dataset packages are hosted (the legacy data lived on `storage.rockrms.com`).
2. Whether the Rock loader replaces the inside of the legacy `SampleDataManager` or sits beside it.
3. Whether photos are stored in the database or through a file storage provider.
4. The final meaning of each connection status in the journey.
