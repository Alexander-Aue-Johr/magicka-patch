using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ShieldContentLifetimePatch
    {
        private const string ShieldTypeName =
            "Magicka.GameLogic.Entities.Shield";

        private static MethodInfo playStateContentGetter;
        private static MethodInfo gameInstanceGetter;
        private static MethodInfo gameContentGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Shield global graphics content lifetime",
                "org.magickacommunitypatch.shield-global-content",
                FindConstructor,
                typeof(ShieldContentLifetimePatch).GetMethod("Transpiler"));

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Type shield = Configure(assembly);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            ConstructorInfo constructor = shield.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(shield.FullName, ".ctor");
            return constructor;
        }

        private static Type Configure(Assembly assembly)
        {
            Type shield = assembly.GetType(ShieldTypeName, true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type game = assembly.GetType("Magicka.Game", true);
            playStateContentGetter = RequireGetter(
                playState,
                "Content",
                false);
            gameInstanceGetter = RequireGetter(game, "Instance", true);
            gameContentGetter = RequireGetter(game, "Content", false);
            if (playStateContentGetter.ReturnType !=
                gameContentGetter.ReturnType)
                throw new InvalidOperationException(
                    "Shield content-manager property types do not match.");
            return shield;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    result[index].opcode != OpCodes.Callvirt ||
                    !SameMember(
                        result[index].operand,
                        playStateContentGetter))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = gameInstanceGetter;
                result[index].operand = gameContentGetter;
                replacements++;
            }
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two Shield level-content reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type shield = RuntimeMember.FindLoadedType(ShieldTypeName);
            Configure(shield.Assembly);
        }

        private static MethodInfo RequireGetter(
            Type type,
            string name,
            bool isStatic)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic |
                    (isStatic
                        ? BindingFlags.Static
                        : BindingFlags.Instance));
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter == null || getter.IsStatic != isStatic)
                throw new MissingMethodException(
                    type.FullName,
                    "get_" + name);
            return getter;
        }

        private static bool SameMember(object left, MemberInfo right)
        {
            MemberInfo leftMember = left as MemberInfo;
            return leftMember != null && right != null &&
                leftMember.Module == right.Module &&
                leftMember.MetadataToken == right.MetadataToken;
        }
    }
}
