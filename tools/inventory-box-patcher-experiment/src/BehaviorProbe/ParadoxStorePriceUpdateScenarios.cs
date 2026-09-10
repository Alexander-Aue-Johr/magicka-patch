using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ParadoxStorePriceUpdateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type type = magicka.GetType(
            "Magicka.CoreFramework.GameSystem.Store.StoreItemDatabase",
            false);
        if (type == null)
        {
            report.Add("store_price_update.background_https",
                new ScenarioResult(true, "not_applicable", "not_applicable"));
            return;
        }
        try
        {
            MethodInfo method = type.GetMethod(
                "UpdateParadoxItems",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            List<CodeInstruction> instructions = Decode(method);
            if (runtimePatchEnabled)
            {
                DynamicMethod dynamic = new DynamicMethod(
                    "StorePriceTransform", typeof(void), Type.EmptyTypes,
                    typeof(ParadoxStorePriceUpdateScenarios), true);
                instructions = new List<CodeInstruction>(
                    (IEnumerable<CodeInstruction>)typeof(
                        Magicka.CommunityPatch.Runtime.ParadoxStorePriceUpdatePatch)
                        .GetMethod("Transpiler").Invoke(null,
                            new object[] { instructions, dynamic.GetILGenerator() }));
            }
            bool queued = false;
            bool https = false;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo call = instructions[index].operand as MethodInfo;
                if (call != null &&
                    (call.Name == "QueueOrRun" ||
                     call.Name == "QueueParadoxStorePriceUpdate"))
                    queued = true;
                string value = instructions[index].operand as string;
                if (value ==
                    "https://services.paradoxplaza.com/adam/offers/red_wizard")
                    https = true;
            }
            report.Add("store_price_update.background_https",
                new ScenarioResult(queued && https,
                    "queued:" + queued + ",https:" + https,
                    "queued:True,https:True"));
        }
        catch (Exception exception)
        {
            report.Add("store_price_update.background_https",
                new ScenarioResult(false,
                    "exception:" + exception.GetType().Name,
                    "queued:True,https:True"));
        }
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadStorePriceUpdate", typeof(void), Type.EmptyTypes,
            typeof(ParadoxStorePriceUpdateScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
