using System;
using System.Collections.Generic;
using ServerSync.Plus;
using Xunit;

public class VersionParserTests
{
	[Theory]
	[InlineData("1.2.3", "1.2.3")]
	[InlineData("1.2.3-beta", "1.2.3")]
	[InlineData("v1.2", "1.2")]
	[InlineData("2", "2.0")]
	[InlineData("1.0.0+build.5", "1.0.0")]
	public void Parses(string input, string expected)
	{
		Assert.True(VersionParser.TryParse(input, out Version v));
		Assert.Equal(new Version(expected), v);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("beta")]
	[InlineData("99999999999.1")]
	public void FallsBackToZero(string? input)
	{
		Assert.False(VersionParser.TryParse(input, out Version v));
		Assert.Equal(new Version(0, 0, 0), v);
	}

	[Fact]
	public void PrereleaseComparesAsRelease() // upstream new Version("1.2.3-beta") throws inside the network handler
	{
		VersionParser.TryParse("1.2.3-beta", out Version a);
		VersionParser.TryParse("1.2.0", out Version b);
		Assert.True(a >= b);
	}
}

public class PatternListTests
{
	[Fact]
	public void GuidOnlyCoversWholeMod()
	{
		PatternList p = new("com.example.mod");
		Assert.True(p.MatchesMod("com.example.mod"));
		Assert.True(p.MatchesEntry("com.example.mod", "Any", "Thing"));
		Assert.False(p.MatchesMod("com.example.other"));
	}

	[Fact]
	public void SectionAndKeyWithWildcards()
	{
		PatternList p = new(" com.example.mod:Graphics:* , other.*:General:Enabled ");
		Assert.True(p.MatchesEntry("com.example.mod", "Graphics", "Bloom"));
		Assert.False(p.MatchesEntry("com.example.mod", "Gameplay", "Bloom"));
		Assert.False(p.MatchesMod("com.example.mod")); // section pattern does not exclude the whole mod
		Assert.True(p.MatchesEntry("other.thing", "general", "ENABLED")); // case-insensitive
		Assert.False(p.MatchesEntry("other.thing", "General", "Disabled"));
	}

	[Fact]
	public void SpecialCharactersAreLiteral()
	{
		PatternList p = new("a.b:Sec (1):Key [x]");
		Assert.True(p.MatchesEntry("a.b", "Sec (1)", "Key [x]"));
		Assert.False(p.MatchesEntry("aXb", "Sec (1)", "Key [x]"));
	}

	[Fact]
	public void EmptyMatchesNothing()
	{
		Assert.True(PatternList.Empty.IsEmpty);
		Assert.False(new PatternList(" , ; ").MatchesEntry("a", "b", "c"));
	}
}

public class SyncableTypesTests
{
	private enum IntEnum { A, B }
	private enum ByteEnum : byte { A }
	private enum KeyCode { None, A }
	private struct Vec { public float x, y, z; }
	private struct Nested { public Vec v; public int i; }
	private struct WithArray { public int[] keys; }

	[Theory]
	[InlineData(typeof(bool))]
	[InlineData(typeof(int))]
	[InlineData(typeof(float))]
	[InlineData(typeof(double))]
	[InlineData(typeof(string))]
	[InlineData(typeof(long))]
	[InlineData(typeof(IntEnum))]
	[InlineData(typeof(Vec))]
	[InlineData(typeof(Nested))]
	public void Syncable(Type t) => Assert.True(SyncableTypes.IsSyncable(t));

	[Theory]
	[InlineData(typeof(byte))]      // not handled by the game's ZRpc serializer
	[InlineData(typeof(ByteEnum))]
	[InlineData(typeof(int[]))]
	[InlineData(typeof(List<string>))]
	[InlineData(typeof(WithArray))] // like BepInEx KeyboardShortcut
	[InlineData(typeof(object))]
	public void NotSyncable(Type t) => Assert.False(SyncableTypes.IsSyncable(t));

	[Fact]
	public void KeyCodeIsKeybind()
	{
		Assert.True(SyncableTypes.IsKeybind(typeof(KeyCode)));
		Assert.False(SyncableTypes.IsKeybind(typeof(IntEnum)));
	}
}

public class SyncMarkersTests
{
	// Shapes of tags other sync systems attach to ConfigDescription.Tags
	private class SyncedConfigEntry<T> { }
	private abstract class OwnConfigEntryBase { }
	private class EmbeddedEntry : OwnConfigEntryBase { }
	private class ConfigurationManagerAttributes { public bool? IsAdminOnly; public bool? ReadOnly; }

	[Fact]
	public void DetectsServerSyncCopies()
	{
		Assert.True(SyncMarkers.IsAlreadySynced(new object[] { new SyncedConfigEntry<int>() }));
		Assert.True(SyncMarkers.IsAlreadySynced(new object[] { new EmbeddedEntry() }));
	}

	[Fact]
	public void DetectsJotunnAdminOnly()
	{
		Assert.True(SyncMarkers.IsAlreadySynced(new object[] { new ConfigurationManagerAttributes { IsAdminOnly = true } }));
		Assert.False(SyncMarkers.IsAlreadySynced(new object[] { new ConfigurationManagerAttributes { IsAdminOnly = false } }));
	}

	[Fact]
	public void PlainTagsAreNotSynced()
	{
		Assert.False(SyncMarkers.IsAlreadySynced(null));
		Assert.False(SyncMarkers.IsAlreadySynced(new object[] { "x", new ConfigurationManagerAttributes { ReadOnly = true } }));
	}
}
