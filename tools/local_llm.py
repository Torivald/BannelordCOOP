#!/usr/bin/env python3
"""Ask the local Ollama model to process large text so Claude reads only the short result.

Usage (from repo root, Windows):
  python tools/local_llm.py "Summarise public methods related to agent control" _decompiled/1.4.8/X/Agent.cs
  type big.log | python tools/local_llm.py "List distinct errors with first timestamp"
  python tools/local_llm.py --check            # verify Ollama is reachable and model exists

Env:
  LOCAL_LLM_MODEL  (default: qwen3.5:9b)   LOCAL_LLM_URL (default: http://localhost:11434)
  LOCAL_LLM_CHUNK  characters per chunk (default: 12000)

Output is a draft from a small model: Claude must verify any fact before relying on it.
Stdlib only, no installs required.
"""
import json
import os
import sys
import urllib.error
import urllib.request

MODEL = os.environ.get("LOCAL_LLM_MODEL", "qwen3.5:9b")
URL = os.environ.get("LOCAL_LLM_URL", "http://localhost:11434").rstrip("/")
CHUNK = int(os.environ.get("LOCAL_LLM_CHUNK", "12000"))


def call(prompt: str) -> str:
    body = json.dumps({"model": MODEL, "prompt": prompt, "stream": False,
                       "options": {"temperature": 0.1}}).encode("utf-8")
    req = urllib.request.Request(URL + "/api/generate", data=body,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=600) as r:
        return json.loads(r.read().decode("utf-8")).get("response", "").strip()


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
    print("OK")
    return 0


def main() -> int:
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    if args[0] == "--check":
        return check()
    task, files = args[0], args[1:]
    text = ""
    if files:
        for f in files:
            with open(f, encoding="utf-8", errors="replace") as fh:
                text += f"\n===== {f} =====\n" + fh.read()
    elif not sys.stdin.isatty():
        text = sys.stdin.read()
    chunks = [text[i:i + CHUNK] for i in range(0, len(text), CHUNK)] or [""]
    parts = []
    for n, c in enumerate(chunks, 1):
        p = (f"Task: {task}\nAnswer concisely. Quote exact identifiers. If unsure, say so.\n"
             f"Input part {n}/{len(chunks)}:\n{c}")
        parts.append(call(p))
    if len(parts) > 1:
        merged = "\n\n".join(f"[part {i}] {t}" for i, t in enumerate(parts, 1))
        print(call(f"Task: {task}\nMerge these partial answers into one concise answer, "
                   f"remove duplicates, keep exact identifiers:\n{merged}"))
    else:
        print(parts[0])
    return 0


if __name__ == "__main__":
    sys.exit(main())
