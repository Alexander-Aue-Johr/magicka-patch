using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class StarfallPlayStatePatch
    {
        private static FieldInfo legacyPlayState;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Starfall unused play-state release",
                "org.magickacommunitypatch.starfall-play-state-release",
                FindExecute,
                typeof(StarfallPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Starfall current play-state update",
                "org.magickacommunitypatch.starfall-current-play-state",
                FindUpdate,
                typeof(StarfallPlayStatePatch).GetMethod("UpdateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type starfallType;
            Type playStateType;
            Configure(targetAssembly, out starfallType, out playStateType);
            MethodInfo target = null;
            MethodInfo[] methods = starfallType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "Execute" &&
                    methods[index].ReturnType == typeof(bool) &&
                    parameters.Length == 4 &&
                    parameters[0].ParameterType.FullName ==
                        "Magicka.GameLogic.Entities.ISpellCaster" &&
                    parameters[1].ParameterType == playStateType &&
                    parameters[2].ParameterType.FullName ==
                        "Microsoft.Xna.Framework.Vector3" &&
                    parameters[3].ParameterType == typeof(bool))
                    target = RequireSingle(target, methods[index], "Starfall Execute");
            }
            if (target == null)
                throw new MissingMethodException(starfallType.FullName, "Execute");
            return target;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type starfallType;
            Type playStateType;
            Configure(targetAssembly, out starfallType, out playStateType);
            MethodInfo target = null;
            MethodInfo[] methods = starfallType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "Update" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType.FullName == "PolygonHead.DataChannel" &&
                    parameters[1].ParameterType == typeof(float))
                    target = RequireSingle(target, methods[index], "Starfall Update");
            }
            if (target == null)
                throw new MissingMethodException(starfallType.FullName, "Update");
            return target;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type starfallType,
            out Type playStateType)
        {
            starfallType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Starfall",
                true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayState = starfallType.GetField(
                "sPlayState",
                BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (legacyPlayState == null || legacyPlayState.FieldType != playStateType)
                throw new MissingFieldException(starfallType.FullName, "sPlayState");
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(playStateType.FullName, "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Stsfld ||
                    field == null || field != legacyPlayState ||
                    result[index - 1].opcode != OpCodes.Ldarg_2)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple Starfall play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "Starfall play-state assignment was not found.");

            for (int index = assignment - 1; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Ldsfld ||
                    field == null || field != legacyPlayState)
                    continue;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                replacements++;
            }
            if (replacements != 4)
                throw new InvalidOperationException(
                    "Expected four Starfall play-state reads, found " + replacements + ".");
            return result;
        }

        private static MethodInfo RequireSingle(
            MethodInfo current,
            MethodInfo candidate,
            string name)
        {
            if (current != null)
                throw new InvalidOperationException("Multiple " + name + " methods matched.");
            return candidate;
        }
    }
}
