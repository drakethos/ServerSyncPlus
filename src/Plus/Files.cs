using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace ServerSync;

/// <summary>
/// Shared, debounced file watching. FileSystemWatcher events arrive on worker threads; they are queued
/// here and delivered on Unity's main thread from <see cref="ServerSyncPlusPlugin"/>'s Update.
/// </summary>
internal static class FileWatchHub
{
	private const double DebounceSeconds = 0.5;

	private sealed class Watch
	{
		public FileSystemWatcher Watcher = null!;
		public readonly List<Action<string>> Callbacks = new();
	}

	private static readonly Dictionary<string, Watch> watches = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, (DateTime due, string key)> pending = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object gate = new();

	/// <summary>Calls <paramref name="callback"/> with the changed file's full path. Returns an unsubscribe action.</summary>
	internal static Action Subscribe(string directory, string filter, bool recursive, Action<string> callback)
	{
		directory = Path.GetFullPath(directory);
		Directory.CreateDirectory(directory);
		string key = $"{directory}|{filter}|{recursive}";
		Watch? watch;
		lock (gate)
		{
			if (!watches.TryGetValue(key, out watch))
			{
				watch = new Watch();
				FileSystemWatcher fsw = new(directory, filter) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
				FileSystemEventHandler onChange = (_, e) => Queue(key, e.FullPath);
				fsw.Changed += onChange;
				fsw.Created += onChange;
				fsw.Deleted += onChange;
				fsw.Renamed += (_, e) => Queue(key, e.FullPath);
				fsw.EnableRaisingEvents = true;
				watch.Watcher = fsw;
				watches[key] = watch;
			}
			watch.Callbacks.Add(callback);
		}
		return () =>
		{
			lock (gate)
			{
				watch.Callbacks.Remove(callback);
				if (watch.Callbacks.Count == 0)
				{
					watch.Watcher.Dispose();
					watches.Remove(key);
				}
			}
		};
	}

	private static void Queue(string key, string fullPath)
	{
		lock (gate)
		{
			pending[fullPath] = (DateTime.UtcNow.AddSeconds(DebounceSeconds), key);
		}
	}

	/// <summary>Main thread only.</summary>
	internal static void Dispatch()
	{
		List<(string path, Action<string>[] callbacks)> due = new();
		lock (gate)
		{
			if (pending.Count == 0)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			foreach (KeyValuePair<string, (DateTime due, string key)> kv in pending.Where(kv => kv.Value.due <= now).ToList())
			{
				pending.Remove(kv.Key);
				if (watches.TryGetValue(kv.Value.key, out Watch watch))
				{
					due.Add((kv.Key, watch.Callbacks.ToArray()));
				}
			}
		}
		foreach ((string path, Action<string>[] callbacks) in due)
		{
			foreach (Action<string> callback in callbacks)
			{
				try
				{
					callback(path);
				}
				catch (Exception e)
				{
					PlusLog.Error($"File watch callback for {path} failed: {e}");
				}
			}
		}
	}

	/// <summary>Reads a file, retrying briefly while an editor still holds it open.</summary>
	internal static string? ReadAllTextOrNull(string path)
	{
		for (int attempt = 0; ; ++attempt)
		{
			try
			{
				return File.Exists(path) ? File.ReadAllText(path) : null;
			}
			catch (IOException) when (attempt < 5)
			{
				System.Threading.Thread.Sleep(50);
			}
		}
	}
}

/// <summary>
/// Reloads the .cfg file of every mod registered with ServerSyncPlus when it changes on disk.
/// On a server, changed values are pushed to connected players by ServerSync as usual.
/// On a client receiving server values, upstream's patches keep the server values and update the local fallback.
/// </summary>
internal static class ConfigFileWatcher
{
	private static Action? unsubscribe;

	internal static void Start()
	{
		if (unsubscribe is not null || !PlusSettings.WatchConfigFiles)
		{
			return;
		}
		unsubscribe = FileWatchHub.Subscribe(BepInEx.Paths.ConfigPath, "*.cfg", false, OnChanged);
	}

	internal static void Stop()
	{
		unsubscribe?.Invoke();
		unsubscribe = null;
	}

	private static void OnChanged(string fullPath)
	{
		if (!File.Exists(fullPath))
		{
			return;
		}
		foreach (ConfigFile file in ConfigSync.AllInstances.Concat(ConfigSync.ForcedInstances)
			         .SelectMany(s => s.Entries).Select(e => e.BaseConfig.ConfigFile).Distinct())
		{
			if (string.Equals(Path.GetFullPath(file.ConfigFilePath), fullPath, StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					file.Reload();
					PlusLog.Info($"Reloaded {Path.GetFileName(fullPath)}");
				}
				catch (Exception e)
				{
					PlusLog.Warning($"Could not reload {Path.GetFileName(fullPath)}: {e.Message}");
				}
			}
		}
	}

