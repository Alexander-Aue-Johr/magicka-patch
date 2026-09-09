using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class BarrierTeardownPatch
    {
        private static Type barrierType;
        private static FieldInfo entityInstancesField;
        private static FieldInfo barrierCacheField;
        private static FieldInfo hitListInstancesField;
        private static FieldInfo hitListCacheField;
        private static FieldInfo soundCueField;
        private static FieldInfo iceControllerField;
        private static FieldInfo earthControllerField;
        private static FieldInfo effectField;
        private static FieldInfo spawnDeathEffectField;
        private static FieldInfo barrierHitListField;
        private static FieldInfo hitListOwnersField;
        private static FieldInfo hitListValueField;
        private static FieldInfo ownerField;
        private static FieldInfo runeModelField;
        private static FieldInfo resistancesField;
        private static FieldInfo statusEffectsField;
        private static FieldInfo iceRenderDataField;
        private static FieldInfo earthRenderDataField;
        private static FieldInfo runeRenderDataField;
        private static MethodInfo cueIsStoppingGetter;
        private static MethodInfo cueStopMethod;
        private static object audioStopAsAuthored;
        private static MethodInfo controllerStopMethod;
        private static MethodInfo controllerSkeletonSetter;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo effectStopMethod;
        private static MethodInfo hitListClearMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Barrier level teardown",
                "org.magickacommunitypatch.barrier-level-teardown",
                FindClearHandles,
                target => typeof(BarrierTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entityType = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            barrierType = assembly.GetType(
                "Magicka.GameLogic.Entities.Barrier",
                true);
            Type hitListType = assembly.GetType(
                "Magicka.GameLogic.Entities.Barrier+HitListWithBarriers",
                true);
            Type effectManagerType = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);

            entityInstancesField = RequireField(entityType, "mInstances", true);
            barrierCacheField = RequireField(barrierType, "mCache", true);
            hitListInstancesField = RequireField(hitListType, "sInstances", true);
            hitListCacheField = RequireField(hitListType, "sHitListCache", true);
            soundCueField = RequireField(barrierType, "mSoundCue", false);
            iceControllerField = RequireField(
                barrierType,
                "mIceAnimationController",
                false);
            earthControllerField = RequireField(
                barrierType,
                "mEarthAnimationController",
                false);
            effectField = RequireField(barrierType, "mEffect", false);
            spawnDeathEffectField = RequireField(
                barrierType,
                "mSpawnDeathEffectReference",
                false);
            barrierHitListField = RequireField(barrierType, "mHitList", false);
            hitListOwnersField = RequireField(hitListType, "mOwners", false);
            hitListValueField = RequireField(hitListType, "mHitList", false);
            ownerField = RequireField(barrierType, "mOwner", false);
            runeModelField = RequireField(barrierType, "mRuneModel", false);
            resistancesField = RequireField(barrierType, "mResistances", false);
            statusEffectsField = RequireField(barrierType, "mStatusEffects", false);
            iceRenderDataField = RequireField(barrierType, "mIceRenderData", false);
            earthRenderDataField = RequireField(barrierType, "mEarthRenderData", false);
            runeRenderDataField = RequireField(barrierType, "mRuneRenderData", false);

            PropertyInfo isStopping = soundCueField.FieldType.GetProperty(
                "IsStopping",
                BindingFlags.Instance | BindingFlags.Public);
            cueIsStoppingGetter = isStopping == null
                ? null
                : isStopping.GetGetMethod();
            cueStopMethod = FindSingleArgumentMethod(
                soundCueField.FieldType,
                "Stop");
            Type stopOptions = cueStopMethod == null
                ? null
                : cueStopMethod.GetParameters()[0].ParameterType;
            if (stopOptions != null && stopOptions.IsEnum)
                audioStopAsAuthored = Enum.Parse(stopOptions, "AsAuthored");

            controllerStopMethod = iceControllerField.FieldType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            PropertyInfo skeleton = iceControllerField.FieldType.GetProperty(
                "Skeleton",
                BindingFlags.Instance | BindingFlags.Public);
            controllerSkeletonSetter = skeleton == null
                ? null
                : skeleton.GetSetMethod();

            PropertyInfo effectManagerInstance = effectManagerType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerInstanceGetter = effectManagerInstance == null
                ? null
                : effectManagerInstance.GetGetMethod();
            effectStopMethod = effectManagerType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { effectField.FieldType.MakeByRefType() },
                null);
            hitListClearMethod = hitListValueField.FieldType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);

            MethodInfo clearHandles = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (!typeof(IEnumerable).IsAssignableFrom(entityInstancesField.FieldType) ||
                !typeof(IList).IsAssignableFrom(barrierCacheField.FieldType) ||
                !typeof(IList).IsAssignableFrom(hitListInstancesField.FieldType) ||
                !typeof(IList).IsAssignableFrom(hitListCacheField.FieldType) ||
                iceControllerField.FieldType != earthControllerField.FieldType ||
                effectField.FieldType != spawnDeathEffectField.FieldType ||
                cueIsStoppingGetter == null ||
                cueIsStoppingGetter.ReturnType != typeof(bool) ||
                cueStopMethod == null || audioStopAsAuthored == null ||
                controllerStopMethod == null ||
                controllerSkeletonSetter == null ||
                effectManagerInstanceGetter == null ||
                effectManagerInstanceGetter.ReturnType != effectManagerType ||
                effectStopMethod == null ||
                effectStopMethod.ReturnType != typeof(void) ||
                hitListClearMethod == null ||
                hitListClearMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "Barrier level teardown members are incomplete.");
            if (clearHandles == null || clearHandles.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");
            return clearHandles;
        }

        public static void Prefix()
        {
            try
            {
                CleanupAll();
            }
            catch
            {
            }
        }

        public static void CleanupAll()
        {
            ArrayList barriers = new ArrayList();
            AddBarriers(barriers, entityInstancesField.GetValue(null));
            object cacheValue = barrierCacheField.GetValue(null);
            AddBarriers(barriers, cacheValue);
            ClearList(cacheValue);

            for (int index = 0; index < barriers.Count; index++)
            {
                try
                {
                    CleanupBarrier(barriers[index]);
                }
                catch
                {
                }
            }

            ClearList(hitListInstancesField.GetValue(null));
            ClearList(hitListCacheField.GetValue(null));
        }

        private static void AddBarriers(ArrayList barriers, object source)
        {
            IEnumerable values = source as IEnumerable;
            if (values == null)
                return;
            foreach (object value in values)
            {
                if (value == null || !barrierType.IsInstanceOfType(value))
                    continue;
                bool found = false;
                for (int index = 0; index < barriers.Count; index++)
                {
                    if (Object.ReferenceEquals(barriers[index], value))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    barriers.Add(value);
            }
        }

        private static void CleanupBarrier(object barrier)
        {
            StopCue(barrier);
            StopController(barrier, iceControllerField);
            StopController(barrier, earthControllerField);
            StopEffect(barrier, effectField);
            StopEffect(barrier, spawnDeathEffectField);
            ReleaseHitList(barrier);
            ownerField.SetValue(barrier, null);
            runeModelField.SetValue(barrier, null);
            resistancesField.SetValue(barrier, null);
            statusEffectsField.SetValue(barrier, null);
            iceRenderDataField.SetValue(barrier, null);
            earthRenderDataField.SetValue(barrier, null);
            runeRenderDataField.SetValue(barrier, null);
        }

        private static void StopCue(object barrier)
        {
            object cue = soundCueField.GetValue(barrier);
            try
            {
                if (cue != null &&
                    !(bool)cueIsStoppingGetter.Invoke(cue, null))
                    cueStopMethod.Invoke(
                        cue,
                        new object[] { audioStopAsAuthored });
            }
            catch
            {
            }
            soundCueField.SetValue(barrier, null);
        }

        private static void StopController(object barrier, FieldInfo field)
        {
            object controller = field.GetValue(barrier);
            if (controller == null)
                return;
            try
            {
                controllerStopMethod.Invoke(controller, null);
            }
            catch
            {
            }
            try
            {
                controllerSkeletonSetter.Invoke(
                    controller,
                    new object[] { null });
            }
            catch
            {
            }
            field.SetValue(barrier, null);
        }

        private static void StopEffect(object barrier, FieldInfo field)
        {
            object[] arguments = new object[] { field.GetValue(barrier) };
            try
            {
                object manager = effectManagerInstanceGetter.Invoke(null, null);
                effectStopMethod.Invoke(manager, arguments);
            }
            catch
            {
            }
            field.SetValue(barrier, arguments[0]);
        }

        private static void ReleaseHitList(object barrier)
        {
            object hitList = barrierHitListField.GetValue(barrier);
            if (hitList == null)
                return;
            IList owners = hitListOwnersField.GetValue(hitList) as IList;
            if (owners != null)
            {
                owners.Remove(barrier);
                if (owners.Count == 0)
                {
                    object values = hitListValueField.GetValue(hitList);
                    if (values != null)
                    {
                        try
                        {
                            hitListClearMethod.Invoke(values, null);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            barrierHitListField.SetValue(barrier, null);
        }

        private static void ClearList(object value)
        {
            IList list = value as IList;
            if (list != null)
                list.Clear();
        }

        private static MethodInfo FindSingleArgumentMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name == name &&
                    methods[index].GetParameters().Length == 1)
                    return methods[index];
            }
            return null;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            bool isStatic)
        {
            BindingFlags scope = isStatic
                ? BindingFlags.Static
                : BindingFlags.Instance;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    scope | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }
}
