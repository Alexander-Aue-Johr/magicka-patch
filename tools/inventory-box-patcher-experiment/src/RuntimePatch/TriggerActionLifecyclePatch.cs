using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TriggerActionLifecyclePatch
    {
        private static FieldInfo actionTypeField;
        private static FieldInfo handleField;
        private static FieldInfo templateField;
        private static FieldInfo argumentField;
        private static FieldInfo instancesField;
        private static FieldInfo entityPlayStateField;
        private static FieldInfo entityDisposedField;
        private static FieldInfo recentPlayStateField;
        private static FieldInfo playStateManagerField;
        private static FieldInfo managerEntitiesField;
        private static MethodInfo containsEntity;
        private static MethodInfo characterTemplateGetter;
        private static MethodInfo physicsTemplateGetter;
        private static PropertyInfo npcDeadProperty;
        private static Type npcType;
        private static HashSet<int> characterSpawnActions;
        private static Dictionary<int, string> reservedSpawnTypes;
        private static HashSet<int> activeSingleActions;
        private static HashSet<int> activeDoubleActions;
        private static int damageablePhysicsAction;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "TriggerAction lifecycle validation",
                "org.magickacommunitypatch.trigger-action-lifecycle",
                FindNetworkAction,
                CreatePrefix);

        private static MethodInfo FindNetworkAction(Assembly targetAssembly)
        {
            Type triggerType = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Trigger",
                true);
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.TriggerActionMessage",
                true);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type managerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);
            Type characterTemplateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            Type physicsTemplateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
                true);
            npcType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);

            actionTypeField = RequireField(messageType, "ActionType");
            handleField = RequireField(messageType, "Handle");
            templateField = RequireField(messageType, "Template");
            argumentField = RequireField(messageType, "Arg");
            instancesField = RequireField(entityType, "mInstances");
            entityPlayStateField = RequireField(entityType, "mPlayState");
            entityDisposedField = FindField(entityType, "mDisposed");
            recentPlayStateField = RequireField(playStateType, "sRecentPlayState");
            playStateManagerField = RequireField(playStateType, "mEntityManager");
            managerEntitiesField = RequireField(managerType, "mEntities");
            containsEntity = managerEntitiesField.FieldType.GetMethod(
                "Contains",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { entityType },
                null);
            characterTemplateGetter = characterTemplateType.GetMethod(
                "GetCachedTemplate",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null);
            physicsTemplateGetter = physicsTemplateType.GetMethod(
                "GetFromCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null);
            npcDeadProperty = npcType.GetProperty(
                "Dead",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (containsEntity == null)
                throw new MissingMethodException(
                    managerEntitiesField.FieldType.FullName,
                    "Contains");
            if (characterTemplateGetter == null)
                throw new MissingMethodException(
                    characterTemplateType.FullName,
                    "GetCachedTemplate");
            if (physicsTemplateGetter == null &&
                HasAction("SpawnDamageablePhysicsEntity"))
                throw new MissingMethodException(
                    physicsTemplateType.FullName,
                    "GetFromCache");
            if (npcDeadProperty == null ||
                npcDeadProperty.GetGetMethod(true) == null)
                throw new MissingMemberException(npcType.FullName, "Dead");

            ConfigureActions(targetAssembly);

            MethodInfo method = triggerType.GetMethod(
                "NetworkAction",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    triggerType.FullName,
                    "NetworkAction(TriggerActionMessage&)");
            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = FindField(type, name);
            if (field != null)
                return field;
            throw new MissingFieldException(type.FullName, name);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static void ConfigureActions(Assembly targetAssembly)
        {
            characterSpawnActions = new HashSet<int>();
            reservedSpawnTypes = new Dictionary<int, string>();
            activeSingleActions = new HashSet<int>();
            activeDoubleActions = new HashSet<int>();
            damageablePhysicsAction = Int32.MinValue;

            AddAction(characterSpawnActions, "SpawnNPC");
            AddAction(characterSpawnActions, "SpawnLuggage");
            AddReservedAction(
                targetAssembly,
                "SpawnElemental",
                "Magicka.GameLogic.Entities.ElementalEgg");
            AddReservedAction(
                targetAssembly,
                "SpawnItem",
                "Magicka.GameLogic.Entities.Items.Item");
            AddReservedAction(
                targetAssembly,
                "SpawnMagick",
                "Magicka.GameLogic.Entities.Items.BookOfMagick");
            if (AddReservedAction(
                targetAssembly,
                "SpawnDamageablePhysicsEntity",
                "Magicka.GameLogic.Entities.DamageablePhysicsEntity"))
                damageablePhysicsAction = ActionValue(
                    "SpawnDamageablePhysicsEntity");
            AddReservedAction(
                targetAssembly,
                "SpawnGrease",
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease+GreaseField");
            AddReservedAction(
                targetAssembly,
                "SpawnTornado",
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity");
            AddAction(activeSingleActions, "Confuse");
            AddAction(activeSingleActions, "Charm");
            AddAction(activeDoubleActions, "OtherworldlyDischarge");
            AddAction(activeDoubleActions, "OtherworldlyBoltDestroyed");
            AddAction(activeDoubleActions, "StarGaze");
        }

        private static bool AddReservedAction(
            Assembly targetAssembly,
            string actionName,
            string entityTypeName)
        {
            if (!HasAction(actionName) ||
                targetAssembly.GetType(entityTypeName, false) == null)
                return false;
            reservedSpawnTypes.Add(ActionValue(actionName), entityTypeName);
            return true;
        }

        private static void AddAction(HashSet<int> actions, string name)
        {
            if (HasAction(name))
                actions.Add(ActionValue(name));
        }

        private static bool HasAction(string name)
        {
            return Enum.IsDefined(actionTypeField.FieldType, name);
        }

        private static int ActionValue(string name)
        {
            return Convert.ToInt32(Enum.Parse(actionTypeField.FieldType, name));
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            Type argument = target.GetParameters()[0].ParameterType;
            if (!argument.IsByRef)
                throw new InvalidOperationException(
                    "Trigger.NetworkAction argument is not by-reference.");
            Type adapter = typeof(TriggerActionLifecyclePrefix<>).MakeGenericType(
                argument.GetElementType());
            return adapter.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }

        public static bool ShouldProcess(object message)
        {
            try
            {
                object playState = recentPlayStateField.GetValue(null);
                if (message == null)
                    return false;
                if (playState == null)
                    return Reject(
                        "trigger_action_without_playstate",
                        message,
                        null,
                        null);

                int action = Convert.ToInt32(actionTypeField.GetValue(message));
                if (characterSpawnActions.Contains(action))
                    return ValidateCharacterSpawn(message, playState);

                string expectedType;
                if (reservedSpawnTypes.TryGetValue(action, out expectedType))
                    return ValidateReservedSpawn(
                        message,
                        playState,
                        action,
                        expectedType);

                int handle = Convert.ToInt32(handleField.GetValue(message));
                if (activeSingleActions.Contains(action))
                {
                    object entity = GetEntity(handle);
                    return IsActive(entity, playState) || Reject(
                        "trigger_action_primary_entity_inactive",
                        message,
                        playState,
                        entity);
                }
                if (activeDoubleActions.Contains(action))
                {
                    int argument = Convert.ToInt32(
                        argumentField.GetValue(message));
                    object primary = GetEntity(handle);
                    if (!IsActive(primary, playState))
                        return Reject(
                            "trigger_action_primary_entity_inactive",
                            message,
                            playState,
                            primary);
                    object secondary = GetEntity(argument);
                    return IsActive(secondary, playState) || Reject(
                        "trigger_action_secondary_entity_inactive",
                        message,
                        playState,
                        secondary);
                }
                return true;
            }
            catch (Exception exception)
            {
                RuntimePatchTelemetry.SendNetworkGuardException(
                    "client",
                    "TriggerAction",
                    String.Empty,
                    String.Empty,
                    "trigger_action_validation_exception",
                    String.Empty,
                    exception);
                return false;
            }
        }

        private static bool ValidateCharacterSpawn(
            object message,
            object playState)
        {
            object entity = GetEntity(Convert.ToInt32(
                handleField.GetValue(message)));
            if (!npcType.IsInstanceOfType(entity) ||
                !IsReservedForSpawn(entity, playState))
                return Reject(
                    "trigger_action_character_spawn_invalid_slot",
                    message,
                    playState,
                    entity);
            int template = Convert.ToInt32(templateField.GetValue(message));
            if (characterTemplateGetter.Invoke(
                null,
                new object[] { template }) != null)
                return true;
            return Reject(
                "trigger_action_character_spawn_template_not_cached",
                message,
                playState,
                entity);
        }

        private static bool ValidateReservedSpawn(
            object message,
            object playState,
            int action,
            string expectedType)
        {
            object entity = GetEntity(Convert.ToInt32(
                handleField.GetValue(message)));
            if (!IsReservedForSpawn(entity, playState))
                return Reject(
                    "trigger_action_spawn_invalid_slot",
                    message,
                    playState,
                    entity);
            if (!IsTypeOrSubclass(entity, expectedType))
                return Reject(
                    "trigger_action_spawn_wrong_entity_type",
                    message,
                    playState,
                    entity);
            if (action != damageablePhysicsAction)
                return true;
            int template = Convert.ToInt32(templateField.GetValue(message));
            if (physicsTemplateGetter.Invoke(
                null,
                new object[] { template }) != null)
                return true;
            return Reject(
                "trigger_action_physics_spawn_template_not_cached",
                message,
                playState,
                entity);
        }

        private static object GetEntity(int handle)
        {
            IList instances = instancesField.GetValue(null) as IList;
            if (instances == null || handle < 0 || handle >= instances.Count)
                return null;
            return instances[handle];
        }

        private static bool IsActive(object entity, object playState)
        {
            if (!HasCompatibleLifetime(entity, playState))
                return false;
            object manager = playStateManagerField.GetValue(playState);
            object entities = managerEntitiesField.GetValue(manager);
            return (bool)containsEntity.Invoke(
                entities,
                new object[] { entity });
        }

        private static bool IsReservedForSpawn(
            object entity,
            object playState)
        {
            if (!HasCompatibleLifetime(entity, playState))
                return false;
            object manager = playStateManagerField.GetValue(playState);
            object entities = managerEntitiesField.GetValue(manager);
            bool active = (bool)containsEntity.Invoke(
                entities,
                new object[] { entity });
            if (!active)
                return true;

            string typeName = entity.GetType().FullName;
            if (typeName == "Magicka.GameLogic.Entities.Items.Item" ||
                typeName == "Magicka.GameLogic.Entities.ElementalEgg" ||
                typeName == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease+GreaseField" ||
                typeName == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity")
            {
                ReportActiveReuse(entity, playState, typeName);
                return true;
            }

            if (!npcType.IsInstanceOfType(entity))
                return false;
            bool dead = (bool)npcDeadProperty.GetValue(entity, null);
            if (dead)
                ReportActiveReuse(entity, playState, typeName);
            return dead;
        }

        private static bool Reject(
            string reason,
            object message,
            object playState,
            object entity)
        {
            int handle = message == null
                ? -1
                : Convert.ToInt32(handleField.GetValue(message));
            int template = message == null
                ? 0
                : Convert.ToInt32(templateField.GetValue(message));
            RuntimePatchTelemetry.SendNetworkGuardDrop(
                "client",
                "TriggerAction",
                String.Empty,
                String.Empty,
                reason,
                "handle=" + handle + "; template=" + template +
                    "; playStateNull=" + (playState == null) +
                    "; entityType=" + (entity == null
                        ? "<null>"
                        : entity.GetType().FullName));
            return false;
        }

        private static void ReportActiveReuse(
            object entity,
            object playState,
            string typeName)
        {
            RuntimePatchTelemetry.SendNetworkDiagnostic(
                "client",
                "TriggerAction",
                "trigger_action_active_slot_reused",
                typeName,
                "entityType=" + typeName +
                    "; playStateNull=" + (playState == null));
        }

        private static bool HasCompatibleLifetime(
            object entity,
            object playState)
        {
            if (entity == null || playState == null ||
                (entityDisposedField != null &&
                    (bool)entityDisposedField.GetValue(entity)) ||
                !Object.ReferenceEquals(
                    entityPlayStateField.GetValue(entity),
                    playState))
                return false;
            object manager = playStateManagerField.GetValue(playState);
            return manager != null &&
                managerEntitiesField.GetValue(manager) != null;
        }

        private static bool IsTypeOrSubclass(
            object entity,
            string expectedTypeName)
        {
            if (entity == null)
                return false;
            for (Type type = entity.GetType(); type != null; type = type.BaseType)
            {
                if (String.Equals(
                    type.FullName,
                    expectedTypeName,
                    StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    public static class TriggerActionLifecyclePrefix<TMessage>
    {
        public static bool Prefix(ref TMessage iMsg)
        {
            return TriggerActionLifecyclePatch.ShouldProcess(iMsg);
        }
    }
}
