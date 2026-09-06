using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class MachineScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        MachineHarness harness = new MachineHarness(magicka);
        report.Add("machine.missing_warlock", harness.InitialiseWithMissingWarlock());
        report.Add("machine.valid_warlock", harness.InitialiseWithValidWarlock());
        report.Add("machine.other_message", harness.IgnoreOtherMessageType());
    }
}

internal sealed class MachineHarness
{
    private readonly Type machineType;
    private readonly Type messageType;
    private readonly Type entityType;
    private readonly Type nonPlayerCharacterType;
    private readonly MethodInfo networkInitialise;
    private readonly IList entities;

    internal MachineHarness(Assembly magicka)
    {
        machineType = magicka.GetType("Magicka.GameLogic.Entities.Bosses.Machine", true);
        messageType = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.BossInitializeMessage",
            true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        networkInitialise = FindNetworkInitialise();

        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        entities = (IList)RuntimeReflection.RequireField(entityType, "mInstances").GetValue(null);
    }

    internal ScenarioResult InitialiseWithMissingWarlock()
    {
        return WithEntityList(false, delegate
        {
            object machine = NewMachine(false);
            Invoke(machine, NewMessage(0));
            return StateResult(machine, false);
        });
    }

    internal ScenarioResult InitialiseWithValidWarlock()
    {
        return WithEntityList(true, delegate
        {
            object machine = NewMachine(false);
            Invoke(machine, NewMessage(0));
            return StateResult(machine, true);
        });
    }

    internal ScenarioResult IgnoreOtherMessageType()
    {
        object machine = NewMachine(true);
        Invoke(machine, NewMessage(1));
        return StateResult(machine, true);
    }

    private ScenarioResult WithEntityList(bool includeWarlock, Func<ScenarioResult> scenario)
    {
        object[] previous = new object[entities.Count];
        entities.CopyTo(previous, 0);
        try
        {
            entities.Clear();
            if (includeWarlock)
            {
                entities.Add(FormatterServices.GetUninitializedObject(nonPlayerCharacterType));
            }
            return scenario();
        }
        finally
        {
            entities.Clear();
            for (int index = 0; index < previous.Length; index++)
            {
                entities.Add(previous[index]);
            }
        }
    }

    private object NewMachine(bool networkInitialised)
    {
        object machine = FormatterServices.GetUninitializedObject(machineType);
        RuntimeReflection.WriteField(machine, "mNetworkInitialized", networkInitialised);
        return machine;
    }

    private object NewMessage(ushort type)
    {
        object message = Activator.CreateInstance(messageType);
        RuntimeReflection.WriteField(message, "Type", type);
        RuntimeReflection.WriteField(message, "Length", (ushort)0);
        return message;
    }

    private void Invoke(object machine, object message)
    {
        networkInitialise.Invoke(machine, new object[] { message });
    }

    private ScenarioResult StateResult(object machine, bool expected)
    {
        bool actual = (bool)RuntimeReflection.ReadField(machine, "mNetworkInitialized");
        return new ScenarioResult(actual == expected, actual.ToString(), expected.ToString());
    }

    private MethodInfo FindNetworkInitialise()
    {
        MethodInfo[] methods = machineType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo method = methods[index];
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == "NetworkInitialize" &&
                method.ReturnType == typeof(void) &&
                parameters.Length == 1 &&
                parameters[0].ParameterType.IsByRef &&
                parameters[0].ParameterType.GetElementType() == messageType)
            {
                return method;
            }
        }
        throw new MissingMethodException(machineType.FullName, "NetworkInitialize");
    }
}
