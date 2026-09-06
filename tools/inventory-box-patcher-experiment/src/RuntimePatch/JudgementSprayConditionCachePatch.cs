using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class JudgementSprayConditionCachePatch
    {
        private const string JudgementSprayTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.JudgementSpray";
        private const string ConditionCollectionTypeName =
            "Magicka.GameLogic.Entities.Items.ConditionCollection";

        private static readonly object[] EmptyArguments = new object[0];
        private static ConstructorInfo conditionCollectionConstructor;
        private static MethodInfo dequeueMethod;
        private static MethodInfo enqueueMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "JudgementSpray empty condition-cache recovery",
                "org.magickacommunitypatch.judgement-spray-condition-cache",
                FindSpawnProjectile,
                typeof(JudgementSprayConditionCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindSpawnProjectile(Assembly targetAssembly)
        {
            Type conditionCollection = targetAssembly.GetType(
                ConditionCollectionTypeName,
                true);
            Type projectileSpell = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
                true);
            FieldInfo cache = projectileSpell.GetField(
                "sCachedConditions",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (cache == null ||
                !cache.FieldType.IsGenericType ||
                cache.FieldType.GetGenericTypeDefinition().FullName !=
                    "System.Collections.Generic.Queue`1" ||
                cache.FieldType.GetGenericArguments()[0] != conditionCollection)
                throw new MissingFieldException(
                    projectileSpell.FullName,
                    "sCachedConditions");

            dequeueMethod = cache.FieldType.GetMethod(
                "Dequeue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (dequeueMethod == null ||
                dequeueMethod.ReturnType != conditionCollection)
                throw new MissingMethodException(cache.FieldType.FullName, "Dequeue");
            enqueueMethod = cache.FieldType.GetMethod(
                "Enqueue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { conditionCollection },
                null);
            if (enqueueMethod == null || enqueueMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cache.FieldType.FullName, "Enqueue");
            conditionCollectionConstructor = conditionCollection.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (conditionCollectionConstructor == null)
                throw new MissingMethodException(conditionCollection.FullName, ".ctor");

            Type judgementSpray = targetAssembly.GetType(
                JudgementSprayTypeName,
                true);
            Type missile = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity",
                true);
            Type spellCaster = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            MethodInfo match = null;
            MethodInfo[] methods = judgementSpray.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "SpawnProjectile" ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 5 ||
                    parameters[0].ParameterType != missile.MakeByRefType() ||
                    parameters[1].ParameterType != spellCaster ||
                    parameters[2].ParameterType != entity.MakeByRefType() ||
                    !IsVectorReference(parameters[3].ParameterType) ||
                    !IsVectorReference(parameters[4].ParameterType))
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple JudgementSpray.SpawnProjectile methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(
                    judgementSpray.FullName,
                    "SpawnProjectile");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int match = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, dequeueMethod))
                    continue;
                match = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one JudgementSpray condition-cache dequeue, found " +
                    matches + ".");

            CodeInstruction duplicateQueue = new CodeInstruction(OpCodes.Dup);
            duplicateQueue.labels.AddRange(result[match].labels);
            duplicateQueue.blocks.AddRange(result[match].blocks);
            result[match].labels.Clear();
            result[match].blocks.Clear();
            result.Insert(match, duplicateQueue);
            result.Insert(
                match + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(JudgementSprayConditionCachePatch).GetMethod(
                        "EnsureConditionCollection")));
            return result;
        }

        public static void EnsureConditionCollection(object cache)
        {
            ICollection collection = cache as ICollection;
            if (collection == null)
                throw new ArgumentException("Expected a condition collection queue.");
            if (collection.Count != 0)
                return;

            object replacement = conditionCollectionConstructor.Invoke(EmptyArguments);
            enqueueMethod.Invoke(cache, new object[] { replacement });
        }

        private static bool IsVectorReference(Type type)
        {
            return type.IsByRef &&
                type.GetElementType().FullName == "Microsoft.Xna.Framework.Vector3";
        }
    }
}
