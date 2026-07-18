using System;
using System.Collections.Generic;
using Magicka.GameLogic;
using Magicka.GameLogic.Controls;
using Magicka.GameLogic.Entities;
using Magicka.GameLogic.GameStates;
using Magicka.GameLogic.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PolygonHead;

namespace Magicka.CommunityPatch
{
	internal static class HybridInputSupport
	{
		private const float StickActivationThreshold = 0.35f;

		private const int KeyboardLabelMode = 0;

		private const int ControllerBaseLabelMode = 1;

		private const int ControllerModifierLabelMode = 2;

		// KeyboardHUD element indices. These names also document the order of every
		// controller-HUD tuning switch below.
		private const int EarthElement = 0;

		private const int WaterElement = 1;

		private const int ColdElement = 2;

		private const int FireElement = 3;

		private const int LightningElement = 4;

		private const int ArcaneElement = 5;

		private const int LifeElement = 6;

		private const int ShieldElement = 7;

		private static readonly Buttons[] sActivityButtons = new Buttons[14]
		{
			Buttons.A,
			Buttons.B,
			Buttons.X,
			Buttons.Y,
			Buttons.LeftShoulder,
			Buttons.RightShoulder,
			Buttons.LeftStick,
			Buttons.RightStick,
			Buttons.Start,
			Buttons.Back,
			Buttons.DPadUp,
			Buttons.DPadDown,
			Buttons.DPadLeft,
			Buttons.DPadRight
		};

		private static readonly GamePadState[] sPreviousPads = new GamePadState[4];

		private static readonly bool[] sModifierActive = new bool[4];

		private static KeyboardState sPreviousKeyboard;

		private static MouseState sPreviousMouse;

		private static bool sInitialized;

		private static bool sControllerHudMode;

		private static int sHudPadIndex = -1;

		private static int sHudLabelMode = -1;

		internal static void Update(List<XInputController> controllers, KeyboardMouseController keyboard)
		{
			KeyboardState currentKeyboard = Game.Instance.KeyboardState;
			MouseState currentMouse = Game.Instance.MouseState;
			bool focused = Game.Instance.Focused;
			for (int i = 0; i < controllers.Count && i < sPreviousPads.Length; i++)
			{
				GamePadState currentPad;
				try
				{
					currentPad = GamePad.GetState((PlayerIndex)i);
				}
				catch (InvalidOperationException)
				{
					currentPad = default(GamePadState);
				}
				sModifierActive[i] = currentPad.IsConnected && currentPad.IsButtonDown(Buttons.LeftShoulder);
				if (sInitialized && focused && currentPad.IsConnected && HasGamePadActivity(currentPad, sPreviousPads[i]))
				{
					bool tookControl = TryTakeControl(controllers[i]);
					if (tookControl || controllers[i].Player != null)
					{
						sControllerHudMode = Magicka2ControllerSupport.IsEnabled();
						sHudPadIndex = i;
						ControlManager.LastActiveController = controllers[i];
					}
				}
				sPreviousPads[i] = currentPad;
			}
			if (!sInitialized)
			{
				InitializeHudMode(controllers);
			}
			if (sInitialized && focused && HasKeyboardMouseActivity(currentKeyboard, sPreviousKeyboard, currentMouse, sPreviousMouse))
			{
				bool tookControl = TryTakeControl(keyboard);
				if (tookControl || keyboard.Player != null)
				{
					sControllerHudMode = false;
					sHudPadIndex = -1;
					ControlManager.LastActiveController = keyboard;
				}
			}
			sPreviousKeyboard = currentKeyboard;
			sPreviousMouse = currentMouse;
			sInitialized = true;
			EnsureHudMode(controllers, keyboard);
		}

		internal static bool IsControllerHudMode()
		{
			return Magicka2ControllerSupport.IsEnabled() && sControllerHudMode;
		}

