using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class InGameMenuScaleScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic;

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The legacy executable predates selectable UI scaling.";
            report.AddNotApplicable("in_game_menu_scale.layout_hooks", reason);
            report.AddNotApplicable("in_game_menu_scale.mouse_hooks", reason);
            report.AddNotApplicable("in_game_menu_scale.active_size", reason);
            report.AddNotApplicable("in_game_menu_scale.inactive_size", reason);
            return;
        }
        Harness harness = new Harness(magicka, runtimePatchEnabled);
        report.Add("in_game_menu_scale.layout_hooks", harness.LayoutHooks());
        report.Add("in_game_menu_scale.mouse_hooks", harness.MouseHooks());
        report.Add("in_game_menu_scale.active_size", harness.Size(true));
        report.Add("in_game_menu_scale.inactive_size", harness.Size(false));
    }

    private sealed class Harness
    {
        private readonly Assembly magicka;
        private readonly bool runtime;
        private readonly Type helper;
        private readonly Type pointType;
        private readonly Type scaleType;

        internal Harness(Assembly magicka, bool runtime)
        {
            this.magicka = magicka;
            this.runtime = runtime;
            helper = runtime
                ? typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.InGameMenuScalePatch", true)
                : magicka.GetType(
                    "Magicka.CommunityPatch.InGameUiCompatibility", false);
            pointType = RuntimeReflection.FindLoadedType(
                "Microsoft.Xna.Framework.Point");
            scaleType = Type.GetType(
                "PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead",
                false);
        }

        internal ScenarioResult LayoutHooks()
        {
            int count = 0;
            Type menu = magicka.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu", true);
            ConstructorInfo constructor = menu.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (runtime)
            {
                MethodInfo transpiler = helper.GetMethod("SizeTranspiler");
                MethodBase[] targets =
                {
                    constructor,
                    menu.GetMethod("Show", Members),
                    menu.GetMethod("UpdateAllPositions", Members)
                };
                for (int index = 0; index < targets.Length; index++)
                {
                    object transformed = transpiler.Invoke(
                        null, new object[] { Decode(targets[index]) });
                    count += CountCalls(
                        (IEnumerable<CodeInstruction>)transformed,
                        "AdjustMenuSize");
                }
                return Result(count == 3, count, 3);
            }
            count += CountCalls(constructor, "AdjustMenuSize");
            count += CountCalls(menu.GetMethod("Show", Members),
                "AdjustMenuSize");
            count += CountCalls(menu.GetMethod("UpdateAllPositions", Members),
                "AdjustMenuSize");
            return Result(count == 3, count, 3);
        }

        internal ScenarioResult MouseHooks()
        {
            int count = 0;
            if (runtime)
            {
                count += CountCalls(helper.GetMethod("MousePrefix"),
                    "AdjustMenuMouse");
                count += CountCalls(helper.GetMethod("MouseScrollPrefix"),
                    "AdjustMenuMouse");
                return Result(count == 2, count, 2);
            }
            Type menu = magicka.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu", true);
            string[] names = { "MouseScroll", "MouseMove", "MouseDown", "MouseUp" };
            for (int index = 0; index < names.Length; index++)
                count += CountCalls(menu.GetMethod(names[index], Members),
                    "AdjustMenuMouse");
            return Result(count == 4, count, 4);
        }

        internal ScenarioResult Size(bool enabled)
        {
            if (helper == null || scaleType == null)
                return new ScenarioResult(false, "helper_missing", "1920x1080");
            MethodInfo setEnabled = scaleType.GetMethod("SetEnabled", Members);
            MethodInfo setScale = scaleType.GetMethod("SetScale", Members);
            MethodInfo adjust = helper.GetMethod("AdjustMenuSize", Members);
            if (runtime)
                adjust = adjust.MakeGenericMethod(pointType);
            object point = Activator.CreateInstance(pointType);
            pointType.GetField("X").SetValue(point, 3840);
            pointType.GetField("Y").SetValue(point, 2160);
            setScale.Invoke(null, new object[] { 2f });
            setEnabled.Invoke(null, new object[] { enabled });
            object[] arguments = { point };
            adjust.Invoke(null, arguments);
            point = arguments[0];
            setEnabled.Invoke(null, new object[] { false });
            int width = (int)pointType.GetField("X").GetValue(point);
            int height = (int)pointType.GetField("Y").GetValue(point);
            int expectedWidth = enabled ? 1920 : 3840;
            int expectedHeight = enabled ? 1080 : 2160;
            return new ScenarioResult(
                width == expectedWidth && height == expectedHeight,
                width + "x" + height,
                expectedWidth + "x" + expectedHeight);
        }

        private static int CountCalls(MethodBase method, string name)
        {
            if (method == null)
                return 0;
            return CountCalls(Decode(method), name);
        }

        private static List<CodeInstruction> Decode(MethodBase method)
        {
            DynamicMethod target = new DynamicMethod(
                "ReadInGameMenuScaleBody",
                typeof(void),
                Type.EmptyTypes,
                typeof(InGameMenuScaleScenarios),
                true);
            List<ILInstruction> body = MethodBodyReader.GetInstructions(
                target.GetILGenerator(), method);
            List<CodeInstruction> result =
                new List<CodeInstruction>(body.Count);
            for (int index = 0; index < body.Count; index++)
                result.Add(body[index].GetCodeInstruction());
            return result;
        }

        private static int CountCalls(
            IEnumerable<CodeInstruction> body,
            string name)
        {
            int count = 0;
            foreach (CodeInstruction instruction in body)
            {
                MethodBase called = instruction.operand as MethodBase;
                if (called != null && called.Name == name)
                    count++;
            }
            return count;
        }

        private static ScenarioResult Result(bool passed, int actual, int expected)
        {
            return new ScenarioResult(passed, actual.ToString(), expected.ToString());
        }
    }
}
