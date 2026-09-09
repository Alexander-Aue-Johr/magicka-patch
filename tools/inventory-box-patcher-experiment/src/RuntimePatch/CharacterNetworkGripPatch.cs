using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterNetworkGripPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static Type characterType;
        private static FieldInfo instancesField;
        private static FieldInfo actionField;
        private static FieldInfo targetHandleField;
        private static FieldInfo gripTypeParameterField;
        private static FieldInfo jointIndexField;
        private static FieldInfo bodyField;
        private static FieldInfo animationControllerField;
        private static PropertyInfo skeletonProperty;
        private static PropertyInfo skeletonCountProperty;
        private static PropertyInfo skeletonItemProperty;
        private static object gripAction;
        private static int pickupGripType;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character late grip packet guard",
                "org.magickacommunitypatch.character-late-grip",
                FindNetworkAction,
                ExactPrefixAdapter.CreateRefArgument);

        private static MethodInfo FindNetworkAction(Assembly assembly)
        {
            Type entityType = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            characterType = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type messageType = assembly.GetType(
                "Magicka.Network.CharacterActionMessage",
                true);

            instancesField = RequireField(entityType, "mInstances");
            actionField = RequireField(messageType, "Action");
            targetHandleField = RequireField(messageType, "TargetHandle");
            gripTypeParameterField = RequireField(messageType, "Param0I");
            jointIndexField = RequireField(messageType, "Param1I");
            bodyField = RequireField(characterType, "mBody");
            animationControllerField = RequireField(
                characterType,
                "mAnimationController");

            gripAction = Enum.Parse(actionField.FieldType, "Grip");
            FieldInfo gripTypeField = RequireField(characterType, "mGripType");
            pickupGripType = Convert.ToInt32(
                Enum.Parse(gripTypeField.FieldType, "Pickup"));
            skeletonProperty = animationControllerField.FieldType.GetProperty(
                "Skeleton",
                BindingFlags.Instance | BindingFlags.Public);
            if (skeletonProperty == null ||
                skeletonProperty.GetGetMethod() == null)
                throw new MissingMemberException(
                    animationControllerField.FieldType.FullName,
                    "Skeleton");
            Type skeletonType = skeletonProperty.PropertyType;
            skeletonCountProperty = skeletonType.GetProperty(
                "Count",
                BindingFlags.Instance | BindingFlags.Public);
            skeletonItemProperty = skeletonType.GetProperty(
                "Item",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                null,
                new Type[] { typeof(int) },
                null);

            MethodInfo method = characterType.GetMethod(
                "NetworkAction",
                InstanceMembers | BindingFlags.DeclaredOnly,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (!typeof(IList).IsAssignableFrom(instancesField.FieldType) ||
                skeletonCountProperty == null ||
                skeletonCountProperty.PropertyType != typeof(int) ||
                skeletonCountProperty.GetGetMethod() == null ||
                skeletonItemProperty == null ||
                skeletonItemProperty.GetGetMethod() == null)
                throw new MissingMemberException(
                    "Character grip validation contract is incomplete.");
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    characterType.FullName,
                    "NetworkAction");
            return method;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type;
                current != null;
                current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        public static bool Prefix(object character, object message)
        {
            if (character == null || message == null ||
                !Object.Equals(actionField.GetValue(message), gripAction))
                return true;

            IList instances = instancesField.GetValue(null) as IList;
            int handle = Convert.ToInt32(targetHandleField.GetValue(message));
            if (instances == null || handle < 0 || handle >= instances.Count)
                return false;
            object target = instances[handle];
            if (target == null || !characterType.IsInstanceOfType(target))
                return false;
            if (bodyField.GetValue(character) == null ||
                bodyField.GetValue(target) == null)
                return false;

            int jointIndex = Convert.ToInt32(jointIndexField.GetValue(message));
            if (jointIndex < 0)
                return true;
            int gripType = Convert.ToInt32(
                gripTypeParameterField.GetValue(message));
            object controller = animationControllerField.GetValue(
                gripType == pickupGripType ? character : target);
            if (controller == null)
                return false;
            object skeleton = skeletonProperty.GetValue(controller, null);
            if (skeleton == null)
                return false;
            int count = (int)skeletonCountProperty.GetValue(skeleton, null);
            if (jointIndex >= count)
                return false;
            return skeletonItemProperty.GetValue(
                skeleton,
                new object[] { jointIndex }) != null;
        }
    }
}
