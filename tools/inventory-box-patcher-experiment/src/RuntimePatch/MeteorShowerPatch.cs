using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class MeteorShowerPatch
    {
        private const string MeteorTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MeteorShower";

        private static FieldInfo ttlField;
        private static FieldInfo sceneField;
        private static FieldInfo playStateField;
        private static FieldInfo ownerField;
        private static FieldInfo rumbleField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo lightTargetIntensitySetter;
        private static MethodInfo isStoppingGetter;
        private static MethodInfo stopMethod;
        private static object asAuthored;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "MeteorShower vector play-state release",
                "org.magickacommunitypatch.meteor-shower-vector-play-state-release",
                FindVectorExecute,
                typeof(MeteorShowerPatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "MeteorShower owner play-state release",
                "org.magickacommunitypatch.meteor-shower-owner-play-state-release",
                FindOwnerExecute,
                typeof(MeteorShowerPatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition SceneDefinition =
            RuntimePatchDefinition.Transpile(
                "MeteorShower current scene selection",
                "org.magickacommunitypatch.meteor-shower-current-scene",
                FindPrivateExecute,
                typeof(MeteorShowerPatch).GetMethod("CurrentPlayStateTranspiler"));

        internal static readonly RuntimePatchDefinition MissileDefinition =
            RuntimePatchDefinition.Transpile(
                "MeteorShower current missile play state",
                "org.magickacommunitypatch.meteor-shower-current-missile-state",
                FindUpdate,
                typeof(MeteorShowerPatch).GetMethod("CurrentPlayStateTranspiler"));

        internal static readonly RuntimePatchDefinition CleanupDefinition =
            RuntimePatchDefinition.Prefix(
                "MeteorShower singleton reference cleanup",
                "org.magickacommunitypatch.meteor-shower-reference-cleanup",
                FindOnRemove,
                CreateCleanupPrefix);

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, false);
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, true);
        }

        private static MethodInfo FindPublicExecute(
            Assembly targetAssembly,
            bool ownerOverload)
        {
            Type meteor;
            Type playState;
            Configure(targetAssembly, out meteor, out playState);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            MethodInfo execute = meteor.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playState },
                null);
            if (execute == null || execute.ReturnType != typeof(bool))
                throw new MissingMethodException(meteor.FullName, "Execute");
            return execute;
        }

        private static MethodInfo FindPrivateExecute(Assembly targetAssembly)
        {
            Type meteor;
            Type ignoredPlayState;
            Configure(targetAssembly, out meteor, out ignoredPlayState);
            MethodInfo execute = meteor.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (execute == null || execute.ReturnType != typeof(bool))
                throw new MissingMethodException(meteor.FullName, "Execute");
            return execute;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type meteor;
            Type ignoredPlayState;
            Configure(targetAssembly, out meteor, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo update = meteor.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(meteor.FullName, "Update");
            return update;
        }

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type meteor;
            Type ignoredPlayState;
            Configure(targetAssembly, out meteor, out ignoredPlayState);
            MethodInfo onRemove = meteor.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (onRemove == null || onRemove.ReturnType != typeof(void))
                throw new MissingMethodException(meteor.FullName, "OnRemove");
            return onRemove;
        }

        private static MethodInfo CreateCleanupPrefix(MethodInfo target)
        {
            return typeof(MeteorShowerPatch).GetMethod("CleanupPrefix");
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type meteor,
            out Type playState)
        {
            meteor = targetAssembly.GetType(MeteorTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type scene = targetAssembly.GetType("Magicka.Levels.GameScene", true);
            Type spellCaster = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");

            ttlField = RequireField(meteor, "mTTL", typeof(float));
            sceneField = RequireField(meteor, "mScene", scene);
            playStateField = RequireField(meteor, "mPlayState", playState);
            ownerField = RequireField(meteor, "mOwner", spellCaster);
            rumbleField = RequireField(meteor, "mRumble", cue);

            PropertyInfo recentPlayState = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");

            PropertyInfo lightTargetIntensity = scene.GetProperty(
                "LightTargetIntensity",
                BindingFlags.Instance | BindingFlags.Public);
            lightTargetIntensitySetter = lightTargetIntensity == null
                ? null
                : lightTargetIntensity.GetSetMethod();
            if (lightTargetIntensitySetter == null ||
                lightTargetIntensity == null ||
                lightTargetIntensity.PropertyType != typeof(float))
                throw new MissingMethodException(
                    scene.FullName,
                    "set_LightTargetIntensity");

            PropertyInfo isStopping = cue.GetProperty(
                "IsStopping",
                BindingFlags.Instance | BindingFlags.Public);
            isStoppingGetter = isStopping == null
                ? null
                : isStopping.GetGetMethod();
            stopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            if (isStoppingGetter == null ||
                isStoppingGetter.ReturnType != typeof(bool))
                throw new MissingMethodException(cue.FullName, "get_IsStopping");
            if (stopMethod == null || stopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cue.FullName, "Stop");
            asAuthored = Enum.Parse(stopOptions, "AsAuthored");
        }

        public static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple MeteorShower play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one MeteorShower play-state assignment, found " +
                    writes + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> CurrentPlayStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != 1)
                throw new InvalidOperationException(
                    "Expected one MeteorShower play-state read, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureTranspiler()
        {
            Type meteor = RuntimeMember.FindLoadedType(MeteorTypeName);
            Type ignoredMeteor;
            Type ignoredPlayState;
            Configure(
                meteor.Assembly,
                out ignoredMeteor,
                out ignoredPlayState);
        }

        public static bool CleanupPrefix(object __instance)
        {
            RequireCleanupContract();
            ttlField.SetValue(__instance, 0f);
            object scene = sceneField.GetValue(__instance);
            object rumble = rumbleField.GetValue(__instance);
            sceneField.SetValue(__instance, null);
            playStateField.SetValue(__instance, null);
            ownerField.SetValue(__instance, null);
            rumbleField.SetValue(__instance, null);
            Invoke(lightTargetIntensitySetter, scene, new object[] { 1f });
            if (rumble != null &&
                !(bool)Invoke(isStoppingGetter, rumble, new object[0]))
                Invoke(stopMethod, rumble, new object[] { asAuthored });
            return false;
        }

        private static void RequireCleanupContract()
        {
            if (ttlField == null || sceneField == null ||
                playStateField == null || ownerField == null ||
                rumbleField == null || lightTargetIntensitySetter == null ||
                isStoppingGetter == null || stopMethod == null ||
                asAuthored == null)
                throw new InvalidOperationException(
                    "MeteorShower cleanup contract has not been initialized.");
        }

        private static object Invoke(
            MethodInfo method,
            object instance,
            object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
