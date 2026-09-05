using System;
using System.Reflection;

namespace Magicka.CommunityPatch
{
    internal static class NetworkEntityHandleGuard
    {
        internal static bool IsUsableWorldSyncSpawnNpc(int handle, object playState)
        {
            Assembly magicka = playState.GetType().Assembly;
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
