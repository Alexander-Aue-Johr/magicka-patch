using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ArrowRainPlayStatePatch
    {
        private const string ArrowRainTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ArrowRain";

        private static FieldInfo playStateField;
        private static FieldInfo sceneField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo levelGetter;
        private static MethodInfo currentSceneGetter;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain vector play-state release",
                "org.magickacommunitypatch.arrow-rain-vector-release",
                FindVectorExecute,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain owner play-state release",
                "org.magickacommunitypatch.arrow-rain-owner-release",
                FindOwnerExecute,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain scene reference release",
                "org.magickacommunitypatch.arrow-rain-scene-release",
                FindPrivateExecute,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition LaunchDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain current launch play state",
                "org.magickacommunitypatch.arrow-rain-current-launch-state",
                FindLaunch,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "LaunchTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain current update play state",
                "org.magickacommunitypatch.arrow-rain-current-update-state",
                FindUpdate,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition RemoveDefinition =
            RuntimePatchDefinition.Transpile(
                "ArrowRain current removal scene",
                "org.magickacommunitypatch.arrow-rain-current-removal-scene",
                FindOnRemove,
                typeof(ArrowRainPlayStatePatch).GetMethod(
                    "OnRemoveTranspiler"));

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
            Type arrowRain;
            Type playState;
            Configure(assembly, out arrowRain, out playState);
            Type firstParameter = ownerOverload
                ? assembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                arrowRain,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { firstParameter, playState },
                typeof(bool));
        }

        private static MethodInfo FindPrivateExecute(Assembly assembly)
        {
            Type arrowRain;
            Type ignoredPlayState;
            Configure(assembly, out arrowRain, out ignoredPlayState);
            return RequireMethod(
                arrowRain,
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(bool));
        }

        private static MethodInfo FindLaunch(Assembly assembly)
        {
            Type arrowRain;
            Type ignoredPlayState;
            Configure(assembly, out arrowRain, out ignoredPlayState);
            return RequireMethod(
                arrowRain,
                "Launch",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(void));
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type arrowRain;
            Type ignoredPlayState;
            Configure(assembly, out arrowRain, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                arrowRain,
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindOnRemove(Assembly assembly)
        {
            Type arrowRain;
            Type ignoredPlayState;
            Configure(assembly, out arrowRain, out ignoredPlayState);
            return RequireMethod(
                arrowRain,
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(void));
        }

        private static void Configure(
            Assembly assembly,
            out Type arrowRain,
            out Type playState)
        {
            arrowRain = assembly.GetType(ArrowRainTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            playStateField = RequireField(
                arrowRain,
                "mPlayState",
                playState);
            sceneField = RequireField(arrowRain, "mScene", scene);
            recentPlayStateGetter = RequireGetter(
                playState,
                "RecentPlayState",
                playState,
                BindingFlags.Static | BindingFlags.Public);
            levelGetter = RequireGetter(
                playState,
                "Level",
                level,
                BindingFlags.Instance | BindingFlags.Public);
            currentSceneGetter = RequireGetter(
                level,
                "CurrentScene",
                scene,
                BindingFlags.Instance | BindingFlags.Public);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireGetter(
            Type type,
            string name,
            Type returnType,
            BindingFlags flags)
        {
            PropertyInfo property = type.GetProperty(name, flags);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
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
                if (result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldarg_2 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one ArrowRain play-state assignment, found " +
                    writes + ".");
            NopRange(result, assignment - 2, assignment);
            return result;
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 5; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, sceneField))
                    writes++;
                if (result[index - 5].opcode == OpCodes.Ldarg_0 &&
                    result[index - 4].opcode == OpCodes.Ldarg_0 &&
                    result[index - 3].opcode == OpCodes.Ldfld &&
                    Object.Equals(
                        result[index - 3].operand as FieldInfo,
                        playStateField) &&
                    IsCall(result[index - 2], levelGetter) &&
                    IsCall(result[index - 1], currentSceneGetter) &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, sceneField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one ArrowRain scene assignment, found " +
                    writes + ".");
            NopRange(result, assignment - 5, assignment);
            return result;
        }

        public static IEnumerable<CodeInstruction> LaunchTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 1, "Launch");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(instructions, 4, "Update");
        }

        public static IEnumerable<CodeInstruction> OnRemoveTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int read = -1;
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(
                        result[index].operand as FieldInfo,
                        sceneField))
                    continue;
                read = index;
                reads++;
            }
            if (read < 0 || reads != 1)
                throw new InvalidOperationException(
                    "Expected one ArrowRain removal scene read, found " +
                    reads + ".");
            result[read - 1].opcode = OpCodes.Call;
            result[read - 1].operand = recentPlayStateGetter;
            result[read].opcode = OpCodes.Callvirt;
            result[read].operand = levelGetter;
            result.Insert(
                read + 1,
                new CodeInstruction(OpCodes.Callvirt, currentSceneGetter));
            return result;
        }

        private static IEnumerable<CodeInstruction> ReplacePlayStateReads(
            IEnumerable<CodeInstruction> instructions,
            int expected,
            string operation)
        {
            ConfigureTranspiler();
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
                    "Expected " + expected + " ArrowRain " + operation +
                    " play-state reads, found " + replacements + ".");
            return result;
        }

        private static void ConfigureTranspiler()
        {
            Type arrowRain = RuntimeMember.FindLoadedType(ArrowRainTypeName);
            Type ignoredArrowRain;
            Type ignoredPlayState;
            Configure(
                arrowRain.Assembly,
                out ignoredArrowRain,
                out ignoredPlayState);
        }

        private static bool IsCall(
            CodeInstruction instruction,
            MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand as MethodInfo, method);
        }

        private static void NopRange(
            IList<CodeInstruction> instructions,
            int first,
            int last)
        {
            for (int index = first; index <= last; index++)
            {
                instructions[index].opcode = OpCodes.Nop;
                instructions[index].operand = null;
            }
        }
    }
}
