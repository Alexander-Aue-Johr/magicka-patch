using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimeTelemetryContextPatch
    {
        private static PropertyInfo levelName;
        private static PropertyInfo sceneName;
        private static FieldInfo levelCurrentScene;
        private static FieldInfo stateLevel;
        private static FieldInfo playStateInitialized;
        private static FieldInfo currentLanguage;
        private static FieldInfo resolutionWidth;
        private static FieldInfo resolutionHeight;

        internal static readonly RuntimePatchDefinition LevelDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "Telemetry play-state navigation context",
                "org.magickacommunitypatch.telemetry-play-state-context",
                FindLevelConstructor,
                typeof(RuntimeTelemetryContextPatch).GetMethod(
                    "LevelPostfix"));

        internal static readonly RuntimePatchDefinition SceneDefinition =
            RuntimePatchDefinition.Transpile(
                "Telemetry scene navigation context",
                "org.magickacommunitypatch.telemetry-scene-context",
                FindChangeScene,
                typeof(RuntimeTelemetryContextPatch).GetMethod(
                    "ChangeSceneTranspiler"));

        internal static readonly RuntimePatchDefinition RestoredSceneDefinition =
            RuntimePatchDefinition.Transpile(
                "Telemetry restored-scene navigation context",
                "org.magickacommunitypatch.telemetry-restored-scene-context",
                FindApplyState,
                typeof(RuntimeTelemetryContextPatch).GetMethod(
                    "ApplyStateTranspiler"));

        internal static readonly RuntimePatchDefinition MenuDefinition =
            RuntimePatchDefinition.Prefix(
                "Telemetry menu navigation context",
                "org.magickacommunitypatch.telemetry-menu-context",
                FindPlayStateExit,
                CreateMenuPrefix);

        internal static readonly RuntimePatchDefinition ResolutionDefinition =
            RuntimePatchDefinition.Postfix(
                "Telemetry cached resolution context",
                "org.magickacommunitypatch.telemetry-resolution-context",
                FindResolutionSetter,
                CreateResolutionPostfix);

        internal static readonly RuntimePatchDefinition LanguageDefinition =
            RuntimePatchDefinition.Transpile(
                "Telemetry cached language context",
                "org.magickacommunitypatch.telemetry-language-context",
                FindSetLanguage,
                typeof(RuntimeTelemetryContextPatch).GetMethod(
                    "LanguageTranspiler"));

        private static ConstructorInfo FindLevelConstructor(Assembly assembly)
        {
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            ConstructorInfo[] constructors = level.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            ConstructorInfo match = null;
            for (int index = 0; index < constructors.Length; index++)
            {
                ParameterInfo[] parameters =
                    constructors[index].GetParameters();
                if (parameters.Length != 5 ||
                    parameters[0].ParameterType != typeof(string))
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple Level content constructors matched.");
                match = constructors[index];
            }
            if (match == null)
                throw new MissingMethodException(level.FullName, ".ctor");
            levelName = RequireProperty(level, "Name", typeof(string));
            return match;
        }

        public static void LevelPostfix(object __instance, string iFileName)
        {
            RuntimeTelemetryContext.RecordPlayState(
                iFileName,
                levelName.GetValue(__instance, null) as string);
        }

        private static MethodInfo FindChangeScene(Assembly assembly)
        {
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            sceneName = RequireProperty(scene, "Name", typeof(string));
            levelCurrentScene = RequireField(level, "mCurrentScene", scene);
            return RequireMethod(level, "ChangeScene", Type.EmptyTypes);
        }

        private static MethodInfo FindApplyState(Assembly assembly)
        {
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            Type state = level.GetNestedType(
                "State",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (state == null)
                throw new TypeLoadException(level.FullName + "+State");
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            sceneName = RequireProperty(scene, "Name", typeof(string));
            levelCurrentScene = RequireField(level, "mCurrentScene", scene);
            stateLevel = RequireField(state, "mLevel", level);
            Type list = typeof(List<>).MakeGenericType(typeof(int));
            Type action = typeof(Action<>).MakeGenericType(typeof(float));
            return RequireMethod(
                state,
                "ApplyState",
                new Type[] { list, action });
        }

        public static IEnumerable<CodeInstruction> ChangeSceneTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return AddSceneRecord(instructions, false);
        }

        public static IEnumerable<CodeInstruction> ApplyStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return AddSceneRecord(instructions, true);
        }

        private static IEnumerable<CodeInstruction> AddSceneRecord(
            IEnumerable<CodeInstruction> instructions,
            bool loadLevelFromState)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, levelCurrentScene))
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple current-scene assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "Current-scene assignment was not found.");
            List<CodeInstruction> addition = new List<CodeInstruction>();
            addition.Add(new CodeInstruction(OpCodes.Ldarg_0));
            if (loadLevelFromState)
                addition.Add(new CodeInstruction(OpCodes.Ldfld, stateLevel));
            addition.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(RuntimeTelemetryContextPatch).GetMethod(
                    "RecordCurrentScene")));
            result.InsertRange(assignment + 1, addition);
            return result;
        }

        public static void RecordCurrentScene(object level)
        {
            object scene = levelCurrentScene.GetValue(level);
            if (scene != null)
                RuntimeTelemetryContext.RecordScene(
                    sceneName.GetValue(scene, null) as string);
        }

        private static MethodInfo FindPlayStateExit(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateInitialized = RequireField(
                playState,
                "mInitialized",
                typeof(bool));
            return RequireMethod(playState, "OnExit", Type.EmptyTypes);
        }

        private static MethodInfo CreateMenuPrefix(MethodInfo target)
        {
            return typeof(RuntimeTelemetryContextPatch).GetMethod(
                "MenuPrefix");
        }

        public static void MenuPrefix(object __instance)
        {
            if ((bool)playStateInitialized.GetValue(__instance))
                RuntimeTelemetryContext.RecordMenu();
        }

        private static MethodInfo FindResolutionSetter(Assembly assembly)
        {
            Type settings = assembly.GetType("Magicka.GlobalSettings", true);
            PropertyInfo resolution = settings.GetProperty(
                "Resolution",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (resolution == null || resolution.GetSetMethod(true) == null)
                throw new MissingMemberException(
                    settings.FullName,
                    "Resolution");
            Type resolutionType = resolution.PropertyType;
            resolutionWidth = RequireField(
                resolutionType,
                "Width",
                typeof(int));
            resolutionHeight = RequireField(
                resolutionType,
                "Height",
                typeof(int));
            return resolution.GetSetMethod(true);
        }

        private static MethodInfo CreateResolutionPostfix(MethodInfo target)
        {
            return typeof(RuntimeTelemetryContextPatch).GetMethod(
                "ResolutionPostfix");
        }

        public static void ResolutionPostfix(object __0)
        {
            if (__0 == null)
                return;
            RuntimeTelemetryContext.RecordResolution(
                (int)resolutionWidth.GetValue(__0),
                (int)resolutionHeight.GetValue(__0));
        }

        private static MethodInfo FindSetLanguage(Assembly assembly)
        {
            Type manager = assembly.GetType(
                "Magicka.Localization.LanguageManager",
                true);
            currentLanguage = manager.GetField(
                "mCurrentLanguage",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (currentLanguage == null || !currentLanguage.FieldType.IsEnum)
                throw new MissingFieldException(
                    manager.FullName,
                    "mCurrentLanguage");
            return RequireMethod(
                manager,
                "SetLanguage",
                new Type[] { currentLanguage.FieldType });
        }

        public static IEnumerable<CodeInstruction> LanguageTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, currentLanguage))
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple current-language assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "Current-language assignment was not found.");
            result.Insert(
                assignment + 1,
                new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(
                assignment + 2,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(RuntimeTelemetryContextPatch).GetMethod(
                        "RecordLanguageFromManager")));
            return result;
        }

        public static void RecordLanguageFromManager(object instance)
        {
            object value = currentLanguage.GetValue(instance);
            RuntimeTelemetryContext.RecordLanguage(
                value == null ? String.Empty : value.ToString());
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(
            Type type,
            string name,
            Type propertyType)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property == null || property.PropertyType != propertyType ||
                property.GetGetMethod(true) == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
