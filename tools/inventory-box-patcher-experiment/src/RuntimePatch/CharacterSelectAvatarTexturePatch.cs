using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterSelectAvatarTexturePatch
    {
        private static FieldInfo customTextureField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character-select avatar texture guard",
                "org.magickacommunitypatch.character-select-avatar-texture",
                FindTarget,
                CreatePrefix);

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                true);
            customTextureField = type.GetField(
                "mCustomTexture",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "DrawAvatar" &&
                    methods[index].GetParameters().Length == 5)
                {
                    if (result != null)
                        throw new AmbiguousMatchException(type.FullName + ".DrawAvatar");
                    result = methods[index];
                }
            if (customTextureField == null || result == null)
                throw new MissingMemberException(type.FullName, "DrawAvatar contract");
            return result;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(CharacterSelectAvatarTexturePatch).GetMethod(
                "Prefix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        public static bool Prefix<TTexture>(
            object __instance, TTexture iTexture, bool iCustom)
        {
            return (object)iTexture != null &&
                (!iCustom || customTextureField.GetValue(__instance) != null);
        }
    }
}
