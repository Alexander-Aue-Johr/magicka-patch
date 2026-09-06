using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Magicka.CommunityPatch;

internal static class Program
{
	private static int Main()
	{
		Console.WriteLine("Runtime: " + Environment.Version);
		string originalDirectory = Environment.CurrentDirectory;
		string testDirectory = Path.Combine(Path.GetTempPath(), "magicka-telemetry-context-" + Guid.NewGuid().ToString("N"));
		try
		{
			string fontDirectory = Path.Combine(Path.Combine(Path.Combine(testDirectory, "content"), "Languages"), Path.Combine("eng", "font"));
			Directory.CreateDirectory(fontDirectory);
			File.WriteAllBytes(Path.Combine(fontDirectory, "font_a.xnb"), Encoding.UTF8.GetBytes("glyph-a"));
			File.WriteAllBytes(Path.Combine(fontDirectory, "font_b.xnb"), Encoding.UTF8.GetBytes("glyph-b-more-data"));
			Environment.CurrentDirectory = testDirectory;

			TelemetryRuntimeContext.RecordPlayState(
				@"G:\SteamLibrary\steamapps\common\Magicka\content\Levels\Tsar\Tsar_Mountaindale.lvl",
				"Tsar_Mountaindale");
			TelemetryRuntimeContext.RecordScene("scene_01");
			TelemetryRuntimeContext.RecordScene("scene_02");
			TelemetryRuntimeContext.RecordMenu();
			TelemetryRuntimeContext.RecordLanguage("deu");
			TelemetryRuntimeContext.RecordResolution(5120, 1440);
			TelemetryRuntimeContext.RecordUiScale(2.5f);

			return RunAssertions(fontDirectory);
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			Environment.CurrentDirectory = originalDirectory;
			if (Directory.Exists(testDirectory))
			{
				Directory.Delete(testDirectory, true);
			}
		}
	}

	private static int RunAssertions(string fontDirectory)
	{
		Dictionary<string, string> properties = new Dictionary<string, string>();
		TelemetryRuntimeContext.AddProperties(properties);
		AssertEqual(
			@"Tsar\Tsar_Mountaindale.lvl -> Tsar_Mountaindale -> scene_01 -> scene_02 -> Menu",
			properties["navigation_history"],
			"navigation_history");
		AssertTrue(!properties["navigation_history"].Contains("SteamLibrary"), "navigation path privacy");
		AssertEqual("1", properties["playstate_count"], "playstate_count");
		AssertEqual("2", properties["scene_transition_count"], "scene_transition_count");
		AssertEqual("false", properties["navigation_history_truncated"], "initial truncation flag");
		AssertEqual("deu", properties["language"], "language");
		AssertEqual("eng", properties["glyph_font_source"], "glyph fallback");
		AssertEqual("2", properties["glyph_file_count"], "glyph_file_count");
		long expectedBytes = new FileInfo(Path.Combine(fontDirectory, "font_a.xnb")).Length
			+ new FileInfo(Path.Combine(fontDirectory, "font_b.xnb")).Length;
		AssertEqual(expectedBytes.ToString(CultureInfo.InvariantCulture), properties["glyph_total_bytes"], "glyph_total_bytes");
		AssertEqual("ok", properties["glyph_fingerprint_status"], "glyph fingerprint status");
		AssertTrue(properties["glyph_sha256"].Length == 64, "glyph SHA-256 length");
		AssertEqual("5120", properties["resolution_width"], "resolution_width");
		AssertEqual("1440", properties["resolution_height"], "resolution_height");
		AssertEqual("250", properties["ui_scale_percent"], "ui_scale_percent");

		string firstHash = properties["glyph_sha256"];
		TelemetryRuntimeContext.RecordLanguage("deu");
		Dictionary<string, string> repeated = new Dictionary<string, string>();
		TelemetryRuntimeContext.AddProperties(repeated);
		AssertEqual(firstHash, repeated["glyph_sha256"], "deterministic glyph SHA-256");

		File.WriteAllBytes(Path.Combine(fontDirectory, "font_b.xnb"), Encoding.UTF8.GetBytes("changed-glyph-data"));
		TelemetryRuntimeContext.RecordLanguage("deu");
		Dictionary<string, string> changed = new Dictionary<string, string>();
		TelemetryRuntimeContext.AddProperties(changed);
		AssertTrue(firstHash != changed["glyph_sha256"], "changed glyph SHA-256");

		for (int i = 0; i < 500; i++)
		{
			TelemetryRuntimeContext.RecordScene("long_scene_name_" + i.ToString(CultureInfo.InvariantCulture));
		}
		Dictionary<string, string> bounded = new Dictionary<string, string>();
		TelemetryRuntimeContext.AddProperties(bounded);
		AssertTrue(bounded["navigation_history"].Length <= 4096, "navigation bound");
		AssertEqual("true", bounded["navigation_history_truncated"], "truncation flag");
		AssertEqual("502", bounded["scene_transition_count"], "bounded scene count");
		Console.WriteLine("Telemetry runtime context assertions passed.");
		return 0;
	}

	private static void AssertEqual(string expected, string actual, string name)
	{
		if (!string.Equals(expected, actual, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(name + ": expected '" + expected + "', actual '" + actual + "'.");
		}
	}

	private static void AssertTrue(bool condition, string name)
	{
		if (!condition)
		{
			throw new InvalidOperationException(name + " failed.");
		}
	}
}
