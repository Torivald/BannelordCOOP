# ADR-0001: Start Without External MCP Servers

## Decision

Start the project with zero external MCP servers.

## Reasoning

Claude Code already provides the core local workflow needed for the initial phases:
- filesystem access
- shell commands
- Git
- project file editing
- repository inspection

Adding MCP servers before a demonstrated need increases configuration and failure surface.

## Reconsider when

Add an MCP only if a concrete repeated workflow justifies it, for example:
- a database/service must be queried
- an external issue tracker must be managed
- a remote documentation/source system is repeatedly queried
- a specialized tool provides capabilities unavailable through normal CLI/web workflows

## Memory

Do not add a Memory MCP initially.

Durable project memory should live in:
- `CLAUDE.md`
- `CURRENT_STATE.md`
- architecture docs
- ADRs
- research notes
- Git history

Claude's native memory/auto-memory features may supplement this but must not be the sole source of project state.
