using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneSwayDepthBufferPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        private static MethodInfo deviceDepthGetter;
        private static MethodInfo renderManagerInstanceGetter;
        private static MethodInfo defaultDepthGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "GameScene sway default depth buffer",
                "org.magickacommunitypatch.game-scene-sway-depth-buffer",
                FindTarget,
                typeof(GameSceneSwayDepthBufferPatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", false);
            Type renderManager = assembly.GetType("PolygonHead.RenderManager", false) ??
                Type.GetType("PolygonHead.RenderManager, PolygonHead", false);
            return scene != null && FindPreRender(scene) != null &&
                renderManager != null &&
                renderManager.GetProperty("DefaultDepthStencilBuffer",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic) != null;
        }

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            MethodInfo target = FindPreRender(scene);
            if (target == null)
                throw new MissingMethodException(scene.FullName, "PreRenderUpdate/5");

            Type game = assembly.GetType("Magicka.Game", true);
            PropertyInfo graphicsProperty = game.GetProperty(
                "GraphicsDevice",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            Type graphicsDevice = graphicsProperty == null
                ? null
                : graphicsProperty.PropertyType;
            if (graphicsDevice == null)
                throw new MissingMemberException(game.FullName, "GraphicsDevice");
            deviceDepthGetter = RequireGetter(graphicsDevice, "DepthStencilBuffer");
            Type renderManager = assembly.GetType("PolygonHead.RenderManager", false) ??
                Type.GetType("PolygonHead.RenderManager, PolygonHead", true);
            renderManagerInstanceGetter = RequireGetter(renderManager, "Instance");
            defaultDepthGetter = RequireGetter(
                renderManager, "DefaultDepthStencilBuffer");
            return target;
        }

        private static MethodInfo FindPreRender(Type scene)
        {
            MethodInfo[] methods = scene.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "PreRenderUpdate" &&
                    methods[index].GetParameters().Length == 5)
                    return methods[index];
            return null;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int match = -1;
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
                if ((result[index].opcode == OpCodes.Call ||
                        result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(result[index].operand, deviceDepthGetter))
                {
                    match = index;
                    matches++;
                }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one depth-buffer capture, found " + matches + ".");

            result[match - 1] = new CodeInstruction(
                OpCodes.Call, renderManagerInstanceGetter);
            result[match] = new CodeInstruction(OpCodes.Callvirt, defaultDepthGetter);
            return result;
        }

        private static MethodInfo RequireGetter(Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getter = property == null ? null : property.GetGetMethod(true);
            if (getter == null)
                throw new MissingMethodException(type.FullName, "get_" + propertyName);
            return getter;
        }
    }
}
