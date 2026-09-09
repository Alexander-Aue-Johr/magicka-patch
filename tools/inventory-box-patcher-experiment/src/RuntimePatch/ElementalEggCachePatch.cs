using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ElementalEggCachePatch
    {
        private static FieldInfo cacheField;
        private static FieldInfo templateLookupField;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Elemental egg cache cleanup",
                "org.magickacommunitypatch.elemental-egg-cache-cleanup",
                FindPlayStateDispose,
                typeof(ElementalEggCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type eggType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ElementalEgg",
                true);
            cacheField = RequireCollectionField(eggType, "sCache", typeof(IList));
            Type[] cacheArguments = cacheField.FieldType.GetGenericArguments();
            if (cacheArguments.Length != 1 || cacheArguments[0] != eggType)
                throw new MissingFieldException(eggType.FullName, "sCache");
            templateLookupField = RequireCollectionField(
                eggType,
                "sTemplateLookup",
                typeof(IDictionary));
            Type[] lookupArguments =
                templateLookupField.FieldType.GetGenericArguments();
            if (lookupArguments.Length != 2 ||
                lookupArguments[0].FullName !=
                    "Magicka.Elements" ||
                lookupArguments[1].FullName !=
                    "Magicka.GameLogic.Entities.CharacterTemplate")
                throw new MissingFieldException(
                    eggType.FullName,
                    "sTemplateLookup");

            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            clearHandlesMethod = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");

            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return dispose;
        }

        private static FieldInfo RequireCollectionField(
            Type type,
            string name,
            Type collectionInterface)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null ||
                !collectionInterface.IsAssignableFrom(field.FieldType) ||
                !field.FieldType.IsGenericType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in PlayState.Dispose, found " +
                    matches + ".");

            result.Insert(
                anchor + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(ElementalEggCachePatch).GetMethod("Clear")));
            return result;
        }

        public static void Clear()
        {
            if (cacheField == null || templateLookupField == null)
                throw new InvalidOperationException(
                    "Elemental egg cache contracts have not been initialized.");

            IList cache = cacheField.GetValue(null) as IList;
            if (cache != null)
                cache.Clear();
            IDictionary lookup = templateLookupField.GetValue(null) as IDictionary;
            if (lookup != null)
                lookup.Clear();
        }
    }
}
