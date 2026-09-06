using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class DeflectionAuraScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        DeflectionAuraHarness harness = new DeflectionAuraHarness(magicka);
        report.Add(
            "deflection_aura.play_state_release",
            harness.PlayStateRelease());
        report.Add(
            "deflection_aura.execute_behavior",
            harness.ExecuteBehavior());
    }
}

internal sealed class DeflectionAuraHarness
{
    private readonly Type auraType;
    private readonly Type avatarType;
    private readonly Type bodyType;
    private readonly Type playStateType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo sphereField;

    internal DeflectionAuraHarness(Assembly magicka)
    {
        auraType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DeflectionAura",
            true);
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = auraType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { ownerType, playStateType },
            null);
        if (execute == null || execute.ReturnType != typeof(bool))
            throw new MissingMethodException(auraType.FullName, "Execute");

        ownerField = RuntimeReflection.RequireField(auraType, "mOwner");
        playStateField = auraType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        sphereField = RuntimeReflection.RequireField(auraType, "mSphere");
        InstallDependencyStubs(magicka);
    }

    internal ScenarioResult PlayStateRelease()
    {
        DeflectionAuraExecution execution = CreateExecution();
        bool result = Invoke(execution);
        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(execution.Effect), execution.PlayState);
        return new ScenarioResult(
            result && !retained,
            "result:" + result + ",state:" + (retained ? "retained" : "released"),
            "result:True,state:released");
    }

    internal ScenarioResult ExecuteBehavior()
    {
        DeflectionAuraExecution execution = CreateExecution();
        DeflectionAuraProbe.Reset();
        bool result = Invoke(execution);
        bool owner = ReferenceEquals(ownerField.GetValue(execution.Effect), execution.Owner);
        object sphere = sphereField.GetValue(execution.Effect);
        float radius = Convert.ToSingle(RuntimeReflection.ReadField(sphere, "Radius"));
        object center = RuntimeReflection.ReadField(sphere, "Center");
        float centerX = Convert.ToSingle(RuntimeReflection.ReadField(center, "X"));
        bool passed = result && owner && Math.Abs(radius - 5f) < 0.0001f &&
            Math.Abs(centerX - 4f) < 0.0001f &&
            DeflectionAuraProbe.AddAuraCalls == 1;
        string actual = "result:" + result + ",owner:" + owner +
            ",radius:" + radius + ",center_x:" + centerX +
            ",add_aura:" + DeflectionAuraProbe.AddAuraCalls;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,radius:5,center_x:4,add_aura:1");
    }

    private DeflectionAuraExecution CreateExecution()
    {
        object effect = NewUninitialized(auraType);
        object owner = NewUninitialized(avatarType);
        object body = NewUninitialized(bodyType);
        object position = Activator.CreateInstance(vectorType);
        vectorType.GetField("X").SetValue(position, 4f);
        bodyType.GetProperty("Position").SetValue(body, position, null);
        RuntimeReflection.WriteField(owner, "mBody", body);
        object playState = NewUninitialized(playStateType);
        return new DeflectionAuraExecution(effect, owner, playState);
    }

    private bool Invoke(DeflectionAuraExecution execution)
    {
        try
        {
            return (bool)execute.Invoke(
                execution.Effect,
                new object[] { execution.Owner, execution.PlayState });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private void InstallDependencyStubs(Assembly magicka)
    {
        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        MethodInfo addAura = null;
        MethodInfo[] methods = characterType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "AddAura" &&
                (methods[index].GetParameters().Length == 2 ||
                    methods[index].GetParameters().Length == 3))
            {
                if (addAura != null)
                    throw new InvalidOperationException(
                        "Multiple Character.AddAura overloads matched.");
                addAura = methods[index];
            }
        }
        if (addAura == null)
            throw new MissingMethodException(
                "DeflectionAura test dependencies are incomplete.");

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-deflection-aura");
        harmony.Patch(
            addAura,
            new HarmonyMethod(typeof(DeflectionAuraProbe).GetMethod("AddAuraPrefix")),
            null,
            null);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

internal sealed class DeflectionAuraExecution
{
    internal object Effect { get; private set; }
    internal object Owner { get; private set; }
    internal object PlayState { get; private set; }

    internal DeflectionAuraExecution(object effect, object owner, object playState)
    {
        Effect = effect;
        Owner = owner;
        PlayState = playState;
    }
}

public static class DeflectionAuraProbe
{
    public static int AddAuraCalls;

    public static void Reset()
    {
        AddAuraCalls = 0;
    }

    public static bool AddAuraPrefix()
    {
        AddAuraCalls++;
        return false;
    }
}
