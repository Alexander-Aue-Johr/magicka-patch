using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TomeLanguageRefreshPatch
    {
        private static FieldInfo[] widgets;
        private static MethodInfo[] refreshMethods;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Tome account-widget language refresh",
                "org.magickacommunitypatch.tome-language-refresh",
                FindLanguageChanged,
                typeof(TomeLanguageRefreshPatch).GetMethod("Transpiler"));

        private static MethodInfo FindLanguageChanged(Assembly assembly)
        {
            Type tome = assembly.GetType("Magicka.GameLogic.UI.Tome", true);
            string[] names = new string[]
            {
                "sIndicatorBackground",
                "sAccountLoginBtn",
                "sAccountCreationBtn"
            };
            widgets = new FieldInfo[names.Length];
            refreshMethods = new MethodInfo[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                widgets[index] = tome.GetField(names[index],
                    BindingFlags.Static | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (widgets[index] == null)
                    throw new MissingFieldException(tome.FullName, names[index]);
                refreshMethods[index] = widgets[index].FieldType.GetMethod(
                    "LanguageChanged",
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (refreshMethods[index] == null ||
                    refreshMethods[index].ReturnType != typeof(void))
                    throw new MissingMethodException(
                        widgets[index].FieldType.FullName, "LanguageChanged");
            }
            MethodInfo method = tome.GetMethod("LanguageChanged",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(tome.FullName, "LanguageChanged");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int returnIndex = -1;
            int returns = 0;
            for (int index = 0; index < result.Count; index++)
                if (result[index].opcode == OpCodes.Ret)
                {
                    returnIndex = index;
                    returns++;
                }
            if (returns != 1)
                throw new InvalidOperationException(
                    "Expected one Tome.LanguageChanged return, found " +
                    returns + ".");
            List<CodeInstruction> added = new List<CodeInstruction>();
            for (int index = 0; index < widgets.Length; index++)
            {
                added.Add(new CodeInstruction(OpCodes.Ldsfld, widgets[index]));
                added.Add(new CodeInstruction(OpCodes.Callvirt,
                    refreshMethods[index]));
            }
            added[0].labels.AddRange(result[returnIndex].labels);
            added[0].blocks.AddRange(result[returnIndex].blocks);
            result[returnIndex].labels.Clear();
            result[returnIndex].blocks.Clear();
            result.InsertRange(returnIndex, added);
            return result;
        }
    }
}
