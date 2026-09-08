using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EffectManagerDuplicatePatch
    {
        private static FieldInfo sourceEffectsField;
        private static MethodInfo containsKey;
        private static MethodInfo fromFile;
        private static MethodInfo add;
        private static MethodInfo stringConcat;
        private static MethodInfo writeLine;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "EffectManager duplicate asset guard",
                "org.magickacommunitypatch.effect-manager-duplicate-asset",
                FindReadDirectory,
                typeof(EffectManagerDuplicatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindReadDirectory(Assembly targetAssembly)
        {
            Type managerType = targetAssembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            sourceEffectsField = managerType.GetField(
                "mSourceEffects",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (sourceEffectsField == null ||
                !sourceEffectsField.FieldType.IsGenericType ||
                sourceEffectsField.FieldType.GetGenericTypeDefinition() !=
                    typeof(Dictionary<,>))
                throw new MissingFieldException(
                    managerType.FullName,
                    "mSourceEffects");

            Type[] dictionaryArguments =
                sourceEffectsField.FieldType.GetGenericArguments();
            if (dictionaryArguments.Length != 2 ||
                dictionaryArguments[0] != typeof(int))
                throw new InvalidOperationException(
                    "EffectManager source-effect dictionary has an unexpected key type.");

            containsKey = sourceEffectsField.FieldType.GetMethod(
                "ContainsKey",
                new Type[] { typeof(int) });
            add = sourceEffectsField.FieldType.GetMethod(
                "Add",
                new Type[] { typeof(int), dictionaryArguments[1] });
            fromFile = dictionaryArguments[1].GetMethod(
                "FromFile",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(string) },
                null);
            stringConcat = typeof(string).GetMethod(
                "Concat",
                new Type[] { typeof(string), typeof(string) });
            writeLine = typeof(Console).GetMethod(
                "WriteLine",
                new Type[] { typeof(string) });
            if (containsKey == null || add == null || fromFile == null ||
                fromFile.ReturnType != dictionaryArguments[1] ||
                stringConcat == null || writeLine == null)
                throw new MissingMethodException(
                    "EffectManager duplicate-asset dependencies are incomplete.");

            MethodInfo readDirectory = managerType.GetMethod(
                "ReadDirectory",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(System.IO.DirectoryInfo) },
                null);
            if (readDirectory == null || readDirectory.ReturnType != typeof(void))
                throw new MissingMethodException(
                    managerType.FullName,
                    "ReadDirectory");
            return readDirectory;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int fromFileIndex = RequireSingleCall(result, fromFile);
            int addIndex = RequireSingleCall(result, add);
            if (fromFileIndex < 2 || addIndex + 1 >= result.Count ||
                addIndex <= fromFileIndex ||
                result[fromFileIndex - 1].opcode != OpCodes.Callvirt ||
                result[fromFileIndex - 2].opcode.Name.IndexOf("ldloc") != 0 ||
                addIndex < 4 ||
                result[addIndex - 4].opcode != OpCodes.Ldarg_0 ||
                result[addIndex - 3].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[addIndex - 3].operand, sourceEffectsField) ||
                result[addIndex - 2].opcode.Name.IndexOf("ldloc") != 0 ||
                result[addIndex - 1].opcode.Name.IndexOf("ldloc") != 0)
                throw new InvalidOperationException(
                    "EffectManager.ReadDirectory has an unexpected load/add shape.");

            int hashStoreIndex = FindPreviousStore(result, fromFileIndex);
            int nameStoreIndex = FindPreviousStore(result, hashStoreIndex);
            if (hashStoreIndex < 0 || nameStoreIndex < 0)
                throw new InvalidOperationException(
                    "EffectManager.ReadDirectory local variables were not found.");

            CodeInstruction loadHash =
                CopyLocalLoad(result[addIndex - 2]);
            CodeInstruction loadName =
                LoadForStore(result[nameStoreIndex]);
            Label duplicate = generator.DefineLabel();
            Label continuation = generator.DefineLabel();

            int insertion = fromFileIndex - 2;
            CodeInstruction loadManager = new CodeInstruction(OpCodes.Ldarg_0);
            loadManager.labels.AddRange(result[insertion].labels);
            result[insertion].labels.Clear();
            result.Insert(insertion++, loadManager);
            result.Insert(
                insertion++,
                new CodeInstruction(OpCodes.Ldfld, sourceEffectsField));
            result.Insert(insertion++, loadHash);
            result.Insert(
                insertion++,
                new CodeInstruction(OpCodes.Callvirt, containsKey));
            result.Insert(
                insertion++,
                new CodeInstruction(OpCodes.Brtrue, duplicate));

            addIndex += 5;
            result.Insert(
                addIndex + 1,
                new CodeInstruction(OpCodes.Br, continuation));
            CodeInstruction message = new CodeInstruction(
                OpCodes.Ldstr,
                "Skip Effect File ");
            message.labels.Add(duplicate);
            result.Insert(addIndex + 2, message);
            result.Insert(addIndex + 3, loadName);
            result.Insert(
                addIndex + 4,
                new CodeInstruction(OpCodes.Call, stringConcat));
            result.Insert(
                addIndex + 5,
                new CodeInstruction(OpCodes.Call, writeLine));
            result[addIndex + 6].labels.Add(continuation);
            return result;
        }

        private static int FindPreviousStore(
            IList<CodeInstruction> instructions,
            int before)
        {
            for (int index = before - 1; index >= 0; index--)
            {
                if (instructions[index].opcode.Name.IndexOf("stloc") == 0)
                    return index;
            }
            return -1;
        }

        private static CodeInstruction CopyLocalLoad(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }

        private static CodeInstruction LoadForStore(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3)
                return new CodeInstruction(OpCodes.Ldloc_3);
            if (store.opcode == OpCodes.Stloc || store.opcode == OpCodes.Stloc_S)
                return new CodeInstruction(OpCodes.Ldloc, store.operand);
            throw new InvalidOperationException(
                "EffectManager.ReadDirectory uses an unsupported local store.");
        }

        private static int RequireSingleCall(
            IList<CodeInstruction> instructions,
            MethodInfo expected)
        {
            int found = -1;
            int count = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(method, expected))
                {
                    found = index;
                    count++;
                }
            }
            if (count != 1)
                throw new InvalidOperationException(
                    "Expected one EffectManager.ReadDirectory " +
                    expected.Name + " call, found " + count + ".");
            return found;
        }
    }
}
