using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal sealed class RuntimePatchDefinition
    {
        internal string Name { get; private set; }
        internal string HarmonyOwner { get; private set; }
        internal Func<Assembly, MethodInfo> FindTarget { get; private set; }
        internal Func<MethodInfo, MethodInfo> CreatePrefix { get; private set; }
        internal Func<MethodInfo, MethodInfo> CreatePostfix { get; private set; }
        internal MethodInfo Transpiler { get; private set; }
        internal string Kind { get; private set; }

        private RuntimePatchDefinition(
            string name,
            string harmonyOwner,
            Func<Assembly, MethodInfo> findTarget,
            Func<MethodInfo, MethodInfo> createPrefix,
            Func<MethodInfo, MethodInfo> createPostfix,
            MethodInfo transpiler,
            string kind)
        {
            Name = name;
            HarmonyOwner = harmonyOwner;
            FindTarget = findTarget;
            CreatePrefix = createPrefix;
            CreatePostfix = createPostfix;
            Transpiler = transpiler;
            Kind = kind;
        }

        internal static RuntimePatchDefinition Prefix(
            string name,
            string harmonyOwner,
            Func<Assembly, MethodInfo> findTarget,
            Func<MethodInfo, MethodInfo> createPrefix)
        {
            return new RuntimePatchDefinition(
                name,
                harmonyOwner,
                findTarget,
                createPrefix,
                null,
                null,
                "prefix");
        }

        internal static RuntimePatchDefinition Postfix(
            string name,
            string harmonyOwner,
            Func<Assembly, MethodInfo> findTarget,
            Func<MethodInfo, MethodInfo> createPostfix)
        {
            return new RuntimePatchDefinition(
                name,
                harmonyOwner,
                findTarget,
                null,
                createPostfix,
                null,
                "postfix");
        }

        internal static RuntimePatchDefinition Transpile(
            string name,
            string harmonyOwner,
            Func<Assembly, MethodInfo> findTarget,
            MethodInfo transpiler)
        {
            return new RuntimePatchDefinition(
                name,
                harmonyOwner,
                findTarget,
                null,
                null,
                transpiler,
                "transpiler");
        }
    }
}
