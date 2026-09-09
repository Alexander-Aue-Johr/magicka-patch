using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PortalLifecyclePatch
    {
        private const string PortalTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal";

        private static FieldInfo outerPlayStateField;
        private static FieldInfo entityPlayStateField;
        private static FieldInfo portalAField;
        private static FieldInfo portalBField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Portal play-state release",
                "org.magickacommunitypatch.portal-state-release",
                FindExecute,
                typeof(PortalLifecyclePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition VectorInitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "Portal current vector initialization play state",
                "org.magickacommunitypatch.portal-vector-current-state",
                FindVectorInitialize,
                typeof(PortalLifecyclePatch).GetMethod(
                    "VectorInitializeTranspiler"));

        internal static readonly RuntimePatchDefinition MessageInitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "Portal current message initialization play state",
                "org.magickacommunitypatch.portal-message-current-state",
                FindMessageInitialize,
                typeof(PortalLifecyclePatch).GetMethod(
                    "MessageInitializeTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Portal current update play state",
                "org.magickacommunitypatch.portal-update-current-state",
                FindUpdate,
                typeof(PortalLifecyclePatch).GetMethod(
                    "UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition CleanupDefinition =
            RuntimePatchDefinition.Transpile(
                "Portal level teardown",
                "org.magickacommunitypatch.portal-level-teardown",
                FindPlayStateDispose,
                typeof(PortalLifecyclePatch).GetMethod(
                    "CleanupTranspiler"));

        private static MethodInfo FindExecute(Assembly assembly)
        {
            Type portal;
            Type portalEntity;
            Type playState;
            Configure(assembly, out portal, out portalEntity, out playState);
            Type owner = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                portal,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindVectorInitialize(Assembly assembly)
        {
            Type portal;
            Type portalEntity;
            Type playState;
            Configure(assembly, out portal, out portalEntity, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                portalEntity,
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector.MakeByRefType() },
                typeof(void));
        }

        private static MethodInfo FindMessageInitialize(Assembly assembly)
        {
            Type portal;
            Type portalEntity;
            Type playState;
            Configure(assembly, out portal, out portalEntity, out playState);
            Type message = assembly.GetType(
                "Magicka.Network.SpawnPortalMessage",
                true);
            return RequireMethod(
                portalEntity,
                "Initialize",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                new Type[] { message.MakeByRefType() },
                typeof(void));
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type portal;
            Type portalEntity;
            Type playState;
            Configure(assembly, out portal, out portalEntity, out playState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                portalEntity,
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindPlayStateDispose(Assembly assembly)
        {
            Type portal;
            Type portalEntity;
            Type playState;
            Configure(assembly, out portal, out portalEntity, out playState);
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            clearHandlesMethod = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(
                    entity.FullName,
                    "ClearHandles");
            return RequireMethod(
                playState,
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(void));
        }

        private static void Configure(
            Assembly assembly,
            out Type portal,
            out Type portalEntity,
            out Type playState)
        {
            portal = assembly.GetType(PortalTypeName, true);
            portalEntity = portal.GetNestedType(
                "PortalEntity",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (portalEntity == null)
                throw new TypeLoadException(portal.FullName + "+PortalEntity");
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            outerPlayStateField = portal.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            entityPlayStateField = FindField(portalEntity, "mPlayState");
            portalAField = RequireField(
                portal,
                "sPortalA",
                portalEntity,
                true);
            portalBField = RequireField(
                portal,
                "sPortalB",
                portalEntity,
                true);
            if (outerPlayStateField == null ||
                outerPlayStateField.FieldType != playState ||
                entityPlayStateField.FieldType != playState)
                throw new MissingFieldException(portal.FullName, "mPlayState");
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return RemoveStore(instructions);
        }

        public static IEnumerable<CodeInstruction> VectorInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return ReplaceReads(instructions, 2, "vector Initialize");
        }

        public static IEnumerable<CodeInstruction> MessageInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return ReplaceReads(instructions, 1, "message Initialize");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            return ReplaceReads(instructions, 1, "Update");
        }

        public static IEnumerable<CodeInstruction> CleanupTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    SameMember(method, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in " +
                    "PlayState.Dispose, found " + matches + ".");
            result.Insert(
                anchor,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(PortalLifecyclePatch).GetMethod(
                        "ReleasePortalEntities")));
            return result;
        }

        public static void ReleasePortalEntities()
        {
            if (portalAField == null || portalBField == null)
                throw new InvalidOperationException(
                    "Portal cleanup contract has not been initialized.");
            portalAField.SetValue(null, null);
            portalBField.SetValue(null, null);
        }

        private static IEnumerable<CodeInstruction> RemoveStore(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !SameMember(result[index].operand, outerPlayStateField))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2)
                    throw new InvalidOperationException(
                        "Portal Execute play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one Portal Execute play-state store, found " +
                    stores + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            int expectedReads,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !SameMember(result[index].operand, entityPlayStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "Portal " + methodName +
                        " play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != expectedReads)
                throw new InvalidOperationException(
                    "Expected " + expectedReads + " Portal " + methodName +
                    " play-state reads, found " + reads + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type portal = RuntimeMember.FindLoadedType(PortalTypeName);
            Type ignoredPortal;
            Type ignoredPortalEntity;
            Type ignoredPlayState;
            Configure(
                portal.Assembly,
                out ignoredPortal,
                out ignoredPortalEntity,
                out ignoredPlayState);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType,
            bool isStatic)
        {
            BindingFlags flags = BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            BindingFlags flags,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                flags,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static bool SameMember(object left, MemberInfo right)
        {
            MemberInfo leftMember = left as MemberInfo;
            return leftMember != null && right != null &&
                leftMember.Module == right.Module &&
                leftMember.MetadataToken == right.MetadataToken;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
