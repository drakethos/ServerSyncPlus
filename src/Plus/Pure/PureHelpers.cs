// Pure logic with no Unity, BepInEx or Valheim dependencies, so it can be unit-tested outside the game.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ServerSync.Plus;

/// <summary>Version parsing that tolerates "1.2.3-beta", "v1.2", "" and null.</summary>
internal static class VersionParser
{
	private static readonly Regex Core = new(@"\d+(\.\d+){0,3}");

	/// <returns>false if nothing usable was found; <paramref name="version"/> is then 0.0.0.</returns>
	internal static bool TryParse(string? value, out System.Version version)
	{
		if (value is not null && Core.Match(value) is { Success: true } m)
		{
			string[] parts = m.Value.Split('.');
			if (parts.Length == 1)
			{
				parts = new[] { parts[0], "0" };
			}
			try
			{
				version = new System.Version(string.Join(".", parts));
				return true;
			}
			catch (Exception)
			{
				// overflowing components etc.
			}
		}
		version = new System.Version(0, 0, 0);
		return false;
	}
}

/// <summary>
/// A comma-separated list of patterns: <c>GUID</c>, <c>GUID:Section</c> or <c>GUID:Section:Key</c>.
/// <c>*</c> is a wildcard in any part. Matching is case-insensitive.
/// </summary>
internal sealed class PatternList
{
	private readonly List<Regex[]> patterns = new();

	public static readonly PatternList Empty = new("");

	public PatternList(string? list)
	{
		foreach (string raw in (list ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string item = raw.Trim();
			if (item.Length == 0)
			{
				continue;
			}
			patterns.Add(item.Split(':').Take(3).Select(p => new Regex("^" + Regex.Escape(p.Trim()).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase)).ToArray());
		}
	}

	public bool IsEmpty => patterns.Count == 0;

	/// <summary>True if a pattern covers this whole mod (pattern has only the GUID part).</summary>
	public bool MatchesMod(string guid) => patterns.Any(p => p.Length == 1 && p[0].IsMatch(guid));

	/// <summary>True if a pattern covers this setting (GUID-only patterns cover every setting of the mod).</summary>
	public bool MatchesEntry(string guid, string section, string key)
	{
		string[] parts = { guid, section, key };
		return patterns.Any(p => p.Select((r, i) => r.IsMatch(parts[i])).All(ok => ok));
	}
}

/// <summary>Which config value types the ServerSync wire format can carry safely.</summary>
internal static class SyncableTypes
{
	private static readonly HashSet<Type> Primitives = new()
	{
		typeof(bool), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(string),
	};

	/// <summary>
	/// Conservative: primitives the game's ZRpc serializer handles, enums backed by int/uint/long/ulong,
	/// and structs whose fields are all syncable (Vector3, Color, ...). Arrays and lists are rejected
	/// (e.g. BepInEx KeyboardShortcut holds a KeyCode[] and cannot be round-tripped).
	/// </summary>
	internal static bool IsSyncable(Type type) => IsSyncable(type, 0);

	private static bool IsSyncable(Type type, int depth)
	{
		if (depth > 4)
		{
			return false;
		}
		if (Primitives.Contains(type))
		{
			return true;
		}
		if (type.IsEnum)
		{
			return Primitives.Contains(Enum.GetUnderlyingType(type)) && Enum.GetUnderlyingType(type) != typeof(bool);
		}
		if (type.IsValueType && !type.IsPrimitive)
		{
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return fields.Length > 0 && fields.All(f => IsSyncable(f.FieldType, depth + 1));
		}
		return false;
	}

	/// <summary>Key bindings: KeyCode enums. (KeyboardShortcut is already rejected by <see cref="IsSyncable(Type)"/>.)</summary>
	internal static bool IsKeybind(Type type) => type.IsEnum && type.Name == "KeyCode";
}

/// <summary>Recognizes config entries that some other system already synchronizes.</summary>
internal static class SyncMarkers
{
	/// <summary>
	/// True if the entry's tags show it is already handled: any ServerSync copy (embedded or ours) tags
	/// entries with a SyncedConfigEntry`1, and Jotunn marks synced entries with IsAdminOnly = true.
	/// </summary>
	internal static bool IsAlreadySynced(IEnumerable<object>? tags)
	{
		foreach (object tag in tags ?? Array.Empty<object>())
		{
			if (tag is null)
			{
				continue;
			}
			Type t = tag.GetType();
			for (Type? b = t; b is not null; b = b.BaseType)
			{
				if (b.Name.StartsWith("SyncedConfigEntry", StringComparison.Ordinal) || b.Name == "OwnConfigEntryBase")
				{
					return true;
				}
			}
			object? adminOnly = t.GetField("IsAdminOnly")?.GetValue(tag) ?? t.GetProperty("IsAdminOnly")?.GetValue(tag, null);
			if (adminOnly is true)
			{
				return true;
			}
		}
		return false;
	}
}
