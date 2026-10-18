# Research 0006 — Saves: serialize, transfer to a client, load

## Question
Can the host serialize the campaign to bytes, send it to a joining client and have the client load
exactly that world (initial sync for ADR-0010)? How big is it, and how are mod types saved?

## Bannerlord version tested
v1.4.8.119303, local decompilation (ADR-0005). Save sizes from the dev PC's own saves (older versions).

## Source
`TaleWorlds.SaveSystem.dll` (`ISaveDriver`, `FileDriver`, `InMemDriver`, `SaveManager`, `GameData`,
`SaveableTypeDefiner`), `TaleWorlds.Core.dll` (`MBSaveLoad`), `SandBox.dll` (`SandBoxGameManager`, `SandBoxSaveHelper`).

## Date checked
2026-10-18

## Verified facts
- `MBSaveLoad` (static) uses a pluggable driver: `private static ISaveDriver _saveDriver = new FileDriver()`,
  **`public static void SetSaveDriver(ISaveDriver)`**; `SaveAsCurrentGame(CampaignSaveMetaDataArgs, string,
  Action<(SaveResult,string)>)`, `QuickSaveCurrentGame`, `AutoSaveCurrentGame`,
  `LoadSaveGameData(string saveName) : LoadResult`.
- `ISaveDriver` (public): `Save(string saveName, int version, MetaData, GameData) : Task<SaveResultWithMessage>`,
  `LoadMetaData(string)`, `Load(string) : LoadData`, `GetSaveGameFileInfos()`, `GetSaveGameFileNames()`,
  `Delete`, `IsSaveGameFileExists`, `IsWorkingAsync`.
- `InMemDriver : ISaveDriver` already exists: keeps `metaData + gameData.GetData()` in a `byte[]` and
  loads it back with `GameData.CreateFrom(byte[])` (both public). Its buffer field is private → write
  our own driver with the same logic that exposes the bytes.
- `SaveManager.Save(object target, MetaData, string saveName, ISaveDriver)`,
  `SaveManager.Load(string saveName, ISaveDriver[, bool loadAsLateInitialize]) : LoadResult`.
- Starting a campaign from a `LoadResult`: `new SandBoxGameManager(LoadResult loadedGameResult)`
  (used by `SandBoxSaveHelper.TryLoadSave(SaveGameFileInfo, Action<LoadResult>, Action)`).
- Mod types become saveable through `SaveableTypeDefiner` subclasses (+ `[SaveableField]`/`[SaveableProperty]`).
- Size: the dev PC's campaign saves are **5.7–7.2 MB** each (23 saves, 2024–2025 game versions).

## Inferences / hypotheses
- **Initial world sync is feasible with public API**: host saves with a capturing `ISaveDriver`
  (`MBSaveLoad.SetSaveDriver` temporarily, or `SaveManager.Save` directly), sends ~6–8 MB (compressible)
  to the client over the transport, client loads with `GameData.CreateFrom` → `SaveManager.Load` →
  `SandBoxGameManager(LoadResult)`. Reconnect = same path.
- Loading the same save on all machines is also the most promising way to get **matching MBGUIDs**
  for 0005 H1 (object registration order on load is deterministic for identical modules + save — to verify).
- The save contains the host's "main hero/party"; on clients the main hero must be switched to the
  client's own hero after load (`Campaign.OnPlayerCharacterChanged`, `ChangePlayerCharacterAction`) —
  to research in Phase 5a.
- Mismatched module lists break loading → handshake must compare module list + versions (incl. Diplomacy, ADR-0008).

## Unknowns
- Does `SaveManager.Save` need the game paused/on map (async save while ticking)?
- Time to save+load a 7 MB world (client join latency).
- MBGUID parity after load (spike 1 in 0005).

## Implementation impact
- Phase 5a: "join = receive save bytes + load", no custom world serializer needed.
- Our own state (sessions, player↔hero mapping) is saved through a `SaveableTypeDefiner` with a
  distinct base id.

## License / usage constraints
Signatures only (ADR-0005). Sending a save between players of the same game is the users' own data.

## Follow-up tests
1. Spike: capture driver → bytes → load in instance 2; compare hero/party counts and GUIDs.
