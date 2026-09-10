using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class NetworkDamageLifecycleScenarios
{
    private const BindingFlags Members = BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Test(magicka, runtime, report, "NetworkClient", "client");
        Test(magicka, runtime, report, "NetworkServer", "server");
    }

    private static void Test(Assembly magicka, bool runtime,
        BehaviorReport report, string typeName, string scenario)
    {
        Type network = magicka.GetType("Magicka.Network." + typeName, false);
        MethodInfo read = FindReadMessage(network);
        if (read == null)
        {
            report.AddNotApplicable("network_damage." + scenario,
                typeName + ".ReadMessage is absent.");
            return;
        }
        List<CodeInstruction> body = Decode(read);
        bool guarded;
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType("Magicka.CommunityPatch.Runtime.NetworkDamageLifecyclePatch", true);
            patch.GetMethod("FindReadMessage", Members).Invoke(null,
                new object[] { magicka, typeName, scenario });
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler", Members).Invoke(null,
                    new object[] { body }));
            guarded = CountCall(body, "ResolveDamage", null) == 2;
        }
        else
        {
            guarded = CountCall(body, "ResolveActive",
                "Magicka.CommunityPatch.NetworkLifecycleCompatibility") >= 1 &&
                CountCall(body, "ResolveDamageTarget",
                    "Magicka.CommunityPatch.NetworkEntityHandleGuard") >= 1;
        }
        report.Add("network_damage." + scenario, new ScenarioResult(
            guarded, guarded ? "guarded" : "unguarded", "guarded"));
    }

    private static MethodInfo FindReadMessage(Type type)
    {
        if (type == null) return null;
        MethodInfo[] methods = type.GetMethods(Members | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == "ReadMessage" &&
                methods[index].GetParameters().Length == 2)
                return methods[index];
        return null;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadDamage", typeof(void),
            Type.EmptyTypes, typeof(NetworkDamageLifecycleScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int CountCall(List<CodeInstruction> body, string namePart,
        string declaringType)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name.IndexOf(namePart,
                StringComparison.Ordinal) >= 0 && (declaringType == null ||
                method.DeclaringType.FullName == declaringType))
                count++;
        }
        return count;
    }
}
