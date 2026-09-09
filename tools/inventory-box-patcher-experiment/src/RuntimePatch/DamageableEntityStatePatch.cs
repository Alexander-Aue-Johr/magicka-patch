using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class DamageableEntityStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.DamageablePhysicsEntity";

        private static FieldInfo gibsField;
        private static FieldInfo resistancesField;
        private static MethodInfo clearGibsMethod;
        private static MethodInfo returnToCacheMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "DamageablePhysicsEntity inactive template release",
                "org.magickacommunitypatch.damageable-inactive-state",
                FindDeinitialize,
                typeof(DamageableEntityStatePatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type damageable = assembly.GetType(TypeName, false);
            if (damageable == null)
                return false;
            MethodInfo returnToCache = damageable.GetMethod(
                "ReturnToCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            return returnToCache != null;
        }

        private static MethodInfo FindDeinitialize(Assembly assembly)
        {
            Type damageable = assembly.GetType(TypeName, true);
            gibsField = RequireField(damageable, "mGibs");
            resistancesField = RequireField(damageable, "mResistances");

            if (!gibsField.FieldType.IsGenericType ||
                gibsField.FieldType.GetGenericTypeDefinition() !=
                    typeof(List<>))
                throw new MissingFieldException(damageable.FullName, "mGibs");
            Type gib = assembly.GetType(
                "Magicka.GameLogic.Entities.GibReference",
                true);
            if (gibsField.FieldType.GetGenericArguments()[0] != gib)
                throw new MissingFieldException(damageable.FullName, "mGibs");
            Type resistance = assembly.GetType(
                "Magicka.GameLogic.Entities.Resistance",
                true);
            if (!resistancesField.FieldType.IsArray ||
                resistancesField.FieldType.GetElementType() != resistance)
                throw new MissingFieldException(
                    damageable.FullName,
                    "mResistances");

            clearGibsMethod = gibsField.FieldType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            returnToCacheMethod = damageable.GetMethod(
                "ReturnToCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { damageable },
                null);
            MethodInfo deinitialize = damageable.GetMethod(
                "Deinitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearGibsMethod == null ||
                clearGibsMethod.ReturnType != typeof(void) ||
                returnToCacheMethod == null ||
                returnToCacheMethod.ReturnType != typeof(void) ||
                deinitialize == null ||
                deinitialize.ReturnType != typeof(void))
                throw new MissingMethodException(
                    damageable.FullName,
                    "Deinitialize template release contract");
            return deinitialize;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int cacheCall = -1;
            int cacheCalls = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                        result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(
                        result[index].operand,
                        returnToCacheMethod))
                    continue;
                cacheCall = index;
                cacheCalls++;
            }
            if (cacheCalls != 1 || cacheCall < 1 ||
                result[cacheCall - 1].opcode != OpCodes.Ldarg_0)
                throw new InvalidOperationException(
                    "DamageablePhysicsEntity cache return shape changed.");

            CodeInstruction loadForGibs =
                new CodeInstruction(OpCodes.Ldarg_0);
            MoveEntryMetadata(result[cacheCall - 1], loadForGibs);
            result.InsertRange(
                cacheCall - 1,
                new CodeInstruction[]
                {
                    loadForGibs,
                    new CodeInstruction(OpCodes.Ldfld, gibsField),
                    new CodeInstruction(OpCodes.Callvirt, clearGibsMethod),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldnull),
                    new CodeInstruction(OpCodes.Stfld, resistancesField)
                });
            return result;
        }

        private static void MoveEntryMetadata(
            CodeInstruction source,
            CodeInstruction destination)
        {
            destination.labels.AddRange(source.labels);
            destination.blocks.AddRange(source.blocks);
            source.labels.Clear();
            source.blocks.Clear();
        }
    }
}
