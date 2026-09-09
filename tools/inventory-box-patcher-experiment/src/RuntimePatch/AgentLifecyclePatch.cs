using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AgentLifecyclePatch
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static Type agentType;
        private static Type npcType;
        private static FieldInfo entityInstancesField;
        private static FieldInfo npcAgentField;
        private static FieldInfo ownerField;
        private static FieldInfo leaderField;
        private static FieldInfo leaderAgeField;
        private static FieldInfo targetsField;
        private static FieldInfo targetAgesField;
        private static FieldInfo attackedByField;
        private static FieldInfo pathField;
        private static FieldInfo statesField;
        private static FieldInfo stateAgeField;
        private static FieldInfo eventsField;
        private static FieldInfo eventIndexField;
        private static FieldInfo eventDelayField;
        private static FieldInfo loopEventsField;
        private static FieldInfo nextAbilityField;
        private static FieldInfo busyAbilityField;
        private static FieldInfo lastSpellAbilityField;
        private static FieldInfo spellRecoveryField;
        private static FieldInfo priorityTargetField;
        private static FieldInfo lastTargetField;
        private static FieldInfo fuzzyEntitiesField;
        private static FieldInfo fuzzyAbilitiesField;
        private static MethodInfo deadGetter;
        private static MethodInfo disableMethod;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Prefix(
                "Agent owner refresh",
                "org.magickacommunitypatch.agent-initialize-lifecycle",
                FindInitialize,
                target => typeof(AgentLifecyclePatch).GetMethod(
                    "InitializePrefix"));

        internal static readonly RuntimePatchDefinition ResetDefinition =
            RuntimePatchDefinition.Postfix(
                "Agent reusable state reset",
                "org.magickacommunitypatch.agent-reset-lifecycle",
                FindReset,
                target => typeof(AgentLifecyclePatch).GetMethod(
                    "ResetPostfix"));

        internal static readonly RuntimePatchDefinition DisableDefinition =
            RuntimePatchDefinition.Postfix(
                "Agent disabled state cleanup",
                "org.magickacommunitypatch.agent-disable-lifecycle",
                FindDisable,
                target => typeof(AgentLifecyclePatch).GetMethod(
                    "DisablePostfix"));

        internal static readonly RuntimePatchDefinition ChooseTargetDefinition =
            RuntimePatchDefinition.Postfix(
                "Agent target scratch cleanup",
                "org.magickacommunitypatch.agent-target-scratch-lifecycle",
                FindChooseTarget,
                target => typeof(AgentLifecyclePatch).GetMethod(
                    "ChooseTargetPostfix"));

        internal static readonly RuntimePatchDefinition FinalTeardownDefinition =
            RuntimePatchDefinition.Prefix(
                "Agent final teardown",
                "org.magickacommunitypatch.agent-final-teardown",
                FindClearHandles,
                target => typeof(AgentLifecyclePatch).GetMethod(
                    "ClearHandlesPrefix"));

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            ResolveContracts(assembly);
            Type template = assembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            return RequireMethod(
                agentType,
                "Initialize",
                new Type[] { npcType, template });
        }

        private static MethodInfo FindReset(Assembly assembly)
        {
            ResolveContracts(assembly);
            return RequireMethod(agentType, "Reset", Type.EmptyTypes);
        }

        private static MethodInfo FindDisable(Assembly assembly)
        {
            ResolveContracts(assembly);
            return disableMethod;
        }

        private static MethodInfo FindChooseTarget(Assembly assembly)
        {
            ResolveContracts(assembly);
            MethodInfo[] methods = agentType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            MethodInfo found = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ChooseTarget" || parameters.Length != 2 ||
                    !parameters[0].ParameterType.IsByRef ||
                    !parameters[1].ParameterType.IsByRef)
                    continue;
                if (found != null)
                    throw new AmbiguousMatchException(
                        agentType.FullName + ".ChooseTarget");
                found = method;
            }
            if (found == null || found.ReturnType != typeof(float))
                throw new MissingMethodException(
                    agentType.FullName,
                    "ChooseTarget");
            return found;
        }

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            ResolveContracts(assembly);
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            MethodInfo method = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(entity.FullName, "ClearHandles");
            return method;
        }

        private static void ResolveContracts(Assembly assembly)
        {
            agentType = assembly.GetType("Magicka.AI.Agent", true);
            npcType = assembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type damageable = assembly.GetType(
                "Magicka.GameLogic.Entities.IDamageable",
                true);

            entityInstancesField = RequireField(
                entity,
                "mInstances",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            npcAgentField = RequireField(npcType, "mAI", InstanceFields);
            ownerField = RequireField(agentType, "mOwner", InstanceFields);
            leaderField = RequireField(agentType, "mLeader", InstanceFields);
            leaderAgeField = RequireField(agentType, "mLeaderAge", InstanceFields);
            targetsField = RequireField(agentType, "mTargets", InstanceFields);
            targetAgesField = RequireField(
                agentType,
                "mTargetAges",
                InstanceFields);
            attackedByField = RequireField(
                agentType,
                "mBeenAttackedBy",
                InstanceFields);
            pathField = RequireField(agentType, "mPath", InstanceFields);
            statesField = RequireField(agentType, "mStates", InstanceFields);
            stateAgeField = RequireField(agentType, "mStateAge", InstanceFields);
            eventsField = RequireField(agentType, "mEvents", InstanceFields);
            eventIndexField = RequireField(
                agentType,
                "mEventIndex",
                InstanceFields);
            eventDelayField = RequireField(
                agentType,
                "mCurrentEventDelay",
                InstanceFields);
            loopEventsField = RequireField(
                agentType,
                "mLoopEvents",
                InstanceFields);
            nextAbilityField = RequireField(
                agentType,
                "mNextAbility",
                InstanceFields);
            busyAbilityField = RequireField(
                agentType,
                "mBusyAbility",
                InstanceFields);
            lastSpellAbilityField = RequireField(
                agentType,
                "mLastSpellAbility",
                InstanceFields);
            spellRecoveryField = RequireField(
                agentType,
                "mSpellRecovery",
                InstanceFields);
            priorityTargetField = RequireField(
                agentType,
                "mPriorityTarget",
                InstanceFields);
            lastTargetField = RequireField(
                agentType,
                "mLastTarget",
                InstanceFields);
            fuzzyEntitiesField = RequireField(
                agentType,
                "mFuzzySortEntities",
                InstanceFields);
            fuzzyAbilitiesField = RequireField(
                agentType,
                "mFuzzySortAbilities",
                InstanceFields);
            PropertyInfo dead = damageable.GetProperty(
                "Dead",
                BindingFlags.Instance | BindingFlags.Public);
            deadGetter = dead == null ? null : dead.GetGetMethod();
            disableMethod = RequireMethod(
                agentType,
                "Disable",
                Type.EmptyTypes);

            if (!typeof(IList).IsAssignableFrom(entityInstancesField.FieldType) ||
                npcAgentField.FieldType != agentType ||
                ownerField.FieldType != npcType ||
                leaderField.FieldType != agentType ||
                leaderAgeField.FieldType != typeof(float) ||
                eventIndexField.FieldType != typeof(int) ||
                eventDelayField.FieldType != typeof(float) ||
                loopEventsField.FieldType != typeof(bool) ||
                !fuzzyEntitiesField.FieldType.IsArray ||
                !fuzzyAbilitiesField.FieldType.IsArray ||
                deadGetter == null || deadGetter.ReturnType != typeof(bool))
                throw new MissingMemberException(
                    "Agent lifecycle field contract is incomplete.");
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static void InitializePrefix(object __instance, object iOwner)
        {
            ownerField.SetValue(__instance, iOwner);
        }

        public static void ResetPostfix(object __instance)
        {
            ClearReusableState(__instance);
        }

        public static void DisablePostfix(object __instance)
        {
            ClearFuzzyState(__instance);
            object owner = ownerField.GetValue(__instance);
            if (owner == null || !(bool)deadGetter.Invoke(owner, null))
                return;
            ClearDeadOwnerState(__instance);
        }

        public static void ChooseTargetPostfix(object __instance)
        {
            ClearFuzzyState(__instance);
        }

        public static void ClearHandlesPrefix()
        {
            IList entities = entityInstancesField.GetValue(null) as IList;
            if (entities == null)
                return;
            object[] snapshot = new object[entities.Count];
            entities.CopyTo(snapshot, 0);
            for (int index = 0; index < snapshot.Length; index++)
            {
                object npc = snapshot[index];
                if (npc == null || !npcType.IsInstanceOfType(npc))
                    continue;
                object agent = npcAgentField.GetValue(npc);
                if (agent != null)
                    CleanupFinal(agent);
            }
        }

        public static void CleanupFinal(object agent)
        {
            if (agent == null)
                return;
            try
            {
                disableMethod.Invoke(agent, null);
            }
            catch
            {
            }
            ownerField.SetValue(agent, null);
            leaderField.SetValue(agent, null);
            leaderAgeField.SetValue(agent, 0f);
            TryClear(targetsField.GetValue(agent));
            TryClear(targetAgesField.GetValue(agent));
            TryClear(attackedByField.GetValue(agent));
            TryClear(pathField.GetValue(agent));
            TryClear(statesField.GetValue(agent));
            TryClear(stateAgeField.GetValue(agent));
            eventsField.SetValue(agent, null);
            nextAbilityField.SetValue(agent, null);
            busyAbilityField.SetValue(agent, null);
            lastSpellAbilityField.SetValue(agent, null);
            spellRecoveryField.SetValue(agent, null);
            priorityTargetField.SetValue(agent, null);
            lastTargetField.SetValue(agent, null);
            ClearFuzzyState(agent);
        }

        private static void ClearReusableState(object agent)
        {
            leaderField.SetValue(agent, null);
            leaderAgeField.SetValue(agent, 0f);
            lastTargetField.SetValue(agent, null);
            lastSpellAbilityField.SetValue(agent, null);
            eventsField.SetValue(agent, null);
            eventIndexField.SetValue(agent, 0);
            eventDelayField.SetValue(agent, 0f);
            loopEventsField.SetValue(agent, false);
            ClearFuzzyState(agent);
        }

        private static void ClearDeadOwnerState(object agent)
        {
            TryClear(targetsField.GetValue(agent));
            TryClear(targetAgesField.GetValue(agent));
            nextAbilityField.SetValue(agent, null);
            busyAbilityField.SetValue(agent, null);
            lastSpellAbilityField.SetValue(agent, null);
            priorityTargetField.SetValue(agent, null);
            lastTargetField.SetValue(agent, null);
            leaderField.SetValue(agent, null);
            leaderAgeField.SetValue(agent, 0f);
        }

        private static void ClearFuzzyState(object agent)
        {
            ClearArray(fuzzyEntitiesField.GetValue(agent) as Array);
            ClearArray(fuzzyAbilitiesField.GetValue(agent) as Array);
        }

        private static void ClearArray(Array values)
        {
            if (values == null)
                return;
            for (int index = 0; index < values.Length; index++)
                values.SetValue(null, index);
        }

        private static void TryClear(object collection)
        {
            if (collection == null)
                return;
            try
            {
                MethodInfo clear = collection.GetType().GetMethod(
                    "Clear",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (clear != null)
                    clear.Invoke(collection, null);
            }
            catch
            {
            }
        }
    }
}
