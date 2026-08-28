using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Magicka.GameLogic.Controls;
using Magicka.GameLogic.GameStates.Menu.Main.Options;
using Magicka.GameLogic.UI.UISystem.Popup;
using Magicka.Misc;
using Magicka.WebTools;
using Magicka.WebTools.Paradox;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PolygonHead;

namespace Magicka.CommunityPatch
{
	internal static class RuntimeCompatibilityGuards
	{
		private static int sDirectInputUnavailable;

		private static int sDirectInputWarningPending;

		private static int sParadoxStorePriceUpdatePending;

		public static void QueueParadoxStorePriceUpdate(ThreadStart update)
		{
			if (update == null)
			{
				return;
			}
			if (Interlocked.CompareExchange(ref sParadoxStorePriceUpdatePending, 1, 0) != 0)
			{
				return;
			}

			if (!ThreadPool.QueueUserWorkItem(RunParadoxStorePriceUpdate, update))
			{
				Interlocked.Exchange(ref sParadoxStorePriceUpdatePending, 0);
			}
		}

		private static void RunParadoxStorePriceUpdate(object state)
		{
			try
			{
				((ThreadStart)state)();
			}
			catch (Exception)
			{
				// A failed legacy store request must not terminate a ThreadPool thread.
			}

			Interlocked.Exchange(ref sParadoxStorePriceUpdatePending, 0);
		}

		public static Stream OpenSteamApi()
		{
			try
			{
				string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
				return File.OpenRead(Path.Combine(directoryName, "steam_api.dll"));
			}
			catch (Exception)
			{
				MessageBox.Show(
					"Magicka could not open steam_api.dll in its installation folder. " +
					"Start Magicka through Steam, check that the folder path is valid and accessible, " +
					"and use Steam's 'Verify integrity of game files'.",
					"Magicka startup error",
					MessageBoxButtons.OK,
					MessageBoxIcon.Hand);
				return null;
			}
		}

		public static bool FindNewGamePads(ControlManager manager)
		{
			if (Interlocked.CompareExchange(ref sDirectInputUnavailable, 0, 0) != 0)
			{
				return false;
			}

			try
			{
				manager.FindNewGamePads();
				return true;
			}
			catch (FileNotFoundException)
			{
				if (Interlocked.Exchange(ref sDirectInputUnavailable, 1) == 0)
				{
					Interlocked.Exchange(ref sDirectInputWarningPending, 1);
				}
				return false;
			}
			catch (FileLoadException)
			{
				if (Interlocked.Exchange(ref sDirectInputUnavailable, 1) == 0)
				{
					Interlocked.Exchange(ref sDirectInputWarningPending, 1);
				}
				return false;
			}
		}

		public static void ShowPendingDirectInputWarning()
		{
			if (Interlocked.CompareExchange(ref sDirectInputWarningPending, 0, 0) == 0)
			{
				return;
			}
			if (Singleton<ParadoxAccount>.Instance.PendingErrorCode != ParadoxAccount.ErrorCode.None)
			{
				return;
			}
			if (Singleton<WidgetPopupSystem>.Instance.Active)
			{
				return;
			}
			if (Interlocked.Exchange(ref sDirectInputWarningPending, 0) == 0)
			{
				return;
			}

			ParadoxPopupUtils.ShowErrorPopup(
				"Controller support unavailable",
				"Managed DirectX 1.1 is missing. Controllers cannot be used until it is installed.\n\n" +
				"Start Magicka from the Community Patch installer's Start Game button, " +
				"or run this file as administrator from the Magicka folder:\n" +
				"Dependencies\\directx_feb2010\\DXSETUP.exe\n\n" +
				"Restart Magicka afterwards.");
		}

		public static void UpdateControllerOptions(SubMenuOptionsControls options)
		{
			if (Interlocked.CompareExchange(ref sDirectInputUnavailable, 0, 0) != 0)
			{
				return;
			}

			try
			{
				options.UpdateControllers();
			}
			catch (FileNotFoundException)
			{
				if (Interlocked.Exchange(ref sDirectInputUnavailable, 1) == 0)
				{
					Interlocked.Exchange(ref sDirectInputWarningPending, 1);
				}
			}
			catch (FileLoadException)
			{
				if (Interlocked.Exchange(ref sDirectInputUnavailable, 1) == 0)
				{
					Interlocked.Exchange(ref sDirectInputWarningPending, 1);
				}
			}
		}

		public static bool IsVersionTextHit(Point screenSize, MouseState mouseState, Text text)
		{
			if (text == null)
			{
				return false;
			}

			int x = mouseState.X;
			int y = mouseState.Y;
			int top = screenSize.Y - 16 - text.Font.LineHeight;
			if (x < 16 || y < top || y > screenSize.Y - 16)
			{
				return false;
			}

			string displayText = new string(text.Characters, 0, text.EndIndex);
			float right = 16f + text.Font.MeasureText(displayText, true).X;
			return x <= right;
		}

		public static void AppendTextSafely(Text text, string suffix)
		{
			if (text == null || suffix == null)
			{
				return;
			}

			string current = new string(text.Characters, 0, text.EndIndex);
			text.SetText(current + suffix);
		}

		public static void ShowSupporters()
		{
			string names = string.Join(", ", CommunityPatchInfo.PatreonSupporters);
			ParadoxPopupUtils.ShowErrorPopup(
				"Community Patch supporters",
				"Thank you for supporting the Community Patch and its continued development:\n\n" + names);
		}
	}
}
