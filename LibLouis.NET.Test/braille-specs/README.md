# Upstream braille specs

Copied verbatim from `upstream/liblouis-<version>/tests/braille-specs/`. Re-copy when the upstream
version is bumped; the diff is the set of expectations that changed.

152 specs are here, from liblouis 3.39.0, covering roughly 80 languages, and they check 283,439
cases. Upstream ships 160.

Every spec upstream ships parses here, and every one of them passes. The eight that are left out
are left out for their size alone.

## What is not here, and why

### The eight dictionary harnesses

The four `da-dk-*-dictionary_harness*` specs, `en-ueb-g2-dictionary_harness.yaml`,
`en-us-g2-dictionary_harness.yaml`, `en-g3-dictionary.yaml` and
`hu-hu-g1_dictionary_special_consonants.yaml`: 60MB between them, taking the suite from about 20
seconds to about two and a half minutes per target framework. All eight passed against 3.39.0 when
copied in, so leaving them out costs coverage of the tables' word lists rather than hiding a known
failure. Add them behind a switch if wanted.

### What used to be here: no.yaml and the display table

It was the one spec still excluded for disagreeing, at 299 of 2,191 cases, and the cause was this
harness rather than the wrapper. Its display table is `unicode-without-blank.dis,no-no-g0.utb`,
borrowing display rules the grade 0 translation table still carries. lou_checkyaml compiles the
display table apart from the translation table (`_lou_translate` in tools/brl_checks.c). The public
API cannot: `lou_translate` compiles one table list as both, so this harness's habit of putting the
display table in front of the translation table added the grade 0 table's translation rules to
every table in the file. Its 8-dot section came out with a 6-dot number sign in front of every
digit.

A spec whose display list names anything but a `.dis` file now goes through `_lou_translate` and
`_lou_backTranslate` directly (`BrailleSpecTests.NamesTranslationTable`): `no`, `zh-tw`,
`ms-my-g2` and the two `fr-bfu` specs. They are internal, but exported on every platform the
runtime packages are built for. Every other spec stays on the wrapper, so the specs keep testing it
end to end rather than only liblouis.

`afr-za-g2.yaml` was never copied in, nor counted among the specs left out. It passed when it was.

### What used to be here: eight specs that disagreed on translation output

`bel.yaml`, `de-g0-detailed-specs.yaml`, `de-g1-detailed-specs.yaml`,
`grc-international-composed.yaml`, `pt.yaml`, `ru.yaml`, `uk.yaml` and `zh-tw.yaml` were listed here
with per-case failure counts measured against 3.33.0. All eight pass now, and they run 3,625 cases
between them with 5 entries skipped, so they are not passing by being skipped.

Nothing was done to any of them. The table-resolution fix is the plausible cause — several resolved
to a table from the wrong language entirely, so they were being compared against the wrong
expectations.

The lesson is the one `spaces.yaml` already taught and nobody acted on: **a recorded failure count
is a measurement, not a property.** These sat in this file for several releases describing a
situation that had stopped being true. Re-measure before diagnosing.

### What used to be here: three specs the YAML parser could not read

`de-g2-sbs.yaml`, `en-ueb.yaml` and `it.yaml` were excluded for years of releases because
YamlDotNet refused them, each for a different reason and none of them a fault in the file:

- `de-g2-sbs.yaml` — a multi-line double-quoted scalar, rejected over indentation.
- `en-ueb.yaml` — ran here until 3.38.0, when it began continuing a flow sequence on an unindented
  line (`- [` then `disingenuous, ...]`).
- `it.yaml` — line 83 is `- [-, ⠤]`. A bare `-` is a valid plain scalar in a flow sequence, since it
  is only an indicator when followed by a space.

libyaml, which is what upstream's `lou_checkyaml` parses these with, accepts all three. Switching
the reader to SharpYaml took them. See `ParserExtensions.cs` for the two helpers that move did need.

The general lesson is worth keeping: a spec that fails to *parse* is a fact about the parser, not
about the wrapper or the file, and the reference implementation for these files is libyaml. Measure
a candidate parser against all 160 before believing it is better — VYaml 1.4.0 reads `it.yaml` and
`de-g2-sbs.yaml` but chokes on `en-g3.yaml`, which the other two manage.

### What used to be here: the 27 table-resolution failures

