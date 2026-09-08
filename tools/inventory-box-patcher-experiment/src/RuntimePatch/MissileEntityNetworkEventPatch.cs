using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MissileEntityNetworkEventPatch
    {
        private static FieldInfo conditionCollectionField;
        private static FieldInfo hitListField;
        private static FieldInfo gibbedHitListField;
        private static FieldInfo playStateField;
        private static FieldInfo collisionField;
        private static FieldInfo conditionTypeField;
        private static FieldInfo targetHandleField;
        private static MethodInfo getFromHandle;
        private static MethodInfo kill;
        private static PropertyInfo bodyProperty;
        private static Type damageableType;
        private static int hitCondition;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "MissileEntity invalid network event guard",
                "org.magickacommunitypatch.missile-network-event",
                FindNetworkEventMessage,
                CreatePrefix);

        private static MethodInfo FindNetworkEventMessage(Assembly targetAssembly)
        {
            Type missileType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity",
                true);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.MissileEntityEventMessage",
                true);
            damageableType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.IDamageable",
                true);

            conditionCollectionField = RequireField(
                missileType,
                "mConditionCollection");
            hitListField = RequireField(missileType, "mHitList");
            gibbedHitListField = RequireField(missileType, "mGibbedHitList");
            playStateField = RequireField(entityType, "mPlayState");
            collisionField = RequireField(messageType, "OnCollision");
            conditionTypeField = RequireField(
                messageType,
                "EventConditionType");
            targetHandleField = RequireField(messageType, "TargetHandle");
            getFromHandle = entityType.GetMethod(
                "GetFromHandle",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null);
            kill = missileType.GetMethod(
                "Kill",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            bodyProperty = entityType.GetProperty(
                "Body",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (getFromHandle == null)
                throw new MissingMethodException(
                    entityType.FullName,
                    "GetFromHandle");
            if (kill == null || kill.ReturnType != typeof(void))
                throw new MissingMethodException(missileType.FullName, "Kill");
            if (bodyProperty == null ||
                bodyProperty.GetGetMethod(true) == null)
                throw new MissingMemberException(
                    entityType.FullName,
                    "Body");
            if (collisionField.FieldType != typeof(bool) ||
                targetHandleField.FieldType != typeof(ushort) ||
                !conditionTypeField.FieldType.IsEnum)
                throw new InvalidOperationException(
                    "MissileEntityEventMessage field contract changed.");
            hitCondition = Convert.ToInt32(
                Enum.Parse(conditionTypeField.FieldType, "Hit"));

            MethodInfo method = missileType.GetMethod(
                "NetworkEventMessage",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    missileType.FullName,
                    "NetworkEventMessage");
            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            Type messageType = target.GetParameters()[0].ParameterType;
            if (!messageType.IsByRef)
                throw new InvalidOperationException(
                    "MissileEntity.NetworkEventMessage argument is not by-reference.");
            Type adapter = typeof(MissileEntityNetworkEventPrefix<,>)
                .MakeGenericType(
                    target.DeclaringType,
                    messageType.GetElementType());
            return adapter.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }

        public static bool Prefix(object missile, object message)
        {
            if (missile == null || message == null)
                return true;
            if (conditionCollectionField.GetValue(missile) == null ||
                hitListField.GetValue(missile) == null ||
                gibbedHitListField.GetValue(missile) == null ||
                playStateField.GetValue(missile) == null)
                return false;

            bool collision = (bool)collisionField.GetValue(message);
            int condition = Convert.ToInt32(
                conditionTypeField.GetValue(message));
            if (!collision && (condition & hitCondition) != hitCondition)
                return true;

            ushort handle = (ushort)targetHandleField.GetValue(message);
            if (handle == ushort.MaxValue)
                return RejectMissingTarget(missile, collision);
            object target = getFromHandle.Invoke(
                null,
                new object[] { (int)handle });
            if (target == null ||
                bodyProperty.GetValue(target, null) == null ||
                playStateField.GetValue(target) == null)
                return RejectMissingTarget(missile, collision);
            return !collision || damageableType.IsInstanceOfType(target);
        }

        private static bool RejectMissingTarget(object missile, bool collision)
        {
            if (collision)
                kill.Invoke(missile, null);
            return false;
        }
    }

    public static class MissileEntityNetworkEventPrefix<TInstance, TMessage>
    {
        public static bool Prefix(TInstance __instance, ref TMessage iMsg)
        {
            return MissileEntityNetworkEventPatch.Prefix(__instance, iMsg);
        }
    }
}
