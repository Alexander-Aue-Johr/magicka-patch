using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class LightningBoltCachePatch
    {
        private static FieldInfo contentField;
        private static FieldInfo cacheField;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Lightning bolt cache cleanup",
                "org.magickacommunitypatch.lightning-bolt-cache-cleanup",
                FindPlayStateDispose,
                typeof(LightningBoltCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type lightningBoltType = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.LightningBolt",
                true);
            contentField = RequireStaticField(lightningBoltType, "sContent");
            if (contentField.FieldType.FullName !=
                "Microsoft.Xna.Framework.Content.ContentManager")
                throw new MissingFieldException(
                    lightningBoltType.FullName,
                    "sContent");

            cacheField = RequireStaticField(lightningBoltType, "sCache");
            if (!typeof(IList).IsAssignableFrom(cacheField.FieldType) ||
                !cacheField.FieldType.IsGenericType ||
                cacheField.FieldType.GetGenericArguments().Length != 1 ||
                cacheField.FieldType.GetGenericArguments()[0] != lightningBoltType)
                throw new MissingFieldException(
                    lightningBoltType.FullName,
                    "sCache");

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

        private static FieldInfo RequireStaticField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null)
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
                    typeof(LightningBoltCachePatch).GetMethod("Clear")));
            return result;
        }

        public static void Clear()
        {
            if (contentField == null || cacheField == null)
                throw new InvalidOperationException(
                    "Lightning bolt cache contracts have not been initialized.");

            contentField.SetValue(null, null);
            IList cache = cacheField.GetValue(null) as IList;
            if (cache != null)
                cache.Clear();
        }
    }
}