Gone as of the fix in `BrailleSpecTests.TableCache`, recorded because the shape of the mistake is
worth not repeating. The harness indexed `tables/` through an extension allow-list that predated
upstream's `.tbl` files, so all 55 of them were invisible to `lou_indexTables` — and `.tbl` is
exactly where upstream keeps the `#+language:` / `#+grade:` / `#+type:` metadata `lou_findTable`
matches on. 20 queries therefore matched nothing and 7 settled for a worse match from another
language: `language:ar grade:1` resolved to `he-IL.utb`, `language:en region:en-US grade:2` to
`en-ueb-g2.ctb`. liblouis applies no filter of its own — `indexTablePath` in metadata.c hands it
every file on `LOUIS_TABLEPATH` and drops the ones with no metadata — so a filter here could only
ever be wrong in one direction. 25 of the 27 passed as soon as it was removed.

### What used to be here: en-us-g2.yaml and the scope of a flags block

`flags:` used to persist until the next `flags:`, so a bare `tests:` block inherited it. Upstream
spends it on the one block that follows: `lou_checkyaml.c` declares `int testmode = MODE_DEFAULT;`
*inside* the loop over the `flags`/`tests` keys (around line 1138), and `MODE_DEFAULT` is
`MODE_TRANSLATION_FORWARD`. `en-us-g2.yaml` follows a `bothDirections` block with a bare one, so six
of its entries were also run backward — back-translating five identical cells and expecting five
different bullet characters cannot pass in any case. It checks 14 cases now rather than 20, and the
six that went was the whole of its failure.

Only `en-us-g2.yaml` was affected. `vi.yaml` is the one other spec here with a bare `tests:` after a
`flags:` block, and every one of its bare blocks follows `{testmode: forward}`, which is what they
now default to anyway. No `{testmode: hyphenate}` or `{testmode: display}` block is followed by a
bare `tests:`, so no entry that used to be skipped started running: the recorded tally moved only by
the one `inputPos` entry `en-us-g2.yaml` brought with it.

### What used to be here: es-g0-g1.yaml and the escape set

`BrailleSpec.Unescape` took `\xNNNN`, `\\` and `\"` and left every other escape as a literal
backslash plus a letter, so `es-g0-g1.yaml`'s `'\t'`, `'\n'`, `'\r'` and `'\s'` translated to two
cells where one was expected. It now mirrors `parseCharsInternal`
(liblouis/compileTranslationTable.c:1263), which lou_checkyaml reaches through
`_lou_extParseChars` for the input and the expected value of every case: `\e \f \n \r \s \t \v \w`,
`\"` as QUOTESUB, `\\`, and the hex forms `\x` (4 digits), `\y` (5) and `\z` (8), the last two
needing the UCS-4 build that `build_runtime_packages.sh` configures.

Completing the set changed no case that already passed, which is worth knowing because it looks as
though it should have. `\s` appears about two thousand times across these specs, but almost always as
`\\s` inside a single quoted scalar — `' ... \\said ...'` in `en-ueb-computer-code.yaml` — where the
`\\` is consumed as a pair and the `s` is an ordinary letter. The only bare `\s` outside
`es-g0-g1.yaml` is a `display \s 0` rule in the inline tables of `bg.yaml` and `nl-g0_harness.yaml`,
and inline table content is passed to liblouis as written rather than through `Unescape`.

`\u` and `\"` are the mirror image: `\u` is not a liblouis escape at all, and both appear only in
double quoted scalars, where YamlDotNet decodes them before the reader sees them, exactly as libyaml
does for upstream. Neither has ever reached `Unescape`, so dropping `\u` and giving `\"` its
upstream meaning changed nothing either.

### What used to be here: spaces.yaml, which neither fix touched

Listed at 11/1849 among the translation disagreements, and re-checked because completing the escape
set looked likely to change its input. It did not — `spaces.yaml` uses `\x` and nothing else — and
neither did the flags change, since every one of its `tests:` blocks carries its own `flags:`. It
passes with the reader exactly as it was before either fix, which was confirmed by running it
against the previous commit, so the 11 failures were already gone: the table-resolution fix is the
only plausible cause, and 1849 was a 3.33.0-era count against a 1842-case file.

The lesson is about the figures above rather than about the spec: they were measured against an
older liblouis and an older harness, and at least one of them was stale.

## Restoring one

Copy it back from the upstream tarball and run the suite. Note that liblouis caches compiled tables
process-wide, so when bisecting a disagreement, use a cold process per variant — otherwise the
result depends on which test compiled a given table first.

`CopyToOutputDirectory` never deletes, so a `bin/` from an older liblouis keeps tables upstream has
since removed, and those get indexed alongside the current set. If a query resolves to a table that
is not in `LibLouis.NET.Tables/tables`, delete `bin/` and build again.
