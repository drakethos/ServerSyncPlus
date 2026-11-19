# ServerSyncPlus

**ServerSync as a shared dependency, plus tools for mod authors and server admins.**

ServerSyncPlus is a public fork of **[ServerSync](https://github.com/blaxxun-boop/ServerSync) by blaxxun**, the config-sync library hundreds of Valheim mods are built on. Instead of every mod bundling its own copy, mods depend on this one package. Mods that still bundle their own ServerSync keep working next to it.

> ### Credit where it's due
> The synchronization engine in this package is **blaxxun's ServerSync** (MIT, © 2021 Tykea). That covers config syncing, locking, admin exemption, version checks, fragmentation, compression and the login buffering. ServerSyncPlus keeps that code nearly as-is in [`src/Upstream/ConfigSync.cs`](src/Upstream/ConfigSync.cs): about ten small marked edits, so upstream fixes merge cleanly. Everything ServerSyncPlus adds lives separately in [`src/Plus/`](src/Plus).
> If you like ServerSync, thank blaxxun first.

---

## For players

Nothing to do. Your mod manager installs ServerSyncPlus automatically when a mod needs it. Keep it installed while any enabled mod depends on it.

ServerSyncPlus:
- does **not** change mods that bundle their own ServerSync; they keep using their own copy
- never overwrites your `.cfg` files with server values; your own values come back when you disconnect
- lets you refuse **forced sync** (server admins forcing settings of mods that have no sync): `Allow server to force settings = false`

## For server admins

Install it on the **server and every client**. The settings are in `BepInEx/config/DrakeMods.ServerSyncPlus.cfg`:

| Section | Setting | Default | What it does |
|---|---|---|---|
| 1 - Network | Config send timeout (seconds) | `30` | How long the server keeps sending configs to a joining player before giving up. Raise it for big modpacks or slow connections (upstream: fixed 30). |
| 2 - Files | Reload config files on change | `true` | Edit a mod's `.cfg` on the server and connected players get the new values immediately. |
| 3 - Forced Sync (server) | Mods | *(empty)* | Plugin GUIDs whose settings the server forces on players, for mods **with no sync of their own**. `*` = every eligible mod. |
| 3 - Forced Sync (server) | Exclude | *(empty)* | Settings never forced: `GUID`, `GUID:Section` or `GUID:Section:Key`, with `*` as a wildcard. |
| 3 - Forced Sync (server) | Sync keybinds | `false` | When off, key bindings stay as each player set them. |
| 4 - Forced Sync (client) | Allow server to force settings | `true` | Players can opt out. |
| 5 - Diagnostics | Log ServerSync copy report | `true` | Lists every ServerSync copy in the game at startup. |

**Forced sync** works on mods that never had server sync, without changing them. Some limits:
- Only settings that ServerSync can transport are eligible: numbers, booleans, text, enums and simple structs. Key-combination settings (`KeyboardShortcut`) never are.
- Settings already synced by ServerSync, Jotunn or Conditional Config Sync are skipped.
- A mod that reads a setting only once at startup won't notice the server's value until the next launch. Test each mod you add.
- Forced settings are always locked. Server admins can still change them from their own config manager.

**Console** (F5): `ssp` shows which mods are synced and whether server values are active. `ssp copies` lists every ServerSync copy in the game. `ssp reload` reloads config files now.

## For mod developers

### Switching from embedded ServerSync: no code changes

Your `ServerSync` code compiles unchanged: same namespace, same classes, same methods.
1. Remove `ServerSync.dll` from your `ILRepack.targets`.
2. Reference `ServerSyncPlus.dll` with `Private="false"`. Never ILRepack it; if you do, it logs an error and falls back to behaving like an embedded copy.
3. Add `[BepInDependency(ServerSyncPlusPlugin.Guid)]` to your plugin class.
4. Add `DrakeMods-ServerSyncPlus-0.9.0` to the `dependencies` in your `manifest.json`.

Players with the old version of your mod (embedded ServerSync) and the new one (ServerSyncPlus) can still play together. The network format is identical.

**ItemManager / PieceManager / SkillManager users:** those helpers look for ServerSync inside your own DLL. ServerSyncPlus answers that lookup for mods that depend on it, so their settings keep syncing after you switch.

### Optional extras (ServerSyncPlus only)

```csharp
private ConfigSync configSync = null!;

private void Awake()
{
    configSync = ConfigSync.ForPlugin(this);                             // Name/DisplayName/versions from your [BepInPlugin]
    Config.BindServerLock(configSync);                                    // the usual "Lock Configuration" setting
    var mult  = Config.BindSynced(configSync, "General", "Multiplier", 2, "Stack multiplier.");   // replaces the copy-pasted config() helper
    var clock = Config.BindSynced(configSync, "Client", "Show clock", true, "Visual only.", synchronized: false);

    var recipes = new SyncedFile(configSync, "recipes", Path.Combine(Paths.ConfigPath, "my.mod.recipes.yml"));
    recipes.Changed += () => Reload(recipes.Content);                     // server content while connected, local file otherwise

    var tables = new SyncedDirectory(configSync, "tables", Path.Combine(Paths.ConfigPath, "my.mod"), "*.yml");
}
```

| API | What it replaces |
|---|---|
| `ConfigSync.ForPlugin(plugin, minimumRequiredVersion, modRequired)` | Hand-filling `Name`, `DisplayName`, `CurrentVersion`, `MinimumRequiredVersion` |
| `Config.BindSynced(...)` / `Config.BindServerLock(...)` | The `config()` wrapper from the ServerSync README. It adds " [Synced with Server]" to descriptions. |
| `SyncedFile` / `SyncedDirectory` | `CustomSyncedValue<string>` + `FileSystemWatcher` + reload boilerplate for YAML/JSON files |
| *(automatic)* config file watching | The `.cfg` watcher most mods copy around |

Using these ties your mod to ServerSyncPlus; everything else stays portable to embedded ServerSync.

A complete example mod is in [`consumer-example/`](consumer-example).

## Compatibility

| Next to… | Result |
|---|---|
| Mods that bundle ServerSync (hidden or public) | Coexist. Each copy handles its own mods. |
| Loose `ServerSync.dll` files, and the deprecated 2024 `DrakeMods-ServerSync` | No conflict with ServerSyncPlus: it is a different DLL (`ServerSyncPlus.dll`). Remove the 2024 package anyway; it predates Valheim 1.0. |
| Jotunn, Conditional Config Sync | Independent; forced sync skips what they handle. |

Why this works, and why the 2024 attempt crashed, is in [docs/DESIGN.md](docs/DESIGN.md).

## Building

```bash
.github/scripts/stage-game-refs.sh ci_valheim        # reference assemblies, if you don't point at a Valheim install
dotnet build src/ServerSyncPlus.csproj -c Release -p:GamePath="$PWD/ci_valheim"
dotnet test tests
```

Releases: push a tag `vX.Y.Z` that matches the version in `src/ServerSyncPlus.csproj`, `src/Plus/Plugin.cs`, `package/manifest.json` and `CHANGELOG.md`. CI builds, attaches the zip to a GitHub release and publishes to Hexium.

## Credits & license

- **ServerSync**: blaxxun ([blaxxun-boop/ServerSync](https://github.com/blaxxun-boop/ServerSync)), MIT, © 2021 Tykea. See [LICENSE-upstream-ServerSync.txt](LICENSE-upstream-ServerSync.txt).
- **ServerSyncPlus additions**: Drakethos / DrakeMods, MIT. See [LICENSE](LICENSE).
- Icon: generated procedurally from [`branding/make_icon.py`](branding/make_icon.py).
