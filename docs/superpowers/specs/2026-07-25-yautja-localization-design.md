# Yautja Russian Localization Design

## Goal

Localize every Yautja-related Fluent message present in the current English locale and missing from `ru-RU`, without changing game logic, prototypes, assets, or existing Russian translations.

## Scope

- Source of truth: `Resources/Locale/en-US/_CMU14/yautja/yautja.ftl` on `origin/master`.
- Target: `Resources/Locale/ru-RU/_CMU14/yautja/yautja.ftl`.
- Add all 464 currently missing keys, including the two Yautja radio-channel labels.
- Do not add obsolete keys that are no longer present in English.
- Do not modify the unrelated working tree in the original checkout.

## Translation rules

- Preserve every Fluent key ID exactly.
- Preserve all Fluent variables, markup tags, selectors, and escaping.
- Reuse existing Russian terminology in the Yautja locale: `яутжа`, `наруч`, `биомаска`, `маскировка`, `молодая кровь`, `дурная кровь`, `трофей`, and `охота`.
- Keep proper names, radio abbreviations, sound/emote identifiers, and fictional language tokens unchanged unless the existing Russian locale already translates the label.
- Match the English file's section order so future parity reviews remain straightforward.

## Validation

Run a key-parity check over the Yautja locale, a duplicate-key scan, a Fluent variable/markup parity check, and `git diff --check`. If the repository's locale linter is available without restoring new dependencies, run it as an additional check.

## Non-goals

- No C# or YAML changes.
- No changes to the recently merged Hunter Ship or Yautja audio PRs.
- No translation of unrelated generic strings that merely contain the word “hunter”.
