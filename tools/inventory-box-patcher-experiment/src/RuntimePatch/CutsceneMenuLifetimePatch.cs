using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CutsceneMenuLifetimePatch
    {
        private const BindingFlags Any = BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type menuType;
        private static MethodInfo gameContentGetter;
        private static object contentManager;
        private static Type contentManagerType;
        private static Type textureType;
        private static bool inactive;
        private static int exitPatchApplied;
        private static readonly string[] TextureFields = new string[] {
            "mTexture", "mMaskTexture", "mPageTexture", "mTexture_Dungeons",
            "mMaskTexture_Dungeons", "mPageTexture_Dungeons",
            "mTexture_Dungeons2", "mMaskTexture_Dungeons2",
            "mPageTexture_Dungeons2" };

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Cutscene menu private content ownership",
                "org.magickacommunitypatch.cutscene-content-owner",
                FindConstructor,
                typeof(CutsceneMenuLifetimePatch).GetMethod(
                    "ConstructorTranspiler"));
        internal static readonly RuntimePatchDefinition EnterDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene menu content activation",
                "org.magickacommunitypatch.cutscene-content-enter",
                assembly => FindMethod(assembly, "OnEnter", 0),
                target => typeof(CutsceneEnterPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));
        internal static readonly RuntimePatchDefinition ExitDefinition =
            RuntimePatchDefinition.Postfix(
                "Cutscene menu content release",
                "org.magickacommunitypatch.cutscene-content-exit",
                FindExitMethod,
                target => typeof(CutsceneMenuLifetimePatch).GetMethod(
                    "ExitPostfix"));
        internal static readonly RuntimePatchDefinition DrawDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene menu inactive draw guard",
                "org.magickacommunitypatch.cutscene-draw-guard",
                assembly => FindMethod(assembly, "Draw", 2),
                target => typeof(CutsceneDrawPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));
        internal static readonly RuntimePatchDefinition DrawOldDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene menu inactive old-draw guard",
                "org.magickacommunitypatch.cutscene-old-draw-guard",
                assembly => FindMethod(assembly, "DrawOld", 2),
                target => typeof(CutsceneDrawPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));
        internal static readonly RuntimePatchDefinition DrawBothDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene menu inactive transition-draw guard",
                "org.magickacommunitypatch.cutscene-transition-draw-guard",
                assembly => FindMethod(assembly, "DrawNewAndOld", 5),
                target => typeof(CutsceneDrawPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));
        internal static readonly RuntimePatchDefinition LevelDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene menu campaign mode reset",
                "org.magickacommunitypatch.cutscene-level-reset",
                FindLevelSetter,
                target => typeof(CutsceneLevelPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Initialize(assembly);
            ConstructorInfo constructor = menuType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic, null,
                Type.EmptyTypes, null);
            if (constructor == null) throw new MissingMethodException(
                menuType.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindMethod(Assembly assembly, string name,
            int parameterCount)
        {
            Initialize(assembly);
            foreach (MethodInfo method in menuType.GetMethods(Any |
                BindingFlags.DeclaredOnly))
                if (method.Name == name &&
                    method.GetParameters().Length == parameterCount)
                    return method;
            throw new MissingMethodException(menuType.FullName, name);
        }

        private static MethodInfo FindLevelSetter(Assembly assembly)
        {
            Initialize(assembly);
            return menuType.GetProperty("Level", Any).GetSetMethod(true);
        }

        private static MethodInfo FindExitMethod(Assembly assembly)
        {
            Initialize(assembly);
            Type type = menuType.BaseType;
            while (type != null)
            {
                MethodInfo method = type.GetMethod("OnExit", Any |
                    BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null) return method;
                type = type.BaseType;
            }
            throw new MissingMethodException(menuType.FullName, "OnExit");
        }

        private static void EnsureExitPatch()
        {
            if (Interlocked.CompareExchange(ref exitPatchApplied, 1, 0) != 0)
                return;
            RuntimePatchSession.Apply(
                Assembly.GetEntryAssembly(),
                ExitDefinition);
        }

        public static void ExitPostfix(object __instance)
        {
            Exit(__instance);
        }

        private static void Initialize(Assembly assembly)
        {
            menuType = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCutscene", true);
            Type game = assembly.GetType("Magicka.Game", true);
            gameContentGetter = game.GetProperty("Content", Any).GetGetMethod(true);
            contentManagerType = gameContentGetter.ReturnType;
            MethodInfo load = FindLoadMethod(contentManagerType);
            textureType = load.GetGenericArguments().Length == 1
                ? RuntimeMember.FindLoadedType("Microsoft.Xna.Framework.Graphics.Texture2D")
                : null;
            if (textureType == null) throw new TypeLoadException("Texture2D");
        }

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            MethodInfo replacement = typeof(CutsceneMenuLifetimePatch)
                .GetMethod("GetContentManager").MakeGenericMethod(contentManagerType);
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                     result[index].opcode == OpCodes.Callvirt) &&
                    called != null && called.Name == "get_Content" &&
                    called.ReturnType == contentManagerType &&
                    called.GetParameters().Length == 0)
                {
                    result[index].opcode = OpCodes.Call;
                    result[index].operand = replacement;
                    replacements++;
                }
            }
            if (replacements != 7)
                throw new InvalidOperationException(
                    "Expected seven cutscene content-owner replacements, found " +
                    replacements + ".");
            return result;
        }

        public static T GetContentManager<T>(object game) where T : class
        {
            return (T)EnsureContentManager();
        }

        public static void Enter(object menu)
        {
            EnsureExitPatch();
            inactive = false;
            object manager = EnsureContentManager();
            Load(menu, manager, "mMaskTexture", "UI/Menu/MapMask");
            Load(menu, manager, "mPageTexture", "UI/ToM/tome_pages_0");
            bool dungeon = GetBool(menu, "mIsDungeons");
            bool dungeon2 = GetBool(menu, "mIsDungeons2");
            if (dungeon)
                Load(menu, manager, "mTexture_Dungeons",
                    "UI/Menu/CampaignMap_Dungeons");
            else if (dungeon2)
                Load(menu, manager, "mTexture_Dungeons2",
                    "UI/Menu/CampaignMap_Dungeons2");
            else Load(menu, manager, "mTexture", "UI/Menu/CampaignMap");
        }

        public static void Exit(object menu)
        {
            if (menu == null || !menuType.IsInstanceOfType(menu)) return;
            if (inactive) return;
            inactive = true;
            try
            {
                object cue = GetField(menu, "mCurrentCue").GetValue(menu);
                if (cue != null)
                {
                    PropertyInfo playing = cue.GetType().GetProperty("IsPlaying");
                    if (playing != null && (bool)playing.GetValue(cue, null))
                    {
                        MethodInfo stop = cue.GetType().GetMethod("Stop");
                        Type option = stop.GetParameters()[0].ParameterType;
                        stop.Invoke(cue, new object[] { Enum.Parse(option, "Immediate") });
                    }
                    ((IDisposable)cue).Dispose();
                    GetField(menu, "mCurrentCue").SetValue(menu, null);
                }
            }
            catch { }
            object manager = contentManager;
            contentManager = null;
            if (manager != null)
            {
                try { manager.GetType().GetMethod("Unload").Invoke(manager, null); }
                finally { ((IDisposable)manager).Dispose(); }
            }
            for (int index = 0; index < TextureFields.Length; index++)
            {
                FieldInfo field = GetField(menu, TextureFields[index]);
                if (field != null) field.SetValue(menu, null);
            }
        }

        public static bool CanDraw() { return !inactive; }

        public static void ResetLevelMode(object menu)
        {
            FieldInfo first = GetField(menu, "mIsDungeons");
            FieldInfo second = GetField(menu, "mIsDungeons2");
            if (first != null) first.SetValue(menu, false);
            if (second != null) second.SetValue(menu, false);
        }

        private static object EnsureContentManager()
        {
            if (contentManager != null) return contentManager;
            Assembly assembly = menuType.Assembly;
            Type gameType = assembly.GetType("Magicka.Game", true);
            object game = gameType.GetProperty("Instance", Any)
                .GetValue(null, null);
            object services = gameType.GetProperty("Services", Any)
                .GetValue(game, null);
            object global = gameContentGetter.Invoke(game, null);
            string root = (string)contentManagerType.GetProperty("RootDirectory")
                .GetValue(global, null);
            contentManager = Activator.CreateInstance(contentManagerType,
                new object[] { services, root });
            return contentManager;
        }

        private static void Load(object menu, object manager, string fieldName,
            string asset)
        {
            FieldInfo field = GetField(menu, fieldName);
            if (field == null || field.GetValue(menu) != null) return;
            MethodInfo load = FindLoadMethod(manager.GetType())
                .MakeGenericMethod(textureType);
            field.SetValue(menu, load.Invoke(manager, new object[] { asset }));
        }

        private static bool GetBool(object target, string field)
        { return Convert.ToBoolean(GetField(target, field).GetValue(target)); }
        private static FieldInfo GetField(object target, string name)
        { return GetField(target.GetType(), name); }
        private static FieldInfo GetField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, Any |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static MethodInfo FindLoadMethod(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public))
                if (method.Name == "Load" && method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string))
                    return method;
            throw new MissingMethodException(type.FullName, "Load<T>(string)");
        }
    }

    public static class CutsceneEnterPrefix<T>
    { public static void Prefix(T __instance)
      { CutsceneMenuLifetimePatch.Enter(__instance); } }
    public static class CutsceneDrawPrefix<T>
    { public static bool Prefix() { return CutsceneMenuLifetimePatch.CanDraw(); } }
    public static class CutsceneLevelPrefix<T>
    { public static void Prefix(T __instance)
      { CutsceneMenuLifetimePatch.ResetLevelMode(__instance); } }
}
