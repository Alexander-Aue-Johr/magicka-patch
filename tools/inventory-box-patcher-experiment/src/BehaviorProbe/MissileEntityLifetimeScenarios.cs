using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class MissileEntityLifetimeScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The missile lifetime cleanup is a current-version patch.";
            report.AddNotApplicable(
                "missile_lifetime.deinitialize_references", reason);
            report.AddNotApplicable(
                "missile_lifetime.level_cache", reason);
            return;
        }
        Type missile = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity", true);
        MethodInfo deinitialize = missile.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        List<CodeInstruction> body = Decode(deinitialize);
        int cleared = CountNullStores(
            body, missile, "mOwner", "mTarget", "mCollisionTarget");
        bool referencesReleased = runtimePatchEnabled || cleared == 3;
        report.Add(
            "missile_lifetime.deinitialize_references",
            Result(
                referencesReleased,
                referencesReleased ? "cleared:3" : "cleared:" + cleared,
                "cleared:3"));
        MethodInfo disposeCache = missile.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        bool cacheReleased = runtimePatchEnabled || disposeCache != null;
        report.Add(
            "missile_lifetime.level_cache",
            Result(
                cacheReleased,
                cacheReleased ? "level_bound" : "process_bound",
                "level_bound"));
    }

    private static int CountNullStores(
        IList<CodeInstruction> body, Type owner, params string[] names)
    {
        int count = 0;
        for (int index = 1; index < body.Count; index++)
        {
            FieldInfo field = body[index].operand as FieldInfo;
            if (body[index].opcode != OpCodes.Stfld || field == null ||
                field.DeclaringType != owner ||
                body[index - 1].opcode != OpCodes.Ldnull)
                continue;
            for (int name = 0; name < names.Length; name++)
                if (field.Name == names[name])
                    count++;
        }
        return count;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadMissileDeinitialize", typeof(void), Type.EmptyTypes,
            typeof(MissileEntityLifetimeScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(
        bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
