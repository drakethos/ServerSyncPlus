# Design notes: why ServerSyncPlus is built this way

> Research and design record from the spike that became ServerSyncPlus 0.9.0. Names in this document were later finalized as: package `ServerSyncPlus`, assembly `ServerSyncPlus`, GUID `DrakeMods.ServerSyncPlus`. For usage, see the [README](../README.md).

**Goal:** ship ServerSync once, as a Hexium dependency, instead of every mod ILRepacking its own copy. It must not crash when other mods in the same profile still embed theirs.

**Status (0.9.0):** builds against real Valheim reference assemblies in CI; unit tests cover the game-independent logic. **Not yet run in-game**: see the [Test plan](#5-test-plan).

---

## 1. What already exists (Hexium, 19 Nov 2026)

Source: the Hexium package index (`/api/experimental/package-index/`, 2,612 packages) plus package READMEs.

There is **no standalone ServerSync package on Hexium.** These are the closest matches:

| Package | What it is | Relevance |
|---|---|---|
| `shudnal-ConditionalConfigSync` 1.0.11 | Standalone sync library with **its own API** (ownership modes, policy files, protected locking). It refuses to run if embedded. **218 dependents.** | Proves the "shared dependency" model works on Hexium, and is the main competitor. It is not a drop-in replacement: mods have to rewrite their sync code. |
| `DrakeMods-DrakeModsLibs` 1.0.6 | DrakeMods' shared library. It ILRepacks ServerSync *internally* and exposes a wrapper (`CreateConfigSync`/`BindSynced`). 7 dependents. | A workaround: consumers depend on the lib, but ServerSync is still embedded inside it. |
| `Manke-ServerConfigSync` 0.1.4 | Pushes *any* mod's `.cfg` from the server, with no API. | Different problem (mods with no sync at all). |
| `Dreich-ServerSyncTimeout` 1.0.0 | Patches ServerSync's hard-coded **30 s** config-send timeout. | Shows demand for a configurable timeout (added in this spike). |
| Thunderstore `DrakeMods-ServerSync` 1.0.0 (Jul 2024, **deprecated**, 6,470 downloads) | Raw upstream ServerSync DLL plus a small `ServerSync.Plugin`. | This matches the earlier attempt you described: a wrapped raw ServerSync DLL. |

### How common embedding is
I scanned the DLLs of a random sample of **300 Hexium packages**:

- **68 (23 %) carry an embedded ServerSync.** Across the whole catalogue that would be roughly 600 mods.
- **23 of those 68 copies are not internalized.** The ServerSync types stay `public` inside the host mod's DLL.
- **1 mod (`KorCaptain-PotalMap`) ships a loose `ServerSync.dll`.** Its identity is `ServerSync, Version=1.0.0.0`, the same as the deprecated standalone package.
- String fingerprints show **at least 4 different ServerSync builds** in circulation.

## 2. Why the raw-DLL approach crashed

I could not reproduce this without the game, so these causes come from reading the code and inspecting the DLLs. Most likely first:

1. **Assembly identity collision.** Upstream builds `AssemblyName=ServerSync` and never moves `AssemblyVersion` off `1.0.0.0`. Every loose copy, the deprecated package included, claims the identical identity `ServerSync, Version=1.0.0.0`. Mono/BepInEx bind a reference to the first assembly loaded under that name. Your mod can therefore end up running against another mod's older or newer `ServerSync.dll`. That fails with `MissingMethodException` or `TypeLoadException` during `Awake` or on connect, depending on which members differ.
2. **A frozen snapshot.** The 2024 package predates Valheim 1.0 and later upstream fixes (PlayFab socket handling, config-save fixes). ServerSync patches `ZNet.RPC_PeerInfo` and wraps the connection socket. A stale copy in that spot breaks the handshake for **every** mod, which looks like a fatal crash rather than one mod failing.
3. **Not the cause: internalized copies running side by side.** Upstream is built for that case:
   - RPC names are per mod (`"<guid> ConfigSync"`).
   - The shared `"ServerSync VersionCheck"` RPC chains to whichever copy registered first.
   - The login buffering keys its state by `Assembly.GetExecutingAssembly()`.

   So a shared copy can live safely next to embedded copies, **as long as it is a distinct assembly and speaks the same wire protocol.**

➡️ **If you still have the BepInEx `LogOutput.log` from the crash, it would confirm (1) or (2) quickly.** Look for `MissingMethodException`, `TypeLoadException` or `HarmonyException` mentioning ServerSync.

## 3. Spike design

| Decision | Why |
|---|---|
| **Assembly named `ServerSyncPlus`**, with `AssemblyVersion` bumped every release | Removes collision (1) completely. No loose or embedded copy can be mistaken for it. |
| **Namespace stays `ServerSync`**, public API unchanged | Migration needs **no code changes**: swap the reference, drop the ILRepack line, add `[BepInDependency]`. |
| **Real BepInEx plugin** (`ServerSyncPlusPlugin`, GUID `DrakeMods.ServerSyncPlus`) | Consumers can declare `[BepInDependency]`. BepInEx then loads us first and refuses to load the consumer if we're missing. Mod managers install us automatically. |
| **Wire protocol identical to upstream** (vendored source, minimal marked patches) | Keeps us interoperable with the ~600 embedded copies and with older clients and servers. |
| **No throwing on misuse** (`PlusGuard`) | If someone ILRepacks the shared DLL, log an error and carry on working as an ordinary embedded copy. ConditionalConfigSync refuses outright; for a drop-in library, degrading is safer. |
| **Copy report at startup** (`CopyDiagnostics`) | Logs every ServerSync copy in the process: shared, embedded (public or internalized) and loose. It warns when two assemblies are named `ServerSync`. Support questions get answered from one log. |
| **Upstream tracking** | `src/Upstream/ConfigSync.cs` is upstream `blaxxun-boop/ServerSync@c57c2aa`. Every local edit carries a `// [shared]` marker, so `git diff` against upstream stays at about 10 lines (see commit history). |

### Improvements already in the spike
- **Configurable send timeout:** `Network / Config send timeout (seconds)`, default 30 as upstream, range 10–600. This makes `Dreich-ServerSyncTimeout` unnecessary for consumers of the shared copy.
- **Tolerant version parsing:** upstream `new System.Version("1.2.3-beta")` throws inside the network handler. `PlusVersion.Parse` strips pre-release and build suffixes and falls back to `0.0.0` with a warning.
- **Diagnostics:** the copy report and the missing-`[BepInDependency]` warning described above.

### Further improvements (not done; ranked)
1. **CI upstream diff:** a job that rebases the `[shared]` patches onto new upstream commits and fails loudly if the protocol-relevant code changed.
2. **One login pass for all shared consumers:** each embedded copy wraps the socket and buffers separately. Every consumer migrated to the shared copy is one fewer wrapper and one fewer coroutine at join. That's already true by construction; measure it.
3. **Reference-only NuGet package** (or a `Hexium dependency string` snippet plus a `.props` file) so the migration is literally two lines.
4. **Clearer join errors:** upstream already lists failed version checks. Add *which package to install* (the Hexium dependency string) to the message.
5. **Do not add** new wire fields such as a protocol version. Embedded copies would misread them, and that is exactly the compatibility we depend on.

### Identity: what is ours vs. what must match upstream
| Ours (fork identity) | Must stay identical to upstream (wire/API compatibility) |
|---|---|
| Plugin GUID and Harmony ID `DrakeMods.ServerSyncPlus` | Namespace `ServerSync` and all public type and member names |
| Assembly `ServerSyncPlus` (version bumped every release) | RPC `"ServerSync VersionCheck"` |
| Package name, icon, manifest | RPC `"<mod guid> ConfigSync"` and the ZPackage payload format |

Never reuse `blaxxun-boop.serversync`, the GUID of the deprecated 2024 package. BepInEx loads only one plugin per GUID and skips the other.

### Known risks and open questions
- **Decided: keep namespace `ServerSync`** (no code changes for mod authors). Safe because the CLR identifies a type by *assembly + full name*, and our assembly name is unique.
- **Manager helpers lose sync silently.** blaxxun's ItemManager, PieceManager and SkillManager (and similar helpers) find ServerSync with `Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync")`, which only searches the mod's own DLL. Once a mod stops embedding ServerSync, those helpers find nothing and their own settings (recipes, piece costs, skill gains) quietly stop syncing. There's no crash. In the 300-mod sample, 11 mods do this lookup. Fix options:
  - (a) **Implemented in 0.9.0** (`src/Plus/ManagerCompat.cs`): Harmony postfix on `Assembly.GetType(string, bool, bool)` that returns ServerSyncPlus's type when the lookup came back null for a `ServerSync.*` name and the calling DLL references `ServerSyncPlus`. Needs an in-game check.
  - (b) Still worth doing: a pull request to the manager projects to fall back to `Type.GetType("ServerSync.ConfigSync, ServerSyncPlus")`.
- **Public `ServerSync.*` names are duplicated** by the 23 % of embedded copies that aren't internalized. That's harmless at runtime because the types live in different assemblies. However, `AccessTools.TypeByName("ServerSync.ConfigSync")` picks whichever copy it finds first, and a consumer that *compiles* against such a mod's DLL gets CS0433. **Decided:** keep `ServerSync` (zero code changes beats this edge case).
- `PlusGuard` uses `Assembly.GetCallingAssembly()` in the `ConfigSync` constructor, which is only used for the report. If inlining makes it misreport, switch to `StackFrame`.
- **Positioning against ConditionalConfigSync:** CCS asks mods to adopt a new API. This package's pitch is *zero-code migration for existing ServerSync users.* It's worth talking to the ServerSync maintainers (blaxxun / MSchmoecker fork) and shudnal before publishing, to avoid a third competing sync library. The upstream licence is MIT (© 2021 Tykea, kept in `LICENSE-upstream-ServerSync.txt`).

## 4. Layout

```
src/
  ServerSyncPlus.csproj      AssemblyName=ServerSyncPlus; game refs from -p:GamePath
  Upstream/ConfigSync.cs     upstream source; every local edit is marked // [plus]
  Plus/                      everything ServerSyncPlus adds (partial classes, plugin, features)
  Plus/Pure/                 game-independent logic, unit-tested in tests/
tests/                       xunit tests (dotnet test tests)
consumer-example/            a mod using ServerSyncPlus as a dependency
package/                     manifest.json + icon.png for the Hexium zip
branding/                    procedural icon source (make_icon.py -> icon.svg -> render_icon.js)
.github/                     CI, release-and-publish workflow, packaging scripts
```

## 5. Test plan

This needs a Valheim client and a dedicated server. Use one profile per row and check the BepInEx log copy report, config sync, locking, and admin exemption.

| # | Profile | Expect |
|---|---|---|
| A | ServerSyncPlus + example consumer | Configs sync. The copy report shows ServerSyncPlus. |
| B | A + a mod with an **internalized** embedded copy (most Azumatt/Smoothbrain mods) | Both mods sync independently. No errors. |
| C | A + a mod with a **public** embedded copy (e.g. `xtavim-BetterConsumables`) | Same as B. The report marks it public. |
| D | A + `KorCaptain-PotalMap` (loose `ServerSync.dll`) | Same as B. The report shows LOOSE. The consumer is unaffected. |
| E | **Control:** `DrakeMods-ServerSync` 1.0.0 + PotalMap, no shared copy | Reproduce the original failure and capture the log. |
| F | Consumer on the server only, or the client's consumer older than `MinimumRequiredVersion` | A clean version-mismatch message on join, not a crash. |
| G | A config over 250 KB (forces fragmentation) on a throttled link, with the timeout at 30 and then at 300 | Disconnects at 30 s; succeeds at 300 s. |
| H | A consumer that wrongly ILRepacks `ServerSyncPlus.dll` | An error in the log, and sync still works. |
| I | A mod using ItemManager/PieceManager, switched to ServerSyncPlus | Its recipe/piece settings still sync (manager compatibility patch). |
| J | Forced sync: server lists a mod without sync; edit its .cfg on the server while a player is connected | Player gets the server value on join and again after the edit; their own .cfg is untouched. |
| K | Forced sync with `Allow server to force settings = false` on the client | Player keeps local values. |
| L | `SyncedFile` in the example consumer; edit the YAML on the server | Clients receive the new content; it reverts to the local file after disconnecting. |
| M | `ssp`, `ssp copies`, `ssp reload` in the F5 console | Output matches the log; reload pushes changed values to players. |

Rows **B–E** are what the spike has to answer. If B–D pass and E fails, the identity-collision explanation is confirmed and the design holds.
