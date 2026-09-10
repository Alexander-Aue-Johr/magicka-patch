using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterAvatarTemplatePatch
    {
        private static Type templateType;
        private static FieldInfo avatarCacheField;
        private static FieldInfo gameSingletonField;
        private static FieldInfo playersField;
        private static PropertyInfo gamerProperty;
        private static PropertyInfo gamerAvatarProperty;
        private static FieldInfo avatarTypeNameField;
        private static PropertyInfo playStateContentProperty;
        private static PropertyInfo recentPlayStateProperty;
        private static MethodInfo contentLoadMethod;

        internal static readonly RuntimePatchDefinition InitialiseDefinition =
            RuntimePatchDefinition.Prefix(
                "Active player avatar template initialization",
                "org.magickacommunitypatch.character-avatar-initialize",
                FindInitialise,
                CreateInitialisePrefix);

        internal static readonly RuntimePatchDefinition GetDefinition =
            RuntimePatchDefinition.Prefix(
                "Lazy player avatar template lookup",
                "org.magickacommunitypatch.character-avatar-lookup",
                FindGet,
                CreateGetPrefix);

        private static MethodInfo FindInitialise(Assembly assembly)
        {
            Configure(assembly);
            MethodInfo method = templateType.GetMethod(
                "InitialisePlayerAvatarCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            if (method == null || method.ReturnType != typeof(void) ||
                method.GetParameters().Length != 1)
                throw new MissingMethodException(
                    templateType.FullName, "InitialisePlayerAvatarCache");
            return method;
        }

        private static MethodInfo FindGet(Assembly assembly)
        {
            Configure(assembly);
            MethodInfo method = templateType.GetMethod(
                "GetCharacterTemplate",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null, new Type[] { typeof(string) }, null);
            if (method == null || method.ReturnType != templateType)
                throw new MissingMethodException(
                    templateType.FullName, "GetCharacterTemplate");
            return method;
        }

        private static void Configure(Assembly assembly)
        {
            templateType = assembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate", true);
            avatarCacheField = RequireField(
                templateType, "sCachedAvatarTemplates",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type gameType = assembly.GetType("Magicka.Game", true);
            gameSingletonField = RequireField(
                gameType, "mSingelton",
                BindingFlags.Static | BindingFlags.NonPublic);
            playersField = RequireField(
                gameType, "mPlayers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Type playerType = playersField.FieldType.GetElementType();
            gamerProperty = RequireProperty(playerType, "Gamer");
            gamerAvatarProperty = RequireProperty(
                gamerProperty.PropertyType, "Avatar");
            avatarTypeNameField = RequireField(
                gamerAvatarProperty.PropertyType, "TypeName",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            Type playStateType = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            playStateContentProperty = RequireProperty(
                playStateType, "Content");
            recentPlayStateProperty = RequireProperty(
                playStateType, "RecentPlayState");
            contentLoadMethod = FindLoad(playStateContentProperty.PropertyType);
            if (contentLoadMethod == null)
                throw new MissingMethodException(
                    playStateContentProperty.PropertyType.FullName, "Load<T>");
        }

        private static FieldInfo RequireField(
            Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(
                name, flags | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name, BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo FindLoad(Type contentType)
        {
            MethodInfo[] methods = contentType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Load" &&
                    methods[index].IsGenericMethodDefinition &&
                    methods[index].GetParameters().Length == 1 &&
                    methods[index].GetParameters()[0].ParameterType ==
                        typeof(string))
                    return methods[index];
            return null;
        }

        private static MethodInfo CreateInitialisePrefix(MethodInfo target)
        {
            return typeof(CharacterAvatarTemplatePatch).GetMethod(
                "InitialisePrefix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        private static MethodInfo CreateGetPrefix(MethodInfo target)
        {
            return typeof(CharacterAvatarTemplatePatch).GetMethod(
                "GetPrefix").MakeGenericMethod(target.ReturnType);
        }

        public static bool InitialisePrefix<TPlayState>(TPlayState iPlayState)
        {
            IDictionary cache = avatarCacheField.GetValue(null) as IDictionary;
            if (cache == null)
                throw new InvalidOperationException(
                    "Character avatar template cache is unavailable.");
            cache.Clear();
            object game = gameSingletonField.GetValue(null);
            Array players = game == null
                ? null : playersField.GetValue(game) as Array;
            object content = (object)iPlayState == null
                ? null : playStateContentProperty.GetValue(iPlayState, null);
            if (players == null || content == null)
                return false;
            for (int index = 0; index < players.Length; index++)
            {
                object player = players.GetValue(index);
                object gamer = player == null
                    ? null : gamerProperty.GetValue(player, null);
                object avatar = gamer == null
                    ? null : gamerAvatarProperty.GetValue(gamer, null);
                string typeName = avatar == null
                    ? null : avatarTypeNameField.GetValue(avatar) as string;
                if (String.IsNullOrEmpty(typeName) || cache.Contains(typeName))
                    continue;
                object template = Load(
                    content, "Data/Characters/" + typeName);
                if (template != null)
                    cache.Add(typeName, template);
            }
            return false;
        }

        public static bool GetPrefix<TTemplate>(
            string iTypeName, ref TTemplate __result)
        {
            if (String.IsNullOrEmpty(iTypeName))
            {
                __result = default(TTemplate);
                return false;
            }
            object playState = recentPlayStateProperty.GetValue(null, null);
            object content = playState == null
                ? null : playStateContentProperty.GetValue(playState, null);
            if (content == null)
            {
                __result = default(TTemplate);
                return false;
            }
            __result = (TTemplate)Load(
                content, "Data/Characters/" + iTypeName);
            return false;
        }

        private static object Load(object content, string assetName)
        {
            try
            {
                return contentLoadMethod.MakeGenericMethod(templateType).Invoke(
                    content, new object[] { assetName });
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                    throw exception.InnerException;
                throw;
            }
        }
    }
}
