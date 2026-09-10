using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PhysicsManagerClearPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo simulatorField;
        private static PropertyInfo bodiesProperty;
        private static PropertyInfo collisionSystemProperty;
        private static PropertyInfo collisionSkinsProperty;
        private static PropertyInfo collisionsProperty;
        private static FieldInfo islandsField;
        private static MethodInfo disableBody;
        private static MethodInfo removeBody;
        private static MethodInfo removeCollisionSkin;
        private static object emptyIsland;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Physics manager complete clear",
                "org.magickacommunitypatch.physics-manager-complete-clear",
                FindClear,
                target => typeof(PhysicsManagerClearPatch).GetMethod("Prefix"));

        private static MethodInfo FindClear(Assembly assembly)
        {
            Type manager = assembly.GetType("Magicka.Physics.PhysicsManager", true);
            simulatorField = manager.GetField("mSimulator", Members);
            MethodInfo clear = manager.GetMethod(
                "Clear", Members, null, Type.EmptyTypes, null);
            if (simulatorField == null || clear == null)
                throw new MissingMethodException(manager.FullName, "Clear");

            Type simulator = simulatorField.FieldType;
            bodiesProperty = RequireProperty(simulator, "Bodies");
            collisionSystemProperty = RequireProperty(simulator, "CollisionSystem");
            islandsField = simulator.GetField("islands", Members);
            removeBody = RequireMethod(simulator, "RemoveBody", 1);
            Type body = removeBody.GetParameters()[0].ParameterType;
            disableBody = RequireMethod(body, "DisableBody", 0);

            Type collisionSystem = collisionSystemProperty.PropertyType;
            collisionSkinsProperty = RequireProperty(
                collisionSystem, "CollisionSkins");
            removeCollisionSkin = RequireMethod(
                collisionSystem, "RemoveCollisionSkin", 1);
            Type skin = removeCollisionSkin.GetParameters()[0].ParameterType;
            collisionsProperty = RequireProperty(skin, "Collisions");

            if (islandsField == null)
                throw new MissingFieldException(simulator.FullName, "islands");
            Type island = islandsField.FieldType.GetGenericArguments()[0];
            PropertyInfo empty = island.GetProperty("Empty", Members);
            if (empty == null)
                throw new MissingMemberException(island.FullName, "Empty");
            emptyIsland = empty.GetValue(null, null);
            return clear;
        }

        public static bool Prefix(object __instance)
        {
            object simulator = simulatorField.GetValue(__instance);
            if (simulator == null)
                return false;

            IList bodies = (IList)bodiesProperty.GetValue(simulator, null);
            ArrayList bodySnapshot = Snapshot(bodies);
            for (int index = 0; index < bodySnapshot.Count; index++)
            {
                object body = bodySnapshot[index];
                if (body == null)
                    continue;
                disableBody.Invoke(body, null);
                if (bodies.Contains(body))
                    removeBody.Invoke(simulator, new object[] { body });
            }

            object collisionSystem =
                collisionSystemProperty.GetValue(simulator, null);
            IList skins = (IList)collisionSkinsProperty.GetValue(
                collisionSystem, null);
            ArrayList skinSnapshot = Snapshot(skins);
            for (int index = 0; index < skinSnapshot.Count; index++)
            {
                object skin = skinSnapshot[index];
                if (skin == null)
                    continue;
                removeCollisionSkin.Invoke(
                    collisionSystem, new object[] { skin });
                IList collisions =
                    (IList)collisionsProperty.GetValue(skin, null);
                collisions.Clear();
            }

            IList islands = (IList)islandsField.GetValue(simulator);
            if (islands == null)
                return false;
            for (int index = 0; index < islands.Count; index++)
            {
                IList island = islands[index] as IList;
                if (island != null && !Object.ReferenceEquals(island, emptyIsland))
                    island.Clear();
            }
            islands.Clear();
            return false;
        }

        private static ArrayList Snapshot(IEnumerable source)
        {
            ArrayList result = new ArrayList();
            foreach (object value in source)
                result.Add(value);
            return result;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, Members);
            if (property == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(Type type, string name, int count)
        {
            MethodInfo[] methods = type.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == name &&
                    methods[index].GetParameters().Length == count)
                    return methods[index];
            throw new MissingMethodException(type.FullName, name);
        }
    }
}
