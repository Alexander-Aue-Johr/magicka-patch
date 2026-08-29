using System.Collections.Generic;
using Magicka.CommunityPatch;
using Magicka.GameLogic.Entities.Items;

namespace Magicka.GameLogic.Entities.Abilities.SpecialAbilities
{
	// Readable equivalent of the helper and call-site edit injected into
	// JudgementSpray.SpawnProjectile.
	internal partial class JudgementSpray
	{
		private static ConditionCollection
			CommunityPatchTakeConditionCollectionLocked(
				Queue<ConditionCollection> cache)
		{
			if (cache.Count != 0)
			{
				return cache.Dequeue();
			}

			PatchTelemetry.SendRuntimeGuard(
				"magicka_patch_runtime_recovery",
				"judgement_spray_condition_cache_empty_recovered",
				"ProjectileSpell.sCachedConditions",
				"Magicka.GameLogic.Entities.Abilities.SpecialAbilities.JudgementSpray",
				"Allocated a replacement ConditionCollection and continued"
					+ " projectile spawn.",
				string.Empty);
			return new ConditionCollection();
		}

		// Existing SpawnProjectile queue lock:
		// conditions = CommunityPatchTakeConditionCollectionLocked(
		//     ProjectileSpell.sCachedConditions);
	}
}
