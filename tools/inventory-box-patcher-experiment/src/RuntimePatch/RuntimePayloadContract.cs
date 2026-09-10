using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimePayloadContract
    {
        public const string Id =
            "magicka-community-patch-payload-0.0.55-r1";

        public static bool IsPolygonHeadCompatible(Assembly polygonHead)
        {
            if (polygonHead == null)
                return false;
            Type marker = polygonHead.GetType(
                "PolygonHead.CommunityPatch.PayloadContract",
                false);
            FieldInfo identifier = marker == null
                ? null
                : marker.GetField(
                    "Id",
                    BindingFlags.Static | BindingFlags.Public);
            if (identifier == null || !identifier.IsLiteral ||
                !Object.Equals(identifier.GetRawConstantValue(), Id))
                return false;

            Type scale = polygonHead.GetType(
                "PolygonHead.CommunityPatch.InGameUiRenderScale",
                false);
            if (scale == null)
                return false;
            MethodInfo begin = FindSignature(
                scale,
                "Begin",
                "Microsoft.Xna.Framework.Point",
                new string[]
                {
                    "Microsoft.Xna.Framework.Graphics.GraphicsDevice",
                    "Microsoft.Xna.Framework.Graphics.RenderTarget2D",
                    "Microsoft.Xna.Framework.Point"
                });
            MethodInfo end = FindSignature(
                scale,
                "End",
                "System.Void",
                new string[]
                {
                    "Microsoft.Xna.Framework.Graphics.GraphicsDevice",
                    "Microsoft.Xna.Framework.Graphics.RenderTarget2D",
                    "Microsoft.Xna.Framework.Graphics.DepthStencilBuffer",
                    "Microsoft.Xna.Framework.Point"
                });
            return begin != null && end != null;
        }

        public static void EnsureCompatible(Assembly magicka)
        {
            if (magicka == null || magicka.GetName().Version.Minor < 10)
                return;
            Assembly polygonHead = null;
            try
            {
                polygonHead = Assembly.Load("PolygonHead");
            }
            catch
            {
            }
            if (IsPolygonHeadCompatible(polygonHead))
                return;

            ShowError(
                "The Community Patch files do not belong to the same " +
                "version. Reinstall one complete Community Patch package " +
                "and try again.");
            Environment.Exit(1);
        }

        private static void ShowError(string message)
        {
            try
            {
                Type messageBox = Type.GetType(
                    "System.Windows.Forms.MessageBox, System.Windows.Forms",
                    false);
                MethodInfo show = messageBox == null
                    ? null
                    : messageBox.GetMethod(
                        "Show",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new Type[] { typeof(string), typeof(string) },
                        null);
                if (show != null)
                    show.Invoke(
                        null,
                        new object[] { message, "Magicka Community Patch" });
            }
            catch
            {
            }
        }

        private static MethodInfo FindSignature(
            Type type,
            string name,
            string returnType,
            string[] parameterTypes)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public);
            for (int methodIndex = 0;
                methodIndex < methods.Length;
                methodIndex++)
            {
                MethodInfo method = methods[methodIndex];
                if (method.Name != name ||
                    method.ReturnType.FullName != returnType)
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;
                bool matches = true;
                for (int parameterIndex = 0;
                    parameterIndex < parameters.Length;
                    parameterIndex++)
                {
                    if (parameters[parameterIndex].ParameterType.FullName !=
                        parameterTypes[parameterIndex])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                    return method;
            }
            return null;
        }
    }
}
