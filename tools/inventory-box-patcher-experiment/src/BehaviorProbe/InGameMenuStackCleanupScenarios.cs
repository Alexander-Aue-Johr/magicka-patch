using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class InGameMenuStackCleanupScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        InGameMenuStackCleanupHarness harness =
            new InGameMenuStackCleanupHarness(magicka, runtimePatchEnabled);
        report.Add(
            "in_game_menu_stack.initialized_dispose",
            harness.InitializedDispose());
        report.Add(
            "in_game_menu_stack.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class InGameMenuStackCleanupHarness
{
    private readonly Type playStateType;
    private readonly Type menuMainType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo push;
    private readonly MethodInfo clear;
    private readonly MethodInfo manualDispose;
    private readonly MethodInfo runtimeClear;
    private readonly ICollection menuStack;

    internal InGameMenuStackCleanupHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type menuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu",
            true);
        menuMainType = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMain",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        FieldInfo stackField = menuType.GetField(
            "sMenuStack",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (stackField == null ||
            !typeof(ICollection).IsAssignableFrom(stackField.FieldType))
            throw new MissingFieldException(menuType.FullName, "sMenuStack");

        menuStack = (ICollection)stackField.GetValue(null);
        push = stackField.FieldType.GetMethod(
            "Push",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { menuType },
            null);
        clear = stackField.FieldType.GetMethod(
            "Clear",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        manualDispose = menuType.GetMethod(
            "Dispose",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (runtimePatchEnabled)
        {
            Type cleanupType =
                typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.InGameMenuStackCleanupPatch",
                    false);
            runtimeClear = cleanupType == null
                ? null
                : cleanupType.GetMethod(
                    "ClearMenuStack",
                    BindingFlags.Static | BindingFlags.Public);
        }

        if (menuStack == null || push == null || clear == null ||
            playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMemberException(
                "InGameMenu stack cleanup behavior contract is incomplete.");
    }

    internal ScenarioResult InitializedDispose()
    {
        return WithPopulatedStack(delegate
        {
            if (manualDispose != null)
                Invoke(manualDispose, null);
            else if (runtimeClear != null)
                Invoke(runtimeClear, null);

            return Result(0);
        });
    }

    internal ScenarioResult UninitializedDispose()
    {
        return WithPopulatedStack(delegate
        {
            object playState = NewUninitialized(playStateType);
            RuntimeReflection.WriteField(playState, "mInitialized", false);
            Invoke(playStateDispose, playState);
            return Result(1);
        });
    }

    private ScenarioResult WithPopulatedStack(Func<ScenarioResult> scenario)
    {
        List<object> original = new List<object>();
        foreach (object entry in menuStack)
            original.Add(entry);

        try
        {
            Invoke(clear, menuStack);
            Invoke(push, menuStack, NewUninitialized(menuMainType));
            return scenario();
        }
        finally
        {
            Invoke(clear, menuStack);
            for (int index = original.Count - 1; index >= 0; index--)
                Invoke(push, menuStack, original[index]);
        }
    }

    private ScenarioResult Result(int expectedCount)
    {
        return new ScenarioResult(
            menuStack.Count == expectedCount,
            "count:" + menuStack.Count,
            "count:" + expectedCount);
    }

    private static object Invoke(MethodInfo method, object target)
    {
        return Invoke(method, target, new object[0]);
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        params object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
