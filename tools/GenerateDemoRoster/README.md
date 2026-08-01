# GenerateDemoRoster

One-off repo tooling (P1T-48) that produces the committed demo dataset at
`api/Infrastructure/Persistence/SeedData/demo-roster.json`: 500 synthetic employees across
ten industry clusters (fintech, gaming, healthtech, e-commerce, embedded, data/ML,
devops/platform, mobile, gov/enterprise, agency), with CV-grade career narratives for
demoing the semantic roster search. It is **not** part of the runtime build path — no api
project references it; a later slice adds the seeder that consumes the JSON.

## How it works

1. **Deterministic assembly** — names, `@demo.example.com` emails (the wipe-tag domain for a
   later cleanup slice), industries, careers, skills (resolvable against the dataset's own
   ~80-skill catalog), qualifications, spoken languages and 0/50/100-style availability
   step-functions are generated from a seeded SplitMix64 PRNG (`--seed`, default 48), so the
   same seed always reproduces the same structure, on any machine.
2. **Narrative prose** — every experience gets a summary + 2–5 achievement bullets from
   hand-authored per-industry career templates (`NarrativeFragments.cs`), combinatorially
   parameterized so no summary text repeats more than 3 times. ~13% of employees are written
   deliberately acronym/product-name-heavy (FIX 4.4, PCI-DSS, HL7/FHIR, Unity ECS, ...).
3. **Optional LLM polish** — when `GEMINI_API_KEY` is set, the tool rewrites the fragment
   prose via the Gemini endpoint (`https://models.github.ai/inference`, model
   `openai/gpt-4o-mini`) in batches of 4 employees. The pass is best-effort: any batch that
   fails, rate-limits or violates the dataset invariants (achievement counts, lengths,
   acronym retention) keeps its offline fragment text. This step is inherently
   non-deterministic; the committed file is the source of truth and is pinned by the
   validation tests in `tests/Application.Tests` (`DemoRosterDatasetTests`).

## Running

```bash
# needs a GitHub token with access to Gemini for the LLM polish pass
export GEMINI_API_KEY=...   # free key: https://aistudio.google.com/apikey

dotnet run --project tools/GenerateDemoRoster              # writes the committed asset path
dotnet run --project tools/GenerateDemoRoster -- --offline # fragments only, fully deterministic
dotnet run --project tools/GenerateDemoRoster -- --count 50 --seed 7 --output /tmp/roster.json
```

Never commit the token anywhere. Without `GEMINI_API_KEY` the tool automatically runs offline.

After regenerating, run the validation suite before committing the file:

```bash
dotnet test tests/Application.Tests --filter DemoRoster
```

## Provenance of the committed file

The committed `demo-roster.json` was produced by the **offline path**: deterministic
assembly with seed 48 over the hand-authored fragment templates (`--offline` output is
bit-for-bit reproducible). The Gemini polish pass was implemented and smoke-verified
end to end, but the full 500-employee enrichment run (~25 minutes of batched calls) did not
survive the generation environment, so the deterministic output was committed instead — it
passes the same variety guards. Rerun with `GEMINI_API_KEY` set to produce an LLM-polished
variant, then re-run the validation tests before committing it.
