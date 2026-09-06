using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AnimatedLevelPartCollisionPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "AnimatedLevelPart detached entity cleanup",
                "org.magickacommunitypatch.animated-level-part-collision",
                FindUpdate,
                typeof(AnimatedLevelPartCollisionPatch).GetMethod("Transpiler"));

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type part = targetAssembly.GetType("Magicka.Levels.AnimatedLevelPart", true);
            Type dataChannel = RuntimeMember.FindLoadedType("PolygonHead.DataChannel");
            Type matrix = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Matrix");
            Type scene = targetAssembly.GetType("Magicka.Levels.GameScene", true);
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type body = RuntimeMember.FindLoadedType("JigLibX.Physics.Body");

            FieldInfo collidingEntities = part.GetField(
                "mCollidingEntities",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (collidingEntities == null ||
                !collidingEntities.FieldType.IsGenericType ||
                collidingEntities.FieldType.GetGenericTypeDefinition().FullName !=
                    "System.Collections.Generic.SortedList`2")
                throw new MissingFieldException(part.FullName, "mCollidingEntities");
            Type[] arguments = collidingEntities.FieldType.GetGenericArguments();
            if (arguments.Length != 2 || arguments[0] != typeof(ushort) ||
                arguments[1] != typeof(float))
                throw new MissingFieldException(part.FullName, "mCollidingEntities");

            MethodInfo getFromHandle = entity.GetMethod(
                "GetFromHandle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(int) },
                null);
            PropertyInfo bodyProperty = entity.GetProperty(
                "Body",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            MethodInfo getBody = bodyProperty == null
                ? null
                : bodyProperty.GetGetMethod();
            if (getFromHandle == null || getFromHandle.ReturnType != entity)
                throw new MissingMethodException(entity.FullName, "GetFromHandle");
            if (getBody == null || getBody.ReturnType != body)
                throw new MissingMethodException(entity.FullName, "get_Body");

            MethodInfo update = part.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    dataChannel,
                    typeof(float),
                    matrix.MakeByRefType(),
                    scene
                },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(part.FullName, "Update");
            return update;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int getFromHandle = FindCall(
                result,
                "GetFromHandle",
                "Magicka.GameLogic.Entities.Entity");
            int entityStore = getFromHandle + 1;
            int bodyGetter = FindCall(
                result,
                "get_Body",
                "Magicka.GameLogic.Entities.Entity");
            int bodyStore = bodyGetter + 1;
            if (!IsStoreLocal(result[entityStore]) ||
                !IsLoadOfSameLocal(result[bodyGetter - 1], result[entityStore]) ||
                !IsStoreLocal(result[bodyStore]))
                throw new InvalidOperationException(
                    "AnimatedLevelPart.Update entity/body locals changed.");

            int removeAt = FindCallBetween(
                result,
                getFromHandle,
                bodyGetter,
                "RemoveAt",
                "System.Collections.Generic.SortedList`2");
            int removalStart = removeAt - 3;
            int removalEnd = removeAt + 4;
            if (removalStart <= entityStore || removalEnd >= bodyGetter ||
                result[removalStart].opcode != OpCodes.Ldarg_0 ||
                !(result[removalStart + 1].operand is FieldInfo) ||
                ((FieldInfo)result[removalStart + 1].operand).Name !=
                    "mCollidingEntities" ||
                !IsLoadLocal(result[removeAt - 1]) ||
                !IsLoadOfSameLocal(result[removeAt + 1], result[removeAt - 1]) ||
                !IsOne(result[removeAt + 2]) ||
                result[removeAt + 3].opcode != OpCodes.Sub ||
                !IsStoreOfSameLocal(result[removeAt + 4], result[removeAt - 1]))
                throw new InvalidOperationException(
                    "AnimatedLevelPart.Update removal block changed.");

            int loopIncrement = FindIncrementOfLocal(
                result,
                bodyStore + 1,
                result[removeAt - 1]);
            Label invalidEntry = generator.DefineLabel();
            Label validEntry = generator.DefineLabel();
            Label nextEntry = generator.DefineLabel();
            result[entityStore + 1].labels.Add(validEntry);
            result[loopIncrement].labels.Add(nextEntry);

            List<CodeInstruction> guard = new List<CodeInstruction>();
            guard.Add(Clone(result[bodyGetter - 1]));
            guard.Add(new CodeInstruction(OpCodes.Brfalse, invalidEntry));
            guard.Add(Clone(result[bodyGetter - 1]));
            guard.Add(Clone(result[bodyGetter]));
            guard.Add(Clone(result[bodyStore]));
            guard.Add(LoadForStore(result[bodyStore]));
            guard.Add(new CodeInstruction(OpCodes.Brtrue, validEntry));
            for (int index = removalStart; index <= removalEnd; index++)
                guard.Add(Clone(result[index]));
            guard[7].labels.Add(invalidEntry);
            guard.Add(new CodeInstruction(OpCodes.Br, nextEntry));

            MoveEntryMetadata(result[bodyGetter - 1], result[bodyStore + 1]);
            result.RemoveRange(bodyGetter - 1, 3);
            result.InsertRange(entityStore + 1, guard);
            return result;
        }

        private static int FindCall(
            IList<CodeInstruction> instructions,
            string name,
            string declaringType)
        {
            int found = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null && method.Name == name &&
                    method.DeclaringType.FullName == declaringType)
                {
                    if (found >= 0)
                        throw new InvalidOperationException(
                            "AnimatedLevelPart.Update contains multiple " + name +
                            " calls.");
                    found = index;
                }
            }
            if (found < 0)
                throw new InvalidOperationException(
                    "AnimatedLevelPart.Update " + name + " call was not found.");
            return found;
        }

        private static int FindCallBetween(
            IList<CodeInstruction> instructions,
            int after,
            int before,
            string name,
            string declaringType)
        {
            for (int index = after + 1; index < before; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (instructions[index].opcode == OpCodes.Callvirt &&
                    method != null && method.Name == name &&
                    method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericTypeDefinition().FullName ==
                        declaringType)
                    return index;
            }
            throw new InvalidOperationException(
                "AnimatedLevelPart.Update " + name + " call was not found.");
        }

        private static int FindIncrementOfLocal(
            IList<CodeInstruction> instructions,
            int start,
            CodeInstruction localLoad)
        {
            for (int index = start; index + 3 < instructions.Count; index++)
            {
                if (IsLoadOfSameLocal(instructions[index], localLoad) &&
                    IsOne(instructions[index + 1]) &&
                    instructions[index + 2].opcode == OpCodes.Add &&
                    IsStoreOfSameLocal(instructions[index + 3], localLoad))
                    return index;
            }
            throw new InvalidOperationException(
                "AnimatedLevelPart.Update collision-loop increment was not found.");
        }

        private static bool IsLoadLocal(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Ldloc_3;
        }

        private static bool IsStoreLocal(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Stloc ||
                instruction.opcode == OpCodes.Stloc_S ||
                instruction.opcode == OpCodes.Stloc_0 ||
                instruction.opcode == OpCodes.Stloc_1 ||
                instruction.opcode == OpCodes.Stloc_2 ||
                instruction.opcode == OpCodes.Stloc_3;
        }

        private static bool IsLoadOfSameLocal(
            CodeInstruction load,
            CodeInstruction other)
        {
            return IsLoadLocal(load) && LocalIndex(load) == LocalIndex(other);
        }

        private static bool IsStoreOfSameLocal(
            CodeInstruction store,
            CodeInstruction other)
        {
            return IsStoreLocal(store) && LocalIndex(store) == LocalIndex(other);
        }

        private static int LocalIndex(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Stloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Stloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Stloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 ||
                instruction.opcode == OpCodes.Stloc_3)
                return 3;
            LocalBuilder local = instruction.operand as LocalBuilder;
            if (local != null)
                return local.LocalIndex;
            if (instruction.operand is byte)
                return (byte)instruction.operand;
            if (instruction.operand is int)
                return (int)instruction.operand;
            throw new InvalidOperationException(
                "AnimatedLevelPart.Update local operand changed.");
        }

        private static bool IsOne(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldc_I4_1 ||
                (instruction.opcode == OpCodes.Ldc_I4 &&
                    Convert.ToInt32(instruction.operand) == 1) ||
                (instruction.opcode == OpCodes.Ldc_I4_S &&
                    Convert.ToInt32(instruction.operand) == 1);
        }

        private static CodeInstruction LoadForStore(CodeInstruction store)
        {
            int index = LocalIndex(store);
            if (index == 0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (index == 1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (index == 2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (index == 3)
                return new CodeInstruction(OpCodes.Ldloc_3);
            return new CodeInstruction(OpCodes.Ldloc, store.operand);
        }

        private static CodeInstruction Clone(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
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
