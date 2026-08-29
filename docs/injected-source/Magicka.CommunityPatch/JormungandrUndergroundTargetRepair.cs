namespace Magicka.GameLogic.Entities.Bosses
{
	// Readable equivalent of the guard injected into the original state method.
	internal partial class Jormungandr
	{
		private sealed partial class UndergroundState
		{
			private void CommunityPatchOnUpdateEquivalent(
				float iDeltaTime,
				Jormungandr iOwner)
			{
				// Existing timer and death handling run first.
				iOwner.SelectTarget(TargettingType.Random);
				if (iOwner.mTarget == null)
				{
					return;
				}

				// Existing warning, positioning, animation, and
				// state-transition logic continues unchanged.
			}
		}
	}
}
