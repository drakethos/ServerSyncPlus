using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ServerSync;

/// <summary>
/// BepInEx entry point of ServerSyncPlus. Mods depend on it with
/// <c>[BepInDependency(ServerSyncPlusPlugin.Guid)]</c>, so BepInEx loads it (and applies its patches) first.
/// </summary>
[BepInPlugin(Guid, ModName, Version)]
public sealed class ServerSyncPlusPlugin : BaseUnityPlugin
{
	public const string Guid = "DrakeMods.ServerSyncPlus"; // never change: every consumer depends on it
	public const string ModName = "ServerSyncPlus";
	public const string Version = "0.9.0";

	internal static bool Loaded;
	internal static ServerSyncPlusPlugin? Instance;

	private ConfigEntry<bool> logCopyReport = null!;

	private void Awake()
	{
		Loaded = true;
		Instance = this;
		PlusLog.Source = Logger;

		ConfigEntry<float> timeout = Config.Bind("1 - Network", "Config send timeout (seconds)", 30f,
			new ConfigDescription("Server only. How long the server keeps sending synced configs to a joining player before giving up and disconnecting them. Upstream ServerSync uses a fixed 30.",
				new AcceptableValueRange<float>(10f, 600f)));
		PlusSettings.SendTimeoutSeconds = timeout.Value;
		timeout.SettingChanged += (_, _) => PlusSettings.SendTimeoutSeconds = timeout.Value;

		ConfigEntry<bool> watch = Config.Bind("2 - Files", "Reload config files on change", true,
			"Reload the .cfg file of every mod using ServerSyncPlus when it is edited. On a server, changes are pushed to connected players right away.");
		PlusSettings.WatchConfigFiles = watch.Value;
		watch.SettingChanged += (_, _) =>
		{
			PlusSettings.WatchConfigFiles = watch.Value;
			if (watch.Value) ConfigFileWatcher.Start();
			else ConfigFileWatcher.Stop();
		};

		ForcedSync.Bind(Config);

		logCopyReport = Config.Bind("5 - Diagnostics", "Log ServerSync copy report", true,
			"Log every ServerSync copy found in the game (ServerSyncPlus, embedded, loose) once after startup.");

		Harmony harmony = new(Guid + ".plus");
		harmony.PatchAll(typeof(ForcedSyncRolePatch));
		harmony.PatchAll(typeof(JoinSummaryPatch));
		ManagerCompat.Apply(harmony);
		ConsoleCommands.Apply(harmony);
	}

	private IEnumerator Start()
	{
		// Start runs after every plugin's Awake: all mods and their settings exist now.
		yield return null;
		ForcedSync.Prepare();
		ConfigFileWatcher.Start();
		if (logCopyReport.Value)
		{
			Logger.LogInfo(Diagnostics.CopyReport());
		}
	}

	private void Update() => FileWatchHub.Dispatch();
}
