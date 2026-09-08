using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class TriggerActionLifecycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        TriggerActionLifecycleHarness harness =
            new TriggerActionLifecycleHarness(magicka, runtimePatchEnabled);
        report.Add(
            "trigger_lifecycle.missing_item_slot",
            harness.MissingItemSlot());
        report.Add(
            "trigger_lifecycle.wrong_item_slot_type",
            harness.WrongItemSlotType());
        report.Add(
            "trigger_lifecycle.active_item_reuse",
            harness.ActiveItemReuse());
        report.Add(
            "trigger_lifecycle.inactive_item_slot",
            harness.InactiveItemSlot());
        report.Add(
            "trigger_lifecycle.active_living_npc",
            harness.ActiveLivingNpc());
        report.Add(
            "trigger_lifecycle.active_dead_npc_reuse",
            harness.ActiveDeadNpcReuse());
        report.Add(
            "trigger_lifecycle.missing_active_target",
            harness.MissingActiveTarget());
        report.Add(
            "trigger_lifecycle.active_target",
            harness.ActiveTarget());
    }
}

internal sealed class TriggerActionLifecycleHarness
{
    private const int TemplateId = 19790507;

    private readonly bool runtimePatchEnabled;
    private readonly Type messageType;
    private readonly Type entityType;
    private readonly Type itemType;
    private readonly Type npcType;
    private readonly Type playStateType;
    private readonly Type managerType;
    private readonly Type staticEntityListType;
    private readonly Type characterTemplateType;
    private readonly FieldInfo actionTypeField;
    private readonly FieldInfo handleField;
    private readonly FieldInfo templateField;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo deadField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo playStateManagerField;
    private readonly FieldInfo managerEntitiesField;
    private readonly FieldInfo characterTemplatesField;
    private readonly MethodInfo manualGuard;

    internal TriggerActionLifecycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        messageType = magicka.GetType(
            "Magicka.Network.TriggerActionMessage",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item",
            true);
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        managerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        staticEntityListType = magicka.GetType(
            "Magicka.StaticObjectList`1",
            true).MakeGenericType(entityType);
        characterTemplateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);

        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        RuntimeHelpers.RunClassConstructor(characterTemplateType.TypeHandle);
        actionTypeField = RuntimeReflection.RequireField(
            messageType,
            "ActionType");
        handleField = RuntimeReflection.RequireField(messageType, "Handle");
        templateField = RuntimeReflection.RequireField(messageType, "Template");
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        playStateField = RuntimeReflection.RequireField(entityType, "mPlayState");
        deadField = RuntimeReflection.RequireField(npcType, "mDead");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        playStateManagerField = RuntimeReflection.RequireField(
            playStateType,
            "mEntityManager");
        managerEntitiesField = RuntimeReflection.RequireField(
            managerType,
            "mEntities");
        characterTemplatesField = RuntimeReflection.RequireField(
            characterTemplateType,
            "mCachedTemplates");

        Type helper = magicka.GetType(
            "Magicka.CommunityPatch.NetworkLifecycleCompatibility",
            false);
        manualGuard = helper == null
            ? null
            : helper.GetMethod(
                "CanProcessTriggerAction",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
    }

    internal ScenarioResult MissingItemSlot()
    {
        return Evaluate("SpawnItem", null, false, false, false);
    }

    internal ScenarioResult WrongItemSlotType()
    {
        return Evaluate("SpawnItem", npcType, false, false, false);
    }

    internal ScenarioResult ActiveItemReuse()
    {
        return Evaluate("SpawnItem", itemType, true, false, true);
    }

    internal ScenarioResult InactiveItemSlot()
    {
        return Evaluate("SpawnItem", itemType, false, false, true);
    }

    internal ScenarioResult ActiveLivingNpc()
    {
        return Evaluate("SpawnNPC", npcType, true, false, false);
    }

    internal ScenarioResult ActiveDeadNpcReuse()
    {
        return Evaluate("SpawnNPC", npcType, true, true, true);
    }

    internal ScenarioResult MissingActiveTarget()
    {
        return Evaluate("Confuse", null, false, false, false);
    }

    internal ScenarioResult ActiveTarget()
    {
        return Evaluate("Confuse", itemType, true, false, true);
    }

    private ScenarioResult Evaluate(
        string actionName,
        Type candidateType,
        bool active,
        bool dead,
        bool expected)
    {
        IList instances = (IList)instancesField.GetValue(null);
        object previousPlayState = recentPlayStateField.GetValue(null);
        IDictionary characterTemplates =
            (IDictionary)characterTemplatesField.GetValue(null);
        object previousTemplate = characterTemplates.Contains(TemplateId)
            ? characterTemplates[TemplateId]
            : null;
        bool hadTemplate = characterTemplates.Contains(TemplateId);
        try
        {
            instances.Clear();
            object playState = FormatterServices.GetUninitializedObject(
                playStateType);
            object manager = FormatterServices.GetUninitializedObject(managerType);
            object entities = Activator.CreateInstance(
                staticEntityListType,
                new object[] { 16 });
            managerEntitiesField.SetValue(manager, entities);
            playStateManagerField.SetValue(playState, manager);
            recentPlayStateField.SetValue(null, playState);

            object candidate = null;
            if (candidateType != null)
            {
                candidate = FormatterServices.GetUninitializedObject(
                    candidateType);
                GC.SuppressFinalize(candidate);
                playStateField.SetValue(candidate, playState);
                if (npcType.IsInstanceOfType(candidate))
                    deadField.SetValue(candidate, dead);
                instances.Add(candidate);
                if (active)
                    entities.GetType().GetMethod("Add").Invoke(
                        entities,
                        new object[] { candidate });
            }

            if (actionName == "SpawnNPC")
            {
                object template = FormatterServices.GetUninitializedObject(
                    characterTemplateType);
                characterTemplates[TemplateId] = template;
            }

            object message = Activator.CreateInstance(messageType);
            actionTypeField.SetValue(
                message,
                Enum.Parse(actionTypeField.FieldType, actionName));
            handleField.SetValue(
                message,
                Convert.ChangeType(0, handleField.FieldType));
            templateField.SetValue(
                message,
                Convert.ChangeType(TemplateId, templateField.FieldType));
            bool actual = CanProcess(message);
            return new ScenarioResult(
                actual == expected,
                actual.ToString(),
                expected.ToString());
        }
        finally
        {
            instances.Clear();
            recentPlayStateField.SetValue(null, previousPlayState);
            if (hadTemplate)
                characterTemplates[TemplateId] = previousTemplate;
            else
                characterTemplates.Remove(TemplateId);
        }
    }

    private bool CanProcess(object message)
    {
        if (runtimePatchEnabled)
        {
            Type helper = FindLoadedType(
                "Magicka.CommunityPatch.Runtime.TriggerActionLifecyclePatch");
            if (helper != null)
            {
                MethodInfo method = helper.GetMethod(
                    "ShouldProcess",
                    BindingFlags.Static | BindingFlags.Public);
                return (bool)method.Invoke(null, new object[] { message });
            }
        }
        if (manualGuard == null)
            return true;
        object[] arguments = new object[] { message };
        return (bool)manualGuard.Invoke(null, arguments);
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }
}
