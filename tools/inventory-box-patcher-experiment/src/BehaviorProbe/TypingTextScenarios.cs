using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class TypingTextScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        TypingTextHarness harness = new TypingTextHarness(magicka);
        report.Add("typing_text.normal_character", harness.NormalCharacter());
        report.Add("typing_text.punctuation", harness.Punctuation());
        report.Add("typing_text.pause_markup", harness.PauseMarkup());
        report.Add("typing_text.truncated_plain", harness.TruncatedPlain());
        report.Add("typing_text.truncated_markup", harness.TruncatedMarkup());
        report.Add("typing_text.empty", harness.Empty());
        report.Add("typing_text.syntax_error", harness.SyntaxError());
    }
}

internal sealed class TypingTextHarness
{
    private readonly Type type;
    private readonly MethodInfo update;
    private readonly FieldInfo nextChar;
    private readonly FieldInfo visibleCharacters;
    private readonly FieldInfo charIndex;
    private readonly FieldInfo typeSpeed;
    private readonly FieldInfo text;
    private readonly FieldInfo primitiveCount;

    internal TypingTextHarness(Assembly magicka)
    {
        type = magicka.GetType("Magicka.Graphics.TypingText", true);
        update = type.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(float) },
            null);
        if (update == null)
            throw new MissingMethodException(type.FullName, "Update");
        nextChar = RequireField(type, "mNextChar");
        visibleCharacters = RequireField(type, "mVisibleCharacters");
        charIndex = RequireField(type, "mCharIndex");
        typeSpeed = RequireField(type, "mTypeSpeed");
        text = RequireField(type, "mText");
        primitiveCount = RequireField(type, "mPrimitiveCount");
    }

    internal ScenarioResult NormalCharacter()
    {
        return Run("a", 2, 0.1f, "visible:1,index:0,next:0.9");
    }

    internal ScenarioResult Punctuation()
    {
        return Run("! ", 2, 0.1f, "visible:1,index:0,next:0.15");
    }

    internal ScenarioResult PauseMarkup()
    {
        return Run("[p=2]a", 2, 0.1f, "visible:0,index:4,next:1.9");
    }

    internal ScenarioResult TruncatedPlain()
    {
        return Run("a", 4, 1.1f, "visible:2,index:0,next:Infinity");
    }

    internal ScenarioResult TruncatedMarkup()
    {
        return Run("[p=2", 2, 0.1f, "visible:1,index:3,next:Infinity");
    }

    internal ScenarioResult Empty()
    {
        return Run("", 2, 0.1f, "visible:1,index:-1,next:Infinity");
    }

    internal ScenarioResult SyntaxError()
    {
        object instance = Create("[p?]", 2);
        try
        {
            Invoke(instance, 0.1f);
            return Result(false, "completed", "Exception");
        }
        catch (Exception exception)
        {
            return Result(
                exception.GetType() == typeof(Exception),
                exception.GetType().Name,
                "Exception");
        }
    }

    private ScenarioResult Run(
        string value,
        int primitives,
        float delta,
        string expected)
    {
        object instance = Create(value, primitives);
        string actual;
        try
        {
            Invoke(instance, delta);
            actual = "visible:" + visibleCharacters.GetValue(instance) +
                ",index:" + charIndex.GetValue(instance) +
                ",next:" + Format((float)nextChar.GetValue(instance));
        }
        catch (Exception exception)
        {
            actual = "exception:" + exception.GetType().Name;
        }
        return Result(actual == expected, actual, expected);
    }

    private object Create(string value, int primitives)
    {
        object instance = FormatterServices.GetUninitializedObject(type);
        nextChar.SetValue(instance, 0f);
        visibleCharacters.SetValue(instance, 0);
        charIndex.SetValue(instance, -1);
        typeSpeed.SetValue(instance, 1f);
        text.SetValue(instance, value.ToCharArray());
        primitiveCount.SetValue(instance, primitives);
        return instance;
    }

    private void Invoke(object instance, float delta)
    {
        try
        {
            update.Invoke(instance, new object[] { delta });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private FieldInfo RequireField(Type start, string name)
    {
        for (Type current = start; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(start.FullName, name);
    }

    private static string Format(float value)
    {
        if (Single.IsPositiveInfinity(value))
            return "Infinity";
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ScenarioResult Result(
        bool passed,
        string actual,
        string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
