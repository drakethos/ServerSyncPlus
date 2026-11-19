// Example mod using ServerSyncPlus as a dependency instead of an embedded ServerSync.
//
// Existing ServerSync code works unchanged (see the "upstream style" lines); only the
// [BepInDependency] attribute and the build setup (ExampleConsumer.csproj) change.
// The "ServerSyncPlus only" lines show the optional helpers.
using BepInEx;
using BepInEx.Configuration;
using ServerSync;

namespace ExampleConsumer;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency(ServerSyncPlusPlugin.Guid)] // load ServerSyncPlus first; refuse to load without it
public class ExampleConsumerPlugin : BaseUnityPlugin
{
	private const string ModGuid = "example.consumer";
	private const string ModName = "Example Consumer";
	private const string ModVersion = "1.0.0";

	// Upstream style (unchanged from embedded ServerSync):
	private static readonly ConfigSync configSync = new(ModGuid) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	internal static ConfigEntry<int> StackMultiplier = null!;
	internal static ConfigEntry<bool> ShowClock = null!;
	internal static SyncedFile Recipes = null!;

	private void Awake()
	{
		// Upstream style would also work: ConfigSync.ForPlugin(this) builds the same instance from the plugin metadata.

		// ServerSyncPlus only: replaces the copy-pasted config() wrapper and lock boilerplate.
		Config.BindServerLock(configSync);
		StackMultiplier = Config.BindSynced(configSync, "2 - Stacks", "Multiplier", 2, "Stack size multiplier.");
		ShowClock = Config.BindSynced(configSync, "3 - Client", "Show clock", true, "Purely visual.", synchronized: false);

		// ServerSyncPlus only: a YAML file synced from the server, re-sent when the admin edits it.
		Recipes = new SyncedFile(configSync, "recipes", System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".recipes.yml"));
		Recipes.Changed += () => Logger.LogInfo($"Recipes now {Recipes.Content.Length} characters");
	}
}
