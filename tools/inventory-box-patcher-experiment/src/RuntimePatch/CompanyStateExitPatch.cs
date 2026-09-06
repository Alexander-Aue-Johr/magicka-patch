using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CompanyStateExitPatch
    {
        private static FieldInfo contentManagerField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "CompanyState deferred content disposal",
                "org.magickacommunitypatch.company-state-exit",
                FindTarget,
                typeof(CompanyStateExitPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type companyStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.CompanyState",
                true);
            contentManagerField = companyStateType.GetField(
                "mContentManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo method = companyStateType.GetMethod(
                "OnExit",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (contentManagerField == null)
                throw new MissingFieldException(companyStateType.FullName, "mContentManager");
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(companyStateType.FullName, "OnExit");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int disposeCall = -1;
            int returnInstruction = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo calledMethod = result[index].operand as MethodInfo;
                if (result[index].opcode == OpCodes.Callvirt &&
                    calledMethod != null && calledMethod.Name == "Dispose" &&
                    calledMethod.GetParameters().Length == 0 &&
                    index >= 2 && result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldfld &&
                    result[index - 1].operand as FieldInfo == contentManagerField)
                {
                    if (disposeCall >= 0)
                        throw new InvalidOperationException(
                            "Multiple CompanyState content disposal blocks matched.");
                    disposeCall = index;
                }
                if (result[index].opcode == OpCodes.Ret)
                {
                    if (returnInstruction >= 0)
                        throw new InvalidOperationException(
                            "CompanyState.OnExit contains multiple returns.");
                    returnInstruction = index;
                }
            }
            if (disposeCall != 2)
                throw new InvalidOperationException(
                    "CompanyState content disposal is not the opening instruction block.");
            if (returnInstruction != result.Count - 1)
                throw new InvalidOperationException(
                    "CompanyState.OnExit does not end in a single return.");

            List<CodeInstruction> disposal = result.GetRange(disposeCall - 2, 3);
            result.RemoveRange(disposeCall - 2, 3);
            result.InsertRange(result.Count - 1, disposal);
            return result;
        }
    }
}
