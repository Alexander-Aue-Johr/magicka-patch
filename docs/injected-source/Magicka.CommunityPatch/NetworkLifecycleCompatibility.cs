using System;
using Magicka.GameLogic;
using Magicka.GameLogic.Entities;
using Magicka.GameLogic.GameStates;
using Magicka.Gamers;
using Magicka.Network;
using SteamWrapper;

namespace Magicka.CommunityPatch
{
	/// <summary>
	/// Keeps network packets from reviving or mutating entities that are no longer
	/// part of the active scene. Spawn packets are handled separately because their
	/// target handle intentionally points at an allocated, but not yet active, slot.
	/// </summary>
	internal static class NetworkLifecycleCompatibility
	{
		internal static Entity ResolveActive(int handle, string side, string reason, bool emitTelemetry)
		{
			Entity entity = GetEntitySafely(handle);
			PlayState playState = PlayState.RecentPlayState;
			if (IsActive(entity, playState))
			{
				return entity;
			}

			if (emitTelemetry)
			{
				ReportDrop(side, "EntityHandle", reason, handle, 0, playState, entity);
			}
			return null;
		}

		internal static Entity ResolveForcedSyncAvatar(int playerId, SteamID sender)
		{
			Player[] players = Game.Instance.Players;
			for (int i = 0; i < players.Length; i++)
			{
				Player player = players[i];
				if (player == null || player.ID != playerId)
				{
					continue;
				}

				Avatar avatar = player.Avatar;
				NetworkGamer networkGamer = player.Gamer as NetworkGamer;
				if (avatar != null && networkGamer != null && networkGamer.ClientID.Equals(sender))
				{
					PatchTelemetry.SendNetworkDiagnostic(
						"server",
						"ForcedPlayerStatusSync",
						"forced_player_status_sync_player_resolved",
						"valid_player",
						string.Format("playerId={0}", playerId));
					return avatar;
				}
				break;
			}

			PatchTelemetry.SendNetworkGuardDrop(
				"server",
				"RequestForcedPlayerStatusSync",
				string.Empty,
				string.Empty,
				"forced_player_status_sync_invalid_player_id_or_sender",
				string.Format("playerId={0}", playerId));
			return null;
		}

		internal static ForceSyncPlayerStatusesMessage BuildForcedSyncPlayerStatuses()
		{
			Player[] players = Game.Instance.Players;
			int count = 0;
			for (int i = 0; i < players.Length; i++)
			{
				Player player = players[i];
				if (player != null && player.Avatar != null && player.Gamer is NetworkGamer)
				{
					count++;
				}
			}

			ForceSyncPlayerStatusesMessage message = default(ForceSyncPlayerStatusesMessage);
			message.numPlayers = (short)count;
			message.playerUpdateMessages = new EntityUpdateMessage[count];
			int writeIndex = 0;
			for (int i = 0; i < players.Length; i++)
			{
				Player player = players[i];
				if (player == null || player.Avatar == null || !(player.Gamer is NetworkGamer))
				{
					continue;
				}
				player.Avatar.GetNetworkUpdate(out message.playerUpdateMessages[writeIndex], NetworkState.Server, 1f);
				message.playerUpdateMessages[writeIndex].Handle = (ushort)player.ID;
				writeIndex++;
			}
			PatchTelemetry.SendNetworkDiagnostic(
				"server",
				"ForcedPlayerStatusSync",
				"forced_player_status_sync_response_built",
				count.ToString(),
				string.Format("playerCount={0}; writtenCount={1}", count, writeIndex));
			return message;
		}

		internal static bool CanProcessTriggerAction(ref TriggerActionMessage message)
		{
			PlayState playState = PlayState.RecentPlayState;
			try
			{
				if (playState == null)
				{
					ReportDrop("client", "TriggerAction", "trigger_action_without_playstate", message.Handle, message.Template, null, null);
					return false;
				}

				switch (message.ActionType)
				{
				case TriggerActionType.SpawnNPC:
				case TriggerActionType.SpawnLuggage:
					return ValidateCharacterSpawn(ref message, playState);

				case TriggerActionType.SpawnElemental:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.ElementalEgg",
						null);

				case TriggerActionType.SpawnItem:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.Items.Item",
						null);

				case TriggerActionType.SpawnMagick:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.Items.BookOfMagick",
						null);

				case TriggerActionType.SpawnDamageablePhysicsEntity:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.DamageablePhysicsEntity",
						PhysicsEntityTemplate.GetFromCache(message.Template));

