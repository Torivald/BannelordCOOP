#!/usr/bin/env python3
"""Offload token-heavy sub-tasks to the local Ollama model (default qwen3.5:9b).

Claude pays tokens for everything it reads and writes. This script lets the local model
read big inputs or write long drafts, so Claude only reads a short answer or a file it
then checks. Rules for WHEN to use it: .claude/skills/local-delegate/SKILL.md.

Usage (from repo root, Windows):
  python tools/local_llm.py --check
  python tools/local_llm.py "List distinct errors with first timestamp and count" rgl_log.txt
  type build.log | python tools/local_llm.py "Which projects failed and why?"
  python tools/local_llm.py --draft --out .local-llm/out/Tests.cs "Write xUnit tests for ..." src/X.cs
  python tools/local_llm.py --task-file .local-llm/task.md --out .local-llm/out/result.md file1 file2
  python tools/local_llm.py --stats          # how much work the local model has absorbed

Modes:
  default  summarise/extract: input is chunked, each chunk answered, partial answers merged.
  --draft  generate: whole input in one request (must fit in context), output is a draft
           (code, docs, tests, translations). Use with --out.

Flags:
  --out FILE     write the answer to FILE and print only a one-line status (saves Claude tokens)
  --task-file F  read the task text from a file (long instructions)
  --think        enable the model's reasoning (slower; default off)

Env:
  LOCAL_LLM_MODEL (qwen3.5:9b)  LOCAL_LLM_URL (http://localhost:11434)
  LOCAL_LLM_CTX   context tokens (16384)  LOCAL_LLM_CHUNK characters per chunk (30000)

Output comes from a small model: it is an UNVERIFIED DRAFT. Claude must check identifiers
with grep, build/test generated code, and never base decisions on it alone.
Stdlib only, no installs required.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

MODEL = os.environ.get("LOCAL_LLM_MODEL", "qwen3.5:9b")
URL = os.environ.get("LOCAL_LLM_URL", "http://localhost:11434").rstrip("/")
CTX = int(os.environ.get("LOCAL_LLM_CTX", "16384"))
CHUNK = int(os.environ.get("LOCAL_LLM_CHUNK", "30000"))
LOG = os.path.join(".local-llm", "usage.jsonl")

SYSTEM = (
    "You are a precise assistant helping on a C# Mount & Blade II Bannerlord mod. "
    "Answer only what is asked, no preamble, no closing remarks. Quote identifiers, file "
    "names and error text exactly as they appear in the input. Never invent APIs, types, "
    "methods or facts that are not in the input; if something is missing or you are unsure, "
    "write 'UNSURE: <reason>'. When asked for code, output only the code."
)

STATS = {"calls": 0, "in": 0, "out": 0, "sec": 0.0, "truncated": False}


def call(prompt: str, think: bool) -> str:
    body = json.dumps({
        "model": MODEL, "system": SYSTEM, "prompt": prompt, "stream": False,
        "think": think, "keep_alive": "15m",
        "options": {"temperature": 0.1, "num_ctx": CTX},
    }).encode("utf-8")
    req = urllib.request.Request(URL + "/api/generate", data=body,
                                 headers={"Content-Type": "application/json"})
    t = time.time()
    with urllib.request.urlopen(req, timeout=1800) as r:
        data = json.loads(r.read().decode("utf-8"))
    pin, pout = data.get("prompt_eval_count", 0), data.get("eval_count", 0)
    STATS["calls"] += 1
    STATS["in"] += pin
    STATS["out"] += pout
    STATS["sec"] += time.time() - t
    if pin >= CTX - 16:
        STATS["truncated"] = True
    return data.get("response", "").strip()


def check() -> int:
    try:
        with urllib.request.urlopen(URL + "/api/tags", timeout=10) as r:
            names = [m["name"] for m in json.loads(r.read().decode("utf-8")).get("models", [])]
    except (urllib.error.URLError, OSError) as e:
        print(f"Ollama not reachable at {URL}: {e}")
        return 1
    print("Models:", ", ".join(names) or "(none)")
    if MODEL not in names:
        print(f"Model '{MODEL}' not pulled. Run: ollama pull {MODEL}")
        return 1
    print(f"OK {MODEL} ctx={CTX}")
    return 0


def stats() -> int:
    if not os.path.exists(LOG):
        print("No local LLM usage logged yet.")
        return 0
    n = tin = tout = 0
    sec = 0.0
    with open(LOG, encoding="utf-8") as fh:
        for line in fh:
            try:
                d = json.loads(line)
            except ValueError:
                continue
            n += 1
            tin += d.get("in", 0)
            tout += d.get("out", 0)
            sec += d.get("sec", 0)
    print(f"{n} runs, local tokens in={tin} out={tout}, {sec / 60:.1f} min on GPU")
    return 0


def log_usage(task: str, mode: str, out_path: str) -> None:
    try:
        os.makedirs(os.path.dirname(LOG), exist_ok=True)
        with open(LOG, "a", encoding="utf-8") as fh:
            fh.write(json.dumps({
                "ts": time.strftime("%Y-%m-%d %H:%M:%S"), "mode": mode, "model": MODEL,
                "task": task[:120], "calls": STATS["calls"], "in": STATS["in"],
                "out": STATS["out"], "sec": round(STATS["sec"], 1),
                "truncated": STATS["truncated"], "out_file": out_path,
            }, ensure_ascii=False) + "\n")
    except OSError:
        pass


def main() -> int:
    for stream in (sys.stdout, sys.stdin):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    if args[0] == "--check":
        return check()
    if args[0] == "--stats":
        return stats()

    draft = think = False
    out_path = task = None
    rest = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--draft":
            draft = True
        elif a == "--think":
            think = True
        elif a == "--out":
            i += 1
            out_path = args[i]
        elif a == "--task-file":
            i += 1
            with open(args[i], encoding="utf-8") as fh:
                task = fh.read()
        else:
            rest.append(a)
        i += 1
    if task is None:
        if not rest:
            print("No task given. See --help.")
            return 2
        task, rest = rest[0], rest[1:]

    text = ""
    for f in rest:
        with open(f, encoding="utf-8", errors="replace") as fh:
            text += f"\n===== FILE: {f} =====\n" + fh.read() + "\n===== END FILE =====\n"
    if not rest and not sys.stdin.isatty():
        text = sys.stdin.read()

    try:
        if draft:
            answer = call(f"Task:\n{task}\n\nInput:\n{text}" if text else f"Task:\n{task}", think)
        else:
            chunks = [text[j:j + CHUNK] for j in range(0, len(text), CHUNK)] or [""]
            parts = [call(f"Task: {task}\nInput part {n}/{len(chunks)}:\n{c}", think)
                     for n, c in enumerate(chunks, 1)]
            if len(parts) > 1:
                merged = "\n\n".join(f"[part {k}] {t}" for k, t in enumerate(parts, 1))
                answer = call(f"Task: {task}\nMerge these partial answers into one concise "
                              f"answer, remove duplicates, keep exact identifiers:\n{merged}", think)
            else:
                answer = parts[0]
    except (urllib.error.URLError, OSError) as e:
        print(f"LOCAL_LLM_UNAVAILABLE: {e}. Do the task without the local model.")
        return 3

    log_usage(task, "draft" if draft else "extract", out_path or "")
    warn = " WARNING: input hit context limit, result may be incomplete" if STATS["truncated"] else ""
    status = (f"[local {MODEL}: {STATS['calls']} call(s), in={STATS['in']} out={STATS['out']} tok, "
              f"{STATS['sec']:.0f}s{warn}]")
    if out_path:
        d = os.path.dirname(out_path)
        if d:
            os.makedirs(d, exist_ok=True)
        with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(answer + "\n")
        print(f"{status} -> {out_path} ({len(answer.splitlines())} lines)")
    else:
        print(answer)
        print(status)
    return 0


if __name__ == "__main__":
    sys.exit(main())
