using System;
using System.Text;
using Magicka.GameLogic.Entities;
using Magicka.GameLogic.Entities.Abilities;

namespace Magicka.CommunityPatch
{
	internal static class WarlordAbilityDiagnostic
	{
		internal static void Inspect(CharacterTemplate template, Ability[] abilities)
		{
			try
			{
				Ability ability = null;
				if (abilities != null && abilities.Length != 0)
				{
					ability = abilities[0];
				}
				if (ability is Melee)
				{
					return;
				}

				string text = (ability == null) ? "null" : ability.GetType().FullName;
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append("template_null=").Append(template == null);
				stringBuilder.Append(";template_disposed=").Append(template != null && template.CommunityPatchIsDisposed());
				stringBuilder.Append(";template_id=");
				if (template != null)
				{
					stringBuilder.Append(template.ID);
				}
				stringBuilder.Append(";abilities_null=").Append(abilities == null);
				stringBuilder.Append(";ability_count=").Append((abilities == null) ? 0 : abilities.Length);
				stringBuilder.Append(";primary_null=").Append(ability == null);
				stringBuilder.Append(";shares_template_abilities=").Append(template != null && object.ReferenceEquals(abilities, template.Abilities));

				PatchTelemetry.SendRuntimeGuard(
					"magicka_patch_warlord_ability_diagnostic",
					"warlord_primary_ability_not_melee",
					"NonPlayerCharacter.Abilities",
					text,
					stringBuilder.ToString(),
					(template == null) ? string.Empty : template.Name);
			}
			catch
			{
			}
		}
	}
}
