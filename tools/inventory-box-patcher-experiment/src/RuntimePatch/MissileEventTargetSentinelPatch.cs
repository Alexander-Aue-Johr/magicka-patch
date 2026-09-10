using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MissileEventTargetSentinelPatch
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static Type messageType;
        private static FieldInfo targetHandleField;

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Missile update empty target sentinel",
                "org.magickacommunitypatch.missile-update-target-sentinel",
                FindUpdate,
                typeof(MissileEventTargetSentinelPatch).GetMethod(
                    "Transpiler"));

        internal static readonly RuntimePatchDefinition CollisionDefinition =
            RuntimePatchDefinition.Transpile(
                "Missile collision empty target sentinel",
                "org.magickacommunitypatch.missile-collision-target-sentinel",
                FindCollision,
                typeof(MissileEventTargetSentinelPatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type missile = Configure(assembly);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                missile, "Update", new Type[] { dataChannel, typeof(float) });
        }

        private static MethodInfo FindCollision(Assembly assembly)
        {
            Type missile = Configure(assembly);
            Type skin = RuntimeMember.FindLoadedType(
                "JigLibX.Collision.CollisionSkin");
            return RequireMethod(
                missile,
                "OnCollision",
                new Type[] { skin, typeof(int), skin, typeof(int) });
        }

        private static Type Configure(Assembly assembly)
        {
            Type missile = assembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity", true);
            messageType = assembly.GetType(
                "Magicka.Network.MissileEntityEventMessage", true);
            targetHandleField = messageType.GetField(
                "TargetHandle",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (targetHandleField == null ||
                targetHandleField.FieldType != typeof(ushort))
                throw new MissingFieldException(
                    messageType.FullName, "TargetHandle");
            return missile;
        }

        private static MethodInfo RequireMethod(
            Type type, string name, Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name, Instance, null, parameters, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int initializers = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Initobj ||
                    result[index].operand as Type != messageType)
                    continue;
                CodeInstruction address = result[index - 1];
                if (!LoadsLocalAddress(address))
                    throw new InvalidOperationException(
                        "Missile message init does not use a local address.");
                result.Insert(
                    index + 1,
                    new CodeInstruction(address.opcode, address.operand));
                result.Insert(
                    index + 2,
                    new CodeInstruction(OpCodes.Ldc_I4, (int)ushort.MaxValue));
                result.Insert(
                    index + 3,
                    new CodeInstruction(OpCodes.Stfld, targetHandleField));
                initializers++;
                index += 3;
            }
            if (initializers != 1)
                throw new InvalidOperationException(
                    "Expected one missile event message initializer, found " +
                    initializers + ".");
            return result;
        }

        private static bool LoadsLocalAddress(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S;
        }
    }
}
