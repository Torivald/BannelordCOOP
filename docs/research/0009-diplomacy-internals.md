# Research 0009 — Diplomacy mod internals (for ADR-0008 compatibility)

## Question
What does Bannerlord.Diplomacy add to the campaign (behaviors, saved data, player actions, patches),
so the co-op host can run it authoritatively and route client UI actions as commands (ADR-0008)?

## Bannerlord version tested
Game v1.4.8.119303; Diplomacy **v1.5.4** (Workshop 2881380744).

## Source
Workshop install folder: `SubModule.xml`, `bin/Win64_Shipping_Client/*.dll` file list, and a **type-name
listing only** (`ilspycmd -l c Bannerlord.Diplomacy.1.4.8.dll`). No method bodies read, no code copied.
License CC BY-NC-SA 4.0 permits reading for compatibility work (ADR-0008); nothing goes into our repo
except type names.

## Date checked
2026-10-18

## Verified facts
- Packaging: one SubModule `Bannerlord.ModuleLoader.Bannerlord.Diplomacy.dll`
  (`SubModuleClassType Bannerlord.ModuleLoader.Bannerlord_Diplomacy`, tag `LoaderFilter =
  Bannerlord.Diplomacy.*.dll`) that loads a **per-game-version build**: `Bannerlord.Diplomacy.1.4.0.dll` …
  `1.4.8.dll`, `1.5.1`…`1.5.4.dll` (+ `ColorMinePortable.dll`). A `1.4.8` build is present.
- Declared dependencies: Bannerlord.Harmony ≥ v2.4.2, ButterLib ≥ v2.11.1, **MCM (Bannerlord.MBOptionScreen)
  ≥ v5.12.3**, UIExtenderEx ≥ v2.13.2; Native/SandBoxCore/Sandbox/StoryMode ≥ v1.4.0.
  **Loads on 1.4.8** (2026-10-18, after Workshop cleanup, MCM v5.13.1 from Nexus): ButterLib log
  "Loading implementation Bannerlord.Diplomacy.1.4.8.dll for game v1.4.8" … "Loaded Diplomacy v1.5.4!";
  game errors log empty. Campaign behaviour not yet exercised.
- 485 types. Campaign behaviors (`Diplomacy.CampaignBehaviors.*`): `CivilWarBehavior`, `CooldownBehavior`,
  `DiplomaticAgreementBehavior`, `ExpansionismBehavior`, `FactionRecruitmentBehavior`,
  `FiefProvenanceBehavior`, `KeepFiefAfterSiegeBehavior`, `KingdomRulerRepairBehavior`,
  `MaintainInfluenceBehavior`, `MessengerBehavior`, `UIBehavior`, `WarExhaustionBehavior`.
- Saved data: `Diplomacy.CustomSavedTypeDefiner` (a `SaveableTypeDefiner`, 0006) → Diplomacy state is in the
  campaign save.
- Player/AI actions (`Diplomacy.Actions.*`, `DiplomaticAction.*`, `CivilWar.Actions.*`): `GiveGoldToClanAction`,
  `GiveGoldToKingdomAction`, `GrantFiefAction`, `ReturnFiefAction`, `SoftlyDestroyKingdomAction`,
  `KingdomPeaceAction`, `FormNonAggressionPactAction`, civil war: `CreateFactionAction`, `JoinFactionAction`,
  `LeaveFactionAction`, `RecruitFactionSupportAction`, `StartRebellionAction`, `ConsolidateKingdomsAction`,
  `ChangeKingdomBannerAction`; factions `RebelFaction`, `SecessionFaction`, `AbdicationFaction`.
- Own events: `Diplomacy.Events.DiplomacyEvents`.
- Harmony patches (`Diplomacy.Patches.*`, own `PatchTools.PatchManager`): `DeclareWarBarterablePatch`,
  `DefaultClanPoliticsModelPatch`, `DefaultDiplomacyModelPatch`, `DefaultEncyclopediaFactionPagePatch`,
  `DiplomaticBartersBehaviorPatch`, `GameMenusCallbacksPatch`, `KingdomDecisionProposalBehaviorPatch`,
  `KingdomManagementVMPatch`, `KingdomPatch`, `MakePeaceKingdomDecisionPatch`,
  `MBBannerEditorGauntletScreenPatch`, `RebelKingdomPatches`, `SupportKingdomQuestPatch`,
  `ViewModelMixin.KingdomManagementScalingPatch`.

## Inferences / hypotheses
- Host-authoritative model (ADR-0010) fits: all 12 behaviors run only on the host; on clients they must be
  inert (suppressed campaign tick, 0002) — but their **Harmony patches still run on clients** and touch
  models/UI; model patches are harmless if clients don't simulate, UI patches are needed for the screens.
- Client commands to intercept (Phase 5g): the action classes above, triggered from Diplomacy's kingdom
  management UI (`KingdomManagementVMPatch`, `UIBehavior`) and messenger flow (`MessengerBehavior`).
  Interception point: prefix on each `*Action.Apply`-style entry → send to host, skip locally.
- Diplomacy's saved data travels with the save (0006), so initial sync needs no special handling;
  incremental state (war exhaustion, cooldowns, pacts) must be in the delta replication or periodic resync.
- Version pinning: co-op handshake must compare Diplomacy version **and** which per-game DLL is active.

## Unknowns
- Exact method signatures of the action classes (next step: targeted `ilspycmd -t <type>` of each action,
  signatures only).
- Whether Diplomacy behaviors use `MBRandom`/time in ways that break if clients' ticks are suppressed.
- ~~Whether it loads on this PC~~ — yes (main menu); campaign not yet tested.

## Implementation impact
- `src/BannerCoopTW.Compat.Diplomacy` (soft dependency) = list of action interceptors + suppression of
  behavior ticks on clients; nothing from Diplomacy is referenced at compile time beyond reflection or a
  compile-only reference.

## License / usage constraints
CC BY-NC-SA 4.0: read for compatibility, **no code copied** into this MIT repo; integrate at runtime only.

## Follow-up tests
1. ~~Confirm Diplomacy loads on 1.4.8~~ done; next: start a campaign with it and check its behaviors run.
2. Signatures of action classes (Phase 5g prep).
