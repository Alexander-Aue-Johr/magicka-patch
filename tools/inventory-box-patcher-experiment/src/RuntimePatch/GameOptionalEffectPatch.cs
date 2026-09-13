using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameOptionalEffectPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Game optional render effects",
                "org.magickacommunitypatch.game-optional-render-effects",
                FindLoadContent,
                typeof(GameOptionalEffectPatch).GetMethod("Transpiler"));

        private static MethodInfo FindLoadContent(Assembly assembly)
        {
            Type game = assembly.GetType("Magicka.Game", true);
            MethodInfo method = game.GetMethod(
                "LoadContent", Members, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(game.FullName, "LoadContent");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            RemoveExpression(
                result,
                "PolygonHead.Effects.RenderDeferredEffect",
                6);
            RemoveExpression(
                result,
                "Magicka.Graphics.Effects.EntangleEffect",
                5);
            return result;
        }
        private static void RemoveExpression(
            List<CodeInstruction> body,
            string effectType,
            int instructionsBeforeConstructor)
        {
            int constructorIndex = -1;
            for (int index = 0; index < body.Count; index++)
            {
                ConstructorInfo constructor =
                    body[index].operand as ConstructorInfo;
                if (constructor == null || constructor.DeclaringType == null ||
                    constructor.DeclaringType.FullName != effectType)
                    continue;
                if (constructorIndex >= 0)
                    throw new InvalidOperationException(
                        "Multiple startup constructors found for " + effectType + ".");
                constructorIndex = index;
            }
            if (constructorIndex < instructionsBeforeConstructor ||
                constructorIndex + 2 >= body.Count)
                throw new InvalidOperationException(
                    "Startup constructor not found for " + effectType + ".");

            MethodInfo managerGetter = body[
                constructorIndex - instructionsBeforeConstructor].operand
                as MethodInfo;
            MethodInfo register = body[constructorIndex + 1].operand as MethodInfo;
            if (!IsRenderManagerMethod(managerGetter, "get_Instance") ||
                !IsRenderManagerMethod(register, "RegisterEffect") ||
                body[constructorIndex + 2].opcode != OpCodes.Pop)
                throw new InvalidOperationException(
                    "Startup registration shape changed for " + effectType + ".");

            for (int index = constructorIndex - instructionsBeforeConstructor;
                index <= constructorIndex + 2;
                index++)
            {
                body[index].opcode = OpCodes.Nop;
                body[index].operand = null;
            }
        }

        private static bool IsRenderManagerMethod(MethodInfo method, string name)
        {
            return method != null && method.Name == name &&
                method.DeclaringType != null &&
                method.DeclaringType.FullName == "PolygonHead.RenderManager";
        }
    }
}
