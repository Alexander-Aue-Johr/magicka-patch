using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NonPlayerCharacterTeardownPatch
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static Type npcType;
        private static FieldInfo instancesField;
        private static FieldInfo cacheField;
        private static FieldInfo agentField;
        private static FieldInfo fairyField;
        private static FieldInfo summonMasterField;
        private static FieldInfo abilitiesField;
        private static FieldInfo spellField;
        private static FieldInfo summonedField;
        private static FieldInfo undeadField;
        private static FieldInfo flamerField;
        private static FieldInfo summonedEffectField;
        private static MethodInfo isDisposedGetter;
        private static MethodInfo despawnedSummonMethod;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo stopEffectMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "NonPlayerCharacter final teardown",
                "org.magickacommunitypatch.npc-final-teardown",
                FindClearHandles,
                target => typeof(NonPlayerCharacterTeardownPatch).GetMethod(
                    "Prefix"));

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type character = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            npcType = assembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            Type effectManager = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);

            instancesField = RequireField(
                entity,
                "mInstances",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            cacheField = RequireField(
                npcType,
                "sCache",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            agentField = RequireField(npcType, "mAI", InstanceFields);
            fairyField = RequireField(npcType, "mFairy", InstanceFields);
            summonMasterField = RequireField(
                npcType,
                "mSummonMaster",
                InstanceFields);
            abilitiesField = RequireField(
                npcType,
                "mAbilities",
                InstanceFields);
            spellField = RequireField(
                npcType,
                "mSpellToCast",
                InstanceFields);
            summonedField = RequireField(
                npcType,
                "mSummoned",
                InstanceFields);
            undeadField = RequireField(
                npcType,
                "mUndeadSummon",
                InstanceFields);
            flamerField = RequireField(
                npcType,
                "mFlamerSummon",
                InstanceFields);
            summonedEffectField = RequireField(
                npcType,
                "mSummonedEffect",
                InstanceFields);

            PropertyInfo isDisposed = entity.GetProperty(
                "IsDisposed",
                BindingFlags.Instance | BindingFlags.Public);
            isDisposedGetter = isDisposed == null
                ? null
                : isDisposed.GetGetMethod();
            despawnedSummonMethod = character.GetMethod(
                "OnDespawnedSummon",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { npcType },
                null);
            PropertyInfo managerInstance = effectManager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerInstanceGetter = managerInstance == null
                ? null
                : managerInstance.GetGetMethod();
            stopEffectMethod = effectManager.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { summonedEffectField.FieldType.MakeByRefType() },
                null);

            MethodInfo clearHandles = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (!typeof(IList).IsAssignableFrom(instancesField.FieldType) ||
                !typeof(IList).IsAssignableFrom(cacheField.FieldType) ||
                summonedField.FieldType != typeof(bool) ||
                undeadField.FieldType != typeof(bool) ||
                flamerField.FieldType != typeof(bool) ||
                (isDisposedGetter != null &&
                    isDisposedGetter.ReturnType != typeof(bool)) ||
                despawnedSummonMethod == null ||
                despawnedSummonMethod.ReturnType != typeof(void) ||
                effectManagerInstanceGetter == null ||
                effectManagerInstanceGetter.ReturnType != effectManager ||
                stopEffectMethod == null ||
                stopEffectMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "NonPlayerCharacter teardown contract is incomplete.");
            if (clearHandles == null || clearHandles.ReturnType != typeof(void))
                throw new MissingMethodException(entity.FullName, "ClearHandles");
            return clearHandles;
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

        public static void Prefix()
        {
            ArrayList npcs = new ArrayList();
            AddNpcs(npcs, instancesField.GetValue(null) as IList);
            IList cache = cacheField.GetValue(null) as IList;
            AddNpcs(npcs, cache);
            for (int index = 0; index < npcs.Count; index++)
                CleanupFinal(npcs[index]);
            if (cache != null)
                cache.Clear();
        }

        private static void AddNpcs(ArrayList destination, IList source)
        {
            if (source == null)
                return;
            for (int index = 0; index < source.Count; index++)
            {
                object value = source[index];
                if (value == null || !npcType.IsInstanceOfType(value) ||
                    ContainsReference(destination, value))
                    continue;
                destination.Add(value);
            }
        }

        private static bool ContainsReference(ArrayList values, object value)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (Object.ReferenceEquals(values[index], value))
                    return true;
            }
            return false;
        }

        public static void CleanupFinal(object npc)
        {
            if (npc == null)
                return;

            IList cache = cacheField.GetValue(null) as IList;
            if (cache != null)
                cache.Remove(npc);
            StopSummonedEffect(npc);

            object master = summonMasterField.GetValue(npc);
            summonMasterField.SetValue(npc, null);
            if (master != null)
            {
                try
                {
                    if (isDisposedGetter == null ||
                        !(bool)isDisposedGetter.Invoke(master, null))
                        despawnedSummonMethod.Invoke(
                            master,
                            new object[] { npc });
                }
                catch
                {
                }
            }

            object agent = agentField.GetValue(npc);
            if (agent != null)
                AgentLifecyclePatch.CleanupFinal(agent);
            agentField.SetValue(npc, null);

            object fairy = fairyField.GetValue(npc);
            if (fairy != null)
            {
                try
                {
                    ArrayList listedNpc = new ArrayList();
                    listedNpc.Add(npc);
                    FairyTeardownPatch.CleanupEntities(listedNpc);
                }
                catch
                {
                }
            }
            fairyField.SetValue(npc, null);
            abilitiesField.SetValue(npc, null);
            spellField.SetValue(
                npc,
                Activator.CreateInstance(spellField.FieldType));
            summonedField.SetValue(npc, false);
            undeadField.SetValue(npc, false);
            flamerField.SetValue(npc, false);
        }

        private static void StopSummonedEffect(object npc)
        {
            object[] arguments = new object[]
            {
                summonedEffectField.GetValue(npc)
            };
            try
            {
                object manager = effectManagerInstanceGetter.Invoke(null, null);
                stopEffectMethod.Invoke(manager, arguments);
            }
            catch
            {
            }
            try
            {
                summonedEffectField.SetValue(npc, arguments[0]);
            }
            catch
            {
            }
        }
    }
}
