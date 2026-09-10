using System;
using System.Reflection;

internal static class PayloadContractScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The legacy executable does not use the patched PolygonHead UI payload.";
            report.AddNotApplicable(
                "payload_contract.compatible_pair",
                reason);
            report.AddNotApplicable(
                "payload_contract.rejects_missing_pair",
                reason);
            return;
        }
        bool compatible;
        bool rejectsMissing;
        if (runtimePatchEnabled)
        {
            Type runtimeContract = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.RuntimePayloadContract",
                    true);
            MethodInfo check = runtimeContract.GetMethod(
                "IsPolygonHeadCompatible",
                BindingFlags.Static | BindingFlags.Public);
            compatible = (bool)check.Invoke(
                null,
                new object[] { Assembly.Load("PolygonHead") });
            rejectsMissing = !(bool)check.Invoke(
                null,
                new object[] { null });
        }
        else
        {
            Type contract = magicka.GetType(
                "Magicka.CommunityPatch.PayloadContract",
                false);
            MethodInfo check = contract == null
                ? null
                : contract.GetMethod(
                    "IsPolygonHeadCompatible",
                    BindingFlags.Static | BindingFlags.Public);
            compatible = check != null &&
                (bool)check.Invoke(null, new object[0]);
            rejectsMissing = check != null;
        }

        report.Add(
            "payload_contract.compatible_pair",
            Result(compatible));
        report.Add(
            "payload_contract.rejects_missing_pair",
            Result(rejectsMissing));
    }

    private static ScenarioResult Result(bool value)
    {
        return new ScenarioResult(
            value,
            "protected:" + value.ToString().ToLowerInvariant(),
            "protected:true");
    }
}
