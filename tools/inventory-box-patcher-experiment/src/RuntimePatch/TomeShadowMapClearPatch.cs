using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TomeShadowMapClearPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static MethodInfo colorOnlyClear;
        private static MethodInfo targetAndDepthClear;
        private static int clearOptions;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Tome shadow-map depth clear",
                "org.magickacommunitypatch.tome-shadow-depth-clear",
                FindDrawShadows,
                typeof(TomeShadowMapClearPatch).GetMethod("Transpiler"));

        private static MethodInfo FindDrawShadows(Assembly assembly)
        {
            Type tome = assembly.GetType("Magicka.GameLogic.UI.Tome", true);
            Type renderData = tome.GetNestedType(
                "RenderData",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (renderData == null)
                throw new TypeLoadException(tome.FullName + "+RenderData");

            Type device = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
            Type color = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.Color");
            Type options = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.ClearOptions");
            colorOnlyClear = RequireMethod(
                device,
                "Clear",
                new Type[] { color });
            targetAndDepthClear = RequireMethod(
                device,
                "Clear",
                new Type[] { options, color, typeof(float), typeof(int) });
            clearOptions = Convert.ToInt32(
                Enum.Parse(options, "Target, DepthBuffer"));

            MethodInfo draw = renderData.GetMethod(
                "DrawShadows",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            if (draw == null || draw.ReturnType != typeof(void))
                throw new MissingMethodException(
                    renderData.FullName,
                    "DrawShadows");
            return draw;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Callvirt ||
                    !Object.Equals(result[index].operand, colorOnlyClear))
                    continue;

                CodeInstruction options = new CodeInstruction(
                    OpCodes.Ldc_I4,
                    clearOptions);
                MoveEntryMetadata(result[index - 1], options);
                result.Insert(index - 1, options);
                int callIndex = index + 1;
                result.Insert(
                    callIndex++,
                    new CodeInstruction(OpCodes.Ldc_R4, 1f));
                result.Insert(
                    callIndex++,
                    new CodeInstruction(OpCodes.Ldc_I4_0));
                result[callIndex].operand = targetAndDepthClear;
                index = callIndex;
                replacements++;
            }
            if (replacements != 1)
                throw new InvalidOperationException(
                    "Expected one Tome shadow-map color clear, found " +
                    replacements + ".");
            return result;
        }

        private static void MoveEntryMetadata(
            CodeInstruction source,
            CodeInstruction destination)
        {
            destination.labels.AddRange(source.labels);
            destination.blocks.AddRange(source.blocks);
            source.labels.Clear();
            source.blocks.Clear();
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
