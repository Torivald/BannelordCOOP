---
name: "coop-architecture"
description: "Use when designing or changing an architectural part of the Bannerlord co-op mod (networking, allocation, battle adapters, UI, campaign) or writing an ADR."
---

# Co-op architecture work

1. Read CLAUDE.md, CURRENT_STATE.md, all of docs/decisions and the relevant docs/architecture files.
2. Check ADR-0006 status (custom netcode vs native MP stack). Do not design against an option that is not chosen without saying so.
3. Keep boundaries: Core (pure C# allocation, reservations, state machines; no TaleWorlds refs) / Networking / Battle adapters / UI / Campaign.
4. Authority: clients send requests (ClaimSlot), the authoritative session answers Accepted/Rejected(reason). Never let a client self-assign.
5. For every design cover the edge cases in CLAUDE.md (simultaneous claims, timeouts, disconnects, death, reinforcements, battle end, host loss).
6. Every Bannerlord API the design depends on must be cited from a research note; otherwise mark it as an unverified assumption.
7. Output: an ADR `docs/decisions/NNNN-<title>.md` (Context, Options, Decision, Consequences, To verify) and updates to docs/architecture. Keep it short; diagrams as Mermaid.