	/// <summary>Reloads every registered config file now. Returns how many were reloaded.</summary>
	internal static int ReloadAll()
	{
		int count = 0;
		foreach (ConfigFile file in ConfigSync.AllInstances.Concat(ConfigSync.ForcedInstances).SelectMany(s => s.Entries).Select(e => e.BaseConfig.ConfigFile).Distinct())
		{
			file.Reload();
			++count;
		}
		return count;
	}
}

/// <summary>
/// Synchronizes one text file (YAML, JSON, ...) from the server to every client. ServerSyncPlus only.
/// Create it in your plugin's Awake, before the game connects, like any CustomSyncedValue.
/// </summary>
/// <remarks>
/// Whoever is the source of truth (the server, or the local game when not connected) reads the file from
/// disk and republishes it whenever it changes. Clients receive the server's content in memory; their own
/// file on disk is never overwritten, and its content comes back when they disconnect.
/// </remarks>
public sealed class SyncedFile : IDisposable
{
	private readonly ConfigSync sync;
	private readonly CustomSyncedValue<string> value;
	private readonly Action unsubscribe;

	/// <summary>Full path of the local file.</summary>
	public string Path { get; }

	/// <summary>The effective content: the server's while connected, otherwise the local file's ("" if missing).</summary>
	public string Content => value.Value ?? "";

	/// <summary>Raised on the main thread whenever <see cref="Content"/> changes.</summary>
	public event Action? Changed;

	/// <param name="identifier">Unique within <paramref name="sync"/>; never change it once released.</param>
	public SyncedFile(ConfigSync sync, string identifier, string path)
	{
		this.sync = sync;
		Path = System.IO.Path.GetFullPath(path);
		value = new CustomSyncedValue<string>(sync, identifier, FileWatchHub.ReadAllTextOrNull(Path) ?? "");
		value.ValueChanged += () => Changed?.Invoke();
		sync.SourceOfTruthChanged += isSource =>
		{
			if (isSource) ReloadFromDisk();
		};
		unsubscribe = FileWatchHub.Subscribe(System.IO.Path.GetDirectoryName(Path)!, System.IO.Path.GetFileName(Path), false, _ => ReloadFromDisk());
	}

	private void ReloadFromDisk()
	{
		if (!sync.IsSourceOfTruth)
		{
			return; // server content is active; the local file is used again after disconnecting
		}
		string content = FileWatchHub.ReadAllTextOrNull(Path) ?? "";
		if (content != value.Value)
		{
			value.Value = content;
		}
	}

	public void Dispose() => unsubscribe();
}

/// <summary>
/// Synchronizes every file matching a pattern in a directory, keyed by path relative to the directory
/// (always with '/' separators). ServerSyncPlus only. Same rules as <see cref="SyncedFile"/>.
/// </summary>
public sealed class SyncedDirectory : IDisposable
{
	private readonly ConfigSync sync;
	private readonly CustomSyncedValue<Dictionary<string, string>> value;
	private readonly Action unsubscribe;
	private readonly string searchPattern;
	private readonly bool recursive;

	public string DirectoryPath { get; }

	/// <summary>Relative path → content. The server's files while connected, otherwise the local ones.</summary>
	public IReadOnlyDictionary<string, string> Files => value.Value ?? new Dictionary<string, string>();

	/// <summary>Raised on the main thread whenever <see cref="Files"/> changes.</summary>
	public event Action? Changed;

	public SyncedDirectory(ConfigSync sync, string identifier, string directory, string searchPattern = "*.yml", bool recursive = false)
	{
		this.sync = sync;
		this.searchPattern = searchPattern;
		this.recursive = recursive;
		DirectoryPath = System.IO.Path.GetFullPath(directory);
		value = new CustomSyncedValue<Dictionary<string, string>>(sync, identifier, ReadAll());
		value.ValueChanged += () => Changed?.Invoke();
		sync.SourceOfTruthChanged += isSource =>
		{
			if (isSource) ReloadFromDisk();
		};
		unsubscribe = FileWatchHub.Subscribe(DirectoryPath, searchPattern, recursive, _ => ReloadFromDisk());
	}

	private Dictionary<string, string> ReadAll()
	{
		Dictionary<string, string> files = new();
		if (!Directory.Exists(DirectoryPath))
		{
			return files;
		}
		foreach (string file in Directory.GetFiles(DirectoryPath, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
		{
			string relative = file.Substring(DirectoryPath.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Replace('\\', '/');
			files[relative] = FileWatchHub.ReadAllTextOrNull(file) ?? "";
		}
		return files;
	}

	private void ReloadFromDisk()
	{
		if (!sync.IsSourceOfTruth)
		{
			return;
		}
		Dictionary<string, string> files = ReadAll();
		Dictionary<string, string> current = value.Value ?? new Dictionary<string, string>();
		if (files.Count != current.Count || files.Any(kv => !current.TryGetValue(kv.Key, out string c) || c != kv.Value))
		{
			value.Value = files;
		}
	}

	public void Dispose() => unsubscribe();
}
