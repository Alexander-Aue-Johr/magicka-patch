using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AgentChooseTargetScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AgentChooseTargetHarness harness = new AgentChooseTargetHarness(magicka);
        report.Add("agent_target.bodyless_player", harness.BodylessPlayer());
        report.Add("agent_target.no_player", harness.NoPlayer());
        report.Add(
            "agent_lifecycle.choose_target_scratch",
            harness.ScratchReleased());
    }
}

internal sealed class AgentChooseTargetHarness
{
    private readonly Type gameType;
    private readonly Type playerType;
    private readonly Type agentType;
    private readonly Type avatarType;
    private readonly Type ownerType;
    private readonly Type characterBodyType;
    private readonly Type abilityType;
    private readonly Type spellType;
    private readonly Type castSpellType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type shieldType;
    private readonly Type staticEquatableListType;
    private readonly FieldInfo gameSingleton;
    private readonly MethodInfo chooseTarget;
    private object bodylessPlayer;

    internal AgentChooseTargetHarness(Assembly magicka)
    {
        gameType = magicka.GetType("Magicka.Game", true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        agentType = magicka.GetType("Magicka.AI.Agent", true);
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        characterBodyType = magicka.GetType("Magicka.Physics.CharacterBody", true);
        abilityType = magicka.GetType("Magicka.GameLogic.Entities.Abilities.Ability", true);
        spellType = magicka.GetType("Magicka.GameLogic.Spells.Spell", true);
        castSpellType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.CastSpell",
            true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        entityManagerType = magicka.GetType("Magicka.GameLogic.Entities.EntityManager", true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        shieldType = magicka.GetType("Magicka.GameLogic.Entities.Shield", true);
        staticEquatableListType = magicka.GetType("Magicka.StaticEquatableList`1", true);
        gameSingleton = RuntimeReflection.RequireField(gameType, "mSingelton");
        chooseTarget = Array.Find(
            agentType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "ChooseTarget" &&
                method.GetParameters().Length == 2);
    }

    internal ScenarioResult BodylessPlayer()
    {
        return Invoke(true, false);
    }

    internal ScenarioResult NoPlayer()
    {
        return Invoke(false, false);
    }

    internal ScenarioResult ScratchReleased()
    {
        return Invoke(false, true);
    }

    private ScenarioResult Invoke(
        bool includeBodylessPlayer,
        bool inspectScratch)
    {
        object previousGame = gameSingleton.GetValue(null);
        try
        {
            gameSingleton.SetValue(null, NewGame(includeBodylessPlayer));
            object agent = NewAgent(inspectScratch);
            object[] arguments = new object[] { null, null };
            try
            {
                chooseTarget.Invoke(agent, arguments);
                string target = arguments[0] == null ? "null" : "set";
                string ability = arguments[1] == null ? "null" : "set";
                bool scratchReleased = !inspectScratch ||
                    ArrayIsClear(RuntimeReflection.ReadField(
                        agent,
                        "mFuzzySortAbilities") as Array);
                string actual = "target:" + target + ",ability:" + ability +
                    ",scratch_released:" + scratchReleased;
                return new ScenarioResult(
                    target == "null" && ability == "null" && scratchReleased,
                    actual,
                    "target:null,ability:null,scratch_released:True");
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                return new ScenarioResult(
                    false,
                    inner.GetType().FullName,
                    "target:null,ability:null,scratch_released:True");
            }
        }
        finally
        {
            GC.KeepAlive(bodylessPlayer);
            bodylessPlayer = null;
            gameSingleton.SetValue(null, previousGame);
        }
    }

    private object NewAgent(bool seedScratch)
    {
        object owner = FormatterServices.GetUninitializedObject(ownerType);
        GC.SuppressFinalize(owner);
        RuntimeReflection.WriteField(
            owner,
            "mBody",
            FormatterServices.GetUninitializedObject(characterBodyType));
        RuntimeReflection.WriteField(owner, "mAbilities", Array.CreateInstance(abilityType, 0));
        RuntimeReflection.WriteField(owner, "mAbilityCooldown", new float[0]);
        RuntimeReflection.WriteField(owner, "mSpellQueue", Activator.CreateInstance(
            staticEquatableListType.MakeGenericType(spellType),
            new object[] { 5 }));
        RuntimeReflection.WriteField(owner, "mPlayState", NewPlayState());

        object agent = FormatterServices.GetUninitializedObject(agentType);
        RuntimeReflection.WriteField(agent, "mOwner", owner);
        RuntimeReflection.WriteField(agent, "mFuzzySortValues", new float[8]);
        RuntimeReflection.WriteField(agent, "mFuzzySortAbilities", Array.CreateInstance(abilityType, 8));
        Type damageableType = RuntimeReflection.FindLoadedType(
            "Magicka.GameLogic.Entities.IDamageable");
        RuntimeReflection.WriteField(
            agent,
            "mFuzzySortEntities",
            Array.CreateInstance(damageableType, 8));
        if (seedScratch)
        {
            Array abilities = (Array)RuntimeReflection.ReadField(
                agent,
                "mFuzzySortAbilities");
            abilities.SetValue(
                FormatterServices.GetUninitializedObject(castSpellType),
                0);
        }
        return agent;
    }

    private static bool ArrayIsClear(Array values)
    {
        if (values == null)
            return false;
        for (int index = 0; index < values.Length; index++)
        {
            if (values.GetValue(index) != null)
                return false;
        }
        return true;
    }

    private object NewGame(bool includeBodylessPlayer)
    {
        object game = FormatterServices.GetUninitializedObject(gameType);
        GC.SuppressFinalize(game);
        Array players = Array.CreateInstance(playerType, 4);
        object target = includeBodylessPlayer
            ? FormatterServices.GetUninitializedObject(avatarType)
            : null;
        bodylessPlayer = target;
        if (target != null)
            GC.SuppressFinalize(target);
        for (int index = 0; index < players.Length; index++)
        {
            object player = FormatterServices.GetUninitializedObject(playerType);
            RuntimeReflection.WriteField(
                player,
                "mAvatar",
                new WeakReference(index == 0 ? target : null));
            players.SetValue(player, index);
        }
        RuntimeReflection.WriteField(game, "mPlayers", players);
        return game;
    }

    private object NewPlayState()
    {
        Type entityListType = typeof(System.Collections.Generic.List<>).MakeGenericType(entityType);
        Array grid = Array.CreateInstance(entityListType, 16, 16);
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
                grid.SetValue(Activator.CreateInstance(entityListType), x, y);
        }

        object manager = FormatterServices.GetUninitializedObject(entityManagerType);
        RuntimeReflection.WriteField(manager, "mQuadGrid", grid);
        RuntimeReflection.WriteField(
            manager,
            "mQuaryLists",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.Queue<>).MakeGenericType(entityListType)));
        RuntimeReflection.WriteField(
            manager,
            "mShields",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.List<>).MakeGenericType(shieldType)));

        object playState = FormatterServices.GetUninitializedObject(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
    }
}
