# Content standards

Every piece of data in this repository follows these rules. Read them before you add anything. A reviewer will check them.

## Everything is fictional

1. **People, churches, and organizations are fictional.** Employers and businesses use invented names, for example Sonoran Systems or Cholla Creek Hardware. Never name a real church.
2. **Phones use the reserved range** 555-0100 to 555-0199, with Phoenix-area codes (602, 480, 623). Each person's number is unique unless the story says it is shared (for example, a couple's home phone, or duplicate records).
3. **Emails use `example.org`.** Staff of the sample church use `rocksolidchurchdemo.com`.
4. **Addresses use real streets with house numbers that do not exist.** No made-up number may match a real address on any street with that name in that ZIP code. Only `addresses/build-pool.js` makes addresses.

## Real data never enters

5. **Never commit raw data from a real church.** Calibration output stays in the git-ignored `calibration/raw/`. Only anonymous, normalized ratios belong in `calibration/profiles/`.
6. **Never name or describe a calibration source** anywhere: files, comments, commit messages, or issues. Sources are labeled `source-1`, `source-2`, and so on. Do not mention a source's size, location, or setup.

## People and photos

7. **Names come from common, public-domain name frequency data.** Pick names first, independent of anything else.
8. **Photo ethnicity exists only to generate matching family photos.** It lives in `photo.ethnicity`, never goes into Rock, and never decides a name.
9. **People photos look like casual profile pictures** in varied indoor and outdoor settings, never studio portraits. No new face may resemble a real or famous person; the 45 legacy portraits are the one exception (rule 10).
10. **Legacy characters stay recognizable.** The 45 legacy Cast portraits were redone once (decision 32), using the original photos as references so long-time Rock users still recognize each face. They are never regenerated again.

## Text

11. **Realistic, warm, and appropriate for any audience.** No graphic content, no political content, and no mockery of any group.
12. **Sensitive stories are handled gently.** Loss, divorce, financial hardship, and background check issues exist in the Cast because churches handle them. Describe them plainly and kindly.
13. **Scripture quotes use public-domain translations only.**
14. **Text pools use many short, varied entries** rather than a few long ones, so repeats are not noticeable.

## How each rule is checked

A rule a script can check is a check, not a reminder. Run `node cast/validate-cast.js` and `node validation/check-standards.js` before you open a pull request. The [verify tool](../verify/README.md) runs both, and also checks the loaded data in a real Rock, before any package is published.

| Rule | Checked by |
|---|---|
| 1. Fictional people and organizations | A reviewer. |
| 2. Reserved phone numbers | `check-standards.js` (the Cast, including when a number may be shared) and `validation/invariants.sql` (every loaded phone, generated households too). |
| 3. Reserved email domains | `validate-cast.js`, `check-standards.js` (every data file), and `validation/invariants.sql` (every loaded email). |
| 4. Addresses with house numbers that do not exist | `addresses/build-pool.js`, the only thing that makes addresses. It needs the county data in `addresses/raw/`, so it runs when the pool is rebuilt. |
| 5. No raw church data in git | `check-standards.js` (the raw folders hold only their README and `.gitignore`). |
| 6. Calibration sources never named or described | `check-standards.js` (sources are `source-N`, and every description is exactly "Calibration source"). A reviewer reads everything else. |
| 7. Names from public frequency data | A reviewer. |
| 8. Photo ethnicity never goes into Rock | `validate-cast.js` (no race or ethnicity attributes), the verify tool's contract (the loader never writes `Person.RaceValueId` or `Person.EthnicityValueId`), and `validation/invariants.sql`. |
| 9 and 10. Photos | `validate-cast.js` (every photo is a 400 by 400 JPEG that matches its person) and a reviewer (how it looks). |
| 11 to 14. Text | A reviewer. |

## Why these rules exist

The data ships with Rock, which is open source, and is shown in demos, documentation, and screenshots. A real name, a real phone number, or a real home in sample data could reach a real person. Churches shared calibration data in confidence, so nothing about them may be traceable.
