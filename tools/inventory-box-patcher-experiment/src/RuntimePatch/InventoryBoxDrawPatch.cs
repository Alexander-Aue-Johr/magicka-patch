using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class InventoryBoxDrawPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "InventoryBox screen size",
                "org.magickacommunitypatch.inventory-box-screen-size-experiment",
                InventoryBoxTargetMethods.FindIn,
                target => typeof(InventoryBoxDrawPatch).GetMethod("Prefix"));

        public static void Prefix(object __instance)
        {
            object textBoxEffect = RuntimeMember.ReadField(__instance, "mTextBoxEffect");
            object screenSize = ReadCurrentScreenSize();
            float width = Convert.ToSingle(RuntimeMember.ReadField(screenSize, "X"));
            float height = Convert.ToSingle(RuntimeMember.ReadField(screenSize, "Y"));
            Type vectorType = textBoxEffect.GetType().GetProperty("ScreenSize").PropertyType;
            object effectScreenSize = Activator.CreateInstance(vectorType, new object[] { width, height });

            RuntimeMember.WriteProperty(textBoxEffect, "ScreenSize", effectScreenSize);
        }

        private static object ReadCurrentScreenSize()
        {
            Type renderManagerType = RuntimeMember.FindLoadedType("PolygonHead.RenderManager");
            object renderManager = renderManagerType.GetProperty("Instance").GetValue(null, null);
            return RuntimeMember.ReadProperty(renderManager, "ScreenSize");
        }
    }
}
