using Magicka.GameLogic.Entities;
using Magicka.GameLogic.GameStates;

namespace Magicka.CommunityPatch
{
	internal static class NetworkEntityHandleGuard
	{
		// Resolve deliberately accepts allocated inactive entities. Spawn messages
		// use those handles before Initialize/AddEntity. Normal action packets must
		// use NetworkLifecycleCompatibility.ResolveActive instead.
		internal static Entity Resolve(int handle, string side, string reason, bool emitTelemetry)
		{
			Entity entity = Entity.GetFromHandle(handle);
			if (entity != null && !entity.IsDisposed && entity.PlayState != null)
			{
				return entity;
			}

			if (emitTelemetry)
			{
				PatchTelemetry.SendNetworkGuardDrop(side, "EntityHandle", string.Empty, string.Empty, reason, string.Format("handle={0}", handle));
			}
			return null;
		}

		internal static Entity ResolveDamageTarget(int handle, string side, string reason, bool emitTelemetry)
		{
			Entity entity = NetworkLifecycleCompatibility.ResolveActive(handle, side, reason, emitTelemetry);
			if (entity == null)
			{
				return null;
			}
			if (entity.Body != null)
			{
				return entity;
			}

			if (emitTelemetry)
			{
				PatchTelemetry.SendNetworkGuardDrop(side, "EntityHandle", string.Empty, string.Empty, reason, string.Format("handle={0}", handle));
			}
			return null;
		}

		internal static bool IsUsableWorldSyncSpawnNpc(int handle, PlayState playState)
		{
			const string reason = "world_sync_spawn_npc_missing_or_unusable_entity";
			Entity entity = NetworkEntityHandleGuard.Resolve(handle, "client", reason, true);
			if (entity == null)
			{
				return false;
			}

			NonPlayerCharacter npc = entity as NonPlayerCharacter;
			if (npc != null && npc.PlayState == playState)
			{
				return true;
			}

			PatchTelemetry.SendNetworkGuardDrop(
				"client",
				"WorldSync",
				string.Empty,
				string.Empty,
				reason,
				string.Format("handle={0}", handle));
			return false;
		}
	}
}
