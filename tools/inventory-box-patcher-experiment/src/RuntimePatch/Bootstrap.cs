using System;
using System.Reflection;
using System.Threading;

namespace Magicka.InventoryBoxRuntimePatch
{
    public static class Bootstrap
    {
        private static int applied;

        public static void Apply()
        {
            Apply(Assembly.GetEntryAssembly());
        }

        public static void Apply(Assembly targetAssembly)
        {
            if (Interlocked.CompareExchange(ref applied, 1, 0) != 0)
                return;

            try
            {
                RuntimePatchPlan.ApplyTo(targetAssembly);
            }
            catch (Exception exception)
            {
                RuntimePatchAudit.WriteFailure(targetAssembly, exception);
                throw;
            }
        }
    }
}
