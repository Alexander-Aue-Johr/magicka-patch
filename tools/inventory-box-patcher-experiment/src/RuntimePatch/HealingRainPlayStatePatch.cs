using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class HealingRainPlayStatePatch
    {
        private const string HealingRainTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.HealingRain";

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
                "HealingRain vector play-state release",
                "org.magickacommunitypatch.healing-rain-vector-release",
                FindVectorExecute,
                typeof(HealingRainPlayStatePatch).GetMethod(
                    "ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "HealingRain owner play-state release",
                "org.magickacommunitypatch.healing-rain-owner-release",
                FindOwnerExecute,
                typeof(HealingRainPlayStatePatch).GetMethod(
                    "ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "HealingRain current execute play state",
                "org.magickacommunitypatch.healing-rain-current-execute-state",
                FindPrivateExecute,
                typeof(HealingRainPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "HealingRain current update play state",
                "org.magickacommunitypatch.healing-rain-current-update-state",
                FindUpdate,
                typeof(HealingRainPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition CleanupDefinition =
            RuntimePatchDefinition.Prefix(
                "HealingRain scene and caster cleanup",
                "org.magickacommunitypatch.healing-rain-cleanup",
                FindOnRemove,
                CreateCleanupPrefix);

        private static MethodInfo FindVectorExecute(Assembly assembly)
        {
            return FindPublicExecute(assembly, false);
        }

        private static MethodInfo FindOwnerExecute(Assembly assembly)
        {
            return FindPublicExecute(assembly, true);
        }

        private static MethodInfo FindPublicExecute(
            Assembly assembly,
            bool ownerOverload)
        {
            Type healingRain;
            Type playState;
            ConfigurePlayState(assembly, out healingRain, out playState);
            Type firstParameter = ownerOverload
                ? assembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                healingRain,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { firstParameter, playState },
                typeof(bool));
        }

        private static MethodInfo FindPrivateExecute(Assembly assembly)
        {
            Type healingRain;
            Type ignoredPlayState;
            ConfigurePlayState(
                assembly,
                out healingRain,
                out ignoredPlayState);
            return RequireMethod(
                healingRain,
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(bool));
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type healingRain;
            Type ignoredPlayState;
            ConfigurePlayState(
                assembly,
                out healingRain,
                out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                healingRain,
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindOnRemove(Assembly assembly)
        {
            Type healingRain = assembly.GetType(HealingRainTypeName, true);
            ConfigureCleanup(assembly, healingRain);
            return RequireMethod(
                healingRain,
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(void));
        }

        private static MethodInfo CreateCleanupPrefix(MethodInfo target)
        {
            return typeof(HealingRainPlayStatePatch).GetMethod(
                "CleanupPrefix");
        }

        private static void ConfigurePlayState(
            Assembly assembly,
            out Type healingRain,
            out Type playState)
        {
            healingRain = assembly.GetType(HealingRainTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = RequireField(
                healingRain,
                "mPlayState",
                playState);
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
            Assembly assembly,
            Type healingRain)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type caster = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type effectManager = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");

            sceneField = RequireField(healingRain, "mScene", scene);
            casterField = RequireField(healingRain, "mCaster", caster);
            ambienceField = RequireField(healingRain, "mAmbience", cue);
            effectField = RequireAnyField(healingRain, "mEffect");

            cueIsStoppingGetter = RequireGetter(
                cue,
                "IsStopping",
                typeof(bool));
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
            lightIntensitySetter = light == null
                ? null
                : light.GetSetMethod();
            if (cueStopMethod == null ||
                cueStopMethod.ReturnType != typeof(void))
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

        private static MethodInfo RequireGetter(
            Type type,
            string name,
            Type returnType)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod();
            if (getter == null || getter.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, "get_" + name);
            return getter;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            BindingFlags flags,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                flags,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
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
                if (result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldarg_2 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one HealingRain play-state assignment, found " +
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
            string operation)
        {
            ConfigurePlayStateTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(
                        result[index].operand as FieldInfo,
                        playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " HealingRain " + operation +
                    " play-state reads, found " + replacements + ".");
            return result;
        }

        private static void ConfigurePlayStateTranspiler()
        {
            Type healingRain = RuntimeMember.FindLoadedType(
                HealingRainTypeName);
            Type ignoredHealingRain;
            Type ignoredPlayState;
            ConfigurePlayState(
                healingRain.Assembly,
                out ignoredHealingRain,
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
                Invoke(
                    cueStopMethod,
                    ambience,
                    new object[] { asAuthored });

            object manager = Invoke(
                effectManagerGetter,
                null,
                new object[0]);
            object[] effect = new object[] {
                effectField.GetValue(__instance)
            };
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
                    "HealingRain cleanup contract has not been initialized.");
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
