# Session resume — reclaiming player heroes after loading a co-op save

Status: spec (user requirement 2026-10-18). Player-facing description: `docs/GAME_GUIDE.md` §8.
Related: ADR-0007 (product), ADR-0010 (campaign sync), research 0002 (time), 0006 (saves).

## Requirement
After an interrupted session the host loads the co-op save. The campaign stays **paused** until every
connected player has picked the hero they played; a selection window ("Продолжение кампании") is shown.
Heroes of players who did not come back are **controlled by a bot (game AI)**. A late player reclaims
their hero through the same window without pausing the campaign again.

## Saved data
Stored in the campaign save through our `SaveableTypeDefiner` (distinct base id, research 0006):

```
CoopSessionData
  SessionId          : Guid
  PlayerHeroSlots    : List<PlayerHeroSlot>
PlayerHeroSlot
  HeroId             : string   (Hero.StringId)
  OwnerPlayerId      : string   (Steam ID / PlayerId of the last owner)
  LastPlayerName     : string
  Mode               : Player | Bot
```

## Host state machine
```
Loaded ──► WaitingForPlayers ──(host presses Start, all connected players have a hero)──► Running
              │  time: TimeControlMode = Stop, SetTimeControlModeLock(true)   (research 0002)
              │  all PlayerHeroSlots start as Unclaimed
              ▼
Running: unclaimed slots → Mode = Bot; late joiner → claim → Mode = Player (no pause)
```
- On entering `Running`: unlock time, set 1x (ADR-0010 §6).
- The host's own hero is claimed by the host automatically.

## Claims
- Same rules as battle slots (BATTLE_PARTICIPATION): FREE → RESERVED(player, timeout 10–15 s) →
  CONTROLLED(player); one hero per player; host validates atomically. Implemented by the shared Core
  reservation service (Phase 3, `src/BannerCoopTW.Core`), catalog = `PlayerHeroSlots`.
- Pre-selection: a connecting player whose `PlayerId == OwnerPlayerId` gets that hero pre-selected in the
  UI (still requires confirm → claim). Another player may claim any FREE slot.

## Messages (host ⇄ client, ADR-0012 reliable channel)
| Message | Direction | Content |
|---|---|---|
| `ResumeLobbyState` | host → all | slots (hero, status, claimant name), connected players, canStart |
| `ClaimHeroRequest` | client → host | heroId |
| `ClaimHeroResult` | host → client | accepted / rejected(reason) |
| `ReleaseHero` | client → host | heroId |
| `StartCampaign` | host → all | — (enter Running) |

## Bot control (host only)
- Unclaimed player party when entering `Running`: `mobileParty.Ai.EnableAi()` and
  `mobileParty.Ai.SetDoNotMakeNewDecisions(false)` → behaves like an NPC lord's party.
- Player takes over: `mobileParty.Ai.DisableAi()` (or keep AI but stop decisions,
  `SetDoNotMakeNewDecisions(true)`) and route that player's commands to the party.
- API verified to exist in 1.4.8: `TaleWorlds.CampaignSystem.Party.MobilePartyAi`
  (`EnableAi()`, `DisableAi()`, `SetDoNotMakeNewDecisions(bool)`, `IsDisabled`, `DoNotMakeNewDecisions`),
  `MobileParty.Ai`. Behaviour of a former "player" hero under AI (clan/kingdom decisions) — to test.

## Client side
- Client loads the host's save (ADR-0010 §2), shows the window, sends claims.
- After `StartCampaign` (or own claim when late) the client makes its hero the local main hero
  (`ChangePlayerCharacterAction.Apply(Hero)` — to verify, ADR-0010 open question).

## Edge cases
- Two players claim the same hero → first valid claim wins, the other gets `rejected(taken)` + fresh state.
- Claimant disconnects while RESERVED → slot back to FREE; while WaitingForPlayers after confirm → FREE.
- Owner of a Bot hero reconnects during a battle → joins as battle participant/spectator; hero handover
  after the battle (campaign paused anyway).
- Host's save has no `CoopSessionData` (vanilla save) → offered as a new co-op campaign, not resume.
- A 5th player when all 4 slots are taken → join rejected (`no_free_slot`).

## Late join with a new hero (gated by spike S3, may be disabled in MVP)
User decision 2026-10-18: `MaxPlayers = 4`. Free slots (no `PlayerHeroSlot` yet) are shown as
«Свободное место — создать героя», both in the resume window and to players joining a running session.
- Client runs the **vanilla character creation** locally (culture, face/body, name, background choices),
  then sends `CreateHeroRequest { culture, bodyProperties, name, gender, creationChoices }`.
- Host validates a free slot (reservation as for claims) and creates the world objects:
  hero `HeroCreator.CreateSpecialHero(template, bornSettlement, clan, …)`, own new clan `Clan.CreateClan(id)`,
  party `MobilePartyHelper.CreateNewClanMobileParty(hero, clan)` or `SpawnLordParty(hero, settlement)` in a town of
  the chosen culture; applies vanilla start gold/troops/skills according to the creation choices; adds a
  `PlayerHeroSlot { Mode = Player }`; replicates (delta or resync, ADR-0010). Campaign is not paused.
  APIs above exist in 1.4.8 (verified); applying creation choices to a non-main hero is not.
- **Spike S3 (Phase 5a)**: run `CharacterCreationState` (built for the new-game main hero) on a client in a running
  campaign, capture its result without creating a local main hero, and rebuild it on the host.
- **Fallback if S3 fails**: feature disabled — only existing slots can be reclaimed; new players only at the start
  of a new co-op campaign (user accepted this, 2026-10-18).

## Tests
See `docs/testing/TEST_MATRIX.md` → "Session resume".
