using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InGameMenuPlayStatePatch
    {
        private const string MenuTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu";
        private const string MainTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMain";
        private const string MagicksTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMagicks";
        private const string ResolutionTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsResolution";
        private const string SurvivalTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuSurvivalStatistics";
        private const string TimedTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuTimedObjectiveStatistics";
        private const string VersusTypeName =
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuVersusStatistics";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;
        private static int expectedReadCount;
        private static readonly object installLock = new object();
        private static Assembly installedAssembly;
        private static readonly MenuReadTarget[] ReadTargets =
            CreateReadTargets();

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "InGameMenu play-state release",
                "org.magickacommunitypatch.in-game-menu-state-release",
                FindInitialize,
                typeof(InGameMenuPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition InstallDefinition =
            RuntimePatchDefinition.Postfix(
                "InGameMenu deferred current-state patch installation",
                "org.magickacommunitypatch.in-game-menu-state-install",
                FindInitialize,
                method => typeof(InGameMenuPlayStatePatch).GetMethod(
                    "InstallPostfix"));

        internal static readonly RuntimePatchDefinition[] ReadDefinitions =
            CreateReadDefinitions();

        private static RuntimePatchDefinition[] CreateReadDefinitions()
        {
            RuntimePatchDefinition[] definitions =
                new RuntimePatchDefinition[ReadTargets.Length];
            for (int index = 0; index < ReadTargets.Length; index++)
                definitions[index] = CreateReadDefinition(ReadTargets[index]);
            return definitions;
        }

        private static MenuReadTarget[] CreateReadTargets()
        {
            return new MenuReadTarget[] {
                new MenuReadTarget(
                    MenuTypeName,
                    "SendButtonPressTelemetry",
                    3,
                    new Version(1, 5),
                    null),
                new MenuReadTarget(MenuTypeName, "IUpdate", 1),
                new MenuReadTarget(MainTypeName, "UpdatePositions", 1),
                new MenuReadTarget(MainTypeName, "QuitCallback", 1),
                new MenuReadTarget(MainTypeName, "RestartCallback", 1),
                new MenuReadTarget(MainTypeName, "TutorialSkipCallback", 1),
                new MenuReadTarget(MainTypeName, "DisconnetCallback", 2),
                new MenuReadTarget(MainTypeName, "SkipCreditsCallback", 2),
                new MenuReadTarget(
                    MainTypeName,
                    "IControllerSelect",
                    2,
                    1),
                new MenuReadTarget(MainTypeName, "IUpdate", 1),
                new MenuReadTarget(
                    MainTypeName,
                    "IDraw",
                    2,
                    null,
                    new Version(1, 9)),
                new MenuReadTarget(MagicksTypeName, "OnEnter", 2),
                new MenuReadTarget(ResolutionTypeName, "OnExit", 1),
                new MenuReadTarget(SurvivalTypeName, "LanguageChanged", 1),
                new MenuReadTarget(SurvivalTypeName, "IControllerSelect", 4),
                new MenuReadTarget(SurvivalTypeName, "OnEnter", 1),
                new MenuReadTarget(SurvivalTypeName, "AddHighScore", 2),
                new MenuReadTarget(TimedTypeName, "IControllerSelect", 4),
                new MenuReadTarget(TimedTypeName, "OnEnter", 5),
                new MenuReadTarget(VersusTypeName, "IControllerSelect", 4),
                new MenuReadTarget(VersusTypeName, "OnEnter", 4)
            };
        }

        private static RuntimePatchDefinition CreateReadDefinition(
            MenuReadTarget target)
        {
            return RuntimePatchDefinition.Transpile(
                "InGameMenu current state " + target.ShortName,
                "org.magickacommunitypatch.in-game-menu-current-state-" +
                    target.OwnerSuffix,
                assembly => FindReadTarget(assembly, target),
                typeof(InGameMenuPlayStatePatch).GetMethod(
                    "ReadTranspiler"));
        }

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type menu;
            Type playState;
            Configure(assembly, out menu, out playState);
            return RequireMethod(
                menu,
                "Initialize",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { playState },
                typeof(void));
        }

        private static MethodInfo FindReadTarget(
            Assembly assembly,
            MenuReadTarget target)
        {
            Type ignoredMenu;
            Type ignoredPlayState;
            Configure(assembly, out ignoredMenu, out ignoredPlayState);
            Type type = assembly.GetType(target.TypeName, true);
            MethodInfo match = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name != target.MethodName)
                    continue;
                if (match != null)
                    throw new AmbiguousMatchException(
                        target.TypeName + "." + target.MethodName);
                match = methods[index];
            }
            if (match == null)
                throw new MissingMethodException(
                    target.TypeName,
                    target.MethodName);
            expectedReadCount = target.ExpectedReadsFor(assembly);
            return match;
        }

        internal static void ValidateDeferredDefinitions(Assembly assembly)
        {
            for (int index = 0; index < ReadDefinitions.Length; index++)
            {
                RuntimePatchDefinition definition = ReadDefinitions[index];
                if (!ReadTargets[index].IsApplicableTo(assembly))
                {
                    RuntimePatchAudit.WriteNotApplicable(
                        definition,
                        "The target method is not present in this Magicka version.");
                    continue;
                }
                MethodBase target = definition.FindTarget(assembly);
                RuntimePatchAudit.WriteDeferred(
                    assembly,
                    target,
                    definition,
                    "InGameMenu.Initialize");
            }
        }

        public static void InstallPostfix()
        {
            Assembly assembly = RuntimeMember.FindLoadedType(MenuTypeName).Assembly;
            if (Object.ReferenceEquals(installedAssembly, assembly))
                return;
            lock (installLock)
            {
                if (Object.ReferenceEquals(installedAssembly, assembly))
                    return;
                for (int index = 0; index < ReadDefinitions.Length; index++)
                {
                    if (!ReadTargets[index].IsApplicableTo(assembly))
                        continue;
                    RuntimePatchSession.Apply(assembly, ReadDefinitions[index]);
                }
                installedAssembly = assembly;
            }
        }

        private static void Configure(
            Assembly assembly,
            out Type menu,
            out Type playState)
        {
            menu = assembly.GetType(MenuTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = RequireField(
                menu,
                "sPlayState",
                playState);
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod(true);
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stsfld ||
                    !Object.Equals(
                        result[index].operand as FieldInfo,
                        legacyPlayStateField))
                    continue;
                writes++;
                if (result[index - 1].opcode == OpCodes.Ldarg_0)
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one InGameMenu play-state assignment, found " +
                    writes + ".");
            result[assignment - 1].opcode = OpCodes.Nop;
            result[assignment - 1].operand = null;
            result[assignment].opcode = OpCodes.Nop;
            result[assignment].operand = null;
            return result;
        }

        public static IEnumerable<CodeInstruction> ReadTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldsfld ||
                    !Object.Equals(
                        result[index].operand as FieldInfo,
                        legacyPlayStateField))
                    continue;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                replacements++;
            }
            if (replacements != expectedReadCount)
                throw new InvalidOperationException(
                    "Expected " + expectedReadCount +
                    " InGameMenu play-state reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureTranspiler()
        {
            Type menu = RuntimeMember.FindLoadedType(MenuTypeName);
            Type ignoredMenu;
            Type ignoredPlayState;
            Configure(
                menu.Assembly,
                out ignoredMenu,
                out ignoredPlayState);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            BindingFlags flags,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                flags,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private sealed class MenuReadTarget
        {
            internal readonly string TypeName;
            internal readonly string MethodName;
            internal readonly int ExpectedReads;
            private readonly Version minimumVersion;
            private readonly Version maximumVersion;
            private readonly int legacyExpectedReads;

            internal MenuReadTarget(
                string typeName,
                string methodName,
                int expectedReads)
                : this(typeName, methodName, expectedReads, -1, null, null)
            {
            }

            internal MenuReadTarget(
                string typeName,
                string methodName,
                int expectedReads,
                int legacyExpectedReads)
                : this(
                    typeName,
                    methodName,
                    expectedReads,
                    legacyExpectedReads,
                    null,
                    null)
            {
            }

            internal MenuReadTarget(
                string typeName,
                string methodName,
                int expectedReads,
                Version minimumVersion,
                Version maximumVersion)
                : this(
                    typeName,
                    methodName,
                    expectedReads,
                    -1,
                    minimumVersion,
                    maximumVersion)
            {
            }

            private MenuReadTarget(
                string typeName,
                string methodName,
                int expectedReads,
                int legacyExpectedReads,
                Version minimumVersion,
                Version maximumVersion)
            {
                TypeName = typeName;
                MethodName = methodName;
                ExpectedReads = expectedReads;
                this.minimumVersion = minimumVersion;
                this.maximumVersion = maximumVersion;
                this.legacyExpectedReads = legacyExpectedReads;
            }

            internal bool IsApplicableTo(Assembly assembly)
            {
                Version version = assembly.GetName().Version;
                return (minimumVersion == null ||
                        version.CompareTo(minimumVersion) >= 0) &&
                    (maximumVersion == null ||
                        version.CompareTo(maximumVersion) <= 0);
            }

            internal int ExpectedReadsFor(Assembly assembly)
            {
                if (legacyExpectedReads >= 0 &&
                    assembly.GetName().Version.Major == 1 &&
                    assembly.GetName().Version.Minor < 10)
                    return legacyExpectedReads;
                return ExpectedReads;
            }

            internal string ShortName
            {
                get
                {
                    return TypeName.Substring(TypeName.LastIndexOf('.') + 1) +
                        "." + MethodName;
                }
            }

            internal string OwnerSuffix
            {
                get
                {
                    return ShortName.Replace('.', '-').ToLowerInvariant();
                }
            }
        }
    }
}
