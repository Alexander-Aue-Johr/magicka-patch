using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate object RadialBlurStaticGetter();

    internal delegate object RadialBlurInstanceGetter(object instance);

    public static class RadialBlurLifetimePatch
    {
        private static FieldInfo contentField;
        private static FieldInfo sceneField;
        private static FieldInfo cacheField;
        private static RadialBlurStaticGetter getGameInstance;
        private static RadialBlurInstanceGetter getGameContent;
        private static RadialBlurStaticGetter getRecentPlayState;
        private static RadialBlurInstanceGetter getPlayStateScene;

        internal static readonly RuntimePatchDefinition InitializeCacheDefinition =
            RuntimePatchDefinition.Transpile(
                "RadialBlur global content lifetime",
                "org.magickacommunitypatch.radial-blur-global-content",
                FindInitializeCache,
                typeof(RadialBlurLifetimePatch).GetMethod(
                    "InitializeCacheTranspiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "RadialBlur scene retention removal",
                "org.magickacommunitypatch.radial-blur-scene-retention",
                FindInitialize,
                typeof(RadialBlurLifetimePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "RadialBlur current-scene rendering",
                "org.magickacommunitypatch.radial-blur-current-scene",
                FindUpdate,
                typeof(RadialBlurLifetimePatch).GetMethod(
                    "UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition DisposeCacheDefinition =
            RuntimePatchDefinition.Transpile(
                "RadialBlur disposed-cache release",
                "org.magickacommunitypatch.radial-blur-cache-release",
                FindDisposeCache,
                typeof(RadialBlurLifetimePatch).GetMethod(
                    "DisposeCacheTranspiler"));

        private static MethodInfo FindInitializeCache(Assembly assembly)
        {
            Type radialBlur = Configure(assembly);
            MethodInfo[] methods = Array.FindAll(
                radialBlur.GetMethods(
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "InitializeCache" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType ==
                        contentField.FieldType &&
                    method.GetParameters()[1].ParameterType == typeof(int));
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur.InitializeCache method, found " +
                    methods.Length + ".");
            return methods[0];
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type radialBlur = Configure(assembly);
            MethodInfo[] methods = Array.FindAll(
                radialBlur.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "Initialize" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 6 &&
                    method.GetParameters()[5].ParameterType ==
                        sceneField.FieldType);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one full RadialBlur.Initialize method, found " +
                    methods.Length + ".");
            return methods[0];
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type radialBlur = Configure(assembly);
            MethodInfo[] methods = Array.FindAll(
                radialBlur.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "Update" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[1].ParameterType == typeof(float));
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur.Update method, found " +
                    methods.Length + ".");
            return methods[0];
        }

        private static MethodInfo FindDisposeCache(Assembly assembly)
        {
            Type radialBlur = Configure(assembly);
            MethodInfo method = radialBlur.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    radialBlur.FullName,
                    "DisposeCache");
            return method;
        }

        private static Type Configure(Assembly assembly)
        {
            Type radialBlur = assembly.GetType(
                "Magicka.Graphics.Effects.RadialBlur",
                true);
            if (sceneField != null && sceneField.DeclaringType == radialBlur)
                return radialBlur;

            contentField = RequireField(
                radialBlur,
                "mContent",
                true);
            sceneField = RequireField(
                radialBlur,
                "mScene",
                false);
            cacheField = RequireField(
                radialBlur,
                "mCache",
                true);
            if (!typeof(IList).IsAssignableFrom(cacheField.FieldType))
                throw new InvalidOperationException(
                    "RadialBlur.mCache must implement IList.");

            Type game = assembly.GetType("Magicka.Game", true);
            PropertyInfo instance = RequireProperty(
                game,
                "Instance",
                true);
            PropertyInfo content = RequireProperty(
                game,
                "Content",
                false);
            if (content.PropertyType != contentField.FieldType)
                throw new InvalidOperationException(
                    "Game.Content does not match RadialBlur.mContent.");
            getGameInstance = CreateStaticGetter(instance.GetGetMethod(true));
            getGameContent = CreateInstanceGetter(content.GetGetMethod(true));

            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            PropertyInfo recent = RequireProperty(
                playState,
                "RecentPlayState",
                true);
            PropertyInfo scene = RequireProperty(
                playState,
                "Scene",
                false);
            if (scene.PropertyType != sceneField.FieldType)
                throw new InvalidOperationException(
                    "PlayState.Scene does not match RadialBlur.mScene.");
            getRecentPlayState = CreateStaticGetter(recent.GetGetMethod(true));
            getPlayStateScene = CreateInstanceGetter(scene.GetGetMethod(true));
            return radialBlur;
        }

        public static TContent GetGlobalContent<TContent>()
            where TContent : class
        {
            object game = getGameInstance();
            return game == null
                ? null
                : (TContent)getGameContent(game);
        }

        public static TScene GetCurrentScene<TScene>()
            where TScene : class
        {
            return ResolveCurrentScene() as TScene;
        }

        public static object ResolveCurrentScene()
        {
            object playState = getRecentPlayState();
            return playState == null
                ? null
                : getPlayStateScene(playState);
        }

        public static void ClearCache(IList cache)
        {
            if (cache != null)
                cache.Clear();
        }

        public static IEnumerable<CodeInstruction> InitializeCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            MethodInfo globalContent = typeof(RadialBlurLifetimePatch)
                .GetMethod("GetGlobalContent")
                .MakeGenericMethod(contentField.FieldType);
            int storesRemoved = 0;
            int loadsReplaced = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Stsfld &&
                    Object.Equals(result[index].operand, contentField))
                {
                    if (index == 0 || result[index - 1].opcode != OpCodes.Ldarg_0)
                        throw new InvalidOperationException(
                            "RadialBlur content store source changed.");
                    MakeNop(result[index - 1]);
                    MakeNop(result[index]);
                    storesRemoved++;
                }
                else if (result[index].opcode == OpCodes.Ldsfld &&
                    Object.Equals(result[index].operand, contentField))
                {
                    result[index].opcode = OpCodes.Call;
                    result[index].operand = globalContent;
                    loadsReplaced++;
                }
            }
            if (storesRemoved != 1 || loadsReplaced != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur content store and load, found " +
                    storesRemoved + " and " + loadsReplaced + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int removed = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, sceneField))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "RadialBlur scene store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                removed++;
            }
            if (removed != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur scene store, found " + removed + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            MethodInfo currentScene = typeof(RadialBlurLifetimePatch)
                .GetMethod("GetCurrentScene")
                .MakeGenericMethod(sceneField.FieldType);
            int replaced = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, sceneField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "RadialBlur scene load source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = currentScene;
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur scene load, found " + replaced + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> DisposeCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            MethodInfo clear = typeof(RadialBlurLifetimePatch).GetMethod(
                "ClearCache");
            int returns = 0;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (result[index].opcode != OpCodes.Ret)
                    continue;
                CodeInstruction load = new CodeInstruction(
                    OpCodes.Ldsfld,
                    cacheField);
                load.labels.AddRange(result[index].labels);
                load.blocks.AddRange(result[index].blocks);
                result[index].labels.Clear();
                result[index].blocks.Clear();
                result.Insert(index, load);
                result.Insert(index + 1, new CodeInstruction(OpCodes.Call, clear));
                returns++;
            }
            if (returns != 1)
                throw new InvalidOperationException(
                    "Expected one RadialBlur.DisposeCache return, found " +
                    returns + ".");
            return result;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            bool isStatic)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.IsStatic != isStatic)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string name,
            bool isStatic)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (property == null || getter == null || getter.IsStatic != isStatic ||
                property.GetIndexParameters().Length != 0)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static RadialBlurStaticGetter CreateStaticGetter(
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetRadialBlurStaticValue",
                typeof(object),
                Type.EmptyTypes,
                typeof(RadialBlurLifetimePatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Call, getter);
            il.Emit(OpCodes.Ret);
            return (RadialBlurStaticGetter)method.CreateDelegate(
                typeof(RadialBlurStaticGetter));
        }

        private static RadialBlurInstanceGetter CreateInstanceGetter(
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetRadialBlurInstanceValue",
                typeof(object),
                new Type[] { typeof(object) },
                typeof(RadialBlurLifetimePatch),
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, getter.DeclaringType);
            il.Emit(getter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getter);
            il.Emit(OpCodes.Ret);
            return (RadialBlurInstanceGetter)method.CreateDelegate(
                typeof(RadialBlurInstanceGetter));
        }
    }
}
