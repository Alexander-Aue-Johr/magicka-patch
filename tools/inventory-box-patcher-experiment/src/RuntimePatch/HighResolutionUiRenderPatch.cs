using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class HighResolutionUiRenderPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static readonly Dictionary<Type, FieldInfo> PositionFields =
            new Dictionary<Type, FieldInfo>();
        private static readonly object PositionSync = new object();
        private static MethodInfo adjustPosition;
        private static MethodInfo adjustNotifierPosition;
        private static MethodInfo setEnabled;
        private static MethodInfo setScale;
        private static PropertyInfo currentStateProperty;
        private static Type playStateType;
        private static FieldInfo notifierOffsetField;
        private static FieldInfo textBoxEffectField;
        private static FieldInfo cutsceneEffectField;
        private static PropertyInfo renderManagerInstanceProperty;
        private static PropertyInfo renderManagerScreenSizeProperty;
        private static PropertyInfo effectScreenSizeProperty;
        private static Type vectorType;
        private static float scaleFactor = LoadScaleFactor();

        internal static readonly RuntimePatchDefinition GameDefinition =
            RuntimePatchDefinition.Prefix(
                "High-resolution UI render activation",
                "org.magickacommunitypatch.ui-render-activation",
                FindGameDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "GamePrefix"));

        internal static readonly RuntimePatchDefinition TextBoxDefinition =
            RuntimePatchDefinition.Prefix(
                "TextBox projected UI scaling",
                "org.magickacommunitypatch.text-box-ui-scaling",
                FindTextBoxDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "TextBoxPrefix"));

        internal static readonly RuntimePatchDefinition CutsceneDefinition =
            RuntimePatchDefinition.Prefix(
                "Cutscene text projected UI scaling",
                "org.magickacommunitypatch.cutscene-text-ui-scaling",
                FindCutsceneDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "CutscenePrefix"));

        internal static readonly RuntimePatchDefinition IconDefinition =
            RuntimePatchDefinition.Prefix(
                "IconRenderer projected UI scaling",
                "org.magickacommunitypatch.icon-renderer-ui-scaling",
                FindIconDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "ProjectedPositionPrefix"));

        internal static readonly RuntimePatchDefinition SpellWheelDefinition =
            RuntimePatchDefinition.Prefix(
                "SpellWheel projected UI scaling",
                "org.magickacommunitypatch.spell-wheel-ui-scaling",
                FindSpellWheelDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "ProjectedPositionPrefix"));

        internal static readonly RuntimePatchDefinition NotifierPrefixDefinition =
            RuntimePatchDefinition.Prefix(
                "Notifier projected UI scaling",
                "org.magickacommunitypatch.notifier-ui-scaling-prefix",
                FindNotifierDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "NotifierPrefix"));

        internal static readonly RuntimePatchDefinition NotifierPostfixDefinition =
            RuntimePatchDefinition.Postfix(
                "Notifier projected position restoration",
                "org.magickacommunitypatch.notifier-ui-scaling-postfix",
                FindNotifierDraw,
                target => typeof(HighResolutionUiRenderPatch).GetMethod(
                    "NotifierPostfix"));

        private static MethodInfo FindGameDraw(Assembly assembly)
        {
            Configure(assembly);
            Type game = assembly.GetType("Magicka.Game", true);
            Type gameTime = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.GameTime");
            return RequireMethod(
                game,
                "Draw",
                new Type[] { gameTime },
                typeof(void));
        }

        private static MethodInfo FindTextBoxDraw(Assembly assembly)
        {
            Configure(assembly);
            Type owner = assembly.GetType("Magicka.Graphics.TextBox", true);
            Type renderData = RequireNested(owner, "RenderData");
            textBoxEffectField = RequireField(renderData, "mTextBoxEffect");
            ConfigureEffect(textBoxEffectField.FieldType);
            return RequireDraw(renderData);
        }

        private static MethodInfo FindCutsceneDraw(Assembly assembly)
        {
            Configure(assembly);
            Type owner = assembly.GetType("Magicka.Graphics.CutsceneText", true);
            Type renderData = RequireNested(owner, "CutSceneRenderData");
            cutsceneEffectField = RequireField(renderData, "mTextBoxEffect");
            ConfigureEffect(cutsceneEffectField.FieldType);
            return RequireDraw(renderData);
        }

        private static MethodInfo FindIconDraw(Assembly assembly)
        {
            Configure(assembly);
            return RequireDraw(RequireNested(
                assembly.GetType("Magicka.GameLogic.UI.IconRenderer", true),
                "RenderData"));
        }

        private static MethodInfo FindSpellWheelDraw(Assembly assembly)
        {
            Configure(assembly);
            return RequireDraw(RequireNested(
                assembly.GetType("Magicka.GameLogic.UI.SpellWheel", true),
                "RenderData"));
        }

        private static MethodInfo FindNotifierDraw(Assembly assembly)
        {
            Configure(assembly);
            Type renderData = RequireNested(
                assembly.GetType("Magicka.Graphics.NotifierButton", true),
                "RenderData");
            notifierOffsetField = RequireField(renderData, "mOffset");
            return RequireDraw(renderData);
        }

        private static void Configure(Assembly assembly)
        {
            Type scaleType = Type.GetType(
                "PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead",
                true);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector2");
            adjustPosition = scaleType.GetMethod(
                "AdjustProjectedPosition",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { vector.MakeByRefType() },
                null);
            adjustNotifierPosition = scaleType.GetMethod(
                "AdjustProjectedPosition",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { vector.MakeByRefType(), vector },
                null);
            setEnabled = RequireStaticMethod(
                scaleType,
                "SetEnabled",
                new Type[] { typeof(bool) },
                typeof(void));
            setScale = RequireStaticMethod(
                scaleType,
                "SetScale",
                new Type[] { typeof(float) },
                typeof(void));
            if (adjustPosition == null || adjustNotifierPosition == null)
                throw new MissingMethodException(
                    scaleType.FullName,
                    "AdjustProjectedPosition");
            vectorType = vector;

            Type manager = assembly.GetType(
                "Magicka.GameLogic.GameStates.GameStateManager",
                true);
            PropertyInfo instance = manager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            currentStateProperty = manager.GetProperty(
                "CurrentState",
                InstanceMembers);
            if (instance == null || currentStateProperty == null)
                throw new MissingMemberException(manager.FullName, "CurrentState");
            GameStateManagerInstance = instance;
            playStateType = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
        }

        private static PropertyInfo GameStateManagerInstance;

        public static void GamePrefix()
        {
            object manager = GameStateManagerInstance.GetValue(null, null);
            object state = manager == null
                ? null
                : currentStateProperty.GetValue(manager, null);
            SetRenderState(state != null && playStateType.IsInstanceOfType(state));
        }

        public static void SetRenderState(bool enabled)
        {
            setScale.Invoke(null, new object[] { scaleFactor });
            setEnabled.Invoke(null, new object[] { enabled });
        }

        public static void ProjectedPositionPrefix(object __instance)
        {
            Adjust(__instance, null);
        }

        public static void TextBoxPrefix(object __instance)
        {
            Adjust(__instance, null);
            SetEffectScreenSize(__instance, textBoxEffectField);
        }

        public static void CutscenePrefix(object __instance)
        {
            Adjust(__instance, null);
            SetEffectScreenSize(__instance, cutsceneEffectField);
        }

        public static void NotifierPrefix(object __instance, out object __state)
        {
            FieldInfo position = PositionField(__instance.GetType());
            __state = position.GetValue(__instance);
            Adjust(__instance, notifierOffsetField.GetValue(__instance));
        }

        public static void NotifierPostfix(object __instance, object __state)
        {
            if (__instance != null && __state != null)
                PositionField(__instance.GetType()).SetValue(
                    __instance,
                    __state);
        }

        private static void Adjust(object instance, object offset)
        {
            if (instance == null)
                return;
            FieldInfo positionField = PositionField(instance.GetType());
            object[] arguments = offset == null
                ? new object[] { positionField.GetValue(instance) }
                : new object[] { positionField.GetValue(instance), offset };
            (offset == null ? adjustPosition : adjustNotifierPosition).Invoke(
                null,
                arguments);
            positionField.SetValue(instance, arguments[0]);
        }

        private static void SetEffectScreenSize(
            object instance,
            FieldInfo field)
        {
            object effect = field.GetValue(instance);
            if (effect == null)
                return;
            object manager = renderManagerInstanceProperty.GetValue(null, null);
            object size = renderManagerScreenSizeProperty.GetValue(manager, null);
            FieldInfo x = RequireField(size.GetType(), "X");
            FieldInfo y = RequireField(size.GetType(), "Y");
            object vector = Activator.CreateInstance(
                vectorType,
                new object[]
                {
                    (float)(int)x.GetValue(size),
                    (float)(int)y.GetValue(size)
                });
            effectScreenSizeProperty.SetValue(effect, vector, null);
        }

        private static void ConfigureEffect(Type effectType)
        {
            effectScreenSizeProperty = effectType.GetProperty(
                "ScreenSize",
                InstanceMembers);
            Type manager = RuntimeMember.FindLoadedType("PolygonHead.RenderManager");
            renderManagerInstanceProperty = manager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            renderManagerScreenSizeProperty = manager.GetProperty(
                "ScreenSize",
                InstanceMembers);
            if (effectScreenSizeProperty == null ||
                renderManagerInstanceProperty == null ||
                renderManagerScreenSizeProperty == null)
                throw new MissingMemberException("UI screen-size contract");
        }

        private static FieldInfo PositionField(Type type)
        {
            lock (PositionSync)
            {
                FieldInfo field;
                if (!PositionFields.TryGetValue(type, out field))
                {
                    field = RequireField(type, "mPosition");
                    PositionFields.Add(type, field);
                }
                return field;
            }
        }

        private static float LoadScaleFactor()
        {
            try
            {
                string path = Path.Combine(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "CommunityPatch"),
                    "ui-scale.ini");
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    for (int index = 0; index < lines.Length; index++)
                    {
                        string line = lines[index].Trim();
                        float value;
                        if (line.StartsWith(
                                "ui_scale=",
                                StringComparison.OrdinalIgnoreCase) &&
                            float.TryParse(
                                line.Substring(9).Trim(),
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out value))
                            return Math.Max(1f, Math.Min(4f, value));
                    }
                }
            }
            catch
            {
            }
            return 2f;
        }

        private static Type RequireNested(Type type, string name)
        {
            Type nested = type.GetNestedType(
                name,
                BindingFlags.Public | BindingFlags.NonPublic);
            if (nested == null)
                throw new TypeLoadException(type.FullName + "+" + name);
            return nested;
        }

        private static MethodInfo RequireDraw(Type type)
        {
            return RequireMethod(
                type,
                "Draw",
                new Type[] { typeof(float) },
                typeof(void));
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static MethodInfo RequireStaticMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Public,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
