using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSparksRetirementPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            Definition("initialize", FindInitialize);

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            Definition("update", assembly => FindServiceMethod(
                assembly, "Update"));

        internal static readonly RuntimePatchDefinition EndRunDefinition =
            Definition("end run", assembly => FindServiceMethod(
                assembly, "Dispose"));

        private static RuntimePatchDefinition Definition(
            string label, Func<Assembly, MethodInfo> findTarget)
        {
            return RuntimePatchDefinition.Prefix(
                "GameSparks retirement " + label,
                "org.magickacommunitypatch.gamesparks-retirement-" +
                    label.Replace(' ', '-'),
                findTarget,
                target => typeof(GameSparksRetirementPatch).GetMethod("Skip"));
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type service = assembly.GetType(
                "Magicka.WebTools.GameSparks.GameSparksServices", true);
            Type platform = assembly.GetType(
                "Magicka.WebTools.GameSparks.Platforms.GSWindowsPlatform",
                true);
            MethodInfo[] methods = service.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if (method.Name == "Initialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 0)
                    return method.MakeGenericMethod(platform);
            }
            throw new MissingMethodException(service.FullName, "Initialize<T>");
        }

        private static MethodInfo FindServiceMethod(
            Assembly assembly,
            string name)
        {
            Type service = assembly.GetType(
                "Magicka.WebTools.GameSparks.GameSparksServices", true);
            MethodInfo method = service.GetMethod(
                name, Members, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(service.FullName, name);
            return method;
        }

        public static bool Skip()
        {
            return false;
        }
    }
}
