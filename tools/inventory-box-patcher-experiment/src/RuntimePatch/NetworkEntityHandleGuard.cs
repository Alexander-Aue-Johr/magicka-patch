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
            if (entity == null || ReadBoolean(entity, "IsDisposed"))
                return false;

            object entityPlayState = ReadProperty(entity, "PlayState");
            Type nonPlayerCharacter = magicka.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            return entityPlayState != null &&
                nonPlayerCharacter.IsInstanceOfType(entity) &&
                Object.ReferenceEquals(entityPlayState, playState);
        }

        private static bool ReadBoolean(object target, string propertyName)
        {
            return (bool)ReadProperty(target, propertyName);
        }

        private static object ReadProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, propertyName);
            return property.GetValue(target, null);
        }
    }
}
