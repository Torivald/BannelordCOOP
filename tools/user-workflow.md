# User Workflow — What You Need To Do

## 1. Prepare the machine

Install/verify:
- Mount & Blade II: Bannerlord
- required modding/development tools
- .NET/SDK required by the installed Bannerlord/modding environment
- Visual Studio or another suitable IDE
- Git
- Claude Code

Do not guess versions. Record the exact versions.

## 2. Create the repository

Put the repository in a stable local path.

Initialize Git and keep generated/build files out of source control.

## 3. Give Claude access to the project

Run Claude Code from the repository root.

Claude should first inspect, not immediately code.

## 4. Give Claude the environment-discovery task

Expected result:
- exact game version
- module path
- API/DLL references
- build toolchain
- logs
- minimal module strategy
- two-instance feasibility

## 5. Do not install a pile of MCPs

Start with zero external MCPs.

Add one only when you can name the repeated task it solves.

## 6. Do not build all skills immediately

Start with three workflow skills:
- research
- architecture
- MVP verification

Add more only after repetition is observed.

## 7. Keep durable project knowledge in files

Important decisions must be written into:
- `CURRENT_STATE.md`
- `docs/architecture/`
- `docs/decisions/`
- `docs/research/`
- `docs/testing/`

Chat history is not the source of truth.

## 8. Use small milestones

Do not ask:
"Build the complete Bannerlord co-op mod."

Ask:
"Investigate X, implement Y, test Z, document the result."

## 9. Personal responsibility

You should provide/confirm:
- local game installation
- exact game version
- permission to modify/test the local installation
- test accounts/instances if needed
- hosting/network environment when remote testing begins
- any third-party licenses you intend to rely on

## 10. Expected effort

Rough focused-work estimates:
- environment: 2–5h
- minimal module: 4–12h
- API research: 4–12h
- network harness: 1–3 weeks
- battle participation: 1–3 weeks
- death/reallocation/reinforcements: 1–3 weeks
- campaign MVP: 1–3 months
- mature co-op: several months to 1 year+

These are planning estimates, not guarantees.

## Biggest risk

The difficult part is expected to be synchronizing Bannerlord's campaign/battle state, not configuring Claude, MCP, or skills.
