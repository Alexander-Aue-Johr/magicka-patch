using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class BorderlessPresentationPatch
    {
        public const bool MovesInitialApplyAfterHandlers = true;
        public const bool PreservesBackbuffer = true;

        private static FieldInfo graphicsManagerField;
        private static FieldInfo focusedField;
        private static FieldInfo formField;
        private static MethodInfo applyChangesMethod;
        private static MethodInfo addPreparingDeviceSettingsMethod;
        private static PropertyInfo graphicsDeviceInformationProperty;
        private static PropertyInfo presentationParametersProperty;
        private static PropertyInfo renderTargetUsageProperty;
        private static PropertyInfo actualFullscreenProperty;
        private static PropertyInfo refreshRateProperty;
        private static PropertyInfo logicalFullscreenProperty;
        private static PropertyInfo globalSettingsInstanceProperty;
        private static PropertyInfo savedFullscreenProperty;
        private static MethodInfo normalizeTopMost;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Initial graphics handler ordering",
                "org.magickacommunitypatch.borderless-initial-order",
                FindConstructor,
                typeof(BorderlessPresentationPatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition SettingsDefinition =
            RuntimePatchDefinition.Prefix(
                "Borderless presentation device settings",
                "org.magickacommunitypatch.borderless-device-settings",
                FindPreparingDeviceSettings,
                target => typeof(BorderlessPresentationPatch).GetMethod(
                    "PreparingDeviceSettingsPrefix"));

        internal static readonly RuntimePatchDefinition TopMostDefinition =
            RuntimePatchDefinition.Transpile(
                "Borderless fullscreen topmost normalization",
                "org.magickacommunitypatch.borderless-topmost",
                FindUpdate,
                typeof(BorderlessPresentationPatch).GetMethod(
                    "UpdateTranspiler"));

        public static bool UseWindowedPresentation(bool logicalFullscreen)
        {
            return logicalFullscreen;
        }

        public static bool ShouldClearTopMost(
            bool logicalFullscreen,
            bool actualFullscreen)
        {
            return logicalFullscreen && !actualFullscreen;
        }

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            ConstructorInfo constructor = gameType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (constructor == null)
                throw new MissingMethodException(gameType.FullName, ".ctor");
            graphicsManagerField = RequireField(
                gameType,
                "mGraphics",
                "Microsoft.Xna.Framework.GraphicsDeviceManager");
            applyChangesMethod = RequireMethod(
                graphicsManagerField.FieldType,
                "ApplyChanges",
                Type.EmptyTypes);
            addPreparingDeviceSettingsMethod = RequireEventAdder(
                graphicsManagerField.FieldType,
                "PreparingDeviceSettings");
            return constructor;
        }

        private static MethodInfo FindPreparingDeviceSettings(
            Assembly targetAssembly)
        {
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            Type argumentsType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.PreparingDeviceSettingsEventArgs");
            MethodInfo method = gameType.GetMethod(
                "mGraphics_PreparingDeviceSettings",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(object), argumentsType },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    gameType.FullName,
                    "mGraphics_PreparingDeviceSettings");
            graphicsManagerField = RequireField(
                gameType,
                "mGraphics",
                "Microsoft.Xna.Framework.GraphicsDeviceManager");
            logicalFullscreenProperty = RequireProperty(
                graphicsManagerField.FieldType,
                "IsFullScreen");
            graphicsDeviceInformationProperty = RequireProperty(
                argumentsType,
                "GraphicsDeviceInformation");
            presentationParametersProperty = RequireProperty(
                graphicsDeviceInformationProperty.PropertyType,
                "PresentationParameters");
            Type presentationType = presentationParametersProperty.PropertyType;
            renderTargetUsageProperty = RequireProperty(
                presentationType,
                "RenderTargetUsage");
            actualFullscreenProperty = RequireProperty(
                presentationType,
                "IsFullScreen");
            refreshRateProperty = RequireProperty(
                presentationType,
                "FullScreenRefreshRateInHz");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            Type gameTimeType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.GameTime");
            MethodInfo update = gameType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { gameTimeType },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(gameType.FullName, "Update");
            focusedField = RequireField(gameType, "mFocused", "System.Boolean");
            formField = RequireField(
                gameType,
                "mForm",
                "System.Windows.Forms.Form");
            PropertyInfo graphicsDeviceProperty = RequireProperty(
                gameType,
                "GraphicsDevice");
            presentationParametersProperty = RequireProperty(
                graphicsDeviceProperty.PropertyType,
                "PresentationParameters");
            actualFullscreenProperty = RequireProperty(
                presentationParametersProperty.PropertyType,
                "IsFullScreen");
            PropertyInfo topMostProperty = RequireProperty(
                formField.FieldType,
                "TopMost");
            Type globalSettingsType = targetAssembly.GetType(
                "Magicka.GlobalSettings",
                true);
            globalSettingsInstanceProperty = RequireProperty(
                globalSettingsType,
                "Instance");
            savedFullscreenProperty = RequireProperty(
                globalSettingsType,
                "Fullscreen");
            normalizeTopMost = BuildTopMostNormalizer(
                gameType,
                graphicsDeviceProperty.GetGetMethod(),
                topMostProperty.GetSetMethod());
            return update;
        }

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int apply = FindCall(result, applyChangesMethod);
            int subscribe = FindCall(result, addPreparingDeviceSettingsMethod);
            if (apply < 2 || subscribe < 0 || apply >= subscribe ||
                result[apply - 2].opcode != OpCodes.Ldarg_0 ||
                result[apply - 1].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[apply - 1].operand, graphicsManagerField))
                throw new InvalidOperationException(
                    "Initial GraphicsDeviceManager.ApplyChanges shape changed.");

            List<CodeInstruction> applySequence = result.GetRange(apply - 2, 3);
            result.RemoveRange(apply - 2, 3);
            subscribe = FindCall(result, addPreparingDeviceSettingsMethod);
            result.InsertRange(subscribe + 1, applySequence);
            return result;
        }

        public static void PreparingDeviceSettingsPrefix(
            object __instance,
            object __1)
        {
            object information = graphicsDeviceInformationProperty.GetValue(
                __1,
                null);
            object presentation = presentationParametersProperty.GetValue(
                information,
                null);
            object preserveContents = Enum.Parse(
                renderTargetUsageProperty.PropertyType,
                "PreserveContents");
            renderTargetUsageProperty.SetValue(
                presentation,
                preserveContents,
                null);

            object graphicsManager = graphicsManagerField.GetValue(__instance);
            bool logicalFullscreen = (bool)logicalFullscreenProperty.GetValue(
                graphicsManager,
                null);
            if (!UseWindowedPresentation(logicalFullscreen))
                return;
            actualFullscreenProperty.SetValue(presentation, false, null);
            refreshRateProperty.SetValue(presentation, 0, null);
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int focusedStores = 0;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, focusedField))
                    continue;
                result.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_0));
                result.Insert(
                    index + 2,
                    new CodeInstruction(OpCodes.Call, normalizeTopMost));
                focusedStores++;
            }
            if (focusedStores != 1)
                throw new InvalidOperationException(
                    "Expected one mFocused assignment, found " +
                    focusedStores + ".");
            return result;
        }

        private static MethodInfo BuildTopMostNormalizer(
            Type gameType,
            MethodInfo getGraphicsDevice,
            MethodInfo setTopMost)
        {
            if (getGraphicsDevice == null || setTopMost == null)
                throw new MissingMethodException(
                    "Borderless topmost property accessor");
            MethodInfo getGlobalSettings =
                globalSettingsInstanceProperty.GetGetMethod();
            MethodInfo getSavedFullscreen =
                savedFullscreenProperty.GetGetMethod();
            MethodInfo getPresentation =
                presentationParametersProperty.GetGetMethod();
            MethodInfo getActualFullscreen =
                actualFullscreenProperty.GetGetMethod();
            if (getGlobalSettings == null || getSavedFullscreen == null ||
                getPresentation == null || getActualFullscreen == null)
                throw new MissingMethodException(
                    "Borderless presentation property accessor");

            DynamicMethod method = new DynamicMethod(
                "Game_NormalizeBorderlessTopMost",
                typeof(void),
                new Type[] { gameType },
                typeof(BorderlessPresentationPatch).Module,
                true);
            ILGenerator il = method.GetILGenerator();
            Label done = il.DefineLabel();
            il.Emit(OpCodes.Call, getGlobalSettings);
            il.Emit(OpCodes.Callvirt, getSavedFullscreen);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getGraphicsDevice);
            il.Emit(OpCodes.Callvirt, getPresentation);
            il.Emit(OpCodes.Callvirt, getActualFullscreen);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, formField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, setTopMost);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static int FindCall(
            IList<CodeInstruction> instructions,
            MethodInfo method)
        {
            int found = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (!Object.Equals(instructions[index].operand, method))
                    continue;
                if (found >= 0)
                    throw new InvalidOperationException(
                        "Multiple calls matched " + method.Name + ".");
                found = index;
            }
            return found;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            string expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType.FullName != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static MethodInfo RequireEventAdder(Type type, string name)
        {
            EventInfo eventInfo = type.GetEvent(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo add = eventInfo == null ? null : eventInfo.GetAddMethod(true);
            if (add == null)
                throw new MissingMethodException(type.FullName, "add_" + name);
            return add;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
