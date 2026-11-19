using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ServerSync.Plus;
using UnityEngine;

namespace ServerSync;

/// <summary>Runtime settings, written by the plugin's config. Defaults match upstream ServerSync.</summary>
internal static class PlusSettings
{
	internal static float SendTimeoutSeconds = 30f;
	internal static bool WatchConfigFiles = true;
}

internal static class PlusLog
{
	internal static ManualLogSource? Source;

	internal static void Info(string msg)
	{
		if (Source is not null) Source.LogInfo(msg);
		else Debug.Log("[ServerSyncPlus] " + msg);
	}

	internal static void Warning(string msg)
	{
		if (Source is not null) Source.LogWarning(msg);
		else Debug.LogWarning("[ServerSyncPlus] " + msg);
	}

	internal static void Error(string msg)
	{
		if (Source is not null) Source.LogError(msg);
		else Debug.LogError("[ServerSyncPlus] " + msg);
	}
}

/// <summary>Version parsing used by the version check. Never throws inside a network handler.</summary>
internal static class PlusVersion
{
	internal static System.Version Parse(string? value)
	{
		if (!VersionParser.TryParse(value, out System.Version version))
		{
			PlusLog.Warning($"Could not parse version '{value ?? "null"}', treating it as 0.0.0");
		}
		return version;
	}
}

/// <summary>
/// Called from the ConfigSync constructor. Never throws: the 2024 standalone package failed by
/// crashing; this one degrades and explains instead.
/// </summary>
internal static class PlusGuard
{
	internal const string ExpectedAssemblyName = "ServerSyncPlus";

	internal static readonly List<(string syncName, string consumer)> Consumers = new();
	private static bool warnedRepacked;

	internal static void OnConfigSyncCreated(string syncName, Assembly caller)
	{
		Consumers.Add((syncName, caller.GetName().Name));

		string self = typeof(PlusGuard).Assembly.GetName().Name;
		if (self != ExpectedAssemblyName)
		{
			if (!warnedRepacked)
			{
				warnedRepacked = true;
				PlusLog.Error($"ServerSyncPlus was merged into '{self}'. Reference ServerSyncPlus.dll as a dependency instead of ILRepacking it. " +
					"This copy keeps working as an ordinary embedded ServerSync, without the shared features.");
			}
		}
		else if (!ServerSyncPlusPlugin.Loaded && caller != typeof(PlusGuard).Assembly)
		{
			PlusLog.Warning($"'{caller.GetName().Name}' created ConfigSync '{syncName}' before ServerSyncPlus loaded. " +
				$"Add [BepInDependency(ServerSyncPlusPlugin.Guid)] to that mod.");
		}
	}
}

public partial class ConfigSync
{
	/// <summary>Every ConfigSync instance owned by this (shared) copy.</summary>
	internal static IEnumerable<ConfigSync> AllInstances => configSyncs;

	internal IEnumerable<OwnConfigEntryBase> Entries => allConfigs;

	internal int CustomValueCount => allCustomValues.Count;

	/// <summary>True for instances ServerSyncPlus created to force-sync a mod that has no sync of its own.</summary>
	internal bool IsForced { get; private set; }

	/// <summary>
	/// Creates a ConfigSync from a plugin's metadata: Name = GUID, DisplayName = plugin name,
	/// CurrentVersion = plugin version. ServerSyncPlus only (not available in embedded ServerSync).
	/// </summary>
	/// <param name="plugin">Your plugin instance (usually <c>this</c>).</param>
	/// <param name="minimumRequiredVersion">Clients older than this are refused. Defaults to the current version.</param>
	/// <param name="modRequired">If true, clients without your mod cannot join a server that has it, and vice versa.</param>
	public static ConfigSync ForPlugin(BepInEx.BaseUnityPlugin plugin, string? minimumRequiredVersion = null, bool modRequired = false)
	{
		BepInEx.BepInPlugin meta = BepInEx.MetadataHelper.GetMetadata(plugin);
		return new ConfigSync(meta.GUID)
		{
			DisplayName = meta.Name,
			CurrentVersion = meta.Version.ToString(),
			MinimumRequiredVersion = minimumRequiredVersion ?? meta.Version.ToString(),
			ModRequired = modRequired,
		};
	}

	/// <summary>Creates an instance for forced sync: always locked, no version handshake.</summary>
	internal static ConfigSync CreateForced(string name, string displayName)
	{
		ConfigSync sync = new(name) { DisplayName = displayName, IsLocked = true, IsForced = true };
		VersionCheck.Detach(sync);
		return sync;
	}

	/// <summary>Adds or removes this instance from the active set (RPC registration and login sends iterate it).</summary>
	internal void SetActive(bool active)
	{
		if (active) configSyncs.Add(this);
		else configSyncs.Remove(this);
	}

	internal static readonly List<ConfigSync> ForcedInstances = new();
}

public partial class VersionCheck
{
	/// <summary>Removes the version handshake of a ConfigSync (used for forced-sync instances).</summary>
	internal static void Detach(ConfigSync sync) => versionChecks.RemoveWhere(v => v.ConfigSync == sync);
}
