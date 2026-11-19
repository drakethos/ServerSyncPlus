using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ServerSync;

/// <summary>
/// Keeps blaxxun's manager helpers (ItemManager, PieceManager, SkillManager, ...) syncing after a mod
/// switches from an embedded ServerSync to ServerSyncPlus.
/// </summary>
/// <remarks>
/// Those helpers look ServerSync up with <c>Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync")</c>,
/// which only searches the mod's own DLL. When that lookup returns null, the calling DLL references
/// ServerSyncPlus, and the name is a ServerSync type, this answers with ServerSyncPlus's type. Mods that
/// never used ServerSync (no reference to us) are left alone, so this never turns sync on for them.
/// </remarks>
internal static class ManagerCompat
{
	private static readonly Dictionary<Assembly, bool> referencesUs = new();
	private static Assembly self = null!;

	internal static void Apply(Harmony harmony)
	{
		self = typeof(ConfigSync).Assembly;
		try
		{
			Type runtimeAssemblyType = self.GetType(); // RuntimeAssembly / MonoAssembly
			MethodInfo? target = null;
			for (Type? t = runtimeAssemblyType; t is not null && target is null; t = t.BaseType)
			{
				target = t.GetMethod("GetType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(string), typeof(bool), typeof(bool) }, null);
			}
			if (target is null)
			{
				PlusLog.Warning("Manager compatibility: Assembly.GetType(string, bool, bool) not found; ItemManager/PieceManager settings of migrated mods will not sync.");
				return;
			}
			harmony.Patch(target, postfix: new HarmonyMethod(typeof(ManagerCompat), nameof(Postfix)));
		}
		catch (Exception e)
		{
			PlusLog.Warning($"Manager compatibility patch failed: {e.Message}");
		}
	}

	private static void Postfix(Assembly __instance, object[] __args, ref Type? __result)
	{
		if (__result is not null || __args.Length == 0 || __args[0] is not string name || !name.StartsWith("ServerSync.", StringComparison.Ordinal) || __instance == self)
		{
			return;
		}
		bool refs;
		lock (referencesUs)
		{
			if (!referencesUs.TryGetValue(__instance, out refs))
			{
				refs = __instance.GetReferencedAssemblies().Any(a => a.Name == PlusGuard.ExpectedAssemblyName);
				referencesUs[__instance] = refs;
			}
		}
		if (refs)
		{
			__result = self.GetType(name, false, false);
		}
	}
}
