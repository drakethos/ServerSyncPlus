using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync.Plus;

namespace ServerSync;

/// <summary>
/// Force-syncs settings of mods that have no server sync of their own, without changing those mods.
/// </summary>
/// <remarks>
/// Every game (client or server) prepares one locked, version-check-free ConfigSync per eligible mod, named
/// "&lt;guid&gt; ServerSyncPlus.Forced". When the game becomes a server (ZNet.Awake) only the mods the admin
/// listed stay active, with excluded settings switched to local. When it is a client, all prepared instances
/// stay active (if the player allows it) so they can receive whatever the server sends.
/// </remarks>
internal static class ForcedSync
{
	internal const string NameSuffix = " ServerSyncPlus.Forced";
	private const string ConditionalConfigSyncGuid = "_shudnal.ConditionalConfigSync";

	internal static ConfigEntry<string> ServerMods = null!;
	internal static ConfigEntry<string> ServerExclude = null!;
	internal static ConfigEntry<bool> ServerSyncKeybinds = null!;
	internal static ConfigEntry<bool> ClientAllow = null!;

	private sealed class ForcedMod
	{
		public string Guid = null!;
		public ConfigSync Sync = null!;
	}

	private static readonly List<ForcedMod> mods = new();
	private static MethodInfo? addConfigEntry;

	internal static void Bind(ConfigFile config)
	{
		ServerMods = config.Bind("3 - Forced Sync (server)", "Mods", "",
			"Comma-separated plugin GUIDs whose settings the server forces on players, for mods with no server sync of their own. " +
			"'*' means every eligible mod. A GUID is shown at the top of each mod's .cfg file. Takes effect when the world starts.");
		ServerExclude = config.Bind("3 - Forced Sync (server)", "Exclude", "",
			"Settings never forced. Comma-separated GUID, GUID:Section or GUID:Section:Key, '*' as a wildcard. Example: com.example.mod:Graphics:*");
		ServerSyncKeybinds = config.Bind("3 - Forced Sync (server)", "Sync keybinds", false,
			"If off, key bindings stay as each player set them.");
		ClientAllow = config.Bind("4 - Forced Sync (client)", "Allow server to force settings", true,
			"If off, this game ignores forced settings from servers and always uses its own values for mods without server sync.");
	}

	/// <summary>Called once, after every plugin's Awake.</summary>
	internal static void Prepare()
	{
		addConfigEntry = typeof(ConfigSync).GetMethod(nameof(ConfigSync.AddConfigEntry))!;
		foreach (PluginInfo info in Chainloader.PluginInfos.Values)
		{
			try
			{
				PrepareMod(info);
			}
			catch (Exception e)
			{
				PlusLog.Warning($"Forced sync: skipped {info.Metadata.GUID}: {e.Message}");
			}
		}
		PlusLog.Info($"Forced sync: {mods.Count} mods without their own sync are eligible.");
	}

	private static void PrepareMod(PluginInfo info)
	{
		if (info.Instance is not BaseUnityPlugin plugin || info.Metadata.GUID == ServerSyncPlusPlugin.Guid)
		{
			return;
		}
		if (info.Dependencies.Any(d => d.DependencyGUID == ConditionalConfigSyncGuid))
		{
			return; // synced by Conditional Config Sync; its markers are not public, so leave the whole mod alone
		}

		List<ConfigEntryBase> eligible = plugin.Config.Keys
			.Select(def => plugin.Config[def])
			.Where(e => SyncableTypes.IsSyncable(e.SettingType) && !SyncMarkers.IsAlreadySynced(e.Description.Tags))
			.ToList();
		if (eligible.Count == 0)
		{
			return;
		}

		ConfigSync sync = ConfigSync.CreateForced(info.Metadata.GUID + NameSuffix, info.Metadata.Name);
		foreach (ConfigEntryBase entry in eligible)
		{
			addConfigEntry!.MakeGenericMethod(entry.SettingType).Invoke(sync, new object[] { entry });
		}
		sync.SetActive(false); // role decided at ZNet.Awake
		ConfigSync.ForcedInstances.Add(sync);
		mods.Add(new ForcedMod { Guid = info.Metadata.GUID, Sync = sync });
	}

	/// <summary>Runs in a ZNet.Awake postfix before upstream's, once the game knows whether it is the server.</summary>
	internal static void ApplyRole(bool isServer)
	{
		PatternList listed = new(ServerMods.Value);
		bool all = ServerMods.Value.Trim() == "*";
		PatternList exclude = new(ServerExclude.Value);
		int active = 0;

		foreach (ForcedMod mod in mods)
		{
			bool on = isServer ? (all || listed.MatchesMod(mod.Guid)) && !exclude.MatchesMod(mod.Guid) : ClientAllow.Value;
			mod.Sync.SetActive(on);
			if (!on)
			{
				continue;
			}
			++active;
			foreach (OwnConfigEntryBase entry in mod.Sync.Entries)
			{
				ConfigDefinition def = entry.BaseConfig.Definition;
				entry.SynchronizedConfig = !isServer || (!exclude.MatchesEntry(mod.Guid, def.Section, def.Key) &&
				                                         (ServerSyncKeybinds.Value || !SyncableTypes.IsKeybind(entry.BaseConfig.SettingType)));
			}
		}

		if (isServer)
		{
			foreach (string guid in ServerMods.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && s != "*" && !s.Contains("*")))
			{
				if (!mods.Any(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase)))
				{
					PlusLog.Warning($"Forced sync: '{guid}' is not installed, already syncs its own settings, or has no syncable settings.");
				}
			}
			PlusLog.Info($"Forced sync: forcing settings of {active} mod(s) on players.");
		}
	}

	internal static IEnumerable<(string guid, ConfigSync sync)> Mods => mods.Select(m => (m.Guid, m.Sync));
}

[HarmonyPatch(typeof(ZNet), "Awake")]
internal static class ForcedSyncRolePatch
{
	// Must run before upstream's RegisterRPCPatch postfix, which registers RPCs for the active set.
	[HarmonyPostfix, HarmonyPriority(Priority.First)]
	private static void Postfix(ZNet __instance) => ForcedSync.ApplyRole(__instance.IsServer());
}
