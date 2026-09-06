using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AvatarTargetMethods
    {
        private const string AvatarTypeName = "Magicka.GameLogic.Entities.Avatar";

        internal static MethodInfo FindFindInteractableIn(Assembly targetAssembly)
        {
            Type avatarType = targetAssembly.GetType(AvatarTypeName, true);
            MethodInfo method = avatarType.GetMethod(
                "FindInteractable",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(bool) },
                null);
            if (method == null)
                throw new MissingMethodException(avatarType.FullName, "FindInteractable");
            return method;
        }
    }
}
