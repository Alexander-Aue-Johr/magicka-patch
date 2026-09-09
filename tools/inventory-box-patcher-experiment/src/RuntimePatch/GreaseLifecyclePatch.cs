using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GreaseLifecyclePatch
    {
        private const string GreaseTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease";

        private static FieldInfo greasePlayStateField;
        private static FieldInfo greaseFieldPlayStateField;
        private static FieldInfo entityPlayStateField;
        private static FieldInfo greaseCacheField;
        private static FieldInfo fieldCacheField;
        private static FieldInfo greaseOwnerField;
        private static FieldInfo greaseEffectField;
        private static FieldInfo greaseCueField;
        private static FieldInfo fieldOwnerField;
        private static FieldInfo fieldBurnEffectField;
        private static FieldInfo fieldParticleEffectField;
        private static FieldInfo animatedPartField;
        private static FieldInfo hitListOwnerField;
        private static FieldInfo hitListField;
        private static MethodInfo ownerPlayStateGetter;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo clearHandlesMethod;
        private static MethodInfo effectManagerGetter;
        private static MethodInfo stopEffectMethod;
        private static MethodInfo cueIsPlayingGetter;
        private static MethodInfo cueStopMethod;
        private static object stopAsAuthored;

        internal static readonly RuntimePatchDefinition GreaseExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Grease play-state release",
                "org.magickacommunitypatch.grease-play-state-release",
                FindGreaseExecute,
                typeof(GreaseLifecyclePatch).GetMethod(
                    "GreaseExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition GreaseUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Grease current play state",
                "org.magickacommunitypatch.grease-current-play-state",
                FindGreaseUpdate,
                typeof(GreaseLifecyclePatch).GetMethod(
                    "GreaseUpdateTranspiler"));

        internal static readonly RuntimePatchDefinition FieldConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "GreaseField constructor play-state release",
                "org.magickacommunitypatch.grease-field-constructor-state-release",
                FindFieldConstructor,
                typeof(GreaseLifecyclePatch).GetMethod(
                    "FieldConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition FieldInitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "GreaseField current play state",
                "org.magickacommunitypatch.grease-field-current-play-state",
                FindFieldInitialize,
                typeof(GreaseLifecyclePatch).GetMethod(
                    "FieldInitializeTranspiler"));

        internal static readonly RuntimePatchDefinition CacheCleanupDefinition =
            RuntimePatchDefinition.Transpile(
                "Grease level cache cleanup",
                "org.magickacommunitypatch.grease-cache-cleanup",
                FindPlayStateDispose,
                typeof(GreaseLifecyclePatch).GetMethod(
                    "PlayStateDisposeTranspiler"));

        private static MethodInfo FindGreaseExecute(Assembly targetAssembly)
        {
            Type grease;
            Type field;
            Type playState;
            Configure(targetAssembly, out grease, out field, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                grease,
                "Execute",
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindGreaseUpdate(Assembly targetAssembly)
        {
            Type grease;
            Type field;
            Type playState;
            Configure(targetAssembly, out grease, out field, out playState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                grease,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static ConstructorInfo FindFieldConstructor(Assembly targetAssembly)
        {
            Type grease;
            Type field;
            Type playState;
            Configure(targetAssembly, out grease, out field, out playState);
            ConstructorInfo constructor = field.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(field.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindFieldInitialize(Assembly targetAssembly)
        {
            Type grease;
            Type field;
            Type playState;
            Configure(targetAssembly, out grease, out field, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type animatedPart = targetAssembly.GetType(
                "Magicka.Levels.AnimatedLevelPart",
                true);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                field,
                "Initialize",
                new Type[]
                {
                    owner,
                    animatedPart,
                    vector.MakeByRefType(),
                    vector.MakeByRefType()
                },
                typeof(void));
        }

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type grease;
            Type field;
            Type playState;
            Configure(targetAssembly, out grease, out field, out playState);
            return RequireMethod(
                playState,
                "Dispose",
                Type.EmptyTypes,
                typeof(void));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type grease,
            out Type field,
            out Type playState)
        {
            grease = targetAssembly.GetType(GreaseTypeName, true);
            field = grease.GetNestedType(
                "GreaseField",
                BindingFlags.Public | BindingFlags.NonPublic);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            if (field == null || !field.IsSubclassOf(entity))
                throw new TypeLoadException(GreaseTypeName + "+GreaseField");

            greasePlayStateField = RequireField(
                grease,
                "mPlayState",
                playState);
            greaseFieldPlayStateField = RequireField(
                field,
                "mPlayState",
                playState);
            entityPlayStateField = RequireField(entity, "mPlayState", playState);
            greaseCacheField = RequireListField(grease, "sCache");
            fieldCacheField = RequireListField(field, "sCache");
            greaseOwnerField = RequireField(grease, "mOwner", null);
            greaseEffectField = RequireField(grease, "mEffect", null);
            greaseCueField = RequireField(grease, "mCue", null);
            fieldOwnerField = RequireField(field, "mOwner", null);
            fieldBurnEffectField = RequireField(field, "mBurnEffect", null);
            fieldParticleEffectField = RequireField(
                field,
                "mParticleEffect",
                null);
            animatedPartField = field.GetField(
                "mAnimatedLevelPart",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            hitListOwnerField = RequireField(field, "sHitListOwner", field);
            hitListField = RequireField(field, "sHitlist", null);

            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            ownerPlayStateGetter = RequireProperty(owner, "PlayState")
                .GetGetMethod();
            recentPlayStateGetter = RequireProperty(
                playState,
                "RecentPlayState").GetGetMethod();
            if (ownerPlayStateGetter == null ||
                ownerPlayStateGetter.ReturnType != playState ||
                recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "Grease play-state accessors");

            clearHandlesMethod = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(entity.FullName, "ClearHandles");

            Type effectManager = targetAssembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            effectManagerGetter = RequireProperty(effectManager, "Instance")
                .GetGetMethod();
            stopEffectMethod = effectManager.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { greaseEffectField.FieldType.MakeByRefType() },
                null);
            if (effectManagerGetter == null ||
                effectManagerGetter.ReturnType != effectManager ||
                stopEffectMethod == null ||
                stopEffectMethod.ReturnType != typeof(void) ||
                fieldBurnEffectField.FieldType != greaseEffectField.FieldType ||
                fieldParticleEffectField.FieldType != greaseEffectField.FieldType)
                throw new MissingMethodException(effectManager.FullName, "Stop");

            Type cue = greaseCueField.FieldType;
            cueIsPlayingGetter = RequireProperty(cue, "IsPlaying").GetGetMethod();
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");
            cueStopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            if (cueIsPlayingGetter == null ||
                cueIsPlayingGetter.ReturnType != typeof(bool) ||
                cueStopMethod == null || cueStopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cue.FullName, "Stop");
            stopAsAuthored = Enum.Parse(stopOptions, "AsAuthored");
        }

        public static IEnumerable<CodeInstruction> GreaseExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return RemoveArgumentAssignment(
                instructions,
                greasePlayStateField,
                OpCodes.Ldarg_2,
                "Grease.Execute");
        }

        public static IEnumerable<CodeInstruction> GreaseUpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return ReplaceReads(
                instructions,
                greasePlayStateField,
                4,
                "Grease.Update");
        }

        public static IEnumerable<CodeInstruction> FieldConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return RemoveArgumentAssignment(
                instructions,
                greaseFieldPlayStateField,
                OpCodes.Ldarg_1,
                "GreaseField constructor");
        }

        public static IEnumerable<CodeInstruction> FieldInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 3; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                MethodInfo getter = result[index - 1].operand as MethodInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, greaseFieldPlayStateField))
                    writes++;
                if (result[index - 3].opcode != OpCodes.Ldarg_0 ||
                    result[index - 2].opcode != OpCodes.Ldarg_1 ||
                    result[index - 1].opcode != OpCodes.Callvirt ||
                    !Object.Equals(getter, ownerPlayStateGetter) ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, greaseFieldPlayStateField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one GreaseField play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 3; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return new List<CodeInstruction>(ReplaceReads(
                result,
                greaseFieldPlayStateField,
                1,
                "GreaseField.Initialize"));
        }

        public static IEnumerable<CodeInstruction> PlayStateDisposeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in PlayState.Dispose, " +
                    "found " + matches + ".");
            result.Insert(
                anchor,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(GreaseLifecyclePatch).GetMethod("CleanupCaches")));
            return result;
        }

        private static IEnumerable<CodeInstruction> RemoveArgumentAssignment(
            IEnumerable<CodeInstruction> instructions,
            FieldInfo targetField,
            OpCode argumentLoad,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, targetField))
                    writes++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != argumentLoad ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, targetField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one " + methodName +
                    " play-state assignment, found " + writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            FieldInfo targetField,
            int expected,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, targetField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " " + methodName +
                    " play-state reads, found " + replacements + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type grease = RuntimeMember.FindLoadedType(GreaseTypeName);
            Type ignoredGrease;
            Type ignoredField;
            Type ignoredPlayState;
            Configure(
                grease.Assembly,
                out ignoredGrease,
                out ignoredField,
                out ignoredPlayState);
        }

        public static void CleanupCaches()
        {
            CleanupGreaseCache();
            CleanupFieldCache();
        }

        private static void CleanupGreaseCache()
        {
            IList cache = greaseCacheField.GetValue(null) as IList;
            if (cache == null)
                return;
            object[] snapshot = new object[cache.Count];
            cache.CopyTo(snapshot, 0);
            cache.Clear();
            for (int index = 0; index < snapshot.Length; index++)
            {
                object grease = snapshot[index];
                if (grease == null)
                    continue;
                StopEffect(grease, greaseEffectField);
                StopCue(grease, greaseCueField);
                greaseOwnerField.SetValue(grease, null);
                greasePlayStateField.SetValue(grease, null);
            }
        }

        private static void CleanupFieldCache()
        {
            IList cache = fieldCacheField.GetValue(null) as IList;
            if (cache != null)
            {
                object[] snapshot = new object[cache.Count];
                cache.CopyTo(snapshot, 0);
                cache.Clear();
                for (int index = 0; index < snapshot.Length; index++)
                {
                    object field = snapshot[index];
                    if (field == null)
                        continue;
                    StopEffect(field, fieldBurnEffectField);
                    StopEffect(field, fieldParticleEffectField);
                    fieldOwnerField.SetValue(field, null);
                    if (animatedPartField != null)
                        animatedPartField.SetValue(field, null);
                    greaseFieldPlayStateField.SetValue(field, null);
                    entityPlayStateField.SetValue(field, null);
                    EntityPhysicsCleanupPatch.DetachEntity(field);
                }
            }
            hitListOwnerField.SetValue(null, null);
            object hitList = hitListField.GetValue(null);
            if (hitList != null)
            {
                MethodInfo clear = hitList.GetType().GetMethod(
                    "Clear",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (clear == null || clear.ReturnType != typeof(void))
                    throw new MissingMethodException(
                        hitList.GetType().FullName,
                        "Clear");
                clear.Invoke(hitList, null);
            }
        }

        private static void StopEffect(object owner, FieldInfo field)
        {
            try
            {
                object manager = effectManagerGetter.Invoke(null, null);
                object[] arguments = new object[] { field.GetValue(owner) };
                stopEffectMethod.Invoke(manager, arguments);
            }
            catch
            {
            }
            field.SetValue(owner, Activator.CreateInstance(field.FieldType));
        }

        private static void StopCue(object owner, FieldInfo field)
        {
            object cue = field.GetValue(owner);
            if (cue != null)
            {
                try
                {
                    if ((bool)cueIsPlayingGetter.Invoke(cue, null))
                        cueStopMethod.Invoke(cue, new object[] { stopAsAuthored });
                }
                catch
                {
                }
            }
            field.SetValue(owner, null);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null ||
                (expectedType != null && field.FieldType != expectedType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static FieldInfo RequireListField(Type type, string name)
        {
            FieldInfo field = RequireField(type, name, null);
            if (!typeof(IList).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanRead)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] arguments,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                arguments,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
