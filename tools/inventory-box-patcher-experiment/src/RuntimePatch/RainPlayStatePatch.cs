using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RainPlayStatePatch
    {
        private const string RainTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Rain";

        private static FieldInfo playStateField;
        private static FieldInfo sceneField;
        private static FieldInfo casterField;
        private static FieldInfo ambienceField;
        private static FieldInfo effectField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo cueIsStoppingGetter;
        private static MethodInfo cueStopMethod;
        private static MethodInfo effectManagerGetter;
        private static MethodInfo effectStopMethod;
        private static MethodInfo lightIntensitySetter;
        private static object asAuthored;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Rain vector play-state release",
                "org.magickacommunitypatch.rain-vector-play-state-release",
                FindVectorExecute,
                typeof(RainPlayStatePatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Rain owner play-state release",
                "org.magickacommunitypatch.rain-owner-play-state-release",
                FindOwnerExecute,
                typeof(RainPlayStatePatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Rain current execute play state",
                "org.magickacommunitypatch.rain-current-execute-state",
                FindPrivateExecute,
                typeof(RainPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Rain current update play state",
                "org.magickacommunitypatch.rain-current-update-state",
                FindUpdate,
                typeof(RainPlayStatePatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition CleanupDefinition =
            RuntimePatchDefinition.Prefix(
                "Rain scene and caster cleanup",
                "org.magickacommunitypatch.rain-scene-caster-cleanup",
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
            Type rain;
            Type playState;
            ConfigurePlayState(targetAssembly, out rain, out playState);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            MethodInfo method = rain.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playState },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(rain.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindPrivateExecute(Assembly targetAssembly)
        {
            Type rain;
            Type ignoredPlayState;
            ConfigurePlayState(targetAssembly, out rain, out ignoredPlayState);
            MethodInfo method = rain.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(rain.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type rain;
            Type ignoredPlayState;
            ConfigurePlayState(targetAssembly, out rain, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = rain.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(rain.FullName, "Update");
            return method;
        }

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type rain = targetAssembly.GetType(RainTypeName, true);
            ConfigureCleanup(targetAssembly, rain);
            MethodInfo method = rain.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(rain.FullName, "OnRemove");
            return method;
        }

        private static MethodInfo CreateCleanupPrefix(MethodInfo target)
        {
            return typeof(RainPlayStatePatch).GetMethod("CleanupPrefix");
        }

        private static void ConfigurePlayState(
            Assembly targetAssembly,
            out Type rain,
            out Type playState)
        {
            rain = targetAssembly.GetType(RainTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = RequireField(rain, "mPlayState", playState);
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        private static void ConfigureCleanup(
            Assembly targetAssembly,
            Type rain)
        {
            Type scene = targetAssembly.GetType("Magicka.Levels.GameScene", true);
            Type caster = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type effectManager = targetAssembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");

            sceneField = RequireField(rain, "mScene", scene);
            casterField = RequireField(rain, "mCaster", caster);
            ambienceField = RequireField(rain, "mAmbience", cue);
            effectField = RequireAnyField(rain, "mEffect");

            PropertyInfo isStopping = cue.GetProperty(
                "IsStopping",
                BindingFlags.Instance | BindingFlags.Public);
            cueIsStoppingGetter = isStopping == null
                ? null
                : isStopping.GetGetMethod();
            cueStopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            PropertyInfo managerInstance = effectManager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerGetter = managerInstance == null
                ? null
                : managerInstance.GetGetMethod();
            effectStopMethod = effectManager.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { effectField.FieldType.MakeByRefType() },
                null);
            PropertyInfo light = scene.GetProperty(
                "LightTargetIntensity",
                BindingFlags.Instance | BindingFlags.Public);
            lightIntensitySetter = light == null ? null : light.GetSetMethod();
            if (cueIsStoppingGetter == null ||
                cueIsStoppingGetter.ReturnType != typeof(bool))
                throw new MissingMethodException(cue.FullName, "get_IsStopping");
            if (cueStopMethod == null || cueStopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cue.FullName, "Stop");
            if (effectManagerGetter == null ||
                effectManagerGetter.ReturnType != effectManager)
                throw new MissingMethodException(
                    effectManager.FullName,
                    "get_Instance");
            if (effectStopMethod == null ||
                effectStopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(effectManager.FullName, "Stop");
            if (lightIntensitySetter == null ||
                lightIntensitySetter.ReturnType != typeof(void))
                throw new MissingMethodException(
                    scene.FullName,
                    "set_LightTargetIntensity");
            asAuthored = Enum.Parse(stopOptions, "AsAuthored");
        }

        public static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigurePlayStateTranspiler();
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
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one Rain play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 4, "Execute");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 2, "Update");
        }

        private static IEnumerable<CodeInstruction> ReplacePlayStateReads(
            IEnumerable<CodeInstruction> instructions,
            int expected,
            string methodName)
        {
            ConfigurePlayStateTranspiler();
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
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " Rain " + methodName +
                    " play-state reads, found " + replacements + ".");
            return result;
        }

        private static void ConfigurePlayStateTranspiler()
        {
            Type rain = RuntimeMember.FindLoadedType(RainTypeName);
            Type ignoredRain;
            Type ignoredPlayState;
            ConfigurePlayState(
                rain.Assembly,
                out ignoredRain,
                out ignoredPlayState);
        }

        public static bool CleanupPrefix(object __instance)
        {
            RequireCleanupContract();
            object ambience = ambienceField.GetValue(__instance);
            if (ambience != null &&
                !(bool)Invoke(
                    cueIsStoppingGetter,
                    ambience,
                    new object[0]))
                Invoke(cueStopMethod, ambience, new object[] { asAuthored });

            object manager = Invoke(
                effectManagerGetter,
                null,
                new object[0]);
            object[] effect = new object[] { effectField.GetValue(__instance) };
            Invoke(effectStopMethod, manager, effect);
            effectField.SetValue(__instance, effect[0]);

            object scene = sceneField.GetValue(__instance);
            sceneField.SetValue(__instance, null);
            casterField.SetValue(__instance, null);
            if (scene != null)
                Invoke(
                    lightIntensitySetter,
                    scene,
                    new object[] { 1f });
            return false;
        }

        private static void RequireCleanupContract()
        {
            if (sceneField == null || casterField == null ||
                ambienceField == null || effectField == null ||
                cueIsStoppingGetter == null || cueStopMethod == null ||
                effectManagerGetter == null || effectStopMethod == null ||
                lightIntensitySetter == null || asAuthored == null)
                throw new InvalidOperationException(
                    "Rain cleanup contract has not been initialized.");
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
            Type expectedType)
        {
            FieldInfo field = RequireAnyField(type, name);
            if (field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static FieldInfo RequireAnyField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
