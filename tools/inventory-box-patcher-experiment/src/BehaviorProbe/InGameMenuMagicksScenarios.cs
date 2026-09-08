using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class InGameMenuMagicksScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        InGameMenuMagicksHarness harness =
            new InGameMenuMagicksHarness(magicka);
        try
        {
            report.Add(
                "magicks_language.negative_selection",
                harness.ChangeLanguage(-1, ""));
            report.Add(
                "magicks_language.past_end_selection",
                harness.ChangeLanguage(1, ""));
            report.Add(
                "magicks_language.valid_selection",
                harness.ChangeLanguage(0, "wrapped"));
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class InGameMenuMagicksHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-magicks-language";

    private readonly Type menuType;
    private readonly Type bitmapFontType;
    private readonly Type textType;
    private readonly FieldInfo languageSingletonField;
    private readonly MethodInfo languageChanged;
    private readonly object originalLanguageSingleton;
    private readonly HarmonyInstance harmony;

    internal InGameMenuMagicksHarness(Assembly magicka)
    {
        menuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMagicks",
            true);
        Type languageType = magicka.GetType(
            "Magicka.Localization.LanguageManager",
            true);
        bitmapFontType = RuntimeReflection.FindLoadedType(
            "PolygonHead.BitmapFont");
        textType = RuntimeReflection.FindLoadedType("PolygonHead.Text");
        languageChanged = menuType.GetMethod(
            "LanguageChanged",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        languageSingletonField = languageType.GetField(
            "sSingelton",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        MethodInfo getString = languageType.GetMethod(
            "GetString",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(int) },
            null);
        MethodInfo wrap = bitmapFontType.GetMethod(
            "Wrap",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { typeof(string), typeof(int), typeof(bool) },
            null);
        MethodInfo setText = textType.GetMethod(
            "SetText",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { typeof(string) },
            null);
        if (languageChanged == null || languageSingletonField == null ||
            getString == null || wrap == null || setText == null)
            throw new MissingMemberException(
                "InGameMenuMagicks language behavior contract is incomplete.");

        originalLanguageSingleton = languageSingletonField.GetValue(null);
        languageSingletonField.SetValue(
            null,
            NewUninitialized(languageType));
        harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            getString,
            new HarmonyMethod(
                typeof(InGameMenuMagicksProbe).GetMethod("GetStringPrefix")),
            null,
            null);
        harmony.Patch(
            wrap,
            new HarmonyMethod(
                typeof(InGameMenuMagicksProbe).GetMethod("WrapPrefix")),
            null,
            null);
        harmony.Patch(
            setText,
            new HarmonyMethod(
                typeof(InGameMenuMagicksProbe).GetMethod("SetTextPrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        InGameMenuMagicksProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        languageSingletonField.SetValue(null, originalLanguageSingleton);
    }

    internal ScenarioResult ChangeLanguage(int markedItem, string expected)
    {
        object menu = NewUninitialized(menuType);
        RuntimeReflection.WriteField(
            menu,
            "mMenuItems",
            Activator.CreateInstance(
                RuntimeReflection.RequireField(menuType, "mMenuItems").FieldType));
        RuntimeReflection.WriteField(menu, "mDescriptions", new string[1]);
        RuntimeReflection.WriteField(menu, "mDescriptionsHash", new int[1]);
        RuntimeReflection.WriteField(
            menu,
            "mFont",
            NewUninitialized(bitmapFontType));
        RuntimeReflection.WriteField(menu, "mMarkedItem", markedItem);
        RuntimeReflection.WriteField(
            menu,
            "mDescription",
            NewUninitialized(textType));

        InGameMenuMagicksProbe.Reset();
        InGameMenuMagicksProbe.Enabled = true;
        string exceptionName = "none";
        try
        {
            Invoke(languageChanged, menu);
        }
        catch (Exception exception)
        {
            exceptionName = exception.GetType().Name;
        }
        finally
        {
            InGameMenuMagicksProbe.Enabled = false;
        }

        bool passed = exceptionName == "none" &&
            InGameMenuMagicksProbe.SetTextCalls == 1 &&
            InGameMenuMagicksProbe.Text == expected;
        return new ScenarioResult(
            passed,
            "exception:" + exceptionName +
                ",calls:" + InGameMenuMagicksProbe.SetTextCalls +
                ",text:" + (InGameMenuMagicksProbe.Text ?? "<null>"),
            "exception:none,calls:1,text:" + expected);
    }

    private static object Invoke(MethodInfo method, object target)
    {
        try
        {
            return method.Invoke(target, new object[0]);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class InGameMenuMagicksProbe
{
    public static bool Enabled;
    public static int SetTextCalls;
    public static string Text;

    public static void Reset()
    {
        SetTextCalls = 0;
        Text = null;
    }

    public static bool GetStringPrefix(ref string __result)
    {
        if (!Enabled)
            return true;
        __result = "localized";
        return false;
    }

    public static bool WrapPrefix(ref string __result)
    {
        if (!Enabled)
            return true;
        __result = "wrapped";
        return false;
    }

    public static bool SetTextPrefix(string iText)
    {
        if (!Enabled)
            return true;
        SetTextCalls++;
        Text = iText;
        return false;
    }
}
