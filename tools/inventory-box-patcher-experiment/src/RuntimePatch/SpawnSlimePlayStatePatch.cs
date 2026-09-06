using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SpawnSlimePlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SpawnSlime play-state reference release",
                "org.magickacommunitypatch.spawn-slime-play-state-release",
                FindExecute,
                typeof(SpawnSlimePlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition OverkillExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SpawnSlimeOverkill play-state reference release",
                "org.magickacommunitypatch.spawn-slime-overkill-play-state-release",
                FindOverkillExecute,
                typeof(SpawnSlimePlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition CreateEntitiesDefinition =
            RuntimePatchDefinition.Transpile(
                "SpawnSlime current NavMesh",
                "org.magickacommunitypatch.spawn-slime-current-nav-mesh",
                FindCreateEntities,
                typeof(SpawnSlimePlayStatePatch).GetMethod("CurrentPlayStateTranspiler"));

        internal static readonly RuntimePatchDefinition SpawnSlimesDefinition =
            RuntimePatchDefinition.Transpile(
                "SpawnSlime SpawnSlimes current NavMesh",
                "org.magickacommunitypatch.spawn-slimes-current-nav-mesh",
                FindSpawnSlimes,
                typeof(SpawnSlimePlayStatePatch).GetMethod("CurrentPlayStateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type spawnSlimeType;
            Type overkillType;
            Type ownerType;
            Type elementsType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spawnSlimeType,
                out overkillType,
                out ownerType,
                out elementsType,
                out playStateType);
            return RequireExecute(spawnSlimeType, ownerType, elementsType, playStateType);
        }

        private static MethodInfo FindOverkillExecute(Assembly targetAssembly)
        {
            Type spawnSlimeType;
            Type overkillType;
            Type ownerType;
            Type elementsType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spawnSlimeType,
                out overkillType,
                out ownerType,
                out elementsType,
                out playStateType);
            return RequireExecute(overkillType, ownerType, elementsType, playStateType);
        }

        private static MethodInfo FindCreateEntities(Assembly targetAssembly)
        {
            Type spawnSlimeType;
            Type overkillType;
            Type ownerType;
            Type elementsType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spawnSlimeType,
                out overkillType,
                out ownerType,
                out elementsType,
                out playStateType);
            return RequireDeclaredMethod(spawnSlimeType, "CreateEntities", 1);
        }

        private static MethodInfo FindSpawnSlimes(Assembly targetAssembly)
        {
            Type spawnSlimeType;
            Type overkillType;
            Type ownerType;
            Type elementsType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spawnSlimeType,
                out overkillType,
                out ownerType,
                out elementsType,
                out playStateType);
            MethodInfo method = spawnSlimeType.GetMethod(
                "SpawnSlimes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(int), typeof(int) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(spawnSlimeType.FullName, "SpawnSlimes");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type spawnSlimeType,
            out Type overkillType,
            out Type ownerType,
            out Type elementsType,
            out Type playStateType)
        {
            spawnSlimeType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlime",
                true);
            overkillType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlimeOverkill",
                true);
            ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            elementsType = targetAssembly.GetType("Magicka.Elements", true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);

            legacyPlayStateField = spawnSlimeType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(spawnSlimeType.FullName, "mPlayState");

            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
        }

        private static MethodInfo RequireExecute(
            Type type,
            Type ownerType,
            Type elementsType,
            Type playStateType)
        {
            MethodInfo method = type.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { ownerType, elementsType, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(type.FullName, "Execute");
            return method;
        }

        private static MethodInfo RequireDeclaredMethod(
            Type type,
            string name,
            int parameterCount)
        {
            MethodInfo result = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name != name ||
                    methods[index].GetParameters().Length != parameterCount)
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple " + type.FullName + "." + name + " methods matched.");
                result = methods[index];
            }
            if (result == null || result.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return result;
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_3 ||
                    result[index].opcode != OpCodes.Stfld ||
                    field == null || field != legacyPlayStateField)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple SpawnSlime play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "SpawnSlime play-state assignment was not found.");

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
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int read = -1;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    field == null || field != legacyPlayStateField)
                    continue;
                if (read >= 0)
                    throw new InvalidOperationException(
                        "Multiple SpawnSlime play-state reads matched.");
                read = index;
            }
            if (read < 0)
                throw new InvalidOperationException(
                    "SpawnSlime play-state read was not found.");

            result[read - 1].opcode = OpCodes.Call;
            result[read - 1].operand = recentPlayStateGetter;
            result[read].opcode = OpCodes.Nop;
            result[read].operand = null;
            return result;
        }
    }
}
