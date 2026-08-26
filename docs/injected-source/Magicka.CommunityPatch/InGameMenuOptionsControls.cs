using System.Collections.Generic;
using Magicka.Audio;
using Magicka.CommunityPatch;
using Magicka.GameLogic.Controls;
using Magicka.GameLogic.GameStates.Menu;
using Magicka.Graphics;
using Microsoft.Xna.Framework;
using PolygonHead;

namespace Magicka.GameLogic.GameStates.InGameMenus
{
	internal sealed class InGameMenuOptionsControls : InGameMenu
	{
		private static readonly string[] OPTION_STRINGS = new string[]
		{
			"controller_mode",
			"back"
		};

		private static InGameMenuOptionsControls sInstance;

		private readonly List<MenuTextItem> mOptions;

		internal static InGameMenuOptionsControls Instance
		{
			get
			{
				if (sInstance == null)
				{
					sInstance = new InGameMenuOptionsControls();
				}
				return sInstance;
			}
		}

		private InGameMenuOptionsControls()
		{
			BitmapFont font = FontManager.Instance.GetFont(MagickaFont.Maiandra18);
			mOptions = new List<MenuTextItem>();
			mOptions.Add(new MenuTextItem(GetModeText(), Vector2.Zero, font, TextAlign.Left));
			AddMenuTextItem("Controller Mode", font, TextAlign.Right);
			AddMenuTextItem(Helper.GetHashCodeCustom("#menu_back"), font, TextAlign.Center);
			mBackgroundSize = new Vector2(600f, 400f);
		}

		public override void UpdatePositions()
		{
			Vector2 position = new Vector2(sScreenSize.X * 0.5f + 80f, 290f * sScale);
			for (int i = 0; i < mMenuItems.Count; i++)
			{
				if (i == mMenuItems.Count - 1)
				{
					position.X -= 80f;
				}
				MenuItem item = mMenuItems[i];
				item.Scale = sScale;
				item.Position = position;
				position.Y += item.BottomRight.Y - item.TopLeft.Y;
			}
			for (int i = 0; i < mOptions.Count; i++)
			{
				position = mMenuItems[i].Position;
				position.X -= 15f * sScale;
				mMenuItems[i].Position = position;
				position.X += 30f * sScale;
				mOptions[i].Position = position;
				mOptions[i].Scale = sScale;
			}
			MenuItem back = mMenuItems[mMenuItems.Count - 1];
			back.Position += new Vector2(0f, 10f * sScale);
		}

		public override void IControllerSelect(Controller controller)
		{
			if (mSelectedItem == 0)
			{
				PlaySound(SOUND_INCREASE);
				ToggleMode();
			}
			else if (mSelectedItem == 1)
			{
				PlaySound(SOUND_DECREASE);
				PopMenu();
			}
		}

		public override void IControllerBack(Controller controller)
		{
			PlaySound(SOUND_DECREASE);
			PopMenu();
		}

		public override void IControllerMove(Controller controller, ControllerDirection direction)
		{
			base.IControllerMove(controller, direction);
			if (mSelectedItem == 0 && (direction == ControllerDirection.Left || direction == ControllerDirection.Right))
			{
				PlaySound(SOUND_INCREASE);
				ToggleMode();
			}
		}

		public override string IGetHighlightedButtonName()
		{
			return OPTION_STRINGS[mSelectedItem];
		}

		public override void OnEnter()
		{
			mSelectedItem = sController is KeyboardMouseController ? -1 : 0;
			mOptions[0].SetText(GetModeText());
			UpdatePositions();
		}

		public override void OnExit()
		{
		}

		public override void IDraw(float transition, ref Vector2 offset)
		{
			base.IDraw(transition, ref offset);
			Vector4 color = mMenuItems[0].Color;
			Vector4 selectedColor = mMenuItems[0].ColorSelected;
			Vector4 disabledColor = mMenuItems[0].ColorDisabled;
			for (int i = 0; i < mOptions.Count; i++)
			{
				MenuItem option = mOptions[i];
				option.Color = color;
				option.ColorSelected = selectedColor;
				option.ColorDisabled = disabledColor;
				option.Selected = mMenuItems[i].Selected;
				option.Enabled = mMenuItems[i].Enabled;
				option.Draw(sEffect);
			}
		}

		private void ToggleMode()
		{
			Magicka2ControllerSupport.SetEnabled(!Magicka2ControllerSupport.IsEnabled());
			mOptions[0].SetText(GetModeText());
		}

		private static string GetModeText()
		{
			return Magicka2ControllerSupport.IsEnabled() ? "Magicka 2-style" : "Magicka 1 (original)";
		}

		private static void PlaySound(int cue)
		{
			AudioManager.Instance.PlayCue(Banks.UI, cue);
		}
	}
}
