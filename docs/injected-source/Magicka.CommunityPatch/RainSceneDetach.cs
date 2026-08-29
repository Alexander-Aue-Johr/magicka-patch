// Readable source equivalent for the focused IL changes in Rain.OnRemove and
// Thunderstorm.OnRemove. The actual types remain in Magicka.exe.

namespace Magicka.GameLogic.Entities.Abilities.SpecialAbilities
{
	public partial class Rain
	{
		public override void OnRemove()
		{
			// Existing ambience and visual-effect cleanup remains unchanged.

			GameScene scene = mScene;
			mScene = null;
			mCaster = null;
			if (scene != null)
			{
				scene.LightTargetIntensity = 1f;
			}
		}
	}

	public partial class Thunderstorm
	{
		public override void OnRemove()
		{
			// Existing bolt, achievement, and ambience cleanup remains unchanged.

			mOwner = null;
		}
	}
}
