using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ParadoxStorePriceUpdatePatch
    {
        private static MethodInfo updateMethod;
        private static int pending;

        [ThreadStatic]
        private static bool worker;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Asynchronous Paradox store price refresh",
                "org.magickacommunitypatch.paradox-store-price-refresh",
                FindTarget,
                typeof(ParadoxStorePriceUpdatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.CoreFramework.GameSystem.Store.StoreItemDatabase",
                true);
            updateMethod = type.GetMethod(
                "UpdateParadoxItems",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (updateMethod == null || updateMethod.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "UpdateParadoxItems");
            return updateMethod;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int boundary = -1;
            int accountUrl = -1;
            int offersUrl = -1;
            for (int index = 0; index < result.Count; index++)
            {
                string text = result[index].operand as string;
                if (text == "https://api.paradoxplaza.com/bertil/steamwallet/userinfo")
                    accountUrl = index;
                if (text == "http://services.paradoxplaza.com/adam/offers/red_wizard")
                {
                    offersUrl = index;
                    result[index].operand =
                        "https://services.paradoxplaza.com/adam/offers/red_wizard";
                }
                MethodInfo call = result[index].operand as MethodInfo;
                if (accountUrl >= 0 && call != null && call.IsStatic &&
                    call.Name == "get_Instance" && call.ReturnType.FullName ==
                        "Magicka.WebTools.ParadoxAccount")
                {
                    boundary = index;
                    break;
                }
            }
            if (accountUrl < 0 || offersUrl < 0 || boundary < 0)
                throw new InvalidOperationException(
                    "The Paradox price-refresh block changed shape.");

            Label normalPath = generator.DefineLabel();
            result[boundary].labels.Add(normalPath);
            result.Insert(boundary, new CodeInstruction(OpCodes.Ret));
            result.Insert(
                boundary,
                new CodeInstruction(OpCodes.Brfalse, normalPath));
            result.Insert(
                boundary,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(ParadoxStorePriceUpdatePatch).GetMethod("IsWorker")));

            CodeInstruction first = result[0];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(first.labels);
            loadThis.blocks.AddRange(first.blocks);
            first.labels.Clear();
            first.blocks.Clear();
            result.Insert(0, loadThis);
            result.Insert(
                1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(ParadoxStorePriceUpdatePatch).GetMethod("QueueOrRun")));
            result.Insert(2, new CodeInstruction(OpCodes.Brfalse, normalPath));
            return result;
        }

        public static bool QueueOrRun(object instance)
        {
            if (worker)
                return true;
            if (instance != null &&
                Interlocked.CompareExchange(ref pending, 1, 0) == 0 &&
                !ThreadPool.QueueUserWorkItem(Run, instance))
                Interlocked.Exchange(ref pending, 0);
            return false;
        }

        public static bool IsWorker()
        {
            return worker;
        }

        private static void Run(object instance)
        {
            worker = true;
            try
            {
                updateMethod.Invoke(instance, null);
            }
            catch
            {
            }
            finally
            {
                worker = false;
                Interlocked.Exchange(ref pending, 0);
            }
        }
    }
}
