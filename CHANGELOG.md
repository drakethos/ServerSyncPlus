# Changelog

## 0.9.0

First release. A public fork of [ServerSync](https://github.com/blaxxun-boop/ServerSync) by blaxxun (upstream commit `c57c2aa`), shipped as a dependency.

**Packaging**
- Own identity: assembly `ServerSyncPlus`, GUID `DrakeMods.ServerSyncPlus`. It can't collide with loose or embedded ServerSync copies.
- Namespace, public API and network format unchanged from upstream: existing ServerSync code compiles as-is and interoperates with embedded copies.

**For admins**
- Configurable config-send timeout (upstream: fixed 30 s).
- Live reload: editing a mod's `.cfg` on the server pushes the new values to connected players.
- Forced sync: force settings of mods that have no sync of their own (`Mods`, `Exclude`, `Sync keybinds`); players can opt out.
- Console command `ssp` (`status`, `copies`, `reload`) and a join summary in the client log.
- Startup report of every ServerSync copy in the game.

**For mod developers**
- `ConfigSync.ForPlugin`, `Config.BindSynced`, `Config.BindServerLock`.
- `SyncedFile` and `SyncedDirectory` for YAML/JSON files.
- ItemManager/PieceManager/SkillManager keep syncing after a mod switches from embedded ServerSync.

**Fixes over upstream**
- Version strings like `1.2.3-beta` no longer throw inside the version check.
