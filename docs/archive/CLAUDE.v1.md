# Bannerlord Co-op — Claude Code Project Instructions

## Project goal

Build a Mount & Blade II: Bannerlord co-op mod, initially focused on a Battle Participation MVP inspired by Total War-style multiplayer battle participation.

Core battle concept:
1. One player initiates a battle.
2. Other connected players receive a side choice: attacker's side, defender's side, or spectator.
3. After choosing a side, a player chooses a controllable character/entity on that side.
4. Priority for available control slots:
   - Lords / heroes
   - Officers / special units
   - Regular troop classes
   - Spectator if no slot remains
5. Regular soldiers are offered as troop classes, not as an unmanageable list of individual soldiers.
6. Every claimed entity/class is reserved atomically so two players cannot claim the same slot.
7. If a controlled character dies or a player disconnects, the system must define whether the player re-enters allocation or becomes spectator.

## Non-negotiable engineering rules

- Do not assume Bannerlord APIs, hooks, networking libraries, or deterministic simulation behavior. Verify them against the installed game version and official API/docs.
- Do not begin by implementing a full campaign/network architecture.
- First prove that the mod loads, then prove two local instances can communicate, then prove one battle entity can be synchronized.
- Prefer an authoritative battle/session model over peer-to-peer gameplay authority.
- Keep networking, battle allocation, UI, and campaign integration as separate modules.
- Every major architectural assumption must have a written decision/research note.
- Never copy third-party source code unless its license explicitly permits it and the use is compatible with this project. Prefer studying public projects for architecture and behavior.
- Avoid adding MCPs or skills unless they solve a demonstrated problem.
- Use Git from the beginning.
- Keep secrets, machine-specific paths, and credentials out of Git.
- Update CURRENT_STATE.md after meaningful milestones.

## Expected repository structure

/docs/
  architecture/
  decisions/
  research/
  testing/
  status/
  plans/
/src/
  BannerlordCoop/
  BannerlordCoop.Networking/
  BannerlordCoop.Battle/
  BannerlordCoop.UI/
  BannerlordCoop.Campaign/       # only when campaign work starts
/tests/
/tools/
CLAUDE.md
CURRENT_STATE.md
README.md

## Working state

Before coding:
1. Read CLAUDE.md.
2. Read docs/status/CURRENT_STATE.md.
3. Read relevant docs/decisions and research notes.
4. Inspect Git status.
5. Inspect the actual installed Bannerlord/modding environment.

At the end of a task:
1. Summarize changes.
2. Record unresolved assumptions.
3. Record tests actually performed.
4. Update CURRENT_STATE.md if project state changed.
5. Do not claim a test passed unless it was actually run.

## First milestone

The first milestone is NOT networking.

It is environment discovery and a minimal loadable mod:
- exact Bannerlord version
- game installation path
- module/mod installation path
- .NET/SDK/IDE/toolchain
- official API availability
- existing module/DLL references
- log locations
- whether two game instances can be launched locally
- whether the module loads without errors

Only after this is proven should networking work begin.

## Battle Participation specification

Use these conceptual states unless Bannerlord constraints require a change:

Player:
  DISCONNECTED
  CONNECTED
  WAITING
  SIDE_SELECTED
  ALLOCATING
  CONTROLLING
  SPECTATING

Battle entity:
  FREE
  RESERVED
  CONTROLLED
  DEAD
  RELEASED

A reservation should have:
- player ID
- entity/class ID
- creation time
- expiration/timeout

Default reservation timeout: 10–15 seconds, subject to implementation constraints.

Allocation priority:
1. Lord/Hero
2. Officer/Special
3. Regular troop class
4. Spectator

For regular troops, expose something like:
- Archers 1/2
- Crossbowmen 0/3
- Infantry 4/6

The exact UI and class aggregation must be validated against the Mission/Battle API.

Important edge cases to design explicitly:
- simultaneous claims
- player disconnect during reservation
- player disconnect during battle
- controlled character death
- reinforcements
- side eliminated
- battle ends
- player joins after battle start
- insufficient playable entities
- all slots occupied
- host/initiator disconnect
- battle cancellation
- reload/reconnect

## Architecture direction

Preferred flow:

Client input
  -> session validation
  -> authoritative BattleSession
  -> allocation/reservation validation
  -> authoritative battle state
  -> snapshot/delta
  -> clients

Do not make each player authoritative over arbitrary battlefield state.

For the first network prototype, use the smallest possible state:
- session ID
- player ID
- ready state
- one test entity
- ownership/control state

Do not optimize before measuring.

## Token/context discipline

At the beginning of long sessions:
- inspect /context or equivalent context status
- use /usage when token/usage information is available
- use /compact before context becomes unwieldy
- use /clear between unrelated phases when appropriate
- keep durable knowledge in repository docs rather than chat history

Use focused tasks. Do not ask Claude to "build the entire mod" in one prompt.

## Research policy

When researching:
- Prefer official Bannerlord documentation/API for API facts.
- Use source projects only for architectural comparison.
- Record the URL/repository, version/commit if known, license, and what was learned.
- Distinguish verified facts from hypotheses.
- If evidence conflicts, record the conflict instead of silently choosing.

## Definition of done

A task is done only when:
- implementation exists where applicable
- relevant tests/checks were actually run
- documentation is updated
- known limitations are recorded
- Git diff is reviewed
