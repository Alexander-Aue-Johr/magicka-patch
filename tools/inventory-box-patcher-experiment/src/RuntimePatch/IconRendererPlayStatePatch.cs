using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class IconRendererPlayStatePatch
    {
        private const string RendererTypeName =
            "Magicka.GameLogic.UI.IconRenderer";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "IconRenderer constructor play-state release",
                "org.magickacommunitypatch.icon-renderer-constructor-state",
                FindConstructor,
                typeof(IconRendererPlayStatePatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "IconRenderer initialization play-state release",
                "org.magickacommunitypatch.icon-renderer-initialize-state",
                FindInitialize,
                typeof(IconRendererPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition SetterDefinition =
            RuntimePatchDefinition.Transpile(
                "IconRenderer current play-state magick selection",
                "org.magickacommunitypatch.icon-renderer-current-state",
                FindSetter,
                typeof(IconRendererPlayStatePatch).GetMethod(
                    "SetterTranspiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type renderer = assembly.GetType(RendererTypeName, false);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                false);
            return renderer != null && playState != null &&
                renderer.GetField(
                    "mPlayState",
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly) != null;
        }

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Type renderer;
            Type playState;
            Configure(assembly, out renderer, out playState);
            Type player = assembly.GetType(
                "Magicka.GameLogic.Player",
                true);
            ConstructorInfo constructor = renderer.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { player, playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(
                    renderer.FullName,
                    ".ctor(Player, PlayState)");
            return constructor;
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type renderer;
            Type playState;
            Configure(assembly, out renderer, out playState);
            return RequireMethod(
                renderer,
                "Initialize",
                new Type[] { playState },
                typeof(void));
        }

        private static MethodInfo FindSetter(Assembly assembly)
        {
            Type renderer;
            Type ignoredPlayState;
            Configure(assembly, out renderer, out ignoredPlayState);
            PropertyInfo property = renderer.GetProperty(
                "TomeMagick",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            MethodInfo setter = property == null
                ? null
                : property.GetSetMethod();
            if (setter == null || setter.ReturnType != typeof(void))
                throw new MissingMethodException(
                    renderer.FullName,
                    "set_TomeMagick");
            return setter;
        }

        private static void Configure(
            Assembly assembly,
            out Type renderer,
            out Type playState)
        {
            renderer = assembly.GetType(RendererTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = renderer.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null || playStateField.FieldType != playState)
                throw new MissingFieldException(renderer.FullName, "mPlayState");

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

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveSingleStore(instructions, OpCodes.Ldarg_2, "constructor");
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveSingleStore(instructions, OpCodes.Ldarg_1, "Initialize");
        }

        public static IEnumerable<CodeInstruction> SetterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, playStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "IconRenderer play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != 1)
                throw new InvalidOperationException(
                    "Expected one IconRenderer play-state read, found " +
                    reads + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> RemoveSingleStore(
            IEnumerable<CodeInstruction> instructions,
            OpCode argumentLoad,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, playStateField))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != argumentLoad)
                    throw new InvalidOperationException(
                        "IconRenderer " + methodName +
                        " play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one IconRenderer " + methodName +
                    " play-state store, found " + stores + ".");
            return result;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
