// Readable source equivalent for the helper injected into Magicka.exe and its
// call from Magicka.GameLogic.Entities.Entity.Dispose.

using System.Reflection;
using JigLibX.Collision;

namespace Magicka.CommunityPatch
{
	internal static class CollisionCallbackCleanup
	{
		private static readonly FieldInfo sCallbackField =
			ResolveField("callbackFn");

		private static readonly FieldInfo sPostCollisionCallbackField =
			ResolveField("postCollisionCallbackFn");

		private static FieldInfo ResolveField(string name)
		{
			try
			{
				return typeof(CollisionSkin).GetField(
					name,
					BindingFlags.Instance | BindingFlags.NonPublic);
			}
			catch
			{
				return null;
			}
		}

		internal static void Clear(CollisionSkin skin)
		{
			if (skin == null)
			{
				return;
			}

			ClearField(sCallbackField, skin);
			ClearField(sPostCollisionCallbackField, skin);
		}

		private static void ClearField(FieldInfo field, CollisionSkin skin)
		{
			if (field == null)
			{
				return;
			}
			try
			{
				field.SetValue(skin, null);
			}
			catch
			{
			}
		}
	}
}

// Called by Entity.DetachPhysicsReferences before clearing Collisions,
// NonCollidables, Tag, Owner, and CollisionSystem.
