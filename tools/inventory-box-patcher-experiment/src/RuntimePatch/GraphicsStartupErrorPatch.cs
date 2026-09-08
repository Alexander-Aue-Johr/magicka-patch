using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GraphicsStartupErrorPatch
    {
        private const string Title = "Magicka graphics startup error";
        private const string AdapterMessage =
            "Magicka could not map the selected graphics adapter to a monitor. " +
            "Update the graphics driver, disconnect virtual displays, and avoid " +
            "Remote Desktop. On Linux or Proton, also verify the display and " +
            "Proton configuration.";
        private const string DeviceMessage =
            "Magicka could not find a suitable graphics device. Install a " +
            "supported graphics driver and verify the DirectX/XNA runtime. On " +
            "Linux or Proton, also verify the selected Proton version and display " +
            "configuration.";

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Graphics startup error guidance",
                "org.magickacommunitypatch.graphics-startup-error",
                FindWriteReport,
                target => typeof(GraphicsStartupErrorPatch).GetMethod("Prefix"));

        private static MethodInfo FindWriteReport(Assembly targetAssembly)
        {
            Type programType = targetAssembly.GetType("Magicka.Program", true);
            MethodInfo method = programType.GetMethod(
                "WriteReport",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(object), typeof(UnhandledExceptionEventArgs) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(programType.FullName, "WriteReport");
            return method;
        }

        public static void Prefix(object __1)
        {
            UnhandledExceptionEventArgs arguments =
                __1 as UnhandledExceptionEventArgs;
            if (arguments == null)
                return;
            string reason = ClassifyGraphicsStartupException(
                arguments.ExceptionObject);
            if (reason == "adapter")
                ShowMessage(AdapterMessage);
            else if (reason == "device")
                ShowMessage(DeviceMessage);
        }

        public static string ClassifyGraphicsStartupException(object exception)
        {
            ArgumentException argument = exception as ArgumentException;
            if (argument != null &&
                argument.ToString().Contains("Microsoft.Xna.Framework"))
                return "adapter";
            if (exception != null &&
                exception.GetType().FullName ==
                    "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException")
                return "device";
            return "none";
        }

        private static void ShowMessage(string message)
        {
            try
            {
                Type messageBoxType = Type.GetType(
                    "System.Windows.Forms.MessageBox, System.Windows.Forms",
                    false);
                Type buttonsType = Type.GetType(
                    "System.Windows.Forms.MessageBoxButtons, System.Windows.Forms",
                    false);
                Type iconType = Type.GetType(
                    "System.Windows.Forms.MessageBoxIcon, System.Windows.Forms",
                    false);
                if (messageBoxType == null || buttonsType == null ||
                    iconType == null)
                    return;
                MethodInfo show = messageBoxType.GetMethod(
                    "Show",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        buttonsType,
                        iconType
                    },
                    null);
                if (show == null)
                    return;
                show.Invoke(
                    null,
                    new object[]
                    {
                        message,
                        Title,
                        Enum.Parse(buttonsType, "OK"),
                        Enum.Parse(iconType, "Hand")
                    });
            }
            catch
            {
            }
        }
    }
}
