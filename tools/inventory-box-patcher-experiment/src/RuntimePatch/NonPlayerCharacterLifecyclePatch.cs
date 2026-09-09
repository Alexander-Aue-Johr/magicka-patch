using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NonPlayerCharacterLifecyclePatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static FieldInfo agentField;
        private static FieldInfo controllerField;
        private static FieldInfo clipsField;
        private static FieldInfo gibsField;
        private static FieldInfo modelField;
        private static FieldInfo templateField;
        private static MethodInfo agentDisableMethod;
        private static MethodInfo agentResetMethod;
        private static ConstructorInfo controllerConstructor;
        private static EventInfo animationLoopedEvent;
        private static EventInfo crossfadeFinishedEvent;
        private static MethodInfo onAnimationLoopedMethod;
        private static MethodInfo onCrossfadeFinishedMethod;

        internal static readonly RuntimePatchDefinition PrefixDefinition =
            RuntimePatchDefinition.Prefix(
                "NonPlayerCharacter reusable teardown begin",
                "org.magickacommunitypatch.npc-reusable-teardown.begin",
                FindDeinitialize,
                target => typeof(NonPlayerCharacterLifecyclePatch).GetMethod(
                    "Prefix"));

        internal static readonly RuntimePatchDefinition PostfixDefinition =
            RuntimePatchDefinition.Postfix(
                "NonPlayerCharacter reusable teardown finish",
                "org.magickacommunitypatch.npc-reusable-teardown.finish",
                FindDeinitialize,
                target => typeof(NonPlayerCharacterLifecyclePatch).GetMethod(
                    "Postfix"));

        private static MethodInfo FindDeinitialize(Assembly assembly)
        {
            Type npc = assembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            Type character = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type agent = assembly.GetType("Magicka.AI.Agent", true);

            agentField = RequireDeclaredField(npc, "mAI");
            controllerField = RequireDeclaredField(
                character,
                "mAnimationController");
            clipsField = RequireDeclaredField(character, "mAnimationClips");
            gibsField = RequireDeclaredField(character, "mGibs");
            modelField = RequireDeclaredField(character, "mModel");
            templateField = RequireDeclaredField(character, "mTemplate");

            agentDisableMethod = RequireMethod(
                agent,
                "Disable",
                Type.EmptyTypes);
            agentResetMethod = RequireMethod(
                agent,
                "Reset",
                Type.EmptyTypes);

            Type controller = controllerField.FieldType;
            controllerConstructor = controller.GetConstructor(
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            animationLoopedEvent = controller.GetEvent(
                "AnimationLooped",
                InstanceMembers);
            crossfadeFinishedEvent = controller.GetEvent(
                "CrossfadeFinished",
                InstanceMembers);
            onAnimationLoopedMethod = character.GetMethod(
                "OnAnimationLooped",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            onCrossfadeFinishedMethod = character.GetMethod(
                "OnCrossfadeFinished",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);

            MethodInfo deinitialize = npc.GetMethod(
                "Deinitialize",
                InstanceMembers | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (controllerConstructor == null ||
                animationLoopedEvent == null ||
                crossfadeFinishedEvent == null ||
                onAnimationLoopedMethod == null ||
                onCrossfadeFinishedMethod == null ||
                !typeof(IList).IsAssignableFrom(gibsField.FieldType) ||
                deinitialize == null ||
                deinitialize.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "NonPlayerCharacter reusable teardown contract is incomplete.");
            return deinitialize;
        }

        private static FieldInfo RequireDeclaredField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name,
                InstanceMembers,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static void Prefix(object __instance)
        {
            object agent = agentField.GetValue(__instance);
            if (agent != null)
                agentDisableMethod.Invoke(agent, null);
        }

        public static void Postfix(object __instance)
        {
            object agent = agentField.GetValue(__instance);
            if (agent != null)
                agentResetMethod.Invoke(agent, null);

            ReplaceAnimationController(__instance);
            IList gibs = gibsField.GetValue(__instance) as IList;
            if (gibs != null)
                gibs.Clear();
            clipsField.SetValue(__instance, null);
            modelField.SetValue(__instance, null);
            templateField.SetValue(__instance, null);
        }

        private static void ReplaceAnimationController(object npc)
        {
            object oldController = controllerField.GetValue(npc);
            if (oldController != null)
            {
                RemoveHandler(
                    animationLoopedEvent,
                    oldController,
                    npc,
                    onAnimationLoopedMethod);
                RemoveHandler(
                    crossfadeFinishedEvent,
                    oldController,
                    npc,
                    onCrossfadeFinishedMethod);
            }

            object controller = controllerConstructor.Invoke(null);
            AddHandler(
                animationLoopedEvent,
                controller,
                npc,
                onAnimationLoopedMethod);
            AddHandler(
                crossfadeFinishedEvent,
                controller,
                npc,
                onCrossfadeFinishedMethod);
            controllerField.SetValue(npc, controller);
        }

        private static void AddHandler(
            EventInfo eventInfo,
            object source,
            object target,
            MethodInfo method)
        {
            eventInfo.AddEventHandler(
                source,
                Delegate.CreateDelegate(
                    eventInfo.EventHandlerType,
                    target,
                    method));
        }

        private static void RemoveHandler(
            EventInfo eventInfo,
            object source,
            object target,
            MethodInfo method)
        {
            eventInfo.RemoveEventHandler(
                source,
                Delegate.CreateDelegate(
                    eventInfo.EventHandlerType,
                    target,
                    method));
        }
    }
}
