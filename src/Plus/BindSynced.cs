using BepInEx.Configuration;

namespace ServerSync;

/// <summary>
/// Replaces the <c>config()</c> wrapper that almost every ServerSync mod copies from the upstream README.
/// ServerSyncPlus only.
/// </summary>
public static class BindSyncedExtensions
{
	public const string SyncedSuffix = " [Synced with Server]";
	public const string NotSyncedSuffix = " [Not Synced with Server]";

	/// <summary>Binds a setting and registers it with <paramref name="sync"/>.</summary>
	/// <param name="synchronized">False keeps the setting local to each player (still registered, like upstream's <c>SynchronizedConfig = false</c>).</param>
	/// <param name="annotate">Appends " [Synced with Server]" / " [Not Synced with Server]" to the description shown in config managers.</param>
	public static ConfigEntry<T> BindSynced<T>(this ConfigFile config, ConfigSync sync, string section, string key, T defaultValue, ConfigDescription description, bool synchronized = true, bool annotate = true)
	{
		if (annotate)
		{
			description = new ConfigDescription(description.Description + (synchronized ? SyncedSuffix : NotSyncedSuffix), description.AcceptableValues, description.Tags);
		}
		ConfigEntry<T> entry = config.Bind(section, key, defaultValue, description);
		sync.AddConfigEntry(entry).SynchronizedConfig = synchronized;
		return entry;
	}

	/// <inheritdoc cref="BindSynced{T}(ConfigFile, ConfigSync, string, string, T, ConfigDescription, bool, bool)"/>
	public static ConfigEntry<T> BindSynced<T>(this ConfigFile config, ConfigSync sync, string section, string key, T defaultValue, string description, bool synchronized = true, bool annotate = true)
		=> config.BindSynced(sync, section, key, defaultValue, new ConfigDescription(description), synchronized, annotate);

	/// <summary>Binds the server lock setting ("Lock Configuration"): when on, only admins may change synced settings.</summary>
	public static ConfigEntry<bool> BindServerLock(this ConfigFile config, ConfigSync sync, string section = "1 - General", string key = "Lock Configuration", bool defaultValue = true)
	{
		ConfigEntry<bool> entry = config.Bind(section, key, defaultValue, "If on, the configuration is locked and can be changed by server admins only." + SyncedSuffix);
		sync.AddLockingConfigEntry(entry);
		return entry;
	}
}
