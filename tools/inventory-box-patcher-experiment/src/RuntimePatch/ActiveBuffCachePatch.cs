using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ActiveBuffCachePatch
    {
        private static FieldInfo initializedField;
        private static FieldInfo hasteCacheField;
        private static FieldInfo hasteActiveField;
        private static FieldInfo shrinkCacheField;
        private static FieldInfo shrinkActiveField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Haste and Shrink level cache cleanup",
                "org.magickacommunitypatch.active-buff-cache-cleanup",
                FindPlayStateDispose,
                typeof(ActiveBuffCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            initializedField = playStateType.GetField(
                "mInitialized",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (initializedField == null || initializedField.FieldType != typeof(bool))
                throw new MissingFieldException(playStateType.FullName, "mInitialized");

            Type hasteType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Haste",
                true);
            Type shrinkType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Shrink",
                true);
            hasteCacheField = RequireCache(hasteType, "sCache");
            hasteActiveField = RequireCache(hasteType, "sActiveHastes");
            shrinkCacheField = RequireCache(shrinkType, "sCache");
            shrinkActiveField = RequireCache(shrinkType, "sActiveCache");

            MethodInfo method = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return method;
        }

        private static FieldInfo RequireCache(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || !typeof(IList).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int branch = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index - 1].operand as FieldInfo;
                bool conditionalBranch =
                    result[index].opcode == OpCodes.Brfalse ||
                    result[index].opcode == OpCodes.Brfalse_S ||
                    result[index].opcode == OpCodes.Brtrue ||
                    result[index].opcode == OpCodes.Brtrue_S;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, initializedField) || !conditionalBranch)
                    continue;
                if (branch >= 0)
                    throw new InvalidOperationException(
                        "Multiple PlayState initialization guards matched.");
                branch = index;
            }
            if (branch < 0)
                throw new InvalidOperationException(
                    "PlayState initialization guard was not found.");

            CodeInstruction cleanup = new CodeInstruction(
                OpCodes.Call,
                typeof(ActiveBuffCachePatch).GetMethod("ClearCaches"));
            if (result[branch].opcode == OpCodes.Brfalse ||
                result[branch].opcode == OpCodes.Brfalse_S)
            {
                result.Insert(branch + 1, cleanup);
            }
            else
            {
                int bodyStart = branch + 2;
                if (bodyStart >= result.Count || result[branch + 1].opcode != OpCodes.Ret)
                    throw new InvalidOperationException(
                        "PlayState initialization guard has an unexpected true branch.");
                Label target = (Label)result[branch].operand;
                if (!result[bodyStart].labels.Contains(target))
                    throw new InvalidOperationException(
                        "PlayState initialization guard target was not found.");
                cleanup.labels.AddRange(result[bodyStart].labels);
                result[bodyStart].labels.Clear();
                result.Insert(bodyStart, cleanup);
            }
            return result;
        }

        public static void ClearCaches()
        {
            ClearCache(hasteCacheField);
            ClearCache(hasteActiveField);
            ClearCache(shrinkCacheField);
            ClearCache(shrinkActiveField);
        }

        private static void ClearCache(FieldInfo field)
        {
            IList cache = (IList)field.GetValue(null);
            if (cache != null)
                cache.Clear();
        }
    }
}
