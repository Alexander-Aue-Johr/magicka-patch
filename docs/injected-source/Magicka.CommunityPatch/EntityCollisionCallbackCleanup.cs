// Readable source equivalent for the helper injected into Magicka.exe and its
// call from Magicka.GameLogic.Entities.Entity.Dispose.

using System.Reflection;
using JigLibX.Collision;

namespace Magicka.CommunityPatch
{
	internal static class CollisionCallbackCleanup
	{
		private static readonly FieldInfo sCallbackField =
			ResolveCallbackField();

		private static FieldInfo ResolveCallbackField()
		{
			try
			{
				return typeof(CollisionSkin).GetField(
					"callbackFn",
					BindingFlags.Instance | BindingFlags.NonPublic);
			}
			catch
			{
				return null;
			}
		}

		internal static void Clear(CollisionSkin skin)
		{
			if (skin == null || sCallbackField == null)
			{
				return;
			}

			try
			{
				sCallbackField.SetValue(skin, null);
			}
			catch
			{
			}
		}
	}
}

// Injected into Entity.Dispose after the mCollision null check and before
// clearing Collisions, NonCollidables, Tag, Owner, and CollisionSystem:
// CollisionCallbackCleanup.Clear(collision);
