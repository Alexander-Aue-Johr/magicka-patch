using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TomeVersionTextPatch
    {
        private static ConstructorInfo textConstructor;
        private static MethodInfo productVersionGetter;
        private static MethodInfo appendMethod;
        private static MethodInfo setTextMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Tome Community Patch version label",
                "org.magickacommunitypatch.tome-version-label",
                FindConstructor,
                typeof(TomeVersionTextPatch).GetMethod("Transpiler"));

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Type tome = assembly.GetType("Magicka.GameLogic.UI.Tome", true);
            ConstructorInfo initializer = tome.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (initializer == null)
                throw new MissingMethodException(tome.FullName, ".ctor");
            FieldInfo versionText = tome.GetField(
                "sVersionText",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (versionText == null)
                throw new MissingFieldException(tome.FullName, "sVersionText");
            Type text = versionText.FieldType;
            ConstructorInfo[] constructors = text.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            for (int index = 0; index < constructors.Length; index++)
            {
                ParameterInfo[] parameters = constructors[index].GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType == typeof(int))
                    textConstructor = constructors[index];
            }
            setTextMethod = text.GetMethod(
                "SetText",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string) },
                null);
            appendMethod = text.GetMethod(
                "Append",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string) },
                null);
            productVersionGetter = typeof(System.Windows.Forms.Application)
                .GetProperty("ProductVersion").GetGetMethod();
            if (textConstructor == null || setTextMethod == null ||
                appendMethod == null || productVersionGetter == null)
                throw new InvalidOperationException(
                    "Tome version-text member contract changed.");
            return initializer;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int capacityChanges = 0;
            int versionChanges = 0;
            int appendChanges = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Newobj &&
                    Object.Equals(result[index].operand, textConstructor))
                {
                    for (int previous = index - 1;
                        previous >= 0 && previous >= index - 8;
                        previous--)
                    {
                        int value;
                        if (TryReadInt(result[previous], out value) && value == 32)
                        {
                            result[previous].opcode = OpCodes.Ldc_I4;
                            result[previous].operand = 512;
                            capacityChanges++;
                            break;
                        }
                    }
                }
                if (IsCall(result[index], productVersionGetter))
                {
                    result.Insert(index + 1, new CodeInstruction(
                        OpCodes.Call,
                        typeof(TomeVersionTextPatch).GetMethod(
                            "BuildVersionText")));
                    index++;
                    versionChanges++;
                }
                if (IsCall(result[index], appendMethod) && index > 0 &&
                    result[index - 1].opcode == OpCodes.Ldstr &&
                    Object.Equals(result[index - 1].operand, " (Modified)"))
                {
                    result[index].opcode = OpCodes.Call;
                    result[index].operand = typeof(TomeVersionTextPatch).GetMethod(
                        "AppendTextSafely");
                    appendChanges++;
                }
            }
            if (capacityChanges != 1 || versionChanges != 1 || appendChanges != 1)
                throw new InvalidOperationException(
                    "Tome version label expected 1/1/1 edits, found " +
                    capacityChanges + "/" + versionChanges + "/" +
                    appendChanges + ".");
            return result;
        }

        public static string BuildVersionText(string gameVersion)
        {
            return gameVersion + " - " + RuntimePatchMetadata.FullVersionText;
        }

        public static void AppendTextSafely(object text, string suffix)
        {
            if (text == null || suffix == null)
                return;
            Type type = text.GetType();
            FieldInfo characters = type.GetField(
                "mCharacters",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (characters == null)
                characters = type.GetField(
                    "Characters",
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
            PropertyInfo charactersProperty = type.GetProperty(
                "Characters",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            PropertyInfo endIndex = type.GetProperty(
                "EndIndex",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            char[] value = charactersProperty != null
                ? (char[])charactersProperty.GetValue(text, null)
                : characters == null ? null : (char[])characters.GetValue(text);
            int length = endIndex == null ? 0 :
                Convert.ToInt32(endIndex.GetValue(text, null));
            if (value == null || length < 0 || length > value.Length)
                return;
            setTextMethod.Invoke(text, new object[]
            {
                new string(value, 0, length) + suffix
            });
        }

        private static bool IsCall(CodeInstruction instruction, MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        private static bool TryReadInt(CodeInstruction instruction, out int value)
        {
            if (instruction.opcode == OpCodes.Ldc_I4)
            {
                value = (int)instruction.operand;
                return true;
            }
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                value = Convert.ToInt32(instruction.operand);
                return true;
            }
            value = 0;
            return false;
        }
    }
}
