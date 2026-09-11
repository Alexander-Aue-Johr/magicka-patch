using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class HybridInputPatch
    {
        private const BindingFlags Any = BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type managerType;
        private static FieldInfo padsField;
        private static FieldInfo keyboardField;
        private static PropertyInfo controllerPlayer;
        private static PropertyInfo playerController;
        private static PropertyInfo playerAvatar;
        private static PropertyInfo lastActiveController;
        private static MethodInfo gamePadGetState;
        private static object[] previousPads = new object[4];
        private static bool[] modifier = new bool[4];
        private static object previousKeyboard;
        private static object previousMouse;
        private static bool initialized;
        private static bool controllerHud;
        private static int hudPad = -1;
        private static int labelMode = -1;

        internal static readonly RuntimePatchDefinition InputDefinition =
            RuntimePatchDefinition.Prefix(
                "Hybrid keyboard and controller handoff",
                "org.magickacommunitypatch.hybrid-input",
                FindHandleInput,
                target => typeof(HybridInputPrefix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Prefix"));
        internal static readonly RuntimePatchDefinition HudDefinition =
            RuntimePatchDefinition.Postfix(
                "Modern controller HUD state",
                "org.magickacommunitypatch.hybrid-controller-hud",
                FindHudUpdate,
                CreateHudPostfix);
        internal static readonly RuntimePatchDefinition LabelsDefinition =
            RuntimePatchDefinition.Postfix(
                "Controller HUD label invalidation",
                "org.magickacommunitypatch.hybrid-controller-labels",
                FindHudUpdateControls,
                target => typeof(HybridLabelPostfix<>).MakeGenericType(
                    target.DeclaringType).GetMethod("Postfix"));

        private static MethodInfo FindHandleInput(Assembly assembly)
        {
            managerType = assembly.GetType(
                "Magicka.GameLogic.Controls.ControlManager", true);
            padsField = RequireField(managerType, "mXInputPads");
            keyboardField = RequireField(managerType, "mMenuController");
            Type controller = assembly.GetType(
                "Magicka.GameLogic.Controls.Controller", true);
            Type player = assembly.GetType("Magicka.GameLogic.Player", true);
            controllerPlayer = RequireProperty(controller, "Player");
            playerController = RequireProperty(player, "Controller");
            playerAvatar = RequireProperty(player, "Avatar");
            lastActiveController = managerType.GetProperty(
                "LastActiveController", Any);
            Type xinput = assembly.GetType(
                "Magicka.GameLogic.Controls.XInputController", true);
            FieldInfo index = RequireField(xinput, "mPlayerIndex");
            Type gamePad = index.FieldType.Assembly.GetType(
                "Microsoft.Xna.Framework.Input.GamePad", true);
            gamePadGetState = gamePad.GetMethod("GetState",
                BindingFlags.Static | BindingFlags.Public, null,
                new Type[] { index.FieldType }, null);
            return FindMethod(managerType, "HandleInput", 2);
        }

        private static MethodInfo FindHudUpdate(Assembly assembly)
        {
            return FindMethod(assembly.GetType(
                "Magicka.GameLogic.UI.KeyboardHUD", true), "Update", 2);
        }

        private static MethodInfo FindHudUpdateControls(Assembly assembly)
        {
            return FindMethod(assembly.GetType(
                "Magicka.GameLogic.UI.KeyboardHUD", true), "UpdateControls", 0);
        }

        private static MethodInfo CreateHudPostfix(MethodInfo target)
        {
            return typeof(HybridHudPostfix<,>).MakeGenericType(
                target.DeclaringType, target.GetParameters()[0].ParameterType)
                .GetMethod("Postfix");
        }

        public static void BeforeInput(object manager)
        {
            try
            {
                IList pads = (IList)padsField.GetValue(manager);
                object keyboard = keyboardField.GetValue(manager);
                Assembly assembly = manager.GetType().Assembly;
                object game = GetSingleton(assembly.GetType("Magicka.Game", true));
                object currentKeyboard = Get(game, "KeyboardState");
                object currentMouse = Get(game, "MouseState");
                bool focused = Bool(game, "Focused");
                for (int index = 0; index < pads.Count && index < 4; index++)
                {
                    object current = GetPadState(index, pads[index]);
                    modifier[index] = Down(current, "LeftShoulder");
                    if (initialized && focused && Connected(current) &&
                        PadActivity(current, previousPads[index]) &&
                        (TryTakeControl(manager, pads[index], assembly) ||
                         controllerPlayer.GetValue(pads[index], null) != null))
                    {
                        controllerHud = ModernControllerSchemePatch.IsEnabled();
                        hudPad = index;
                        lastActiveController.SetValue(null, pads[index], null);
                    }
                    previousPads[index] = current;
                }
                if (initialized && focused && KeyboardMouseActivity(
                    currentKeyboard, previousKeyboard, currentMouse, previousMouse) &&
                    (TryTakeControl(manager, keyboard, assembly) ||
                     controllerPlayer.GetValue(keyboard, null) != null))
                {
                    controllerHud = false;
                    hudPad = -1;
                    lastActiveController.SetValue(null, keyboard, null);
                }
                previousKeyboard = currentKeyboard;
                previousMouse = currentMouse;
                initialized = true;
                if (!ModernControllerSchemePatch.IsEnabled())
                {
                    controllerHud = false;
                    hudPad = -1;
                }
            }
            catch { }
        }

        public static void AfterHudUpdate(object hud, object channel)
        {
            try
            {
                FieldInfo textsField = RequireField(hud.GetType(), "mKeyTexts");
                Array texts = (Array)textsField.GetValue(hud);
                ApplyLabels(hud, texts);
                if (!IsControllerHudMode()) return;
                FieldInfo dataField = RequireField(hud.GetType(), "mRenderData");
                Array data = (Array)dataField.GetValue(hud);
                object renderData = data.GetValue(Convert.ToInt32(channel));
                Array icons = (Array)RequireField(renderData.GetType(), "Icons")
                    .GetValue(renderData);
                bool modified = ModifierActive();
                for (int index = 0; index < icons.Length; index++)
                {
                    if (IsModifierElement(index) == modified) continue;
                    object icon = icons.GetValue(index);
                    FieldInfo intensity = RequireField(icon.GetType(), "Intensity");
                    intensity.SetValue(icon, Convert.ToSingle(
                        intensity.GetValue(icon)) * 0.42f);
                    icons.SetValue(icon, index);
                }
            }
            catch { }
        }

        public static void InvalidateLabels() { labelMode = -1; }

        public static bool IsControllerHudMode()
        {
            return ModernControllerSchemePatch.IsEnabled() && controllerHud;
        }

        public static void AdjustIcon(int element, ref int position,
            ref float xOffset, ref float yOffset)
        {
            if (!IsControllerHudMode()) return;
            GetIconPlacement(element, out position, out xOffset, out yOffset);
        }

        public static void GetIconPlacement(int element, out int position,
            out float xOffset, out float yOffset)
        {
            int[] positions = new int[] { 3, 0, 2, 1, 0, 2, 1, 3 };
            float[] xs = new float[] { 35f, 0f, 20f, 15f, 0f, 20f, 15f, 35f };
            float[] ys = new float[] { 35f, 35f, 60f, 60f, -25f, -50f, -50f, -25f };
            if (element < 0 || element >= 8)
            { position = 0; xOffset = 0f; yOffset = 0f; return; }
            position = positions[element]; xOffset = xs[element]; yOffset = ys[element];
        }

        public static string HudLabel(int element, bool modified)
        {
            string[] normal = new string[] { "B", "LB", "LB", "A", "X", "Y", "LB", "LB" };
            string[] alternate = new string[] { "B", "X", "A", "A", "X", "Y", "Y", "B" };
            if (element < 0 || element >= 8) return null;
            return (modified ? alternate : normal)[element];
        }

        private static void ApplyLabels(object hud, Array texts)
        {
            if (!IsControllerHudMode())
            {
                if (labelMode > 0)
                    hud.GetType().GetMethod("UpdateControls", Any, null,
                        Type.EmptyTypes, null).Invoke(hud, null);
                labelMode = 0;
                return;
            }
            bool modified = ModifierActive();
            int mode = modified ? 2 : 1;
            if (labelMode == mode) return;
            for (int index = 0; index < 8; index++)
                texts.GetValue(index).GetType().GetMethod("SetText", Any,
                    null, new Type[] { typeof(string) }, null).Invoke(
                    texts.GetValue(index), new object[] { HudLabel(index, modified) });
            labelMode = mode;
        }

        private static bool TryTakeControl(object manager, object candidate,
            Assembly assembly)
        {
            object stateManager = GetSingleton(assembly.GetType(
                "Magicka.GameLogic.GameStates.GameStateManager", true));
            object state = Get(stateManager, "CurrentState");
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            if (!playState.IsInstanceOfType(state) || Bool(manager, "IsInputLimited"))
                return false;
            object game = GetSingleton(assembly.GetType("Magicka.Game", true));
            IEnumerable connected = (IEnumerable)Get(game, "ConnectedPlayers");
            object local = null;
            foreach (object player in connected)
            {
                if (Bool(player, "IsNetworkGamer")) continue;
                if (local != null) return false;
                local = player;
            }
            if (local == null || !Bool(local, "Playing") ||
                Object.ReferenceEquals(playerController.GetValue(local, null), candidate))
                return false;
            object occupant = controllerPlayer.GetValue(candidate, null);
            if (occupant != null && !Object.ReferenceEquals(occupant, local)) return false;
            object old = playerController.GetValue(local, null);
            if (old == null || Bool(old, "Inverted")) return false;
            MethodInfo locked = managerType.GetMethod("IsPlayerInputLocked", Any,
                null, new Type[] { Get(local, "ID").GetType() }, null);
            if (locked != null && (bool)locked.Invoke(manager,
                new object[] { Get(local, "ID") })) return false;
            Neutralize(playerAvatar.GetValue(local, null));
            Invoke(old, "Clear"); Invoke(candidate, "Clear");
            controllerPlayer.SetValue(old, null, null);
            playerController.SetValue(local, candidate, null);
            controllerPlayer.SetValue(candidate, local, null);
            lastActiveController.SetValue(null, candidate, null);
            Set(game, "IsMouseVisible", candidate.GetType().Name ==
                "KeyboardMouseController");
            return true;
        }

        private static void Neutralize(object avatar)
        {
            if (avatar == null || Bool(avatar, "Dead")) return;
            InvokeDirection(avatar, "UpdatePadDirection");
            InvokeDirection(avatar, "UpdateMouseDirection");
            Invoke(avatar, "MouseMoveStop"); Invoke(avatar, "AreaReleased");
            Invoke(avatar, "ForceReleased"); Invoke(avatar, "AttackRelease");
            Invoke(avatar, "SpecialRelease"); Set(avatar, "IsBlocking", false);
        }

        private static void InvokeDirection(object avatar, string name)
        {
            foreach (MethodInfo method in avatar.GetType().GetMethods(Any))
            {
                if (method.Name != name || method.GetParameters().Length != 2)
                    continue;
                Type vector = method.GetParameters()[0].ParameterType;
                method.Invoke(avatar, new object[] {
                    Activator.CreateInstance(vector), false });
                return;
            }
        }

        private static bool PadActivity(object current, object previous)
        {
            if (current == null || previous == null) return false;
            string[] names = new string[] { "A", "B", "X", "Y", "LeftShoulder",
                "RightShoulder", "LeftStick", "RightStick", "Start", "Back",
                "DPadUp", "DPadDown", "DPadLeft", "DPadRight" };
            for (int i = 0; i < names.Length; i++)
                if (Down(current, names[i]) && !Down(previous, names[i])) return true;
            return Crossed(current, previous, "Left") || Crossed(current, previous, "Right") ||
                TriggerCrossed(current, previous, "Left") ||
                TriggerCrossed(current, previous, "Right");
        }

        private static bool Crossed(object current, object previous, string side)
        {
            object a = Get(Get(current, "ThumbSticks"), side);
            object b = Get(Get(previous, "ThumbSticks"), side);
            float ax = Convert.ToSingle(Get(a, "X"));
            float ay = Convert.ToSingle(Get(a, "Y"));
            float bx = Convert.ToSingle(Get(b, "X"));
            float by = Convert.ToSingle(Get(b, "Y"));
            return ax * ax + ay * ay >= 0.1225f && bx * bx + by * by < 0.1225f;
        }

        private static bool TriggerCrossed(object current, object previous,
            string side)
        {
            return Convert.ToSingle(Get(Get(current, "Triggers"), side)) >= 0.5f &&
                Convert.ToSingle(Get(Get(previous, "Triggers"), side)) < 0.5f;
        }

        private static bool KeyboardMouseActivity(object keyboard, object oldKeyboard,
            object mouse, object oldMouse)
        {
            if (keyboard == null || mouse == null || oldKeyboard == null || oldMouse == null)
                return false;
            Array keys = (Array)keyboard.GetType().GetMethod("GetPressedKeys")
                .Invoke(keyboard, null);
            MethodInfo up = oldKeyboard.GetType().GetMethod("IsKeyUp");
            for (int i = 0; i < keys.Length; i++)
                if ((bool)up.Invoke(oldKeyboard, new object[] { keys.GetValue(i) }))
                    return true;
            if (Convert.ToInt32(Get(mouse, "ScrollWheelValue")) !=
                Convert.ToInt32(Get(oldMouse, "ScrollWheelValue"))) return true;
            int dx = Math.Abs(Convert.ToInt32(Get(mouse, "X")) -
                Convert.ToInt32(Get(oldMouse, "X")));
            int dy = Math.Abs(Convert.ToInt32(Get(mouse, "Y")) -
                Convert.ToInt32(Get(oldMouse, "Y")));
            if (dx >= 2 || dy >= 2) return true;
            string[] buttons = new string[] { "LeftButton", "MiddleButton",
                "RightButton", "XButton1", "XButton2" };
            for (int i = 0; i < buttons.Length; i++)
                if (Get(mouse, buttons[i]).ToString() == "Pressed" &&
                    Get(oldMouse, buttons[i]).ToString() == "Released") return true;
            return false;
        }

        private static object GetPadState(int index, object controller)
        {
            FieldInfo field = RequireField(controller.GetType(), "mPlayerIndex");
            object value = Enum.ToObject(field.FieldType, index);
            try { return gamePadGetState.Invoke(null, new object[] { value }); }
            catch { return null; }
        }
        private static bool Connected(object state)
        { return state != null && Bool(state, "IsConnected"); }
        private static bool Down(object state, string name)
        {
            if (state == null) return false;
            MethodInfo method = state.GetType().GetMethod("IsButtonDown");
            Type type = method.GetParameters()[0].ParameterType;
            return (bool)method.Invoke(state,
                new object[] { Enum.Parse(type, name) });
        }
        private static bool ModifierActive()
        { return hudPad >= 0 && hudPad < 4 && modifier[hudPad]; }
        private static bool IsModifierElement(int index)
        { return index == 1 || index == 2 || index == 6 || index == 7; }
        private static object GetSingleton(Type type)
        { return type.GetProperty("Instance", Any).GetValue(null, null); }
        private static object Get(object target, string name)
        {
            PropertyInfo property = FindProperty(target.GetType(), name);
            if (property != null) return property.GetValue(target, null);
            return RequireField(target.GetType(), name).GetValue(target);
        }
        private static void Set(object target, string name, object value)
        {
            PropertyInfo property = FindProperty(target.GetType(), name);
            if (property != null) { property.SetValue(target, value, null); return; }
            RequireField(target.GetType(), name).SetValue(target, value);
        }
        private static bool Bool(object target, string name)
        { try { return Convert.ToBoolean(Get(target, name)); } catch { return false; } }
        private static void Invoke(object target, string name)
        { target.GetType().GetMethod(name, Any, null, Type.EmptyTypes, null)
            .Invoke(target, null); }
        private static MethodInfo FindMethod(Type type, string name, int count)
        {
            foreach (MethodInfo method in type.GetMethods(Any))
                if (method.Name == name && method.GetParameters().Length == count)
                    return method;
            throw new MissingMethodException(type.FullName, name);
        }
        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, Any |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = FindProperty(type, name);
            if (property == null) throw new MissingMemberException(type.FullName, name);
            return property;
        }
        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, Any |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }

    public static class HybridInputPrefix<T>
    { public static void Prefix(T __instance) { HybridInputPatch.BeforeInput(__instance); } }
    public static class HybridHudPostfix<T, TChannel>
    { public static void Postfix(T __instance, TChannel iDataChannel)
      { HybridInputPatch.AfterHudUpdate(__instance, iDataChannel); } }
    public static class HybridLabelPostfix<T>
    { public static void Postfix() { HybridInputPatch.InvalidateLabels(); } }
}
