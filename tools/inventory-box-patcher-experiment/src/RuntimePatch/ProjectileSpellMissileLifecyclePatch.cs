using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;
using Harmony.ILCopying;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ProjectileSpellMissileLifecyclePatch
    {
        private const string ProjectileSpellTypeName =
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell";

        private static MethodInfo getMissileInstanceMethod;
        private static MethodInfo initializeMissileMethod;
        private static MethodInfo enqueueMethod;
        private static MethodInfo dequeueMethod;
        private static FieldInfo conditionCacheField;
        private static ProjectileSpawnObjectGetter getPlayState;
        private static ProjectileSpawnObjectGetter getEntityManager;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "ProjectileSpell incomplete missile guard",
                "org.magickacommunitypatch.projectile-spell-missile-lifecycle",
                FindSpawnMissile,
                typeof(ProjectileSpellMissileLifecyclePatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindSpawnMissile(Assembly assembly)
        {
            Type projectileSpell = assembly.GetType(
                ProjectileSpellTypeName,
                true);
            Type missile = assembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity",
                true);
            Type model = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.Model");
            Type owner = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            Type spell = assembly.GetType(
                "Magicka.GameLogic.Spells.Spell",
                true);
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type entityManager = assembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);
            Type conditionCollection = assembly.GetType(
                "Magicka.GameLogic.Entities.Items.ConditionCollection",
                true);

            conditionCacheField = projectileSpell.GetField(
                "sCachedConditions",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (conditionCacheField == null)
                throw new MissingFieldException(
                    projectileSpell.FullName,
                    "sCachedConditions");

            getMissileInstanceMethod = owner.GetMethod(
                "GetMissileInstance",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (getMissileInstanceMethod == null ||
                getMissileInstanceMethod.ReturnType != missile)
                throw new MissingMethodException(
                    owner.FullName,
                    "GetMissileInstance");

            initializeMissileMethod = FindMissileInitialize(
                missile,
                entity,
                vector,
                model,
                conditionCollection,
                spell);

            PropertyInfo playStateProperty = entity.GetProperty(
                "PlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo playStateGetter = playStateProperty == null
                ? null
                : playStateProperty.GetGetMethod(true);
            if (playStateGetter == null || playStateGetter.ReturnType != playState)
                throw new MissingMethodException(entity.FullName, "get_PlayState");
            PropertyInfo entityManagerProperty = playState.GetProperty(
                "EntityManager",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo entityManagerGetter = entityManagerProperty == null
                ? null
                : entityManagerProperty.GetGetMethod(true);
            if (entityManagerGetter == null ||
                entityManagerGetter.ReturnType != entityManager)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_EntityManager");
            getPlayState = CreateGetter(entity, playStateGetter);
            getEntityManager = CreateGetter(playState, entityManagerGetter);

            Type queue = typeof(Queue<>).MakeGenericType(conditionCollection);
            if (conditionCacheField.FieldType != queue)
                throw new InvalidOperationException(
                    "Unexpected ProjectileSpell condition-cache type.");
            enqueueMethod = queue.GetMethod(
                "Enqueue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { conditionCollection },
                null);
            if (enqueueMethod == null || enqueueMethod.ReturnType != typeof(void))
                throw new MissingMethodException(queue.FullName, "Enqueue");
            dequeueMethod = queue.GetMethod(
                "Dequeue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (dequeueMethod == null ||
                dequeueMethod.ReturnType != conditionCollection)
                throw new MissingMethodException(queue.FullName, "Dequeue");

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
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int getMissile = FindSingleCall(
                result,
                getMissileInstanceMethod,
                "missile-instance request");
            int initialize = FindSingleCall(
                result,
                initializeMissileMethod,
                "missile initialization");
            int enqueue = FindSingleCall(
                result,
                enqueueMethod,
                "condition-cache return");
            if (getMissile >= initialize || initialize >= enqueue)
                throw new InvalidOperationException(
                    "Unexpected ProjectileSpell missile lifecycle order.");
            if (getMissile + 1 >= result.Count ||
                result[getMissile + 1].opcode != OpCodes.Stind_Ref)
                throw new InvalidOperationException(
                    "Expected the missile-instance result to be stored by reference.");
            if (enqueue < 2 || !IsLoadLocal(result[enqueue - 1]) ||
                result[enqueue - 2].opcode != OpCodes.Ldsfld ||
                !Object.Equals(result[enqueue - 2].operand, conditionCacheField))
                throw new InvalidOperationException(
                    "Expected the condition cache and borrowed collection before Enqueue.");

            int dequeue = FindSingleCall(
                result,
                dequeueMethod,
                "condition-cache borrow");
            if (dequeue + 1 >= result.Count ||
                !IsStoreLocal(result[dequeue + 1]))
                throw new InvalidOperationException(
                    "Expected borrowed ProjectileSpell conditions to be stored locally.");

            int cleanupStart = FindCleanupStart(result, enqueue);
            CodeInstruction cleanupInstruction = result[cleanupStart];
            CodeInstruction protectedStart = result[dequeue + 2];
            Label cleanupLabel = generator.DefineLabel();
            cleanupInstruction.labels.Add(cleanupLabel);

            CodeInstruction queueLoad = CopyWithoutFlow(result[enqueue - 2]);
            CodeInstruction conditionLoad = CopyWithoutFlow(result[enqueue - 1]);
            InsertGuard(
                result,
                generator,
                getMissile + 2,
                false,
                cleanupLabel);

            initialize = FindSingleCall(
                result,
                initializeMissileMethod,
                "missile initialization");
            InsertGuard(
                result,
                generator,
                initialize + 1,
                true,
                cleanupLabel);

            protectedStart.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.BeginExceptionBlock,
                null));
            cleanupStart = result.IndexOf(cleanupInstruction);
            List<CodeInstruction> handlers = new List<CodeInstruction>();
            CodeInstruction nullHandler = new CodeInstruction(
                OpCodes.Call,
                typeof(ProjectileSpellMissileLifecyclePatch).GetMethod(
                    "HandleSpawnNullReference"));
            nullHandler.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.BeginCatchBlock,
                typeof(NullReferenceException)));
            handlers.Add(nullHandler);
            handlers.Add(new CodeInstruction(OpCodes.Leave, cleanupLabel));

            CodeInstruction exceptionHandler = new CodeInstruction(OpCodes.Pop);
            exceptionHandler.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.BeginCatchBlock,
                typeof(Exception)));
            handlers.Add(exceptionHandler);
            handlers.Add(CopyWithoutFlow(queueLoad));
            handlers.Add(CopyWithoutFlow(conditionLoad));
            handlers.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(ProjectileSpellMissileLifecyclePatch).GetMethod(
                    "ReturnConditionCollection")));
            handlers.Add(new CodeInstruction(OpCodes.Rethrow));
            CodeInstruction end = new CodeInstruction(OpCodes.Nop);
            end.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.EndExceptionBlock,
                null));
            handlers.Add(end);
            result.InsertRange(cleanupStart, handlers);
            return result;
        }

        public static void HandleSpawnNullReference(
            NullReferenceException exception)
        {
            RuntimePatchTelemetry.SendNetworkGuardException(
                "projectile_spell",
                "SpawnMissile",
                String.Empty,
                String.Empty,
                "spawn_missile_nullreference_inside_projectile_spawn",
                String.Empty,
                exception);
        }

        public static bool HasUsableEntityManager(object missile)
        {
            if (missile == null)
                return false;
            object playState = getPlayState(missile);
            return playState != null && getEntityManager(playState) != null;
        }

        public static void ReturnConditionCollection(object queue, object collection)
        {
            if (queue == null || collection == null)
                return;
            Monitor.Enter(queue);
            try
            {
                enqueueMethod.Invoke(queue, new object[] { collection });
            }
            finally
            {
                Monitor.Exit(queue);
            }
        }

        private static void InsertGuard(
            List<CodeInstruction> instructions,
            ILGenerator generator,
            int index,
            bool requireEntityManager,
            Label cleanupLabel)
        {
            Label continueLabel = generator.DefineLabel();
            instructions[index].labels.Add(continueLabel);
            List<CodeInstruction> guard = new List<CodeInstruction>();
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldind_Ref));
            if (requireEntityManager)
            {
                guard.Add(new CodeInstruction(
                    OpCodes.Call,
                    typeof(ProjectileSpellMissileLifecyclePatch).GetMethod(
                        "HasUsableEntityManager")));
            }
            guard.Add(new CodeInstruction(OpCodes.Brtrue, continueLabel));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldind_Ref));
            guard.Add(new CodeInstruction(
                requireEntityManager ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            guard.Add(new CodeInstruction(OpCodes.Call,
                typeof(ProjectileSpellMissileLifecyclePatch).GetMethod(
                    "ReportUnusableMissile")));
            guard.Add(new CodeInstruction(OpCodes.Br, cleanupLabel));
            instructions.InsertRange(index, guard);
        }

        public static void ReportUnusableMissile(
            object missile,
            bool afterInitialize)
        {
            string reason = afterInitialize
                ? "spawn_missile_missile_has_no_playstate_after_initialize"
                : "spawn_missile_get_missile_instance_returned_null";
            RuntimePatchTelemetry.SendNetworkGuardDrop(
                "projectile_spell",
                "SpawnMissile",
                String.Empty,
                String.Empty,
                reason,
                missile == null
                    ? String.Empty
                    : "missileType=" + missile.GetType().FullName);
        }

        private static int FindCleanupStart(
            IList<CodeInstruction> instructions,
            int enqueue)
        {
            for (int index = enqueue - 1;
                index >= Math.Max(0, enqueue - 12);
                index--)
            {
                if (instructions[index].opcode == OpCodes.Ldsfld &&
                    Object.Equals(instructions[index].operand, conditionCacheField))
                    return index;
            }
            throw new InvalidOperationException(
                "ProjectileSpell condition-cache cleanup start was not found.");
        }

        private static MethodInfo FindMissileInitialize(
            Type missile,
            Type entity,
            Type vector,
            Type model,
            Type conditionCollection,
            Type spell)
        {
            MethodInfo method = missile.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    entity,
                    typeof(float),
                    vector.MakeByRefType(),
                    vector.MakeByRefType(),
                    model,
                    conditionCollection,
                    typeof(bool),
                    spell
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(missile.FullName, "Initialize");
            return method;
        }

        private static int FindSingleCall(
            IList<CodeInstruction> instructions,
            MethodInfo method,
            string description)
        {
            int found = -1;
            int matches = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                if ((instructions[index].opcode != OpCodes.Call &&
                    instructions[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(instructions[index].operand, method))
                    continue;
                found = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one ProjectileSpell " + description +
                    ", found " + matches + ".");
            return found;
        }

        private static bool IsLoadLocal(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool IsStoreLocal(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
                opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
                opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
        }

        private static CodeInstruction CopyWithoutFlow(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }

        private static ProjectileSpawnObjectGetter CreateGetter(
            Type instanceType,
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "Get" + getter.Name + "ForProjectileLifecycle",
                typeof(object),
                new Type[] { typeof(object) },
                typeof(ProjectileSpellMissileLifecyclePatch),
                true);
            ILGenerator generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Castclass, instanceType);
            generator.EmitCall(OpCodes.Callvirt, getter, null);
            generator.Emit(OpCodes.Ret);
            return (ProjectileSpawnObjectGetter)method.CreateDelegate(
                typeof(ProjectileSpawnObjectGetter));
        }
    }
}
