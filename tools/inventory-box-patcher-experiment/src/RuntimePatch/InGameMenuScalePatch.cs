using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class InGameMenuScalePatch
    {
        private static Type menuType;
        private static Type pointType;
        private static Type vectorType;
        private static FieldInfo pointXField;
        private static FieldInfo pointYField;
        private static FieldInfo vectorXField;
        private static FieldInfo vectorYField;
        private static MethodInfo shouldScaleMethod;
        private static MethodInfo getScaleMethod;
        private static PropertyInfo renderManagerInstance;
        private static PropertyInfo renderManagerScreenSize;
        private static MethodInfo adjustPointMethod;
        private static MethodInfo adjustVectorMethod;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "In-game menu constructor scale",
                "org.magickacommunitypatch.in-game-menu-constructor-scale",
                FindConstructor,
                typeof(InGameMenuScalePatch).GetMethod("SizeTranspiler"));

        internal static readonly RuntimePatchDefinition ShowDefinition =
            RuntimePatchDefinition.Transpile(
                "In-game menu activation scale",
                "org.magickacommunitypatch.in-game-menu-activation-scale",
                FindShow,
                typeof(InGameMenuScalePatch).GetMethod("SizeTranspiler"));

        internal static readonly RuntimePatchDefinition PositionsDefinition =
            RuntimePatchDefinition.Transpile(
                "In-game menu position scale",
                "org.magickacommunitypatch.in-game-menu-position-scale",
                FindUpdatePositions,
                typeof(InGameMenuScalePatch).GetMethod("SizeTranspiler"));

        internal static IList<RuntimePatchDefinition> CreateMouseDefinitions(
            Assembly assembly)
        {
            Configure(assembly);
            string[] names = new string[]
            {
                "MouseScroll", "MouseMove", "MouseDown", "MouseUp"
            };
            List<RuntimePatchDefinition> result =
                new List<RuntimePatchDefinition>(names.Length);
            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                MethodInfo target = FindMouse(name);
                MethodInfo prefix = typeof(InGameMenuScalePatch).GetMethod(
                    name == "MouseScroll"
                        ? "MouseScrollPrefix" : "MousePrefix")
                    .MakeGenericMethod(vectorType);
                result.Add(RuntimePatchDefinition.Prefix(
                    "In-game menu scaled " + name,
                    "org.magickacommunitypatch.in-game-menu-" +
                        name.ToLowerInvariant(),
                    targetAssembly => target,
                    targetMethod => prefix));
            }
            return result;
        }

        private static void Configure(Assembly assembly)
        {
            if (menuType != null && menuType.Assembly == assembly)
                return;
            menuType = assembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu", true);
            pointType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Point");
            vectorType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector2");
            pointXField = RequireField(pointType, "X");
            pointYField = RequireField(pointType, "Y");
            vectorXField = RequireField(vectorType, "X");
            vectorYField = RequireField(vectorType, "Y");
            Type scale = Type.GetType(
                "PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead",
                true);
            shouldScaleMethod = scale.GetMethod("ShouldScale",
                BindingFlags.Static | BindingFlags.Public,
                null, new Type[] { typeof(int), typeof(int) }, null);
            getScaleMethod = scale.GetMethod("GetScaleFactor",
                BindingFlags.Static | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            Type manager = RuntimeMember.FindLoadedType(
                "PolygonHead.RenderManager");
            renderManagerInstance = manager.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public);
            renderManagerScreenSize = manager.GetProperty("ScreenSize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            adjustPointMethod = typeof(InGameMenuScalePatch).GetMethod(
                "AdjustMenuSize").MakeGenericMethod(pointType);
            adjustVectorMethod = typeof(InGameMenuScalePatch).GetMethod(
                "AdjustMenuMouse").MakeGenericMethod(vectorType);
            if (shouldScaleMethod == null || getScaleMethod == null ||
                renderManagerInstance == null || renderManagerScreenSize == null)
                throw new MissingMemberException("In-game UI scale contract");
        }

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Configure(assembly);
            ConstructorInfo constructor = menuType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (constructor == null)
                throw new MissingMethodException(menuType.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindShow(Assembly assembly)
        {
            Configure(assembly);
            MethodInfo[] methods = menuType.GetMethods(
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Show" &&
                    methods[index].GetParameters().Length == 1)
                    return methods[index];
            throw new MissingMethodException(menuType.FullName, "Show");
        }

        private static MethodInfo FindUpdatePositions(Assembly assembly)
        {
            Configure(assembly);
            return RequireMethod("UpdateAllPositions", Type.EmptyTypes);
        }

        private static MethodInfo FindMouse(string name)
        {
            MethodInfo[] methods = menuType.GetMethods(
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == name && parameters.Length >= 2 &&
                    parameters[1].ParameterType == vectorType.MakeByRefType())
                    return methods[index];
            }
            throw new MissingMethodException(menuType.FullName, name);
        }

        public static IEnumerable<CodeInstruction> SizeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int yStore = -1;
            object local = null;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, pointYField))
                    continue;
                for (int scan = index - 1; scan >= 0 && scan >= index - 8;
                    scan--)
                    if (result[scan].opcode == OpCodes.Ldloca ||
                        result[scan].opcode == OpCodes.Ldloca_S)
                    {
                        local = result[scan].operand;
                        yStore = index;
                        break;
                    }
                if (yStore >= 0)
                    break;
            }
            int insert;
            if (yStore >= 0)
                insert = yStore + 1;
            else
            {
                insert = -1;
                for (int index = 0; index < result.Count; index++)
                    if (result[index].opcode == OpCodes.Ldloca ||
                        result[index].opcode == OpCodes.Ldloca_S)
                    {
                        local = result[index].operand;
                        insert = index;
                        break;
                    }
            }
            if (insert < 0 || local == null)
                throw new InvalidOperationException(
                    "In-game menu size local changed.");
            CodeInstruction load = new CodeInstruction(
                OpCodes.Ldloca, local);
            CodeInstruction call = new CodeInstruction(
                OpCodes.Call, adjustPointMethod);
            if (insert < result.Count)
            {
                load.labels.AddRange(result[insert].labels);
                load.blocks.AddRange(result[insert].blocks);
                result[insert].labels.Clear();
                result[insert].blocks.Clear();
            }
            result.Insert(insert, load);
            result.Insert(insert + 1, call);
            return result;
        }

        public static void MousePrefix<TVector>(ref TVector iMousePos)
        {
            AdjustMenuMouse(ref iMousePos);
        }

        public static void MouseScrollPrefix<TVector>(ref TVector iMousePos)
        {
            AdjustMenuMouse(ref iMousePos);
        }

        public static void AdjustMenuSize<TPoint>(ref TPoint size)
        {
            object boxed = size;
            int width = Convert.ToInt32(pointXField.GetValue(boxed));
            int height = Convert.ToInt32(pointYField.GetValue(boxed));
            if (!(bool)shouldScaleMethod.Invoke(
                null, new object[] { width, height }))
                return;
            float scale = Convert.ToSingle(
                getScaleMethod.Invoke(null, null));
            pointXField.SetValue(boxed,
                (int)((float)width / scale + 0.5f));
            pointYField.SetValue(boxed,
                (int)((float)height / scale + 0.5f));
            size = (TPoint)boxed;
        }

        public static void AdjustMenuMouse<TVector>(ref TVector position)
        {
            object manager = renderManagerInstance.GetValue(null, null);
            object size = renderManagerScreenSize.GetValue(manager, null);
            int width = Convert.ToInt32(pointXField.GetValue(size));
            int height = Convert.ToInt32(pointYField.GetValue(size));
            if (!(bool)shouldScaleMethod.Invoke(
                null, new object[] { width, height }))
                return;
            float scale = Convert.ToSingle(
                getScaleMethod.Invoke(null, null));
            object boxed = position;
            vectorXField.SetValue(boxed,
                Convert.ToSingle(vectorXField.GetValue(boxed)) / scale);
            vectorYField.SetValue(boxed,
                Convert.ToSingle(vectorYField.GetValue(boxed)) / scale);
            position = (TVector)boxed;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(string name, Type[] parameters)
        {
            MethodInfo method = menuType.GetMethod(name,
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (method == null)
                throw new MissingMethodException(menuType.FullName, name);
            return method;
        }
    }
}
