using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkDamageLifecyclePatch
    {
        private static MethodInfo getFromHandle;
        private static MethodInfo internalDamage;
        private static MethodInfo attackerAdapter;
        private static MethodInfo targetAdapter;
        [ThreadStatic]
        private static bool lastDamageAttackerValid;

        internal static readonly RuntimePatchDefinition ClientDefinition =
            Definition("NetworkClient", "client");
        internal static readonly RuntimePatchDefinition ServerDefinition =
            Definition("NetworkServer", "server");

        private static RuntimePatchDefinition Definition(
            string typeName, string side)
        {
            return RuntimePatchDefinition.Transpile(
                typeName + " damage entity lifecycle",
                "org.magickacommunitypatch." + typeName.ToLowerInvariant() +
                    "-damage-lifecycle",
                assembly => FindReadMessage(assembly, typeName, side),
                typeof(NetworkDamageLifecyclePatch).GetMethod("Transpiler"));
        }

        private static MethodInfo FindReadMessage(Assembly assembly,
            string typeName, string side)
        {
            Magicka.CommunityPatch.NetworkEntityHandleGuard.Initialize(assembly);
            Type entity = assembly.GetType("Magicka.GameLogic.Entities.Entity", true);
            Type damageable = assembly.GetType("Magicka.GameLogic.Entities.IDamageable", true);
            getFromHandle = entity.GetMethod("GetFromHandle", BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic, null,
                new Type[] { typeof(int) }, null);
            MethodInfo[] damageMethods = damageable.GetMethods();
            internalDamage = null;
            for (int index = 0; index < damageMethods.Length; index++)
                if (damageMethods[index].Name == "InternalDamage")
                    internalDamage = damageMethods[index];
            if (getFromHandle == null || internalDamage == null)
                throw new MissingMemberException("Network damage contract is incomplete.");
            attackerAdapter = CreateAdapter(entity, side, false);
            targetAdapter = CreateAdapter(entity, side, true);
            Type network = assembly.GetType("Magicka.Network." + typeName, true);
            MethodInfo[] methods = network.GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "ReadMessage" &&
                    methods[index].GetParameters().Length == 2)
                    return methods[index];
            throw new MissingMethodException(network.FullName, "ReadMessage");
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int damageCall = FindCall(result, internalDamage, result.Count - 1, -1);
            int targetResolve = FindCall(result, getFromHandle, damageCall - 1, -1);
            int attackerResolve = FindCall(result, getFromHandle, targetResolve - 1, -1);
            int attackerStore = FindStore(result, attackerResolve + 1);
            int targetStore = FindStore(result, targetResolve + 1);
            if (damageCall < 0 || attackerResolve < 0 || targetResolve < 0 ||
                attackerStore < 0 || targetStore < 0)
                throw new InvalidOperationException("Network damage handle shape changed: damage=" +
                    damageCall + "; target=" + targetResolve + "; attacker=" +
                    attackerResolve + "; targetStore=" + targetStore +
                    "; attackerStore=" + attackerStore + ".");
            result[attackerResolve].operand = attackerAdapter;
            result[targetResolve].operand = targetAdapter;

            return result;
        }

        public static object ResolveDamageHandle(int handle, string side,
            bool requireBody)
        {
            if (requireBody && !lastDamageAttackerValid)
                return null;
            object entity = Magicka.CommunityPatch.NetworkEntityHandleGuard
                .ResolveActive(handle, side,
                    requireBody ? "damage_missing_or_unusable_target" :
                        "damage_missing_or_unusable_attacker", false);
            bool valid = requireBody
                ? Magicka.CommunityPatch.NetworkEntityHandleGuard.HasBody(entity)
                : entity != null;
            if (!requireBody)
                lastDamageAttackerValid = valid;
            if (valid)
                return entity;
            RuntimePatchTelemetry.SendNetworkGuardDrop(side, "EntityHandle",
                String.Empty, String.Empty,
                requireBody ? "damage_missing_or_unusable_target" :
                    "damage_missing_or_unusable_attacker",
                "handle=" + handle);
            return null;
        }

        private static MethodInfo CreateAdapter(Type entity, string side,
            bool requireBody)
        {
            DynamicMethod method = new DynamicMethod(
                requireBody ? "ResolveDamageTarget" : "ResolveDamageAttacker",
                entity, new Type[] { typeof(int) },
                typeof(NetworkDamageLifecyclePatch), true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, side);
            il.Emit(requireBody ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.EmitCall(OpCodes.Call,
                typeof(NetworkDamageLifecyclePatch).GetMethod(
                    "ResolveDamageHandle"), null);
            il.Emit(OpCodes.Castclass, entity);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static int FindCall(IList<CodeInstruction> body,
            MethodInfo method, int start, int end)
        {
            for (int index = start; index > end; index--)
                if ((body[index].opcode == OpCodes.Call ||
                    body[index].opcode == OpCodes.Callvirt) &&
                    CallsMethod(body[index].operand as MethodInfo, method))
                    return index;
            return -1;
        }

        private static bool CallsMethod(MethodInfo candidate, MethodInfo expected)
        {
            if (candidate == null || expected == null ||
                !candidate.Name.EndsWith(expected.Name,
                    StringComparison.Ordinal))
                return false;
            return candidate.GetParameters().Length ==
                expected.GetParameters().Length;
        }

        private static int FindStore(IList<CodeInstruction> body, int start)
        {
            if (start < 0)
                return -1;
            for (int index = start; index < Math.Min(body.Count, start + 3); index++)
                if (IsStoreLocal(body[index]))
                    return index;
            return -1;
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

    }
}
