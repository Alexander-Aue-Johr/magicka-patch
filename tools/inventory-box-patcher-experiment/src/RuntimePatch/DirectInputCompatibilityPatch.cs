using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DirectInputCompatibilityPatch
    {
        private const string WarningTitle = "Controller support unavailable";
        private const string WarningMessage =
            "Managed DirectX 1.1 is missing. Controllers cannot be used until it is installed.\n\n" +
            "Start Magicka from the Community Patch installer's Start Game button, " +
            "or run this file as administrator from the Magicka folder:\n" +
            "Dependencies\\directx_feb2010\\DXSETUP.exe\n\n" +
            "Restart Magicka afterwards.";

        private static int sDirectInputUnavailable;
        private static int sDirectInputWarningPending;
        private static MethodInfo updateControllersMethod;
        private static MethodInfo findNewGamePadsMethod;
        private static MethodInfo baseMenuUpdateMethod;
        private static FieldInfo findTimerField;
        private static DynamicMethod safeUpdateControllers;
        private static DynamicMethod safeFindNewGamePads;
        private static MethodInfo paradoxAccountInstance;
        private static MethodInfo pendingErrorCode;
        private static MethodInfo widgetPopupInstance;
        private static MethodInfo popupActive;
        private static Action<string, string> showErrorPopup;

        internal static readonly RuntimePatchDefinition OptionsConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Controller options constructor DirectInput guard",
                "org.magickacommunitypatch.direct-input-options-constructor",
                FindOptionsConstructor,
                typeof(DirectInputCompatibilityPatch).GetMethod(
                    "OptionsCallTranspiler"));

        internal static readonly RuntimePatchDefinition OptionsOnEnterDefinition =
            RuntimePatchDefinition.Transpile(
                "Controller options entry DirectInput guard",
                "org.magickacommunitypatch.direct-input-options-entry",
                FindOptionsOnEnter,
                typeof(DirectInputCompatibilityPatch).GetMethod(
                    "OptionsCallTranspiler"));

        internal static readonly RuntimePatchDefinition ControllerScanDefinition =
            RuntimePatchDefinition.Transpile(
                "Menu controller scan DirectInput guard",
                "org.magickacommunitypatch.direct-input-controller-scan",
                FindControllerScan,
                typeof(DirectInputCompatibilityPatch).GetMethod(
                    "ControllerScanTranspiler"));

        internal static readonly RuntimePatchDefinition WarningDefinition =
            RuntimePatchDefinition.Transpile(
                "Deferred DirectInput warning",
                "org.magickacommunitypatch.direct-input-warning",
                FindMenuUpdate,
                typeof(DirectInputCompatibilityPatch).GetMethod(
                    "MenuUpdateTranspiler"));

        internal static bool HasWarningSupport(Assembly targetAssembly)
        {
            return targetAssembly.GetType("Magicka.WebTools.ParadoxAccount", false) != null &&
                targetAssembly.GetType(
                    "Magicka.GameLogic.UI.UISystem.Popup.WidgetPopupSystem",
                    false) != null &&
                targetAssembly.GetType(
                    "Magicka.WebTools.Paradox.ParadoxPopupUtils",
                    false) != null;
        }

        private static ConstructorInfo FindOptionsConstructor(Assembly targetAssembly)
        {
            Type optionsType = FindOptionsType(targetAssembly);
            ConstructorInfo constructor = optionsType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (constructor == null)
                throw new MissingMethodException(optionsType.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindOptionsOnEnter(Assembly targetAssembly)
        {
            Type optionsType = FindOptionsType(targetAssembly);
            MethodInfo method = optionsType.GetMethod(
                "OnEnter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(optionsType.FullName, "OnEnter");
            return method;
        }

        private static Type FindOptionsType(Assembly targetAssembly)
        {
            Type optionsType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.Options.SubMenuOptionsControls",
                true);
            MethodInfo method = optionsType.GetMethod(
                "UpdateControllers",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(optionsType.FullName, "UpdateControllers");
            updateControllersMethod = method;
            safeUpdateControllers = BuildSafeUpdateControllers(optionsType, method);
            return optionsType;
        }

        private static MethodInfo FindControllerScan(Assembly targetAssembly)
        {
            Type menuStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.MenuState",
                true);
            Type managerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.ControlManager",
                true);
            findNewGamePadsMethod = managerType.GetMethod(
                "FindNewGamePads",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            findTimerField = menuStateType.GetField(
                "mFindGamepadsTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (findNewGamePadsMethod == null ||
                findNewGamePadsMethod.ReturnType != typeof(void))
                throw new MissingMethodException(managerType.FullName, "FindNewGamePads");
            if (findTimerField == null || findTimerField.FieldType != typeof(float))
                throw new MissingFieldException(menuStateType.FullName, "mFindGamepadsTimer");
            if (updateControllersMethod == null)
                FindOptionsType(targetAssembly);
            safeFindNewGamePads = BuildSafeFindNewGamePads(
                managerType,
                findNewGamePadsMethod);

            MethodInfo method = menuStateType.GetMethod(
                "FindNewControllers",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(menuStateType.FullName, "FindNewControllers");
            return method;
        }

        private static MethodInfo FindMenuUpdate(Assembly targetAssembly)
        {
            Type menuStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.MenuState",
                true);
            MethodInfo method = FindUpdateMethod(menuStateType);
            baseMenuUpdateMethod = FindUpdateMethod(menuStateType.BaseType);
            ConfigureWarning(targetAssembly);
            return method;
        }

        private static MethodInfo FindUpdateMethod(Type type)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "Update" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType.FullName != "PolygonHead.DataChannel" ||
                    parameters[1].ParameterType != typeof(float))
                    continue;
                if (result != null)
                    throw new AmbiguousMatchException(type.FullName + ".Update");
                result = methods[index];
            }
            if (result == null)
                throw new MissingMethodException(type.FullName, "Update");
            return result;
        }

        private static void ConfigureWarning(Assembly targetAssembly)
        {
            Type paradoxType = targetAssembly.GetType(
                "Magicka.WebTools.ParadoxAccount",
                true);
            Type widgetType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.UISystem.Popup.WidgetPopupSystem",
                true);
            Type popupUtilsType = targetAssembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxPopupUtils",
                true);
            paradoxAccountInstance = FindSingletonGetter(paradoxType);
            widgetPopupInstance = FindSingletonGetter(widgetType);
            pendingErrorCode = RequireGetter(paradoxType, "PendingErrorCode");
            popupActive = RequireGetter(widgetType, "Active");
            MethodInfo show = popupUtilsType.GetMethod(
                "ShowErrorPopup",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string), typeof(string) },
                null);
            if (show == null || show.ReturnType != typeof(void))
                throw new MissingMethodException(popupUtilsType.FullName, "ShowErrorPopup");
            showErrorPopup = (Action<string, string>)Delegate.CreateDelegate(
                typeof(Action<string, string>),
                show);
        }

        private static MethodInfo FindSingletonGetter(Type concreteType)
        {
            for (Type current = concreteType; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(
                    "Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == concreteType)
                    return property.GetGetMethod();
            }
            throw new MissingMemberException(concreteType.FullName, "Instance");
        }

        private static MethodInfo RequireGetter(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo getter = property == null ? null : property.GetGetMethod();
            if (getter == null)
                throw new MissingMemberException(type.FullName, name);
            return getter;
        }

        public static IEnumerable<CodeInstruction> OptionsCallTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            ReplaceSingleCall(
                result,
                updateControllersMethod,
                safeUpdateControllers,
                "UpdateControllers");
            return result;
        }

        public static IEnumerable<CodeInstruction> ControllerScanTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int findCall = FindSingleCall(
                result,
                findNewGamePadsMethod,
                "FindNewGamePads");
            int updateCall = FindSingleCall(
                result,
                updateControllersMethod,
                "UpdateControllers");
            int timerStart = FindTimerAssignmentStart(result);
            if (findCall >= updateCall || updateCall >= timerStart)
                throw new InvalidOperationException(
                    "MenuState.FindNewControllers has an unexpected call order.");

            result[findCall].opcode = OpCodes.Call;
            result[findCall].operand = safeFindNewGamePads;
            result[updateCall].opcode = OpCodes.Call;
            result[updateCall].operand = safeUpdateControllers;

            Label resetTimer = generator.DefineLabel();
            result[timerStart].labels.Add(resetTimer);
            result.Insert(
                findCall + 1,
                new CodeInstruction(OpCodes.Brfalse, resetTimer));
            return result;
        }

        public static IEnumerable<CodeInstruction> MenuUpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int baseUpdate = FindSingleCall(
                result,
                baseMenuUpdateMethod,
                "base Update");
            result.Insert(
                baseUpdate + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(DirectInputCompatibilityPatch).GetMethod(
                        "ShowPendingDirectInputWarning")));
            return result;
        }

        public static void ShowPendingDirectInputWarning()
        {
            if (Interlocked.CompareExchange(
                    ref sDirectInputWarningPending,
                    0,
                    0) == 0 ||
                paradoxAccountInstance == null ||
                pendingErrorCode == null ||
                widgetPopupInstance == null ||
                popupActive == null ||
                showErrorPopup == null)
                return;

            object account = paradoxAccountInstance.Invoke(null, null);
            if (Convert.ToInt32(pendingErrorCode.Invoke(account, null)) != 0)
                return;
            object popupSystem = widgetPopupInstance.Invoke(null, null);
            if (Convert.ToBoolean(popupActive.Invoke(popupSystem, null)))
                return;
            if (Interlocked.Exchange(ref sDirectInputWarningPending, 0) == 0)
                return;
            showErrorPopup(WarningTitle, WarningMessage);
        }

        public static bool IsDirectInputUnavailable()
        {
            return Interlocked.CompareExchange(
                ref sDirectInputUnavailable,
                0,
                0) != 0;
        }

        public static void MarkDirectInputUnavailable()
        {
            if (Interlocked.Exchange(ref sDirectInputUnavailable, 1) == 0)
                Interlocked.Exchange(ref sDirectInputWarningPending, 1);
        }

        private static int FindTimerAssignmentStart(List<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 2; index < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Stfld ||
                    instructions[index].operand as FieldInfo != findTimerField ||
                    instructions[index - 2].opcode != OpCodes.Ldarg_0 ||
                    instructions[index - 1].opcode != OpCodes.Ldc_R4 ||
                    Convert.ToSingle(instructions[index - 1].operand) != 5f)
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple controller scan timer assignments matched.");
                match = index - 2;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Controller scan timer assignment was not found.");
            return match;
        }

        private static void ReplaceSingleCall(
            List<CodeInstruction> instructions,
            MethodInfo original,
            MethodInfo replacement,
            string label)
        {
            int index = FindSingleCall(instructions, original, label);
            instructions[index].opcode = OpCodes.Call;
            instructions[index].operand = replacement;
        }

        private static int FindSingleCall(
            List<CodeInstruction> instructions,
            MethodInfo method,
            string label)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if ((instructions[index].opcode != OpCodes.Call &&
                        instructions[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(instructions[index].operand, method))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple " + label + " calls matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(label + " call was not found.");
            return match;
        }

        private static DynamicMethod BuildSafeUpdateControllers(
            Type optionsType,
            MethodInfo updateMethod)
        {
            DynamicMethod wrapper = new DynamicMethod(
                "DirectInput_UpdateControllerOptions",
                typeof(void),
                new Type[] { optionsType },
                typeof(DirectInputCompatibilityPatch).Module,
                true);
            ILGenerator il = wrapper.GetILGenerator();
            Label invoke = il.DefineLabel();
            Label done = il.DefineLabel();
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "IsDirectInputUnavailable"));
            il.Emit(OpCodes.Brfalse_S, invoke);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(invoke);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, updateMethod);
            il.Emit(OpCodes.Leave_S, done);
            il.BeginCatchBlock(typeof(FileNotFoundException));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "MarkDirectInputUnavailable"));
            il.Emit(OpCodes.Leave_S, done);
            il.BeginCatchBlock(typeof(FileLoadException));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "MarkDirectInputUnavailable"));
            il.Emit(OpCodes.Leave_S, done);
            il.EndExceptionBlock();
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        private static DynamicMethod BuildSafeFindNewGamePads(
            Type managerType,
            MethodInfo findMethod)
        {
            DynamicMethod wrapper = new DynamicMethod(
                "DirectInput_FindNewGamePads",
                typeof(bool),
                new Type[] { managerType },
                typeof(DirectInputCompatibilityPatch).Module,
                true);
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder result = il.DeclareLocal(typeof(bool));
            Label invoke = il.DefineLabel();
            Label done = il.DefineLabel();
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "IsDirectInputUnavailable"));
            il.Emit(OpCodes.Brfalse_S, invoke);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(invoke);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, findMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Leave_S, done);
            il.BeginCatchBlock(typeof(FileNotFoundException));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "MarkDirectInputUnavailable"));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Leave_S, done);
            il.BeginCatchBlock(typeof(FileLoadException));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, typeof(DirectInputCompatibilityPatch).GetMethod(
                "MarkDirectInputUnavailable"));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Leave_S, done);
            il.EndExceptionBlock();
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
