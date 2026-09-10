using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class NetworkSpawnHandleScenarios
{
    private const BindingFlags Members = BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Test(magicka, runtime, report, "NetworkClient", "client");
        Test(magicka, runtime, report, "NetworkServer", "server");
    }

    private static void Test(Assembly magicka, bool runtime,
        BehaviorReport report, string typeName, string side)
    {
        MethodInfo read = FindReadMessage(magicka.GetType(
            "Magicka.Network." + typeName, false));
        if (read == null)
        {
            report.AddNotApplicable("network_spawn_handles." + side,
                typeName + ".ReadMessage is absent.");
            return;
        }
        List<CodeInstruction> body = Decode(read);
        bool guarded;
        int count;
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType("Magicka.CommunityPatch.Runtime.NetworkSpawnHandlePatch", true);
            patch.GetMethod("FindReadMessage", Members).Invoke(null,
                new object[] { magicka, typeName, side });
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler", Members).Invoke(null,
                    new object[] { body }));
            count = CountCalls(body, "ResolveSpawnHandle", null);
            guarded = count >= (side == "client" ? 10 : 4);
        }
        else
        {
            count = CountCalls(body, "Resolve", "Magicka.CommunityPatch.NetworkEntityHandleGuard") +
                CountCalls(body, "ResolveActive", "Magicka.CommunityPatch.NetworkLifecycleCompatibility");
            guarded = count >= (side == "client" ? 10 : 4);
        }
        report.Add("network_spawn_handles." + side, new ScenarioResult(
            guarded, "guarded_calls:" + count,
            side == "client" ? "guarded_calls>=10" : "guarded_calls>=4"));
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
        DynamicMethod target = new DynamicMethod("ReadSpawnHandles", typeof(void),
            Type.EmptyTypes, typeof(NetworkSpawnHandleScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int CountCalls(IList<CodeInstruction> body, string name,
        string declaringType)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name == name &&
                (declaringType == null || method.DeclaringType.FullName == declaringType))
                count++;
        }
        return count;
    }
}
