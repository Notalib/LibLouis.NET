# Upstream braille specs

Copied verbatim from `upstream/liblouis-<version>/tests/braille-specs/`. Re-copy when the upstream
version is bumped; the diff is the set of expectations that changed.

139 specs are here, from liblouis 3.39.0, covering roughly 75 languages. Upstream ships 160.

Everything upstream has added since the original 3.33.0 selection has now been tried. Eleven of the
thirteen passed as-is and are here. The two that are not are covered below.

## What is not here, and why

### The nine dictionary harnesses

200,000+ cases each, around 800,000 in total. They would dominate every run for little extra signal.
Add them behind a switch if wanted.

`en-g3-dictionary.yaml` is counted here rather than among the untried. It carries no `_harness` in
its name, but at 1.4MB and 40,000 lines it is one of these in everything but spelling.

### 12 specs that do not yet pass

Not excluded because they are wrong — excluded because nobody has worked out yet whether the
disagreement is this harness or the wrapper, and a red suite that is expected to be red is worse
than a smaller green one. They divide into two groups.

**9 disagree on translation output**, a fraction of their cases each. These are the interesting
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
uk.yaml  3/45
zh-tw.yaml  2/20
```

These counts were measured against 3.33.0 and have not all been re-measured since. `spaces.yaml`
was in this group at 11/1849 and passes now, so the rest are worth re-checking before anyone spends
time diagnosing them.

**3 do not parse**, every one of them YamlDotNet 18.1.0 refusing something libyaml accepts, so none
is a wrapper fault and none is fixable here without pre-processing a file that is meant to be
verbatim.

- One has a multi-line double-quoted scalar YamlDotNet rejects.
- `en-ueb.yaml` ran here until 3.38.0: since that release it continues a flow sequence on an
  unindented line (`- [` then `disingenuous, ...]`).
- `it.yaml` line 83 is `- [-, ⠤]`. A bare `-` is a valid plain scalar in a flow sequence — it is
  only an indicator when followed by a space — and libyaml reads it as `["-", "⠤"]`, but YamlDotNet
  throws `SemanticErrorException: Invalid key indicator format`. Worth reporting upstream to
  YamlDotNet; it is the third spec lost to that parser rather than to anything here.

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