				case TriggerActionType.SpawnGrease:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease+GreaseField",
						null);

				case TriggerActionType.SpawnTornado:
					return ValidateReservedSpawnSlot(
						ref message,
						playState,
						"Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity",
						null);

				case TriggerActionType.Confuse:
				case TriggerActionType.Charm:
					return ValidateActiveTriggerHandle(message.Handle, ref message, playState, "trigger_action_primary_entity_inactive");

				case TriggerActionType.OtherworldlyDischarge:
				case TriggerActionType.OtherworldlyBoltDestroyed:
				case TriggerActionType.StarGaze:
					return ValidateActiveTriggerHandle(message.Handle, ref message, playState, "trigger_action_primary_entity_inactive") &&
						ValidateActiveTriggerHandle(message.Arg, ref message, playState, "trigger_action_secondary_entity_inactive");

				default:
					return true;
				}
			}
			catch (Exception exception)
			{
				PatchTelemetry.SendNetworkGuardException(
					"client",
					"TriggerAction",
					string.Empty,
					string.Empty,
					"trigger_action_validation_exception",
					BuildDetails(message.Handle, message.Template, playState, null),
					exception);
				return false;
			}
		}

		private static bool ValidateCharacterSpawn(ref TriggerActionMessage message, PlayState playState)
		{
			Entity entity = GetEntitySafely(message.Handle);
			if (!(entity is NonPlayerCharacter) || !IsReservedForSpawn(entity, playState))
			{
				ReportDrop("client", "TriggerAction", "trigger_action_character_spawn_invalid_slot", message.Handle, message.Template, playState, entity);
				return false;
			}

			if (CharacterTemplate.GetCachedTemplate(message.Template) == null)
			{
				ReportDrop("client", "TriggerAction", "trigger_action_character_spawn_template_not_cached", message.Handle, message.Template, playState, entity);
				return false;
			}

			return true;
		}

		private static bool ValidateReservedSpawnSlot(
			ref TriggerActionMessage message,
			PlayState playState,
			string expectedEntityType,
			object requiredTemplate)
		{
			Entity entity = GetEntitySafely(message.Handle);
			if (!IsReservedForSpawn(entity, playState))
			{
				ReportDrop("client", "TriggerAction", "trigger_action_spawn_invalid_slot", message.Handle, message.Template, playState, entity);
				return false;
			}

			if (!IsTypeOrSubclass(entity, expectedEntityType))
			{
				ReportDrop("client", "TriggerAction", "trigger_action_spawn_wrong_entity_type", message.Handle, message.Template, playState, entity);
				return false;
			}

			if (message.ActionType == TriggerActionType.SpawnDamageablePhysicsEntity && requiredTemplate == null)
			{
				ReportDrop("client", "TriggerAction", "trigger_action_physics_spawn_template_not_cached", message.Handle, message.Template, playState, entity);
				return false;
			}

			return true;
		}

		private static bool IsTypeOrSubclass(Entity entity, string expectedTypeName)
		{
			if (entity == null || string.IsNullOrEmpty(expectedTypeName))
			{
				return entity != null;
			}

			Type type = entity.GetType();
			while (type != null)
			{
				if (string.Equals(type.FullName, expectedTypeName, StringComparison.Ordinal))
				{
					return true;
				}
				type = type.BaseType;
			}
			return false;
		}

		private static bool ValidateActiveTriggerHandle(
			int handle,
			ref TriggerActionMessage message,
			PlayState playState,
			string reason)
		{
			Entity entity = GetEntitySafely(handle);
			if (IsActive(entity, playState))
			{
				return true;
			}

			ReportDrop("client", "TriggerAction", reason, handle, message.Template, playState, entity);
			return false;
		}

		private static Entity GetEntitySafely(int handle)
		{
			if (handle < 0)
			{
				return null;
			}
			return Entity.GetFromHandle(handle);
		}

		private static bool IsActive(Entity entity, PlayState playState)
		{
			return entity != null &&
				!entity.IsDisposed &&
				playState != null &&
				entity.PlayState == playState &&
				playState.EntityManager != null &&
				playState.EntityManager.Contains(entity);
		}

		private static bool IsReservedForSpawn(Entity entity, PlayState playState)
		{
			if (entity == null || entity.IsDisposed || playState == null ||
				entity.PlayState != playState || playState.EntityManager == null)
			{
				return false;
			}

			if (!playState.EntityManager.Contains(entity))
			{
				return true;
			}

			string typeName = entity.GetType().FullName;
			if (typeName == "Magicka.GameLogic.Entities.Items.Item" ||
				typeName == "Magicka.GameLogic.Entities.ElementalEgg" ||
				typeName == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease+GreaseField" ||
				typeName == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity")
			{
				ReportActiveReuse(entity, playState, typeName);
				return true;
			}

			NonPlayerCharacter nonPlayerCharacter = entity as NonPlayerCharacter;
			if (nonPlayerCharacter == null || !nonPlayerCharacter.Dead)
			{
				return false;
			}
			ReportActiveReuse(entity, playState, typeName);
			return true;
		}

		private static void ReportActiveReuse(Entity entity, PlayState playState, string typeName)
		{
			PatchTelemetry.SendNetworkDiagnostic(
				"client",
				"TriggerAction",
				"trigger_action_active_slot_reused",
				typeName,
				BuildDetails(entity.Handle, 0, playState, entity));
		}

		private static void ReportDrop(
			string side,
			string packetType,
			string reason,
			int handle,
			int template,
			PlayState playState,
			Entity entity)
		{
			PatchTelemetry.SendNetworkGuardDrop(
				side,
				packetType,
				string.Empty,
				string.Empty,
				reason,
				BuildDetails(handle, template, playState, entity));
		}

		private static string BuildDetails(
			int handle,
			int template,
			PlayState playState,
			Entity entity)
		{
			bool active = false;
			try
			{
				active = IsActive(entity, playState);
			}
			catch
			{
			}

			return string.Format(
				"handle={0}; template={1}; playStateNull={2}; initialized={3}; worldSync={4}; entityType={5}; entityDisposed={6}; entityActive={7}",
				handle,
				template,
				playState == null,
				playState != null && playState.Initialized,
				playState != null && playState.WorldSync,
				entity == null ? "<null>" : entity.GetType().FullName,
				entity != null && entity.IsDisposed,
				active);
		}
	}
}