		internal static void ApplyKeyboardHudState(KeyboardHUD.Icon[] icons, Text[] labels)
		{
			bool controllerMode = IsControllerHudMode();
			bool modifierActive = controllerMode && IsHudModifierActive();
			int labelMode = !controllerMode ? KeyboardLabelMode :
				(modifierActive ? ControllerModifierLabelMode : ControllerBaseLabelMode);
			if (sHudLabelMode != labelMode)
			{
				if (controllerMode)
				{
					labels[0].SetText("B");
					labels[1].SetText(modifierActive ? "X" : "LB");
					labels[2].SetText(modifierActive ? "A" : "LB");
					labels[3].SetText("A");
					labels[4].SetText("X");
					labels[5].SetText("Y");
					labels[6].SetText(modifierActive ? "Y" : "LB");
					labels[7].SetText(modifierActive ? "B" : "LB");
				}
				else
				{
					labels[0].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Earth));
					labels[1].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Water));
					labels[2].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Cold));
					labels[3].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Fire));
					labels[4].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Lightning));
					labels[5].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Arcane));
					labels[6].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Life));
					labels[7].SetText(KeyboardMouseController.KeyToString(KeyboardBindings.Shield));
				}
				sHudLabelMode = labelMode;
			}
			if (!controllerMode)
			{
				return;
			}
			for (int i = 0; i < icons.Length; i++)
			{
				if (IsModifierElement(i) != modifierActive)
				{
					icons[i].Intensity *= 0.42f;
				}
			}
		}

		internal static void InvalidateKeyboardHudLabels()
		{
			sHudLabelMode = -1;
		}

		// Controller HUD fine tuning. "position" selects one of DrawIcon's four
		// 50-pixel X slots. xOffset/yOffset then move the complete existing texture
		// group (shadow, element icon, and key-label background). Positive X moves
		// right; positive Y moves down.
		internal static void GetControllerHudIconPlacement(int elementIndex,
			out int position, out float xOffset, out float yOffset)
		{
			position = 0;
			xOffset = 0f;
			yOffset = 0f;
			switch (elementIndex)
			{
			case EarthElement:
				position = 3;
				xOffset = 35f;
				yOffset = 35f;
				break;
			case WaterElement:
				position = 0;
				xOffset = 0f;
				yOffset = 35f;
				break;
			case ColdElement:
				position = 2;
				xOffset = 20f;
				yOffset = 60f;
				break;
			case FireElement:
				position = 1;
				xOffset = 15f;
				yOffset = 60f;
				break;
			case LightningElement:
				position = 0;
				xOffset = 0f;
				yOffset = -25f;
				break;
			case ArcaneElement:
				position = 2;
				xOffset = 20f;
				yOffset = -50f;
				break;
			case LifeElement:
				position = 1;
				xOffset = 15f;
				yOffset = -50f;
				break;
			case ShieldElement:
				position = 3;
				xOffset = 35f;
				yOffset = -25f;
				break;
			}
		}

		// Per-element label tuning. xOffset/yOffset affect only the text inside the
		// existing key-label background. A scale of 1 is the original Maiandra14
		// size. The four inactive LB labels start at 0.72 so both letters fit.
		internal static void GetControllerHudLabelTuning(int elementIndex,
			out float xOffset, out float yOffset, out float scale)
		{
			xOffset = 0f;
			yOffset = 0f;
			scale = 1f;
			if (!IsControllerHudMode())
			{
				return;
			}
			bool modifierActive = IsHudModifierActive();
			switch (elementIndex)
			{
			case EarthElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = 1f;
				break;
			case WaterElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = modifierActive ? 1f : 0.72f;
				break;
			case ColdElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = modifierActive ? 1f : 0.72f;
				break;
			case FireElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = 1f;
				break;
			case LightningElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = 1f;
				break;
			case ArcaneElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = 1f;
				break;
			case LifeElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = modifierActive ? 1f : 0.72f;
				break;
			case ShieldElement:
				xOffset = 0f;
				yOffset = 0f;
				scale = modifierActive ? 1f : 0.72f;
				break;
			}
		}

		private static bool IsHudModifierActive()
		{
			return sHudPadIndex >= 0 && sHudPadIndex < sModifierActive.Length && sModifierActive[sHudPadIndex];
		}

		private static bool IsModifierElement(int elementIndex)
		{
			return elementIndex == 1 || elementIndex == 2 || elementIndex == 6 || elementIndex == 7;
		}

		private static void EnsureHudMode(List<XInputController> controllers, KeyboardMouseController keyboard)
		{
			if (sControllerHudMode)
			{
				if (sHudPadIndex >= 0 && sHudPadIndex < controllers.Count &&
					controllers[sHudPadIndex].Player != null)
				{
					return;
				}
			}
			else if (keyboard.Player != null)
			{
				return;
			}
			InitializeHudMode(controllers);
		}

		private static void InitializeHudMode(List<XInputController> controllers)
		{
			sControllerHudMode = false;
			sHudPadIndex = -1;
			if (!Magicka2ControllerSupport.IsEnabled())
			{
				return;
			}
			Controller lastActive = ControlManager.LastActiveController;
			if (lastActive is KeyboardMouseController)
			{
				return;
			}
			for (int i = 0; i < controllers.Count && i < sModifierActive.Length; i++)
			{
				if (controllers[i] == lastActive)
				{
					sControllerHudMode = true;
					sHudPadIndex = i;
					return;
				}
			}
			Player localPlayer = null;
			foreach (Player player in Game.Instance.ConnectedPlayers)
			{
				if (player.IsNetworkGamer)
				{
					continue;
				}
				if (localPlayer != null)
				{
					return;
				}
				localPlayer = player;
			}
			if (localPlayer == null || !localPlayer.Playing || !(localPlayer.Controller is XInputController))
			{
				return;
			}
			for (int i = 0; i < controllers.Count && i < sModifierActive.Length; i++)
			{
				if (controllers[i] == localPlayer.Controller)
				{
					sControllerHudMode = true;
					sHudPadIndex = i;
					return;
				}
			}
		}

		private static bool HasGamePadActivity(GamePadState current, GamePadState previous)
		{
			for (int i = 0; i < sActivityButtons.Length; i++)
			{
				Buttons button = sActivityButtons[i];
				if (current.IsButtonDown(button) && previous.IsButtonUp(button))
				{
					return true;
				}
			}
			if ((current.Triggers.Left >= 0.5f && previous.Triggers.Left < 0.5f) ||
				(current.Triggers.Right >= 0.5f && previous.Triggers.Right < 0.5f))
			{
				return true;
			}
			return CrossedStickThreshold(current.ThumbSticks.Left, previous.ThumbSticks.Left) ||
				CrossedStickThreshold(current.ThumbSticks.Right, previous.ThumbSticks.Right);
		}

		private static bool CrossedStickThreshold(Vector2 current, Vector2 previous)
		{
			float thresholdSquared = StickActivationThreshold * StickActivationThreshold;
			return current.LengthSquared() >= thresholdSquared && previous.LengthSquared() < thresholdSquared;
		}

		private static bool HasKeyboardMouseActivity(KeyboardState currentKeyboard, KeyboardState previousKeyboard, MouseState currentMouse, MouseState previousMouse)
		{
			Keys[] pressedKeys = currentKeyboard.GetPressedKeys();
			for (int i = 0; i < pressedKeys.Length; i++)
			{
				if (previousKeyboard.IsKeyUp(pressedKeys[i]))
				{
					return true;
				}
			}
			if ((currentMouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton == ButtonState.Released) ||
				(currentMouse.MiddleButton == ButtonState.Pressed && previousMouse.MiddleButton == ButtonState.Released) ||
				(currentMouse.RightButton == ButtonState.Pressed && previousMouse.RightButton == ButtonState.Released) ||
				(currentMouse.XButton1 == ButtonState.Pressed && previousMouse.XButton1 == ButtonState.Released) ||
				(currentMouse.XButton2 == ButtonState.Pressed && previousMouse.XButton2 == ButtonState.Released) ||
				currentMouse.ScrollWheelValue != previousMouse.ScrollWheelValue)
			{
				return true;
			}
			return Math.Abs(currentMouse.X - previousMouse.X) >= 2 || Math.Abs(currentMouse.Y - previousMouse.Y) >= 2;
		}

		private static bool TryTakeControl(Controller candidate)
		{
			if (!(GameStateManager.Instance.CurrentState is PlayState))
			{
				return false;
			}
			ControlManager controlManager = ControlManager.Instance;
			if (controlManager.IsInputLimited)
			{
				return false;
			}
			Player localPlayer = null;
			foreach (Player player in Game.Instance.ConnectedPlayers)
			{
				if (player.IsNetworkGamer)
				{
					continue;
				}
				if (localPlayer != null)
				{
					return false;
				}
				localPlayer = player;
			}
			if (localPlayer == null || !localPlayer.Playing || localPlayer.Controller == candidate)
			{
				return false;
			}
			if ((candidate.Player != null && candidate.Player != localPlayer) || controlManager.IsPlayerInputLocked(localPlayer.ID))
			{
				return false;
			}
			Controller previousController = localPlayer.Controller;
			if (previousController == null || previousController.Inverted ||
				(!(previousController is XInputController) && !(previousController is KeyboardMouseController)))
			{
				return false;
			}
			Neutralize(localPlayer.Avatar);
			previousController.Clear();
			candidate.Clear();
			previousController.Player = null;
			localPlayer.Controller = candidate;
			candidate.Player = localPlayer;
			ControlManager.LastActiveController = candidate;
			Game.Instance.IsMouseVisible = candidate is KeyboardMouseController;
			return true;
		}

		private static void Neutralize(Avatar avatar)
		{
			if (avatar == null || avatar.Dead)
			{
				return;
			}
			avatar.UpdatePadDirection(Vector2.Zero, false);
			avatar.UpdateMouseDirection(Vector2.Zero, false);
			avatar.MouseMoveStop();
			avatar.AreaReleased();
			avatar.ForceReleased();
			avatar.AttackRelease();
			avatar.SpecialRelease();
			avatar.IsBlocking = false;
		}
	}
}
