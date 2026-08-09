using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using PolygonHead;
using PolygonHead.CommunityPatch;

namespace Magicka.CommunityPatch
{
	internal static class InGameUiCompatibility
	{
		private static float sScaleFactor = LoadScaleFactor();
		private static bool sScaleSelectionActive;

		internal static void ApplyScaleSetting()
		{
			InGameUiRenderScale.SetScale(sScaleFactor);
		}

		internal static string GetScaleText()
		{
			return sScaleFactor <= 1.001f ? "Off" : ((int)(sScaleFactor * 100f + 0.5f)).ToString(CultureInfo.InvariantCulture) + "%";
		}

		internal static void BeginScaleSelection()
		{
			sScaleSelectionActive = true;
		}

		internal static bool IsScaleSelection()
		{
			return sScaleSelectionActive;
		}

		internal static void EndScaleSelection()
		{
			sScaleSelectionActive = false;
		}

		internal static void ApplyScalePercent(int percent)
		{
			sScaleFactor = MathHelper.Clamp(percent * 0.01f, 1f, 4f);
			ApplyScaleSetting();
			SaveScaleFactor();
		}

		internal static void AdjustMenuSize(ref Point size)
		{
			if (InGameUiRenderScale.ShouldScale(size.X, size.Y))
			{
				float scale = InGameUiRenderScale.GetScaleFactor();
				size.X = (int)(size.X / scale + 0.5f);
				size.Y = (int)(size.Y / scale + 0.5f);
			}
		}

		internal static void AdjustMenuMouse(ref Vector2 position)
		{
			Point screenSize = RenderManager.Instance.ScreenSize;
			if (InGameUiRenderScale.ShouldScale(screenSize.X, screenSize.Y))
			{
				float scale = InGameUiRenderScale.GetScaleFactor();
				position.X /= scale;
				position.Y /= scale;
			}
		}

		private static float LoadScaleFactor()
		{
			try
			{
				string path = GetSettingsPath();
				if (File.Exists(path)) foreach (string line in File.ReadAllLines(path))
				{
					string value = line.Trim();
					if (value.StartsWith("ui_scale=", StringComparison.OrdinalIgnoreCase))
					{
						float parsed;
						if (float.TryParse(value.Substring(9).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return MathHelper.Clamp(parsed, 1f, 4f);
					}
				}
			}
			catch { }
			return 2f;
		}

		private static void SaveScaleFactor()
		{
			try
			{
				string path = GetSettingsPath();
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				string[] lines = File.Exists(path) ? File.ReadAllLines(path) : new string[] { "[MagickaCommunityPatch]" };
				bool found = false;
				for (int i = 0; i < lines.Length; i++) if (lines[i].TrimStart().StartsWith("ui_scale=", StringComparison.OrdinalIgnoreCase)) { lines[i] = "ui_scale=" + sScaleFactor.ToString("0.##", CultureInfo.InvariantCulture); found = true; }
				if (!found) { string[] expanded = new string[lines.Length + 1]; lines.CopyTo(expanded, 0); expanded[lines.Length] = "ui_scale=" + sScaleFactor.ToString("0.##", CultureInfo.InvariantCulture); lines = expanded; }
				File.WriteAllLines(path, lines);
			}
			catch { }
		}

		private static string GetSettingsPath()
		{
			return Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CommunityPatch"), "ui-scale.ini");
		}
	}
}
