using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AnimationClipCompatibilityScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        AnimationClipCompatibilityHarness harness =
            new AnimationClipCompatibilityHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("animation_clip.lookup_missing", harness.LookupMissing());
        report.Add("animation_clip.lookup_present", harness.LookupPresent());
        report.Add("animation_clip.invalid_slot", harness.InvalidSlot());
        report.Add("animation_clip.valid_slot", harness.ValidSlot());
        report.Add("animation_clip.null_set", harness.NullSet());
        report.Add("animation_clip.out_of_range", harness.OutOfRange());
        report.Add("animation_clip.missing_idle", harness.MissingIdle());
    }
}

internal sealed class AnimationClipCompatibilityHarness
{
    private readonly Type actionType;
    private readonly Type clipType;
    private readonly Type dictionaryType;
    private readonly FieldInfo clipField;
    private readonly Type manualCompatibilityType;
    private readonly MethodInfo manualSafeLookup;
    private readonly bool runtimePatchEnabled;

    internal AnimationClipCompatibilityHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        actionType = magicka.GetType(
            "Magicka.GameLogic.Entities.AnimationClipAction",
            true);
        ConstructorInfo constructor = FindContentConstructor(actionType);
        dictionaryType = constructor.GetParameters()[2].ParameterType;
        clipType = actionType.GetProperty("Clip").PropertyType;
        clipField = actionType.GetField(
            "mClip",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (clipField == null)
            throw new MissingFieldException(actionType.FullName, "mClip");
        manualCompatibilityType = magicka.GetType(
            "Magicka.CommunityPatch.AnimationClipCompatibility",
            false);
        if (manualCompatibilityType != null)
        {
            manualSafeLookup = manualCompatibilityType.GetMethod(
                "TryGetAnimationAction",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(Array), typeof(int), typeof(int) },
                null);
        }
    }

    internal ScenarioResult LookupMissing()
    {
        object dictionary = NewDictionary();
        try
        {
            object value = Lookup(dictionary, "missing");
            string actual = value == null ? "null" : "value";
            return Result(actual == "null", actual, "null");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return Result(false, inner.GetType().Name, "null");
        }
        catch (Exception exception)
        {
            return Result(false, exception.GetType().Name, "null");
        }
    }

    internal ScenarioResult LookupPresent()
    {
        object dictionary = NewDictionary();
        object clip = NewUninitialized(clipType);
        AddDictionaryValue(dictionary, "idle", clip);
        object value = Lookup(dictionary, "idle");
        string actual = Object.ReferenceEquals(value, clip)
            ? "same"
            : "other";
        return Result(actual == "same", actual, "same");
    }

    internal ScenarioResult InvalidSlot()
    {
        Array actions = Array.CreateInstance(actionType, 1);
        object action = NewAction(null);
        Store(actions, 0, action);
        string actual = actions.GetValue(0) == null ? "empty" : "stored";
        return Result(actual == "empty", actual, "empty");
    }

    internal ScenarioResult ValidSlot()
    {
        Array actions = Array.CreateInstance(actionType, 1);
        object action = NewAction(NewUninitialized(clipType));
        Store(actions, 0, action);
        string actual = Object.ReferenceEquals(actions.GetValue(0), action)
            ? "stored"
            : "empty";
        return Result(actual == "stored", actual, "stored");
    }

    internal ScenarioResult NullSet()
    {
        Array sets = Array.CreateInstance(actionType.MakeArrayType(), 2);
        return SafeLookupResult(sets, 0, 1, "null");
    }

    internal ScenarioResult OutOfRange()
    {
        Array sets = Array.CreateInstance(actionType.MakeArrayType(), 1);
        sets.SetValue(Array.CreateInstance(actionType, 2), 0);
        return SafeLookupResult(sets, 0, 5, "null");
    }

    internal ScenarioResult MissingIdle()
    {
        Array sets = Array.CreateInstance(actionType.MakeArrayType(), 27);
        sets.SetValue(Array.CreateInstance(actionType, 231), 0);
        try
        {
            object action;
            if (runtimePatchEnabled)
            {
                action = Magicka.CommunityPatch.Runtime
                    .AnimationClipCompatibilityPatch.TryGetAnimationAction(
                        sets,
                        0,
                        1);
            }
            else if (manualSafeLookup != null)
            {
                action = manualSafeLookup.Invoke(
                    null,
                    new object[] { sets, 0, 1 });
            }
            else
            {
                action = ((Array)sets.GetValue(0)).GetValue(1);
                clipField.GetValue(action);
            }
            string actual = action == null ? "skipped" : "used";
            return Result(actual == "skipped", actual, "skipped");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return Result(false, inner.GetType().Name, "skipped");
        }
        catch (Exception exception)
        {
            return Result(false, exception.GetType().Name, "skipped");
        }
    }

    private object Lookup(object dictionary, string key)
    {
        if (!runtimePatchEnabled && manualCompatibilityType == null)
            return GetDictionaryItem(dictionary, key);
        if (!runtimePatchEnabled)
            return TryGetDictionaryValue(dictionary, key);

        MethodInfo helper = typeof(Magicka.CommunityPatch.Runtime
            .AnimationClipCompatibilityPatch).GetMethod(
                "GetValueOrDefault")
            .MakeGenericMethod(dictionaryType, clipType);
        return helper.Invoke(null, new object[] { dictionary, key });
    }

    private void Store(Array actions, int index, object action)
    {
        if (!runtimePatchEnabled && manualCompatibilityType == null)
        {
            actions.SetValue(action, index);
            return;
        }
        if (!runtimePatchEnabled)
        {
            if (clipField.GetValue(action) != null)
                actions.SetValue(action, index);
            return;
        }
        MethodInfo helper = typeof(Magicka.CommunityPatch.Runtime
            .AnimationClipCompatibilityPatch).GetMethod("StoreIfUsable")
            .MakeGenericMethod(actionType);
        helper.Invoke(null, new object[] { actions, index, action });
    }

    private ScenarioResult SafeLookupResult(
        Array sets,
        int set,
        int animation,
        string expected)
    {
        try
        {
            object value;
            if (runtimePatchEnabled)
            {
                value = Magicka.CommunityPatch.Runtime
                    .AnimationClipCompatibilityPatch.TryGetAnimationAction(
                        sets,
                        set,
                        animation);
            }
            else if (manualSafeLookup != null)
            {
                value = manualSafeLookup.Invoke(
                    null,
                    new object[] { sets, set, animation });
            }
            else
            {
                Array inner = (Array)sets.GetValue(set);
                value = inner.GetValue(animation);
            }
            string actual = value == null ? "null" : "value";
            return Result(actual == expected, actual, expected);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return Result(false, inner.GetType().Name, expected);
        }
        catch (Exception exception)
        {
            return Result(false, exception.GetType().Name, expected);
        }
    }

    private object NewAction(object clip)
    {
        object action = NewUninitialized(actionType);
        clipField.SetValue(action, clip);
        return action;
    }

    private object NewDictionary()
    {
        Type backingType = typeof(Dictionary<,>).MakeGenericType(
            typeof(string),
            clipType);
        object backing = Activator.CreateInstance(backingType);
        ConstructorInfo constructor = dictionaryType.GetConstructor(
            new Type[]
            {
                typeof(IDictionary<,>).MakeGenericType(
                    typeof(string),
                    clipType)
            });
        if (constructor == null)
            throw new MissingMethodException(dictionaryType.FullName, ".ctor");
        return constructor.Invoke(new object[] { backing });
    }

    private void AddDictionaryValue(object dictionary, string key, object value)
    {
        FieldInfo items = dictionaryType.BaseType.GetField(
            "items",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (items == null)
            throw new MissingFieldException(dictionaryType.BaseType.FullName, "items");
        object backing = items.GetValue(dictionary);
        backing.GetType().GetMethod("Add").Invoke(
            backing,
            new object[] { key, value });
    }

    private object GetDictionaryItem(object dictionary, string key)
    {
        return dictionaryType.GetProperty("Item").GetValue(
            dictionary,
            new object[] { key });
    }

    private object TryGetDictionaryValue(object dictionary, string key)
    {
        object[] arguments = new object[] { key, null };
        bool found = (bool)dictionaryType.GetMethod("TryGetValue").Invoke(
            dictionary,
            arguments);
        return found ? arguments[1] : null;
    }

    private static object NewUninitialized(Type type)
    {
        return FormatterServices.GetUninitializedObject(type);
    }

    private static ConstructorInfo FindContentConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        for (int index = 0; index < constructors.Length; index++)
        {
            ParameterInfo[] parameters = constructors[index].GetParameters();
            if (parameters.Length == 4 &&
                parameters[1].ParameterType.FullName ==
                    "Microsoft.Xna.Framework.Content.ContentReader")
                return constructors[index];
        }
        throw new MissingMethodException(type.FullName, ".ctor");
    }

    private static ScenarioResult Result(
        bool passed,
        string actual,
        string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
