---
name: local-delegate
description: Use BEFORE reading big inputs (logs, build/test output, several or large decompiled files) or writing long routine text (boilerplate, test scaffolds, XML, docs drafts, translations) — route it to the local qwen model via tools/local_llm.py to save Claude tokens.
---

# Delegate to the local model (qwen3.5:9b via Ollama)

Goal: Claude's tokens go to judgement; bulk reading and bulk typing go to the local GPU.
The local model is small (9B): fast and free, but it misreads and invents. Everything it
returns is an **unverified draft**.

## Decide: local or Claude

Delegate (default for these, no need to ask):
- **Reading big text, getting a short answer**: game logs (`rgl_log*.txt`, crash dumps),
  `dotnet build`/`dotnet test` output > ~100 lines, decompiled files > ~300 lines or more
  than 2 files ("which methods/events relate to X", "list signatures of Y").
- **Typing long routine text from a precise spec**: xUnit test scaffolds and test data,
  DTO/record classes, `SubModule.xml`/`.csproj`/XML boilerplate, Gauntlet XML layout drafts,
  XML-doc comments, commit messages and PR descriptions from a diff, first drafts of
  research notes from Claude's bullet facts, RU⇄EN translation of docs.
- **Mechanical transforms**: reformat tables, convert lists to Markdown, rename across a
  given text, extract all `TODO`/`UNSURE` lines.

Keep with Claude (never delegate):
- Decisions, ADRs, architecture, plans, `CURRENT_STATE.md`, final review.
- Anything touching networking/authority/allocation logic, Harmony patches, threading,
  engine lifecycle — correctness matters more than tokens.
- Bannerlord API facts as final truth (the local model may only point where to grep).
- Small tasks: if input < ~100 lines and output < ~40 lines, doing it directly is cheaper
  than delegating + checking.
- License-sensitive material: never send `BannerlordCoop-development/` content (ADR-0004).

## How

```
python tools/local_llm.py --check                                   # once per session
python tools/local_llm.py "<question>" <files...>                   # extract → short answer
<cmd> 2>&1 | python tools/local_llm.py "<question>"                  # pipe output
python tools/local_llm.py --draft --out .local-llm/out/<name> "<spec>" <context files>
python tools/local_llm.py --draft --task-file .local-llm/task.md --out .local-llm/out/<name> <files>
```
- Write a precise spec: exact class/namespace names, target framework, test framework,
  what to cover, output format ("only the C# file", "Markdown table with columns …").
  Long specs go into `.local-llm/task.md` with `--task-file`.
- Give the model only the files it needs. `--draft` sends everything in one request
  (≈16k-token context): keep context files small.
- Use `--out` for drafts so the result does not flow into Claude's context; then read only
  what needs checking.
- If the status line says `WARNING: input hit context limit`, split the input or narrow it.
- If it prints `LOCAL_LLM_UNAVAILABLE`, skip delegation for the session and work normally
  (tell the user once that Ollama is not running).

## Verify (mandatory)

- Extracted facts: confirm every identifier/signature you will rely on with one `rg` hit
  against the source before using it.
- Generated code: read it (or its diff), fix it, then build and run tests. Never commit a
  draft that was not built.
- Generated docs: Claude rewrites anything that states a decision or a verified fact.
- If the draft is poor after one retry with a sharper spec, do the task yourself — do not loop.
- In the final summary mention in one line what was delegated (e.g. "local: log triage,
  test scaffold") so the user sees the savings; `python tools/local_llm.py --stats` shows totals.
