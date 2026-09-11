using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ModernControllerSchemePatch
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        private static Type xinputType;
        private static Type functionType;
        private static Type stateType;
        private static Type buttonsType;
        private static FieldInfo avatarField;
        private static FieldInfo oldStateField;
        private static FieldInfo playerIndexField;
        private static FieldInfo invertedField;
        private static PropertyInfo controllerPlayerProperty;
        private static PropertyInfo gameStateManagerInstance;
        private static PropertyInfo currentStateProperty;
        private static Type playStateType;
        private static MethodInfo gamePadGetState;
        private static MethodInfo isButtonDown;
        private static PropertyInfo thumbSticksProperty;
        private static PropertyInfo triggersProperty;
        private static PropertyInfo leftStickProperty;
        private static PropertyInfo rightStickProperty;
        private static PropertyInfo leftTriggerProperty;
        private static PropertyInfo rightTriggerProperty;
        private static PropertyInfo vectorXProperty;
        private static PropertyInfo vectorYProperty;
        private static readonly bool[] ModifierUsed = new bool[4];
        private static readonly bool[] TriggerMagick = new bool[4];
        private static readonly bool[] AimActive = new bool[4];
        private static readonly float[] LastAimX = new float[4];
        private static readonly float[] LastAimY = new float[4];
        private static bool enabled = !RuntimePatchSettings.Load()
            .UseMagicka1ControllerScheme;

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Prefix(
                "Magicka 2 controller gameplay",
                "org.magickacommunitypatch.modern-controller-update",
                FindUpdate,
                CreateUpdatePrefix);
        internal static readonly RuntimePatchDefinition FloatDefinition =
            RuntimePatchDefinition.Prefix(
                "Magicka 2 controller analog bindings",
                "org.magickacommunitypatch.modern-controller-float",
                assembly => FindBoundMethod(assembly, "GetBoundValueF", 2),
                CreateFloatPrefix);
        internal static readonly RuntimePatchDefinition BoolDefinition =
            RuntimePatchDefinition.Prefix(
                "Magicka 2 controller held bindings",
                "org.magickacommunitypatch.modern-controller-bool",
                assembly => FindBoundMethod(assembly, "GetBoundValueB", 2),
                CreateBoolPrefix);
        internal static readonly RuntimePatchDefinition PressedDefinition =
            RuntimePatchDefinition.Prefix(
                "Magicka 2 controller pressed bindings",
                "org.magickacommunitypatch.modern-controller-pressed",
                assembly => FindBoundMethod(assembly, "GetBoundValuePressed", 3),
                CreatePressedPrefix);
        internal static readonly RuntimePatchDefinition ReleasedDefinition =
            RuntimePatchDefinition.Prefix(
                "Magicka 2 controller released bindings",
                "org.magickacommunitypatch.modern-controller-released",
                assembly => FindBoundMethod(assembly, "GetBoundValueReleased", 3),
                CreateReleasedPrefix);

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Initialize(assembly);
            MethodInfo method = null;
            MethodInfo[] methods = xinputType.GetMethods(AnyInstance |
                BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Update" &&
                    methods[index].GetParameters().Length == 2)
                    method = methods[index];
            if (method == null)
                throw new MissingMethodException(xinputType.FullName, "Update");
            return method;
        }

        private static MethodInfo FindBoundMethod(Assembly assembly,
            string name, int parameterCount)
        {
            Initialize(assembly);
            MethodInfo[] methods = xinputType.GetMethods(AnyInstance |
                BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == name &&
                    methods[index].GetParameters().Length == parameterCount)
                    return methods[index];
            throw new MissingMethodException(xinputType.FullName, name);
        }

        private static void Initialize(Assembly assembly)
        {
            xinputType = assembly.GetType(
                "Magicka.GameLogic.Controls.XInputController", true);
            functionType = assembly.GetType(
                "Magicka.GameLogic.Controls.ControllerFunction", true);
            avatarField = RequireField(xinputType, "mAvatar");
            oldStateField = RequireField(xinputType, "mOldState");
            playerIndexField = RequireField(xinputType, "mPlayerIndex");
            invertedField = RequireField(xinputType, "mInverted");
            controllerPlayerProperty = FindProperty(xinputType, "Player");
            stateType = oldStateField.FieldType;
            Assembly xna = stateType.Assembly;
            buttonsType = xna.GetType("Microsoft.Xna.Framework.Input.Buttons", true);
            Type gamePad = xna.GetType("Microsoft.Xna.Framework.Input.GamePad", true);
            gamePadGetState = gamePad.GetMethod("GetState",
                BindingFlags.Static | BindingFlags.Public, null,
                new Type[] { playerIndexField.FieldType }, null);
            isButtonDown = stateType.GetMethod("IsButtonDown", new Type[] { buttonsType });
            thumbSticksProperty = stateType.GetProperty("ThumbSticks");
            triggersProperty = stateType.GetProperty("Triggers");
            leftStickProperty = thumbSticksProperty.PropertyType.GetProperty("Left");
            rightStickProperty = thumbSticksProperty.PropertyType.GetProperty("Right");
            leftTriggerProperty = triggersProperty.PropertyType.GetProperty("Left");
            rightTriggerProperty = triggersProperty.PropertyType.GetProperty("Right");
            vectorXProperty = leftStickProperty.PropertyType.GetProperty("X");
            vectorYProperty = leftStickProperty.PropertyType.GetProperty("Y");
            Type manager = assembly.GetType(
                "Magicka.GameLogic.GameStates.GameStateManager", true);
            gameStateManagerInstance = manager.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            currentStateProperty = manager.GetProperty("CurrentState", AnyInstance);
            playStateType = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
        }

        private static MethodInfo CreateUpdatePrefix(MethodInfo target)
        {
            return typeof(ModernControllerUpdatePrefix<>).MakeGenericType(
                target.DeclaringType).GetMethod("Prefix");
        }

        private static MethodInfo CreateFloatPrefix(MethodInfo target)
        {
            Type[] args = new Type[] { target.DeclaringType,
                target.GetParameters()[0].ParameterType,
                target.GetParameters()[1].ParameterType };
            return typeof(ModernControllerFloatPrefix<,,>).MakeGenericType(args)
                .GetMethod("Prefix");
        }

        private static MethodInfo CreateBoolPrefix(MethodInfo target)
        {
            Type[] args = new Type[] { target.DeclaringType,
                target.GetParameters()[0].ParameterType,
                target.GetParameters()[1].ParameterType };
            return typeof(ModernControllerBoolPrefix<,,>).MakeGenericType(args)
                .GetMethod("Prefix");
        }

        private static MethodInfo CreatePressedPrefix(MethodInfo target)
        {
            Type[] args = new Type[] { target.DeclaringType,
                target.GetParameters()[0].ParameterType,
                target.GetParameters()[1].ParameterType };
            return typeof(ModernControllerPressedPrefix<,,>)
                .MakeGenericType(args).GetMethod("Prefix");
        }

        private static MethodInfo CreateReleasedPrefix(MethodInfo target)
        {
            Type[] args = new Type[] { target.DeclaringType,
                target.GetParameters()[0].ParameterType,
                target.GetParameters()[1].ParameterType };
            return typeof(ModernControllerReleasedPrefix<,,>)
                .MakeGenericType(args).GetMethod("Prefix");
        }

        public static void BeforeUpdate(object controller)
        {
            if (!enabled || !IsPlaying()) return;
            object avatar = GetCurrentAvatar(controller);
            if (!CanDriveAvatar(avatar)) return;
            object playerIndex = playerIndexField.GetValue(controller);
            int index = Math.Max(0, Math.Min(3, Convert.ToInt32(playerIndex)));
            object current = gamePadGetState.Invoke(null, new object[] { playerIndex });
            object previous = oldStateField.GetValue(controller);
            bool modifier = Down(current, "LeftShoulder");
            if (Pressed(current, previous, "LeftShoulder")) ModifierUsed[index] = false;
            string[] activity = new string[] { "A", "B", "X", "Y",
                "DPadUp", "DPadDown", "DPadLeft", "DPadRight" };
            for (int i = 0; i < activity.Length; i++)
                if (modifier && Down(current, activity[i])) ModifierUsed[index] = true;
            if (!GetBool(avatar, "Polymorphed"))
            {
                SelectElement(avatar, current, previous, modifier, "A", "Fire", "Cold");
                SelectElement(avatar, current, previous, modifier, "B", "Earth", "Shield");
                SelectElement(avatar, current, previous, modifier, "X", "Lightning", "Water");
                SelectElement(avatar, current, previous, modifier, "Y", "Arcane", "Life");
            }
            if (Released(current, previous, "LeftShoulder") && !ModifierUsed[index])
                Invoke(avatar, "Action");
            if (Pressed(current, previous, "RightStick")) ClearSpellQueue(avatar);
            if (Pressed(current, previous, "LeftStick") ||
                Pressed(current, previous, "DPadRight")) Invoke(avatar, "Boost");
            if (Pressed(current, previous, "Back")) Invoke(avatar, "CheckInventory");
            if (Pressed(current, previous, "RightShoulder")) Invoke(avatar, "Special");
            else if (Released(current, previous, "RightShoulder")) Invoke(avatar, "SpecialRelease");
            UpdateRightTrigger(index, avatar, current, previous);
            if (Trigger(current, false) >= 0.5f &&
                (!GetBool(avatar, "ChantingMagick") || CastButton(avatar, "Area")))
                Invoke(avatar, "AreaPressed");
            else Invoke(avatar, "AreaReleased");
            UpdateAim(avatar, current, index,
                Convert.ToBoolean(invertedField.GetValue(controller)));
        }

        public static bool TryOverrideBool(object function, out bool result)
        {
            result = false;
            if (!enabled || !IsPlaying()) return false;
            string name = function.ToString();
            return name == "Attack" || name == "Boost" || name == "Interact" ||
                name == "Special" || name == "Inventory" || name == "Spell_Wheel" ||
                name == "Block" || name == "Cast_Area" || name == "Cast_Force" ||
                name == "Magick_Next" || name == "Magick_Prev";
        }

        public static bool TryOverrideFloat(object function, out float result)
        {
            result = 0f;
            if (!enabled || !IsPlaying()) return false;
            string name = function.ToString();
            return name == "Spell_Right" || name == "Spell_Left" ||
                name == "Spell_Up" || name == "Spell_Down";
        }

        public static bool TryOverridePressed(object controller, object function,
            object current, object previous, out bool result)
        {
            result = false;
            if (!enabled || !IsPlaying()) return false;
            string name = function.ToString();
            if (name == "Interact")
            {
                int index = Math.Max(0, Math.Min(3, Convert.ToInt32(
                    playerIndexField.GetValue(controller))));
                if (Pressed(current, previous, "LeftShoulder"))
                    ModifierUsed[index] = false;
                if (Down(current, "LeftShoulder") &&
                    (Down(current, "A") || Down(current, "B") ||
                     Down(current, "X") || Down(current, "Y") ||
                     Down(current, "DPadUp") || Down(current, "DPadDown") ||
                     Down(current, "DPadLeft") || Down(current, "DPadRight")))
                    ModifierUsed[index] = true;
                result = ShouldInvokeAction(
                    Down(current, "LeftShoulder"),
                    Down(previous, "LeftShoulder"),
                    ModifierUsed[index]);
                return true;
            }
            return TryOverrideBool(function, out result);
        }

        public static bool IsEnabled() { return enabled; }

        public static bool ShouldInvokeAction(bool currentModifierDown,
            bool previousModifierDown, bool modifierUsed)
        {
            return !currentModifierDown && previousModifierDown &&
                !modifierUsed;
        }

        public static string MappedElement(string button, bool modifier)
        {
            if (button == "A") return modifier ? "Cold" : "Fire";
            if (button == "B") return modifier ? "Shield" : "Earth";
            if (button == "X") return modifier ? "Water" : "Lightning";
            if (button == "Y") return modifier ? "Life" : "Arcane";
            return null;
        }

        public static void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value;
            Array.Clear(ModifierUsed, 0, ModifierUsed.Length);
            Array.Clear(TriggerMagick, 0, TriggerMagick.Length);
            Array.Clear(AimActive, 0, AimActive.Length);
            RuntimePatchSettings settings = RuntimePatchSettings.Load();
            settings.UseMagicka1ControllerScheme = !value;
            settings.Save();
        }

        private static void SelectElement(object avatar, object current,
            object previous, bool modifier, string button, string normal,
            string modified)
        {
            if (!Pressed(current, previous, button)) return;
            string element = modifier ? modified : normal;
            if (!CanSelectElement(avatar.GetType().Assembly, element)) return;
            Invoke(avatar, "Conjure" + element);
            RuntimePatchTelemetry.RecordControllerElementSelection();
        }

        private static bool CanSelectElement(Assembly assembly, string name)
        {
            try
            {
                Type manager = assembly.GetType("Magicka.GameLogic.UI.TutorialManager", true);
                object instance = manager.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .GetValue(null, null);
                Type elements = assembly.GetType("Magicka.GameLogic.Spells.Elements", true);
                object value = Enum.Parse(elements, name);
                return (bool)manager.GetMethod("IsElementEnabled", AnyInstance,
                    null, new Type[] { elements }, null).Invoke(instance,
                    new object[] { value });
            }
            catch { return true; }
        }

        private static void ClearSpellQueue(object avatar)
        {
            try
            {
                Assembly assembly = avatar.GetType().Assembly;
                Type networkManager = assembly.GetType(
                    "Magicka.Network.NetworkManager", true);
                object network = networkManager.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .GetValue(null, null);
                object state = FindProperty(networkManager, "State")
                    .GetValue(network, null);
                if (state.ToString() != "Offline") return;
                object player = FindProperty(avatar.GetType(), "Player")
                    .GetValue(avatar, null);
                ClearObject(FindProperty(player.GetType(), "IconRenderer")
                    .GetValue(player, null));
                object queue = FindProperty(avatar.GetType(), "SpellQueue")
                    .GetValue(avatar, null);
                ClearObject(queue);
                ClearObject(FindProperty(player.GetType(), "InputQueue")
                    .GetValue(player, null));
                FieldInfo chanted = FindField(avatar.GetType(), "mChantedSpells");
                FieldInfo chanting = FindField(avatar.GetType(), "mChantingMagick");
                FieldInfo direction = FindField(avatar.GetType(), "mChantDirection");
                if (chanted != null) chanted.SetValue(avatar, 0);
                if (chanting != null) chanting.SetValue(avatar, false);
                if (direction != null) direction.SetValue(avatar,
                    Enum.Parse(direction.FieldType, "Center"));
            }
            catch { }
        }

        private static void ClearObject(object value)
        {
            if (value == null) return;
            MethodInfo method = value.GetType().GetMethod("Clear", AnyInstance,
                null, Type.EmptyTypes, null);
            if (method != null) method.Invoke(value, null);
        }

        private static void UpdateRightTrigger(int index, object avatar,
            object current, object previous)
        {
            bool attack = Trigger(current, true) >= 0.5f;
            bool oldAttack = Trigger(previous, true) >= 0.5f;
            if (attack)
            {
                if (!oldAttack)
                {
                    TriggerMagick[index] = GetBool(avatar, "ChantingMagick");
                    if (!TriggerMagick[index]) Invoke(avatar, "Attack");
                }
                else if (!TriggerMagick[index] &&
                    (GetBool(avatar, "WieldingGun") || IsPrimaryItemSpellCharged(avatar)))
                    Invoke(avatar, "Attack");
            }
            else if (oldAttack)
            {
                Invoke(avatar, TriggerMagick[index] ? "Boost" : "AttackRelease");
                TriggerMagick[index] = false;
            }
            else TriggerMagick[index] = false;
        }

        private static void UpdateAim(object avatar, object state, int index,
            bool inverted)
        {
            object sticks = thumbSticksProperty.GetValue(state, null);
            object right = rightStickProperty.GetValue(sticks, null);
            float x = Convert.ToSingle(vectorXProperty.GetValue(right, null));
            float y = Convert.ToSingle(vectorYProperty.GetValue(right, null));
            if (inverted) { x = -x; y = -y; }
            float length = (float)Math.Sqrt(x * x + y * y);
            bool active = AimActive[index] ? length > 0.25f : length >= 0.35f;
            if (length > 0.25f) { LastAimX[index] = x; LastAimY[index] = y; }
            if (active || AimActive[index]) SetDesiredDirection(avatar,
                LastAimX[index], LastAimY[index]);
            Invoke(avatar, active ? "ForcePressed" : "ForceReleased");
            AimActive[index] = active;
        }

        private static void SetDesiredDirection(object avatar, float x, float y)
        {
            try
            {
                object body = FindProperty(avatar.GetType(), "CharacterBody")
                    .GetValue(avatar, null);
                PropertyInfo direction = FindProperty(body.GetType(), "DesiredDirection");
                object vector = Activator.CreateInstance(direction.PropertyType,
                    new object[] { x, 0f, -y });
                direction.SetValue(body, vector, null);
            }
            catch { }
        }

        private static bool IsPlaying()
        {
            try
            {
                object manager = gameStateManagerInstance.GetValue(null, null);
                return playStateType.IsInstanceOfType(
                    currentStateProperty.GetValue(manager, null));
            }
            catch { return false; }
        }

        private static object GetCurrentAvatar(object controller)
        {
            try
            {
                object player = controllerPlayerProperty.GetValue(controller, null);
                if (player == null || !GetBool(player, "Playing")) return null;
                PropertyInfo property = FindProperty(player.GetType(), "Avatar");
                return property == null ? null : property.GetValue(player, null);
            }
            catch { return null; }
        }

        private static bool CanDriveAvatar(object avatar)
        {
            if (avatar == null || GetBool(avatar, "Dead")) return false;
            try
            {
                Type managerType = avatar.GetType().Assembly.GetType(
                    "Magicka.GameLogic.Controls.ControlManager", true);
                object manager = managerType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .GetValue(null, null);
                if (GetBool(manager, "IsInputLimited")) return false;
                object player = FindProperty(avatar.GetType(), "Player")
                    .GetValue(avatar, null);
                object id = FindProperty(player.GetType(), "ID").GetValue(player, null);
                MethodInfo locked = managerType.GetMethod("IsPlayerInputLocked",
                    AnyInstance, null, new Type[] { id.GetType() }, null);
                return locked == null || !(bool)locked.Invoke(manager, new object[] { id });
            }
            catch { return false; }
        }

        private static bool CastButton(object avatar, string value)
        {
            try
            {
                MethodInfo method = avatar.GetType().GetMethod("CastButton", AnyInstance);
                Type type = method.GetParameters()[0].ParameterType;
                return (bool)method.Invoke(avatar,
                    new object[] { Enum.Parse(type, value) });
            }
            catch { return false; }
        }

        private static bool IsPrimaryItemSpellCharged(object avatar)
        {
            try
            {
                object equipment = FindProperty(avatar.GetType(), "Equipment")
                    .GetValue(avatar, null);
                object slot = ((IList)equipment)[0];
                object item = FindProperty(slot.GetType(), "Item").GetValue(slot, null);
                return item != null && GetBool(item, "SpellCharged");
            }
            catch { return false; }
        }

        private static bool GetBool(object target, string name)
        {
            if (target == null) return false;
            try
            {
                PropertyInfo property = FindProperty(target.GetType(), name);
                if (property != null) return Convert.ToBoolean(
                    property.GetValue(target, null));
                FieldInfo field = FindField(target.GetType(), name);
                return field != null && Convert.ToBoolean(field.GetValue(target));
            }
            catch { return false; }
        }

        private static bool Down(object state, string button)
        {
            return state != null && (bool)isButtonDown.Invoke(state,
                new object[] { Enum.Parse(buttonsType, button) });
        }
        private static bool Pressed(object current, object previous, string button)
        { return Down(current, button) && !Down(previous, button); }
        private static bool Released(object current, object previous, string button)
        { return !Down(current, button) && Down(previous, button); }
        private static float Trigger(object state, bool right)
        {
            object triggers = triggersProperty.GetValue(state, null);
            return Convert.ToSingle((right ? rightTriggerProperty : leftTriggerProperty)
                .GetValue(triggers, null));
        }
        private static void Invoke(object target, string name)
        {
            try { target.GetType().GetMethod(name, AnyInstance, null,
                Type.EmptyTypes, null).Invoke(target, null); } catch { }
        }
        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = FindField(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, AnyInstance |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }
        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, AnyInstance |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
    }

    public static class ModernControllerUpdatePrefix<TInstance>
    {
        public static void Prefix(TInstance __instance)
        { ModernControllerSchemePatch.BeforeUpdate(__instance); }
    }
    public static class ModernControllerFloatPrefix<TInstance, TFunction, TState>
    {
        public static bool Prefix(TInstance __instance, TFunction iFunction,
            TState iState, ref float __result)
        { return !ModernControllerSchemePatch.TryOverrideFloat(iFunction, out __result); }
    }
    public static class ModernControllerBoolPrefix<TInstance, TFunction, TState>
    {
        public static bool Prefix(TInstance __instance, TFunction iFunction,
            TState iState, ref bool __result)
        { return !ModernControllerSchemePatch.TryOverrideBool(iFunction, out __result); }
    }
    public static class ModernControllerPressedPrefix<TInstance, TFunction, TState>
    {
        public static bool Prefix(TInstance __instance, TFunction iFunction,
            TState iNewState, TState iOldState, ref bool __result)
        { return !ModernControllerSchemePatch.TryOverridePressed(__instance,
            iFunction, iNewState, iOldState, out __result); }
    }
    public static class ModernControllerReleasedPrefix<TInstance, TFunction, TState>
    {
        public static bool Prefix(TInstance __instance, TFunction iFunction,
            TState iNewState, TState iOldState, ref bool __result)
        { return !ModernControllerSchemePatch.TryOverrideBool(iFunction, out __result); }
    }
}
