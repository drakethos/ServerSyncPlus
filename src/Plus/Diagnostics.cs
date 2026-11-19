using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ServerSync;

internal static class Diagnostics
{
	/// <summary>Every ServerSync copy in the process: ours, embedded (public or internalized) and loose.</summary>
	internal static string CopyReport()
	{
		StringBuilder sb = new();
		List<(Assembly asm, Type type)> copies = new();
		foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type? t;
			try
			{
				t = asm.GetType("ServerSync.ConfigSync", false);
			}
			catch (Exception)
			{
				continue;
			}
			if (t is not null)
			{
				copies.Add((asm, t));
			}
		}

		sb.AppendLine($"ServerSync copies in this game: {copies.Count}");
		foreach ((Assembly asm, Type type) in copies)
		{
			string name = asm.GetName().Name;
			string kind = name == PlusGuard.ExpectedAssemblyName ? "ServerSyncPlus" : name == "ServerSync" ? "LOOSE ServerSync.dll" : "embedded";
			sb.AppendLine($"  {kind,-20} {name} ({(type.IsPublic ? "public" : "internalized")})");
		}
		if (copies.Count(c => c.asm.GetName().Name == "ServerSync") > 1)
		{
			sb.AppendLine("  WARNING: several assemblies are named 'ServerSync'. Mods referencing 'ServerSync' by name may bind to the wrong one.");
		}
		return sb.ToString().TrimEnd();
	}

	internal static string SyncStatus()
	{
		StringBuilder sb = new();
		List<ConfigSync> own = ConfigSync.AllInstances.Where(s => !s.IsForced).ToList();
		sb.AppendLine($"Mods using ServerSyncPlus: {own.Count}");
		foreach (ConfigSync s in own.OrderBy(s => s.DisplayName ?? s.Name))
		{
			sb.AppendLine($"  {s.DisplayName ?? s.Name,-32} {(s.IsSourceOfTruth ? "local values " : "SERVER values")} {(s.IsLocked ? "locked  " : "unlocked")} {s.Entries.Count()} settings, {s.CustomValueCount} custom values");
		}
		List<(string guid, ConfigSync sync)> forced = ForcedSync.Mods.Where(m => ConfigSync.AllInstances.Contains(m.sync)).ToList();
		sb.AppendLine($"Forced-sync mods active: {forced.Count}");
		foreach ((string guid, ConfigSync s) in forced.OrderBy(m => m.guid))
		{
			sb.AppendLine($"  {guid,-32} {(s.IsSourceOfTruth ? "local values " : "SERVER values")} {s.Entries.Count(e => e.SynchronizedConfig)} settings");
		}
		return sb.ToString().TrimEnd();
	}

	internal static IEnumerator JoinSummary()
	{
		yield return new WaitForSeconds(20);
		if (ZNet.instance is null || ZNet.instance.IsServer())
		{
			yield break;
		}
		List<ConfigSync> all = ConfigSync.AllInstances.ToList();
		List<string> fromServer = all.Where(s => !s.IsSourceOfTruth).Select(s => s.DisplayName ?? s.Name).OrderBy(n => n).ToList();
		List<string> notSent = all.Where(s => s.IsSourceOfTruth && !s.IsForced).Select(s => s.DisplayName ?? s.Name).OrderBy(n => n).ToList();
		PlusLog.Info($"Join summary: server values applied for {fromServer.Count} mod(s): {string.Join(", ", fromServer)}");
		if (notSent.Count > 0)
		{
			PlusLog.Info($"Join summary: no server values for {notSent.Count} mod(s) (not installed on the server?): {string.Join(", ", notSent)}");
		}
	}
}

[HarmonyPatch(typeof(ZNet), "OnNewConnection")]
internal static class JoinSummaryPatch
{
	[HarmonyPostfix]
	private static void Postfix(ZNet __instance)
	{
		if (!__instance.IsServer() && ServerSyncPlusPlugin.Instance is { } plugin)
		{
			plugin.StartCoroutine(Diagnostics.JoinSummary());
		}
	}
}

/// <summary>Console command: <c>ssp status | copies | reload</c>.</summary>
internal static class ConsoleCommands
{
	internal static void Apply(Harmony harmony)
	{
		try
		{
			MethodInfo? init = AccessTools.Method(typeof(Terminal), "InitTerminal");
			if (init is null)
			{
				PlusLog.Warning("Console command not registered: Terminal.InitTerminal not found.");
				return;
			}
			harmony.Patch(init, postfix: new HarmonyMethod(typeof(ConsoleCommands), nameof(Register)));
		}
		catch (Exception e)
		{
			PlusLog.Warning($"Console command not registered: {e.Message}");
		}
	}

	private static void Register()
	{
		_ = new Terminal.ConsoleCommand("ssp", "[status|copies|reload] ServerSyncPlus: sync status, ServerSync copies, reload config files", args =>
		{
			string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
			string output = sub switch
			{
				"copies" => Diagnostics.CopyReport(),
				"reload" => $"Reloaded {ConfigFileWatcher.ReloadAll()} config file(s). On a server, changed values are pushed to players.",
				_ => Diagnostics.SyncStatus(),
			};
			foreach (string line in output.Split('\n'))
			{
				args.Context.AddString(line.TrimEnd('\r'));
			}
			PlusLog.Info(output);
		});
	}
}
