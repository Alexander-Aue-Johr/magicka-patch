using System;
using System.Collections;
using System.Reflection;

internal static class PhysicsManagerClearScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        report.Add("physics_clear.retained_collision_state", Populated(magicka));
        report.Add("physics_clear.repeated", Repeated(magicka));
        report.Add("physics_clear.missing_simulator", MissingSimulator(magicka));
    }

    private static ScenarioResult Populated(Assembly magicka)
    {
        try
        {
            Fixture fixture = new Fixture(magicka);
            fixture.Populate();
            fixture.Clear.Invoke(fixture.Manager, null);
            bool passed = fixture.Bodies.Count == 0 &&
                fixture.Skins.Count == 0 && fixture.Collisions.Count == 0 &&
                fixture.Island.Count == 0 && fixture.Islands.Count == 0;
            return Result(passed,
                fixture.Bodies.Count + "/" + fixture.Skins.Count + "/" +
                fixture.Collisions.Count + "/" + fixture.Island.Count + "/" +
                fixture.Islands.Count,
                "0/0/0/0/0");
        }
        catch (TargetInvocationException exception)
        {
            return Result(false, exception.InnerException.GetType().Name, "0/0/0/0/0");
        }
    }

    private static ScenarioResult Repeated(Assembly magicka)
    {
        try
        {
            Fixture fixture = new Fixture(magicka);
            fixture.Clear.Invoke(fixture.Manager, null);
            fixture.Clear.Invoke(fixture.Manager, null);
            return Result(true, "completed", "completed");
        }
        catch (TargetInvocationException exception)
        {
            return Result(false, exception.InnerException.GetType().Name, "completed");
        }
    }

    private static ScenarioResult MissingSimulator(Assembly magicka)
    {
        try
        {
            Fixture fixture = new Fixture(magicka);
            fixture.SimulatorField.SetValue(fixture.Manager, null);
            fixture.Clear.Invoke(fixture.Manager, null);
            return Result(true, "completed", "completed");
        }
        catch (TargetInvocationException exception)
        {
            return Result(false, exception.InnerException.GetType().Name, "completed");
        }
    }

    private static ScenarioResult Result(bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }

    private sealed class Fixture
    {
        private readonly object simulator;
        private readonly Type bodyType;
        private readonly Type skinType;
        private readonly Type collisionInfoType;
        private readonly Type islandType;
        private readonly object collisionSystem;

        internal object Manager { get; private set; }
        internal FieldInfo SimulatorField { get; private set; }
        internal MethodInfo Clear { get; private set; }
        internal IList Bodies { get; private set; }
        internal IList Skins { get; private set; }
        internal IList Collisions { get; private set; }
        internal IList Island { get; private set; }
        internal IList Islands { get; private set; }

        internal Fixture(Assembly magicka)
        {
            Type managerType = magicka.GetType(
                "Magicka.Physics.PhysicsManager", true);
            ConstructorInfo constructor = null;
            ConstructorInfo[] constructors = managerType.GetConstructors(Members);
            for (int index = 0; index < constructors.Length; index++)
                if (!constructors[index].IsStatic &&
                    constructors[index].GetParameters().Length == 0)
                {
                    constructor = constructors[index];
                    break;
                }
            if (constructor == null)
                throw new MissingMethodException(managerType.FullName, ".ctor");
            Manager = constructor.Invoke(null);
            SimulatorField = managerType.GetField("mSimulator", Members);
            simulator = SimulatorField.GetValue(Manager);
            Clear = managerType.GetMethod("Clear", Members);
            Type simulatorType = simulator.GetType();
            Bodies = (IList)simulatorType.GetProperty("Bodies", Members)
                .GetValue(simulator, null);
            collisionSystem = simulatorType.GetProperty(
                "CollisionSystem", Members).GetValue(simulator, null);
            Skins = (IList)collisionSystem.GetType().GetProperty(
                "CollisionSkins", Members).GetValue(collisionSystem, null);
            Islands = (IList)simulatorType.GetField(
                "islands", Members).GetValue(simulator);
            bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
            skinType = RuntimeReflection.FindLoadedType(
                "JigLibX.Collision.CollisionSkin");
            collisionInfoType = RuntimeReflection.FindLoadedType(
                "JigLibX.Collision.CollisionInfo");
            islandType = RuntimeReflection.FindLoadedType(
                "JigLibX.Physics.CollisionIsland");
        }

        internal void Populate()
        {
            object body = Activator.CreateInstance(bodyType);
            bodyType.GetMethod("EnableBody", Members).Invoke(body, null);
            object skin = Activator.CreateInstance(skinType);
            collisionSystem.GetType().GetMethod(
                "AddCollisionSkin", Members, null,
                new Type[] { skinType }, null).Invoke(
                    collisionSystem, new object[] { skin });
            Collisions = (IList)skinType.GetProperty(
                "Collisions", Members).GetValue(skin, null);
            object collision = Activator.CreateInstance(
                collisionInfoType, true);
            Collisions.Add(collision);
            Island = (IList)Activator.CreateInstance(islandType);
            Island.Add(body);
            Islands.Add(Island);
        }
    }
}
