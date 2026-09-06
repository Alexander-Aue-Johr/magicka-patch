using System;
using System.Reflection;

internal static class HelperArrayEqualsScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type helperType = magicka.GetType("Magicka.Helper", true);
        MethodInfo method = helperType.GetMethod(
            "ArrayEquals",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(byte[]), typeof(byte[]) },
            null);

        report.Add("helper_array_equals.equal", Invoke(method, new byte[] { 1, 2 }, new byte[] { 1, 2 }, true));
        report.Add("helper_array_equals.different", Invoke(method, new byte[] { 1, 2 }, new byte[] { 1, 3 }, false));
        report.Add("helper_array_equals.left_null", Invoke(method, null, new byte[] { 1 }, false));
        report.Add("helper_array_equals.right_null", Invoke(method, new byte[] { 1 }, null, false));
        report.Add("helper_array_equals.both_null", Invoke(method, null, null, false));
    }

    private static ScenarioResult Invoke(
        MethodInfo method,
        byte[] left,
        byte[] right,
        bool expected)
    {
        try
        {
            bool actual = (bool)method.Invoke(null, new object[] { left, right });
            return new ScenarioResult(actual == expected, actual.ToString(), expected.ToString());
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, expected.ToString());
        }
    }
}
