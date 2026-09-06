using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class DynamicLightCachePatch
    {
        private static FieldInfo lightCache;
        private static MethodInfo clearCache;
        private static MethodInfo dequeue;
        private static MethodInfo enqueue;
        private static MethodInfo disposeShadowMap;
        private static MethodInfo monitorEnter;
        private static MethodInfo monitorExit;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "DynamicLight cache release",
                "org.magickacommunitypatch.dynamic-light-cache",
                FindDisposeCache,
                typeof(DynamicLightCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindDisposeCache(Assembly targetAssembly)
        {
            Type dynamicLight = targetAssembly.GetType(
                "Magicka.Graphics.Lights.DynamicLight",
                true);
            lightCache = dynamicLight.GetField(
                "sLightCache",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (lightCache == null || !lightCache.FieldType.IsGenericType ||
                lightCache.FieldType.GetGenericTypeDefinition().FullName !=
                    "System.Collections.Generic.Queue`1" ||
                lightCache.FieldType.GetGenericArguments()[0] != dynamicLight)
                throw new MissingFieldException(
                    dynamicLight.FullName,
                    "sLightCache");

            clearCache = lightCache.FieldType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (clearCache == null || clearCache.ReturnType != typeof(void))
                throw new MissingMethodException(
                    lightCache.FieldType.FullName,
                    "Clear");

            dequeue = lightCache.FieldType.GetMethod(
                "Dequeue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            enqueue = lightCache.FieldType.GetMethod(
                "Enqueue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { dynamicLight },
                null);
            Type light = RuntimeMember.FindLoadedType(
                "PolygonHead.Lights.Light");
            disposeShadowMap = light.GetMethod(
                "DisposeShadowMap",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            monitorEnter = typeof(System.Threading.Monitor).GetMethod(
                "Enter",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(object) },
                null);
            monitorExit = typeof(System.Threading.Monitor).GetMethod(
                "Exit",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(object) },
                null);
            if (dequeue == null || dequeue.ReturnType != dynamicLight)
                throw new MissingMethodException(
                    lightCache.FieldType.FullName,
                    "Dequeue");
            if (enqueue == null || enqueue.ReturnType != typeof(void))
                throw new MissingMethodException(
                    lightCache.FieldType.FullName,
                    "Enqueue");
            if (disposeShadowMap == null ||
                disposeShadowMap.ReturnType != typeof(void))
                throw new MissingMethodException(
                    light.FullName,
                    "DisposeShadowMap");
            if (monitorEnter == null || monitorExit == null)
                throw new MissingMethodException(
                    typeof(System.Threading.Monitor).FullName,
                    "Enter/Exit");

            MethodInfo disposeCache = dynamicLight.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (disposeCache == null || disposeCache.ReturnType != typeof(void))
                throw new MissingMethodException(
                    dynamicLight.FullName,
                    "DisposeCache");
            return disposeCache;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            RequireSingleCall(result, dequeue);
            RequireSingleCall(result, enqueue);
            RequireSingleCall(result, disposeShadowMap);
            RequireSingleCall(result, monitorEnter);
            int monitorExitIndex = RequireSingleCall(result, monitorExit);

            int leave = -1;
            int leaveCount = 0;
            for (int index = 0; index < monitorExitIndex; index++)
            {
                if (result[index].opcode == OpCodes.Leave ||
                    result[index].opcode == OpCodes.Leave_S)
                {
                    leave = index;
                    leaveCount++;
                }
            }
            if (leaveCount != 1)
                throw new InvalidOperationException(
                    "Expected one DynamicLight.DisposeCache leave instruction " +
                    "before Monitor.Exit, found " + leaveCount + ".");

            CodeInstruction load = new CodeInstruction(
                OpCodes.Ldsfld,
                lightCache);
            load.labels.AddRange(result[leave].labels);
            result[leave].labels.Clear();
            result.Insert(leave, load);
            result.Insert(
                leave + 1,
                new CodeInstruction(OpCodes.Callvirt, clearCache));
            return result;
        }

        private static int RequireSingleCall(
            IList<CodeInstruction> instructions,
            MethodInfo expected)
        {
            int found = -1;
            int count = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(method, expected))
                {
                    found = index;
                    count++;
                }
            }
            if (count != 1)
                throw new InvalidOperationException(
                    "Expected one DynamicLight.DisposeCache " + expected.Name +
                    " call, found " + count + ".");
            return found;
        }
    }
}
