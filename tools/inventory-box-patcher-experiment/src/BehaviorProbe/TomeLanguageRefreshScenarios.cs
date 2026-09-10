using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class TomeLanguageRefreshScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            report.AddNotApplicable("tome_language.account_widgets",
                "legacy executable predates the account widgets");
            report.AddNotApplicable("tome_language.version_dirty",
                "legacy executable predates the account widgets");
            return;
        }
        TomeLanguageRefreshHarness harness =
            new TomeLanguageRefreshHarness(magicka, runtime);
        report.Add("tome_language.account_widgets", harness.AccountWidgets());
        report.Add("tome_language.version_dirty", harness.VersionDirty());
    }
}

internal sealed class TomeLanguageRefreshHarness
{
    private readonly List<CodeInstruction> body;
    private readonly FieldInfo[] widgetFields;
    private readonly MethodInfo[] widgetRefreshMethods;
    private readonly MethodInfo markAsDirty;

    internal TomeLanguageRefreshHarness(Assembly magicka, bool runtime)
    {
        Type tome = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        MethodInfo changed = tome.GetMethod("LanguageChanged",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        string[] names = new string[]
        {
            "sIndicatorBackground", "sAccountLoginBtn", "sAccountCreationBtn"
        };
        widgetFields = new FieldInfo[names.Length];
        widgetRefreshMethods = new MethodInfo[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            widgetFields[index] = tome.GetField(names[index],
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            widgetRefreshMethods[index] = widgetFields[index].FieldType.GetMethod(
                "LanguageChanged",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
        }
        FieldInfo version = tome.GetField("sVersionText",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        markAsDirty = version.FieldType.GetMethod("MarkAsDirty",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        body = Decode(changed);
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.TomeLanguageRefreshPatch",
                    true);
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler").Invoke(null, new object[] { body }));
        }
    }

    internal ScenarioResult AccountWidgets()
    {
        int count = 0;
        for (int index = 0; index < widgetRefreshMethods.Length; index++)
            count += CountPair(widgetFields[index], widgetRefreshMethods[index]);
        return new ScenarioResult(count == 3, "refreshes:" + count,
            "refreshes:3");
    }

    private int CountPair(FieldInfo field, MethodBase method)
    {
        int count = 0;
        for (int index = 0; index + 1 < body.Count; index++)
            if (Object.Equals(body[index].operand, field) &&
                SameMethod(body[index + 1].operand as MethodBase, method)) count++;
        return count;
    }

    internal ScenarioResult VersionDirty()
    {
        int count = Count(markAsDirty);
        return new ScenarioResult(count == 1, "dirty_marks:" + count,
            "dirty_marks:1");
    }

    private int Count(MethodBase method)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
            if (SameMethod(body[index].operand as MethodBase, method)) count++;
        return count;
    }

    private static bool SameMethod(MethodBase left, MethodBase right)
    {
        if (left == null || right == null) return false;
        try
        {
            return left.MetadataToken == right.MetadataToken &&
                left.Module == right.Module;
        }
        catch
        {
            return left.Name == right.Name &&
                left.DeclaringType == right.DeclaringType;
        }
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadTomeLanguageBody",
            typeof(void), Type.EmptyTypes,
            typeof(TomeLanguageRefreshHarness), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
