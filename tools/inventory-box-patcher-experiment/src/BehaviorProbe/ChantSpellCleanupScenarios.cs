using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ChantSpellCleanupScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ChantSpellCleanupHarness harness = new ChantSpellCleanupHarness(magicka);
        report.Add(
            "chant_spell_cleanup.uninitialized_dispose",
            harness.UninitializedDispose());
        report.Add(
            "chant_spell_cleanup.initialized_dispose",
            harness.InitializedDispose());
    }
}

internal sealed class ChantSpellCleanupHarness
{
    private readonly Type playStateType;
    private readonly Type chantSpellType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo add;
    private readonly MethodInfo getChantSpell;
    private readonly MethodInfo explicitClear;
    private readonly FieldInfo activeField;
    private readonly FieldInfo indexField;
    private readonly FieldInfo ownerField;
    private readonly int activeIndex;

    internal ChantSpellCleanupHarness(Assembly magicka)
    {
        Type managerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ChantSpellManager",
            true);
        chantSpellType = magicka.GetType(
            "Magicka.GameLogic.Entities.ChantSpells",
            true);
        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        Type avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        playStateDispose = RequireMethod(
            playStateType,
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            Type.EmptyTypes);
        add = RequireMethod(
            managerType,
            "Add",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { chantSpellType.MakeByRefType() });
        getChantSpell = RequireMethod(
            managerType,
            "GetChantSpell",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { typeof(int) });
        explicitClear = managerType.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        activeField = RequireField(chantSpellType, "Active", typeof(bool));
        indexField = RequireField(chantSpellType, "Index", typeof(int));
        ownerField = RequireField(chantSpellType, "Owner", characterType);

        object owner = NewUninitialized(avatarType);
        object spell = Activator.CreateInstance(chantSpellType);
        FieldInfo effectField = RuntimeReflection.RequireField(
            chantSpellType,
            "mEffect");
        object effect = Activator.CreateInstance(effectField.FieldType);
        RuntimeReflection.WriteField(effect, "ID", -1);
        effectField.SetValue(spell, effect);
        ownerField.SetValue(spell, owner);
        object[] arguments = new object[] { spell };
        Invoke(add, null, arguments);
        activeIndex = (int)indexField.GetValue(arguments[0]);
    }

    internal ScenarioResult UninitializedDispose()
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState, new object[0]);

        object spell = Invoke(getChantSpell, null, new object[] { activeIndex });
        bool active = (bool)activeField.GetValue(spell);
        bool ownerRetained = ownerField.GetValue(spell) != null;
        return new ScenarioResult(
            active && ownerRetained,
            "active:" + active + ",owner:" + (ownerRetained ? "retained" : "released"),
            "active:True,owner:retained");
    }

    internal ScenarioResult InitializedDispose()
    {
        if (explicitClear != null)
        {
            Invoke(explicitClear, null, new object[0]);
        }
        else
        {
            object playState = NewUninitialized(playStateType);
            RuntimeReflection.WriteField(playState, "mInitialized", true);
            try
            {
                Invoke(playStateDispose, playState, new object[0]);
            }
            catch (Exception)
            {
                // Chant cleanup runs before disposal reaches live game singletons.
            }
        }

        object spell = Invoke(getChantSpell, null, new object[] { activeIndex });
        bool active = (bool)activeField.GetValue(spell);
        bool ownerReleased = ownerField.GetValue(spell) == null;
        return new ScenarioResult(
            !active && ownerReleased,
            "active:" + active + ",owner:" + (ownerReleased ? "released" : "retained"),
            "active:False,owner:released");
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        BindingFlags flags,
        Type[] parameterTypes)
    {
        MethodInfo method = type.GetMethod(name, flags, null, parameterTypes, null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static FieldInfo RequireField(Type type, string name, Type fieldType)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != fieldType)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static object Invoke(MethodInfo method, object target, object[] arguments)
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
