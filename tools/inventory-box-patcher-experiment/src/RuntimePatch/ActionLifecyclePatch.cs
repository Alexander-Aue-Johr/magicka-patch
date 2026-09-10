using System;
using System.Collections;
using System.Reflection;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ActionLifecyclePatch
    {
        private static readonly object[] EmptyArguments = new object[0];
        private static readonly object[] NullArgument = new object[] { null };

        private static FieldInfo instancesField;
        private static FieldInfo queueField;
        private static FieldInfo triggerField;
        private static FieldInfo sceneField;
        private static FieldInfo initializedField;
        private static MethodInfo tagSetter;
        private static MethodInfo clearInstances;

        internal static readonly RuntimePatchDefinition ClearDefinition =
            RuntimePatchDefinition.Prefix(
                "Action instance reference cleanup",
                "org.magickacommunitypatch.action-instance-cleanup",
                FindClearInstances,
                target => typeof(ActionLifecyclePatch).GetMethod(
                    "ClearPrefix"));

        internal static readonly RuntimePatchDefinition ResetDefinition =
            RuntimePatchDefinition.Postfix(
                "Action state tag cleanup",
                "org.magickacommunitypatch.action-state-tag-cleanup",
                FindStateReset,
                target => typeof(ActionLifecyclePatch).GetMethod(
                    "ResetPostfix"));

        internal static readonly RuntimePatchDefinition DisposeDefinition =
            RuntimePatchDefinition.Prefix(
                "Action cleanup at play-state disposal",
                "org.magickacommunitypatch.action-play-state-cleanup",
                FindPlayStateDispose,
                target => typeof(ActionLifecyclePatch).GetMethod(
                    "DisposePrefix"));

        private static MethodInfo FindClearInstances(Assembly targetAssembly)
        {
            Type action = ConfigureAction(targetAssembly);
            clearInstances = action.GetMethod(
                "ClearInstances",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearInstances == null ||
                clearInstances.ReturnType != typeof(void))
                throw new MissingMethodException(
                    action.FullName,
                    "ClearInstances");
            return clearInstances;
        }

        private static MethodInfo FindStateReset(Assembly targetAssembly)
        {
            Type action = ConfigureAction(targetAssembly);
            Type state = action.GetNestedType(
                "State",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (state == null)
                throw new TypeLoadException(action.FullName + "+State");
            MethodInfo reset = state.GetMethod(
                "Reset",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { action },
                null);
            if (reset == null || reset.ReturnType != typeof(void))
                throw new MissingMethodException(state.FullName, "Reset");
            return reset;
        }

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            FindClearInstances(targetAssembly);
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            initializedField = RequireField(
                playState,
                "mInitialized",
                typeof(bool),
                false);
            MethodInfo dispose = playState.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playState.FullName, "Dispose");
            return dispose;
        }

        private static Type ConfigureAction(Assembly targetAssembly)
        {
            Type action = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Actions.Action",
                true);
            Type trigger = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Trigger",
                true);
            Type scene = targetAssembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            instancesField = RequireField(
                action,
                "sInstances",
                null,
                true);
            if (!typeof(IList).IsAssignableFrom(instancesField.FieldType))
                throw new InvalidOperationException(
                    action.FullName + ".sInstances is not an IList.");
            queueField = RequireField(action, "mQueue", typeof(int), false);
            triggerField = RequireField(action, "mTrigger", trigger, false);
            sceneField = RequireField(action, "mScene", scene, false);

            PropertyInfo tag = action.GetProperty(
                "Tag",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            tagSetter = tag == null ? null : tag.GetSetMethod(true);
            if (tagSetter == null || tag.PropertyType != typeof(object))
                throw new MissingMethodException(action.FullName, "set_Tag");
            return action;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            bool isStatic)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.IsStatic != isStatic ||
                (fieldType != null && field.FieldType != fieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static void ClearPrefix()
        {
            IList instances = (IList)instancesField.GetValue(null);
            if (instances == null)
                return;
            for (int index = 0; index < instances.Count; index++)
                Cleanup(instances[index]);
        }

        internal static void Cleanup(object action)
        {
            if (action == null)
                return;
            queueField.SetValue(action, 0);
            triggerField.SetValue(action, null);
            sceneField.SetValue(action, null);
        }

        public static void ResetPostfix(object __0)
        {
            if (__0 != null)
                tagSetter.Invoke(__0, NullArgument);
        }

        public static void DisposePrefix(object __instance)
        {
            if (__instance != null &&
                (bool)initializedField.GetValue(__instance))
                clearInstances.Invoke(null, EmptyArguments);
        }
    }
}
