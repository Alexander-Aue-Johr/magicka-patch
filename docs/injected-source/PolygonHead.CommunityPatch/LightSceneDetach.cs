// Readable source equivalent for the IL changes in PolygonHead.Lights.Light.
// The actual type remains in PolygonHead.dll; this file documents the focused
// Community Patch replacement used for review and future regeneration.

namespace PolygonHead.Lights
{
	public abstract partial class Light
	{
		// Existing immediate-disable branch after updating transition state.
		private void RemoveImmediatelySourceEquivalent()
		{
			OnRemove();
		}

		// Existing fade-out completion branch after mTime reaches zero.
		private void CompleteFadeOutSourceEquivalent()
		{
			OnRemove();
			mIntensity = 0f;
		}

		protected virtual void OnRemove()
		{
			Scene scene = mScene;
			mScene = null;

			if (scene != null)
			{
				scene.RemoveLight(this);
			}
		}
	}
}
