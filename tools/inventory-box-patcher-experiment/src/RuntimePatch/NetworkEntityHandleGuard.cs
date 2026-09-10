using System;
using System.Reflection;
using System.Collections;

namespace Magicka.CommunityPatch
{
    public static class NetworkEntityHandleGuard
    {
        private static Assembly targetAssembly;

        internal static void Initialize(Assembly assembly)
        {
            targetAssembly = assembly;
        }

        internal static object Resolve(
            int handle,
            string side,
            string reason,
            bool emitTelemetry)
        {
            object entity = GetEntity(handle);
            if (IsUsable(entity))
                return entity;
            if (emitTelemetry)
                Runtime.RuntimePatchTelemetry.SendNetworkGuardDrop(
                    side, "EntityHandle", String.Empty, String.Empty,
                    reason, "handle=" + handle);
            return null;
        }

        internal static object ResolveActive(
            int handle,
            string side,
            string reason,
            bool emitTelemetry)
        {
            object entity = GetEntity(handle);
            if (IsActive(entity))
                return entity;
            if (emitTelemetry)
                Runtime.RuntimePatchTelemetry.SendNetworkGuardDrop(
                    side, "EntityHandle", String.Empty, String.Empty,
                    reason, "handle=" + handle);
            return null;
        }

        public static bool IsActive(object entity)
        {
            if (!IsUsable(entity))
                return false;
            object playState = ReadProperty(entity, "PlayState");
            object manager = ReadProperty(playState, "EntityManager");
            if (manager == null)
                return false;
            MethodInfo contains = manager.GetType().GetMethod(
                "Contains",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { targetAssembly.GetType(
                    "Magicka.GameLogic.Entities.Entity", true) },
                null);
            return contains != null && (bool)contains.Invoke(
                manager, new object[] { entity });
        }

        internal static bool IsUsable(object entity)
        {
            return entity != null &&
                !ReadOptionalBoolean(entity, "IsDisposed", "mDisposed") &&
                ReadProperty(entity, "PlayState") != null;
        }

        internal static bool HasBody(object entity)
        {
            return IsActive(entity) && ReadProperty(entity, "Body") != null;
        }

        private static object GetEntity(int handle)
        {
            if (targetAssembly == null || handle < 0)
                return null;
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity", true);
            FieldInfo instances = FindField(entityType, "mInstances");
            IList list = instances == null
                ? null
                : instances.GetValue(null) as IList;
            return list != null && handle < list.Count ? list[handle] : null;
        }

        internal static bool IsUsableWorldSyncSpawnNpc(int handle, object playState)
        {
            Assembly magicka = playState.GetType().Assembly;
            Initialize(magicka);
            Type entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
            object entity = entityType.GetMethod(
                "GetFromHandle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null).Invoke(null, new object[] { handle });
            if (entity == null || ReadOptionalBoolean(entity, "IsDisposed", "mDisposed"))
                return false;

            object entityPlayState = ReadProperty(entity, "PlayState");
            Type nonPlayerCharacter = magicka.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            return entityPlayState != null &&
                nonPlayerCharacter.IsInstanceOfType(entity) &&
                Object.ReferenceEquals(entityPlayState, playState);
        }

        private static bool ReadOptionalBoolean(
            object target,
            string propertyName,
            string fieldName)
        {
            PropertyInfo property = FindProperty(target.GetType(), propertyName);
            if (property != null)
                return (bool)property.GetValue(target, null);

            FieldInfo field = FindField(target.GetType(), fieldName);
            return field != null && (bool)field.GetValue(target);
        }

        private static object ReadProperty(object target, string propertyName)
        {
            PropertyInfo property = FindProperty(target.GetType(), propertyName);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, propertyName);
            return property.GetValue(target, null);
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                    return property;
            }
            return null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;
            }
            return null;
        }
    }
}
