using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class BossHealthBarTargetMethods
    {
        private const string HealthBarTypeName = "Magicka.GameLogic.UI.BossHealthBar";

        internal static ConstructorInfo FindConstructorIn(Assembly targetAssembly)
        {
            BossHealthBarScenePatch.Configure(targetAssembly);
            Type healthBarType = targetAssembly.GetType(HealthBarTypeName, true);
            ConstructorInfo[] constructors = healthBarType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public);
            if (constructors.Length != 1 || constructors[0].GetParameters().Length != 1)
                throw new InvalidOperationException(
                    "Expected one BossHealthBar constructor with one argument.");
            return constructors[0];
        }

        internal static MethodInfo FindSceneGetterIn(Assembly targetAssembly)
        {
            BossHealthBarScenePatch.Configure(targetAssembly);
            return FindSceneProperty(targetAssembly).GetGetMethod();
        }

        internal static MethodInfo FindSceneSetterIn(Assembly targetAssembly)
        {
            BossHealthBarScenePatch.Configure(targetAssembly);
            return FindSceneProperty(targetAssembly).GetSetMethod();
        }

        private static PropertyInfo FindSceneProperty(Assembly targetAssembly)
        {
            Type healthBarType = targetAssembly.GetType(HealthBarTypeName, true);
            PropertyInfo property = healthBarType.GetProperty(
                "Scene",
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite)
                throw new MissingMemberException(
                    "BossHealthBar.Scene property is incomplete.");
            return property;
        }
    }
}
