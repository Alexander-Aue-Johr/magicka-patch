using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate IList RailgunParentGetter(object instance);

    internal delegate void RailgunLockedSetter(object instance, bool value);

    public static class RailgunParentCyclePatch
    {
        private const int TraversalLimit = 256;
        private static Type railgunType;
        private static FieldInfo lengthField;
        private static RailgunParentGetter getParents;
        private static RailgunLockedSetter setLocked;

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Railgun parent-cycle prevention",
                "org.magickacommunitypatch.railgun-parent-cycle",
                FindUpdate,
                typeof(RailgunParentCyclePatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition LockAllDefinition =
            RuntimePatchDefinition.Prefix(
                "Railgun cycle-safe lock traversal",
                "org.magickacommunitypatch.railgun-lock-cycle",
                FindLockAll,
                target => typeof(RailgunParentCyclePatch).GetMethod(
                    "LockAllPrefix"));

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Configure(targetAssembly);
            MethodInfo[] matches = Array.FindAll(
                railgunType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "Update" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType.FullName ==
                        "PolygonHead.DataChannel" &&
                    method.GetParameters()[1].ParameterType == typeof(float));
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one Railgun.Update method, found " +
                    matches.Length + ".");
            return matches[0];
        }

        private static MethodInfo FindLockAll(Assembly targetAssembly)
        {
            Configure(targetAssembly);
            MethodInfo method = railgunType.GetMethod(
                "LockAll",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(railgunType.FullName, "LockAll");
            return method;
        }

        private static void Configure(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.Railgun",
                true);
            if (type == railgunType && getParents != null && setLocked != null)
                return;

            FieldInfo parents = RequireField(type, "mParents");
            Type expectedParents = typeof(List<>).MakeGenericType(type);
            if (parents.FieldType != expectedParents)
                throw new MissingFieldException(type.FullName, "mParents");
            FieldInfo locked = RequireField(type, "mLocked");
            if (locked.FieldType != typeof(bool))
                throw new MissingFieldException(type.FullName, "mLocked");
            FieldInfo length = RequireField(type, "mLength");
            if (length.FieldType != typeof(float))
                throw new MissingFieldException(type.FullName, "mLength");

            railgunType = type;
            lengthField = length;
            getParents = CreateParentGetter(parents);
            setLocked = CreateLockedSetter(locked);
        }

        public static bool WouldCreateParentCycle(
            object current,
            object candidate)
        {
            try
            {
                if (current == null || candidate == null)
                {
                    ReportRecovery("railgun_parent_cycle_check_failed", 0, 0, 0);
                    return true;
                }
                object[] pending = new object[TraversalLimit];
                object[] visited = new object[TraversalLimit];
                int pendingCount = 1;
                int visitedCount = 0;
                pending[0] = current;
                while (pendingCount > 0)
                {
                    object node = pending[--pendingCount];
                    if (Object.ReferenceEquals(node, candidate))
                    {
                        ReportRecovery("railgun_parent_cycle_prevented",
                            visitedCount, pendingCount, ParentCount(candidate));
                        return true;
                    }
                    if (Contains(visited, visitedCount, node))
                        continue;
                    if (visitedCount >= TraversalLimit)
                    {
                        ReportRecovery("railgun_parent_cycle_check_limit_reached",
                            visitedCount, pendingCount, ParentCount(candidate));
                        return true;
                    }
                    visited[visitedCount++] = node;
                    IList parents = getParents(node);
                    if (parents == null)
                    {
                        ReportRecovery("railgun_parent_cycle_check_failed",
                            visitedCount, pendingCount, ParentCount(candidate));
                        return true;
                    }
                    for (int index = 0; index < parents.Count; index++)
                    {
                        object parent = parents[index];
                        if (parent == null || Contains(visited, visitedCount, parent))
                            continue;
                        if (pendingCount >= TraversalLimit)
                        {
                            ReportRecovery(
                                "railgun_parent_cycle_check_limit_reached",
                                visitedCount, pendingCount,
                                ParentCount(candidate));
                            return true;
                        }
                        pending[pendingCount++] = parent;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                ReportRecovery("railgun_parent_cycle_check_failed", 0, 0, 0);
                return true;
            }
        }

        private static int ParentCount(object railgun)
        {
            try
            {
                IList parents = railgun == null ? null : getParents(railgun);
                return parents == null ? 0 : parents.Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void ReportRecovery(string reason, int visitedCount,
            int pendingCount, int candidateParentCount)
        {
            RuntimePatchTelemetry.SendRuntimeGuard(
                "magicka_patch_runtime_recovery",
                reason,
                "Railgun.mParents",
                "Magicka.GameLogic.Spells.Railgun",
                "visited_count=" + visitedCount + ";pending_count=" +
                    pendingCount + ";candidate_parent_count=" +
                    candidateParentCount,
                String.Empty);
        }

        public static bool LockAllPrefix(object __instance)
        {
            object[] pending = new object[TraversalLimit];
            object[] visited = new object[TraversalLimit];
            int pendingCount = 1;
            int visitedCount = 0;
            pending[0] = __instance;
            try
            {
                while (pendingCount > 0)
                {
                    object node = pending[--pendingCount];
                    if (node == null || Contains(visited, visitedCount, node))
                        continue;
                    if (visitedCount >= TraversalLimit)
                        break;
                    visited[visitedCount++] = node;
                    setLocked(node, true);
                    IList parents = getParents(node);
                    if (parents == null)
                        continue;
                    for (int index = 0;
                        index < parents.Count && pendingCount < TraversalLimit;
                        index++)
                    {
                        object parent = parents[index];
                        if (parent != null &&
                            !Contains(visited, visitedCount, parent))
                            pending[pendingCount++] = parent;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int distanceCall = FindDistanceCall(result);
            int rangeBranch = FindSecondAndBranch(result, distanceCall);
            object skipTarget = result[rangeBranch].operand;
            int candidateLoadIndex = FindCandidateLoad(
                result,
                rangeBranch + 1);
            CodeInstruction candidateLoad = CloneLocalLoad(
                result[candidateLoadIndex]);

            int insertion = rangeBranch + 1;
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(result[insertion].labels);
            result[insertion].labels.Clear();
            result.Insert(insertion++, loadThis);
            result.Insert(insertion++, candidateLoad);
            result.Insert(
                insertion++,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(RailgunParentCyclePatch).GetMethod(
                        "WouldCreateParentCycle")));
            result.Insert(
                insertion,
                new CodeInstruction(OpCodes.Brtrue, skipTarget));
            return result;
        }

        private static int FindDistanceCall(IList<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo called = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode != OpCodes.Call &&
                        instructions[index].opcode != OpCodes.Callvirt) ||
                    called == null ||
                    called.Name != "SegmentSegmentDistanceSq" ||
                    called.DeclaringType == null ||
                    called.DeclaringType.FullName !=
                        "JigLibX.Geometry.Distance")
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple Railgun segment-distance calls matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Railgun segment-distance call was not found.");
            return match;
        }

        private static int FindSecondAndBranch(
            IList<CodeInstruction> instructions,
            int start)
        {
            int matches = 0;
            for (int index = start + 1;
                index + 1 < instructions.Count && index < start + 80;
                index++)
            {
                if (instructions[index].opcode != OpCodes.And ||
                    !IsFalseBranch(instructions[index + 1].opcode))
                    continue;
                matches++;
                if (matches == 2)
                    return index + 1;
            }
            throw new InvalidOperationException(
                "Railgun intersection range branch was not found.");
        }

        private static int FindCandidateLoad(
            IList<CodeInstruction> instructions,
            int start)
        {
            for (int index = start + 1;
                index < instructions.Count && index < start + 20;
                index++)
            {
                if (instructions[index].opcode == OpCodes.Ldfld &&
                    Object.Equals(instructions[index].operand, lengthField) &&
                    IsLocalLoad(instructions[index - 1]))
                    return index - 1;
            }
            throw new InvalidOperationException(
                "Railgun candidate local was not found.");
        }

        private static CodeInstruction CloneLocalLoad(CodeInstruction source)
        {
            if (!IsLocalLoad(source))
                throw new InvalidOperationException(
                    "Expected a Railgun candidate local load.");
            return source.operand == null
                ? new CodeInstruction(source.opcode)
                : new CodeInstruction(source.opcode, source.operand);
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool IsFalseBranch(OpCode opcode)
        {
            return opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S;
        }

        private static bool Contains(object[] objects, int count, object value)
        {
            for (int index = 0; index < count; index++)
            {
                if (Object.ReferenceEquals(objects[index], value))
                    return true;
            }
            return false;
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

        private static RailgunParentGetter CreateParentGetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "GetRailgunParents",
                typeof(IList),
                new Type[] { typeof(object) },
                typeof(RailgunParentCyclePatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (RailgunParentGetter)method.CreateDelegate(
                typeof(RailgunParentGetter));
        }

        private static RailgunLockedSetter CreateLockedSetter(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod(
                "SetRailgunLocked",
                typeof(void),
                new Type[] { typeof(object), typeof(bool) },
                typeof(RailgunParentCyclePatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (RailgunLockedSetter)method.CreateDelegate(
                typeof(RailgunLockedSetter));
        }
    }
}
