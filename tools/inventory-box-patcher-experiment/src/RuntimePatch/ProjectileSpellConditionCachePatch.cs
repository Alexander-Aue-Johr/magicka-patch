using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ProjectileSpellConditionCachePatch
    {
        private const string ProjectileSpellTypeName =
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell";
        private const string ConditionCollectionTypeName =
            "Magicka.GameLogic.Entities.Items.ConditionCollection";

        private static readonly object[] EmptyArguments = new object[0];
        private static ConstructorInfo conditionCollectionConstructor;
        private static MethodInfo dequeueMethod;
        private static MethodInfo enqueueMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "ProjectileSpell empty condition-cache recovery",
                "org.magickacommunitypatch.projectile-spell-condition-cache",
                FindSpawnMissile,
                typeof(ProjectileSpellConditionCachePatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindSpawnMissile(Assembly targetAssembly)
        {
            Type conditionCollection = targetAssembly.GetType(
                ConditionCollectionTypeName,
                true);
            Type projectileSpell = targetAssembly.GetType(
                ProjectileSpellTypeName,
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

            Type missile = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity",
                true);
            Type model = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.Model");
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            Type spell = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.Spell",
                true);
            MethodInfo method = projectileSpell.GetMethod(
                "SpawnMissile",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    missile.MakeByRefType(),
                    model,
                    owner,
                    typeof(float),
                    vector.MakeByRefType(),
                    vector.MakeByRefType(),
                    spell.MakeByRefType(),
                    typeof(float),
                    typeof(int)
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    projectileSpell.FullName,
                    "SpawnMissile");
            return method;
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
                    "Expected one ProjectileSpell condition-cache dequeue, found " +
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
                    typeof(ProjectileSpellConditionCachePatch).GetMethod(
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
    }
}
