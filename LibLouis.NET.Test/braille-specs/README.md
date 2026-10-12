# Upstream braille specs

Copied verbatim from `upstream/liblouis-<version>/tests/braille-specs/`. Re-copy when the upstream
version is bumped; the diff is the set of expectations that changed.

126 specs are here, from liblouis 3.39.0, covering roughly 70 languages. Upstream ships 160.

The selection below was made against 3.33.0. Specs upstream has added since have not been tried yet:
`en-g3-dictionary.yaml`, `en-g3.yaml`, `en-gb-comp8.yaml`, `et_harness.yaml`, `it.yaml`,
`ja-rokutenkanji.yaml`, `lt.yaml`, `mk.yaml`, `ovd.yaml` and `smi.yaml` (3.38.0), and `en-nz.yaml`,
`ht.yaml` and `mi.yaml` (3.39.0).

## What is not here, and why

### The eight dictionary harnesses

200,000+ cases each, around 800,000 in total. They would dominate every run for little extra signal.
Add them behind a switch if wanted.

### 13 specs that do not yet pass

Not excluded because they are wrong — excluded because nobody has worked out yet whether the
disagreement is this harness or the wrapper, and a red suite that is expected to be red is worse
than a smaller green one. They divide into three groups.

**1 fails on a construct this harness reads differently from upstream.** It resolves its tables
correctly and disagrees on a handful of cases; the harness is the one that is wrong.

```
es-g0-g1.yaml  8/992
```

`es-g0-g1.yaml` uses `'\n'`, `'\r'` and `'\s'`. `BrailleSpec.Unescape` handles only `\xNNNN`,
`\yNNNN`, `\uNNNN`, `\\` and `\"`, so those three reach liblouis as a literal backslash plus a
letter and translate to two cells. liblouis's own parser (`parseCharsInternal` in
compileTranslationTable.c, reached from `lou_checkyaml` via `_lou_extParseChars`) also takes
`\e \f \n \r \s \t \v \w` and the 8-digit `\ZNNNNNNNN`. Worth fixing with care: `\s` in particular
is used across the specs, so completing the list changes the input of cases that pass today —
`spaces.yaml`, in the next group, is the obvious one to re-check afterwards.

**10 disagree on translation output**, a fraction of their cases each. These are the interesting
ones: either a per-case option this harness does not model, or a real difference between the wrapper
and upstream.

```
bel.yaml  1/45
de-g0-detailed-specs.yaml  35/476
de-g1-detailed-specs.yaml  35/476
grc-international-composed.yaml  30/432
no.yaml  167/868
pt.yaml  21/806
ru.yaml  39/140
spaces.yaml  11/1849
uk.yaml  3/45
zh-tw.yaml  2/20
```

**2 do not parse.** One has a multi-line double-quoted scalar YamlDotNet rejects. The other is
`en-ueb.yaml`, which ran here until 3.38.0: since that release it continues a flow sequence on an
unindented line (`- [` then `disingenuous, ...]`), which libyaml accepts and YamlDotNet 18.1.0
throws on.

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

## Restoring one

Copy it back from the upstream tarball and run the suite. Note that liblouis caches compiled tables
process-wide, so when bisecting a disagreement, use a cold process per variant — otherwise the
result depends on which test compiled a given table first.

`CopyToOutputDirectory` never deletes, so a `bin/` from an older liblouis keeps tables upstream has
since removed, and those get indexed alongside the current set. If a query resolves to a table that
is not in `LibLouis.NET.Tables/tables`, delete `bin/` and build again.
