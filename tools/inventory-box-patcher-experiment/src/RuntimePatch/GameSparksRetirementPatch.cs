using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSparksRetirementPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            Definition("initialize", "Initialize", null);

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            Definition(
                "update", "Update", "System.Single");

        internal static readonly RuntimePatchDefinition EndRunDefinition =
            Definition("end run", "EndRun", null);

        private static RuntimePatchDefinition Definition(
            string label, string methodName, string parameterTypeName)
        {
            return RuntimePatchDefinition.Transpile(
                "GameSparks retirement " + label,
                "org.magickacommunitypatch.gamesparks-retirement-" +
                    methodName.ToLowerInvariant(),
                assembly => FindGameMethod(
                    assembly, methodName, parameterTypeName),
                typeof(GameSparksRetirementPatch).GetMethod("Transpiler"));
        }

        private static MethodInfo FindGameMethod(
            Assembly assembly, string name, string parameterTypeName)
        {
            Type game = assembly.GetType("Magicka.Game", true);
            MethodInfo[] methods = game.GetMethods(Members);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != name ||
                    (parameterTypeName == null && parameters.Length != 0) ||
                    (parameterTypeName != null &&
                        (parameters.Length != 1 ||
                            parameters[0].ParameterType.FullName !=
                                parameterTypeName)))
                    continue;
                if (result != null)
                    throw new AmbiguousMatchException(game.FullName + "." + name);
                result = method;
            }
            if (result == null)
                throw new MissingMethodException(game.FullName, name);
            return result;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int removed = 0;
            for (int index = 1; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (!IsGameSparksCall(called))
                    continue;
                MethodInfo getter = result[index - 1].operand as MethodInfo;
                if (!IsSingletonGetter(getter, called.DeclaringType))
                    throw new InvalidOperationException(
                        "GameSparks singleton getter shape changed.");
                result[index - 1].opcode = OpCodes.Nop;
                result[index - 1].operand = null;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                removed++;
            }
            if (removed != 1)
                throw new InvalidOperationException(
                    "Expected one GameSparks lifecycle call, found " +
                    removed + ".");
            return result;
        }

        private static bool IsGameSparksCall(MethodInfo method)
        {
            return method != null && method.DeclaringType != null &&
                method.DeclaringType.FullName ==
                    "Magicka.WebTools.GameSparks.GameSparksServices";
        }

        private static bool IsSingletonGetter(
            MethodInfo method, Type serviceType)
        {
            if (method == null || method.Name != "get_Instance" ||
                method.DeclaringType == null ||
                !method.DeclaringType.IsGenericType)
                return false;
            Type[] arguments = method.DeclaringType.GetGenericArguments();
            return arguments.Length == 1 && arguments[0] == serviceType;
        }
    }
}
