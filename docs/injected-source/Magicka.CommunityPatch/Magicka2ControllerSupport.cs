using Magicka.CoreFramework.CoreGame;
using Magicka.CoreFramework.GameSystem.HUDCustomisation;
using Magicka.GameLogic.Entities;
using Magicka.GameLogic.Spells;
using Magicka.Misc;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Magicka.CommunityPatch
{
	internal static class Magicka2ControllerSupport
	{
		private const float MovementDeadZone = 0.25f;

		private const float AimEnterDeadZone = 0.35f;

		private const float AimExitDeadZone = 0.25f;

		private const float TriggerThreshold = 0.5f;

		private static readonly bool sEnabled = !PatchSettings.Load().UseMagicka1ControllerScheme;

		private static readonly bool[] sAimActive = new bool[4];

		private static readonly Vector2[] sLastAim = new Vector2[4];

		private static readonly bool[] sModifierUsed = new bool[4];

		private static readonly bool[] sTriggerMagick = new bool[4];

		internal static bool IsEnabled()
		{
			return sEnabled;
		}

		internal static bool Pressed(GamePadState current, GamePadState previous, Buttons button)
		{
			return current.IsButtonDown(button) && previous.IsButtonUp(button);
		}

		internal static bool Released(GamePadState current, GamePadState previous, Buttons button)
		{
			return current.IsButtonUp(button) && previous.IsButtonDown(button);
		}

		internal static void CommunityPatchTrackModifierUse(int playerIndex, GamePadState current, GamePadState previous)
		{
			if (Pressed(current, previous, Buttons.LeftShoulder))
			{
				sModifierUsed[playerIndex] = false;
			}
			if (current.IsButtonDown(Buttons.LeftShoulder) &&
				(current.IsButtonDown(Buttons.A) || current.IsButtonDown(Buttons.B) ||
				 current.IsButtonDown(Buttons.X) || current.IsButtonDown(Buttons.Y) ||
				 current.IsButtonDown(Buttons.DPadUp) || current.IsButtonDown(Buttons.DPadDown) ||
				 current.IsButtonDown(Buttons.DPadLeft) || current.IsButtonDown(Buttons.DPadRight)))
			{
				sModifierUsed[playerIndex] = true;
			}
		}

		internal static bool CommunityPatchActionReleased(int playerIndex, GamePadState current, GamePadState previous)
		{
			CommunityPatchTrackModifierUse(playerIndex, current, previous);
			return Released(current, previous, Buttons.LeftShoulder) && !sModifierUsed[playerIndex];
		}

		internal static void CommunityPatchControllerElementSelected(Elements element)
		{
			MagickaElement selectedElement = Singleton<ElementManager>.Instance.GetElement(element);
			if (selectedElement != null)
			{
				Singleton<HUDManager>.Instance.Cooldown(selectedElement);
			}
			PatchTelemetry.CommunityPatchRecordControllerElementSelection();
		}

		internal static ButtonChar NotifierButton(ButtonChar legacyButton)
		{
			return sEnabled ? ButtonChar.None : legacyButton;
		}

		internal static void Update(int playerIndex, Avatar avatar, GamePadState current, GamePadState previous, bool inverted)
		{
			GamePadThumbSticks thumbSticks = current.ThumbSticks;
			Vector2 movement = thumbSticks.Left;
			NormalizeMovement(ref movement);
			avatar.UpdatePadDirection(movement, inverted);
			avatar.IsBlocking = false;

			bool aPressed = Pressed(current, previous, Buttons.A);
			bool bPressed = Pressed(current, previous, Buttons.B);
			bool xPressed = Pressed(current, previous, Buttons.X);
			bool yPressed = Pressed(current, previous, Buttons.Y);
			bool modifierDown = current.IsButtonDown(Buttons.LeftShoulder);

			if (Pressed(current, previous, Buttons.LeftShoulder))
			{
				sModifierUsed[playerIndex] = false;
			}
			if (modifierDown && (aPressed || bPressed || xPressed || yPressed ||
				current.IsButtonDown(Buttons.DPadUp) || current.IsButtonDown(Buttons.DPadDown) ||
				current.IsButtonDown(Buttons.DPadLeft) || current.IsButtonDown(Buttons.DPadRight)))
			{
				sModifierUsed[playerIndex] = true;
			}
			CommunityPatchTrackModifierUse(playerIndex, current, previous);

			if (!avatar.Polymorphed)
			{
				if (aPressed)
				{
					if (modifierDown)
					{
						avatar.ConjureCold();
						CommunityPatchControllerElementSelected(Elements.Cold);
					}
					else
					{
						avatar.ConjureFire();
						CommunityPatchControllerElementSelected(Elements.Fire);
					}
				}
				if (bPressed)
				{
					if (modifierDown)
					{
						avatar.ConjureShield();
						CommunityPatchControllerElementSelected(Elements.Shield);
					}
					else
					{
						avatar.ConjureEarth();
						CommunityPatchControllerElementSelected(Elements.Earth);
					}
				}
				if (xPressed)
				{
					if (modifierDown)
					{
						avatar.ConjureWater();
						CommunityPatchControllerElementSelected(Elements.Water);
					}
					else
					{
						avatar.ConjureLightning();
						CommunityPatchControllerElementSelected(Elements.Lightning);
					}
				}
				if (yPressed)
				{
					if (modifierDown)
					{
						avatar.ConjureLife();
						CommunityPatchControllerElementSelected(Elements.Life);
					}
					else
					{
						avatar.ConjureArcane();
						CommunityPatchControllerElementSelected(Elements.Arcane);
					}
				}
			}

			if (Released(current, previous, Buttons.LeftShoulder) && !sModifierUsed[playerIndex])
			{
				avatar.Action();
			}
			if (Pressed(current, previous, Buttons.RightStick))
			{
				avatar.CommunityPatchClearSpellQueue();
			}
			if (Pressed(current, previous, Buttons.Back))
			{
				avatar.CheckInventory();
			}

			UpdateRightTrigger(playerIndex, avatar, current, previous);
			if (Pressed(current, previous, Buttons.RightShoulder))
			{
				avatar.Special();
			}
			else if (Released(current, previous, Buttons.RightShoulder))
			{
				avatar.SpecialRelease();
			}

			if (current.Triggers.Left >= TriggerThreshold && (!avatar.ChantingMagick || avatar.CastButton(CastType.Area)))
			{
				avatar.AreaPressed();
			}
			else
			{
				avatar.AreaReleased();
			}

			UpdateAim(playerIndex, avatar, thumbSticks.Right, inverted);
		}

		private static void UpdateRightTrigger(int playerIndex, Avatar avatar, GamePadState current, GamePadState previous)
		{
			bool triggerDown = current.Triggers.Right >= TriggerThreshold;
			bool triggerWasDown = previous.Triggers.Right >= TriggerThreshold;
			if (triggerDown)
			{
				if (!triggerWasDown)
				{
					sTriggerMagick[playerIndex] = avatar.ChantingMagick;
					if (!sTriggerMagick[playerIndex])
					{
						avatar.Attack();
					}
				}
				else if (!sTriggerMagick[playerIndex] && (avatar.WieldingGun || avatar.Equipment[0].Item.SpellCharged))
				{
					avatar.Attack();
				}
			}
			else if (triggerWasDown)
			{
				if (sTriggerMagick[playerIndex]) avatar.Boost(); else avatar.AttackRelease();
				sTriggerMagick[playerIndex] = false;
			}
			else
			{
				sTriggerMagick[playerIndex] = false;
			}
		}

		private static void UpdateAim(int playerIndex, Avatar avatar, Vector2 aim, bool inverted)
		{
			if (inverted)
			{
				Vector2.Negate(ref aim, out aim);
			}
			float length = aim.Length();
			bool wasActive = sAimActive[playerIndex];
			bool active = wasActive ? length > AimExitDeadZone : length >= AimEnterDeadZone;
			if (length > AimExitDeadZone)
			{
				sLastAim[playerIndex] = aim;
			}
			if (active)
			{
				SetAimDirection(avatar, sLastAim[playerIndex]);
				if (avatar.CastButton(CastType.Force) || (avatar.SpellQueue.Count > 0 && !avatar.ChantingMagick))
				{
					avatar.ForcePressed();
				}
				else
				{
					avatar.ForceReleased();
				}
			}
			else
			{
				if (wasActive)
				{
					SetAimDirection(avatar, sLastAim[playerIndex]);
				}
				avatar.ForceReleased();
			}
			sAimActive[playerIndex] = active;
		}

		private static void NormalizeMovement(ref Vector2 movement)
		{
			float length = movement.Length();
			if (length > 1f)
			{
				Vector2.Divide(ref movement, length, out movement);
			}
			else if (length < MovementDeadZone)
			{
				movement = Vector2.Zero;
			}
		}

		private static void SetAimDirection(Avatar avatar, Vector2 aim)
		{
			avatar.CharacterBody.DesiredDirection = new Vector3(aim.X, 0f, -aim.Y);
		}
	}
}
