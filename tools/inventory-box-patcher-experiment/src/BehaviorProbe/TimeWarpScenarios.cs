using System;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using Harmony;

internal static class TimeWarpScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        TimeWarpHarness.InstallProbesEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        TimeWarpHarness harness = new TimeWarpHarness(magicka);
        try
        {
            report.Add("time_warp.start_current_state", harness.Start(false));
            report.Add("time_warp.update_current_state", harness.Update(false));
            report.Add("time_warp.remove_current_state", harness.Remove(false));
            report.Add("time_warp_staff.start_current_state", harness.Start(true));
            report.Add("time_warp_staff.update_current_state", harness.Update(true));
            report.Add("time_warp_staff.remove_current_state", harness.Remove(true));
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class TimeWarpHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-time-warp";

    private readonly Type timeWarpType;
    private readonly Type staffType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type spellCasterType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo playStateLevelField;
    private readonly FieldInfo levelSceneField;
    private readonly HarmonyInstance harmony;
    private readonly object originalRecentPlayState;
    private readonly object originalTimeWarpCue;
    private readonly object originalStaffCue;

    internal TimeWarpHarness(Assembly magicka)
    {
        timeWarpType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarp",
            true);
        staffType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarpStaff",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        spellCasterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        playStateLevelField = RuntimeReflection.RequireField(
            playStateType,
            "mLevel");
        levelSceneField = RuntimeReflection.RequireField(
            levelType,
            "mCurrentScene");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalTimeWarpCue = RuntimeReflection.RequireField(
            timeWarpType,
            "sCue").GetValue(null);
        originalStaffCue = RuntimeReflection.RequireField(
            staffType,
            "sCue").GetValue(null);
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        TimeWarpProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        RuntimeReflection.RequireField(timeWarpType, "sCue").SetValue(
            null,
            originalTimeWarpCue);
        RuntimeReflection.RequireField(staffType, "sCue").SetValue(
            null,
            originalStaffCue);
    }

    internal ScenarioResult Start(bool staff)
    {
        Type effectType = staff ? staffType : timeWarpType;
        object supplied = CreatePlayState(0.25f, 2f, 0.5f);
        object current = CreatePlayState(0.75f, 1f, 0.5f);
        recentPlayStateField.SetValue(null, current);
        object effect = NewUninitialized(effectType);
        FieldInfo legacy = LegacyStateField(effectType);
        if (legacy != null)
            legacy.SetValue(effect, null);

        MethodInfo execute = effectType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            staff
                ? new Type[] { spellCasterType, playStateType }
                : new Type[] { vectorType, playStateType },
            null);
        if (execute == null)
            throw new MissingMethodException(effectType.FullName, "Execute");

        object first = staff
            ? new SpellCasterProxy(spellCasterType).GetTransparentProxy()
            : Activator.CreateInstance(vectorType);
        TimeWarpProbe.Reset();
        TimeWarpProbe.Enabled = true;
        bool returned = (bool)Invoke(
            execute,
            effect,
            new object[] { first, supplied });
        TimeWarpProbe.Enabled = false;

        float saturation = ReadFloat(effect, "mSaturation");
        bool released = legacy == null || legacy.GetValue(effect) == null;
        bool passed = returned && released && Close(saturation, 0.75f) &&
            TimeWarpProbe.AddEffectCalls == 1 &&
            TimeWarpProbe.GetCueCalls == 1 &&
            TimeWarpProbe.CuePlayCalls == 1 &&
            TimeWarpProbe.StartVisualEffectCalls == (staff ? 1 : 0);
        string actual = "returned:" + returned +
            ",released:" + released +
            ",saturation:" + saturation.ToString("R") +
            ",spell_add:" + TimeWarpProbe.AddEffectCalls +
            ",cue_get:" + TimeWarpProbe.GetCueCalls +
            ",cue_play:" + TimeWarpProbe.CuePlayCalls +
            ",visual_start:" + TimeWarpProbe.StartVisualEffectCalls;
        return new ScenarioResult(
            passed,
            actual,
            "returned:True,released:True,saturation:0.75,spell_add:1," +
                "cue_get:1,cue_play:1,visual_start:" + (staff ? 1 : 0));
    }

    internal ScenarioResult Update(bool staff)
    {
        Type effectType = staff ? staffType : timeWarpType;
        object stale = CreatePlayState(0.25f, 2f, 0.5f);
        object current = CreatePlayState(0.75f, 1f, 0.5f);
        recentPlayStateField.SetValue(null, current);
        object effect = NewUninitialized(effectType);
        WriteFloat(effect, "mTTL", 10f);
        WriteFloat(effect, "mFadeTime", 1f);
        WriteFloat(effect, "mTimeMultiplier", 0.6f);
        WriteFloat(effect, "mTimeMultiplierTarget", 0.5f);
        WriteFloat(effect, "mSaturation", 0.7f);
        WriteFloat(effect, "mSaturationTarget", 0.1f);
        FieldInfo legacy = LegacyStateField(effectType);
        if (legacy != null)
            legacy.SetValue(effect, stale);

        MethodInfo update = effectType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (update == null)
            throw new MissingMethodException(effectType.FullName, "Update");
        TimeWarpProbe.Reset();
        TimeWarpProbe.Enabled = true;
        Invoke(
            update,
            effect,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.2f });
        TimeWarpProbe.Enabled = false;

        float ttl = ReadFloat(effect, "mTTL");
        float currentMultiplier = ReadFloat(current, "mTimeMultiplier");
        float staleMultiplier = ReadFloat(stale, "mTimeMultiplier");
        bool passed = Close(ttl, 9.6f) &&
            Close(currentMultiplier, 0.56f) &&
            Close(staleMultiplier, 0.5f) &&
            TimeWarpProbe.SaturationWrites == 1;
        string actual = "ttl:" + ttl.ToString("R") +
            ",current_multiplier:" + currentMultiplier.ToString("R") +
            ",stale_multiplier:" + staleMultiplier.ToString("R") +
            ",render_writes:" + TimeWarpProbe.SaturationWrites;
        return new ScenarioResult(
            passed,
            actual,
            "ttl:9.6,current_multiplier:0.56,stale_multiplier:0.5," +
                "render_writes:1");
    }

    internal ScenarioResult Remove(bool staff)
    {
        Type effectType = staff ? staffType : timeWarpType;
        object stale = CreatePlayState(0.25f, 2f, 0.5f);
        object current = CreatePlayState(0.75f, 1f, 0.5f);
        recentPlayStateField.SetValue(null, current);
        object effect = NewUninitialized(effectType);
        FieldInfo legacy = LegacyStateField(effectType);
        if (legacy != null)
            legacy.SetValue(effect, stale);
        RuntimeReflection.RequireField(effectType, "sCue").SetValue(null, null);

        MethodInfo onRemove = effectType.GetMethod(
            "OnRemove",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (onRemove == null)
            throw new MissingMethodException(effectType.FullName, "OnRemove");
        TimeWarpProbe.Reset();
        TimeWarpProbe.Enabled = true;
        Invoke(onRemove, effect, new object[0]);
        TimeWarpProbe.Enabled = false;

        float currentMultiplier = ReadFloat(current, "mTimeMultiplier");
        float staleMultiplier = ReadFloat(stale, "mTimeMultiplier");
        bool passed = Close(currentMultiplier, 1f) &&
            Close(staleMultiplier, 0.5f) &&
            TimeWarpProbe.SaturationWrites == 1 &&
            Close(TimeWarpProbe.LastSaturation, 0.75f);
        string actual = "current_multiplier:" +
            currentMultiplier.ToString("R") +
            ",stale_multiplier:" + staleMultiplier.ToString("R") +
            ",render_writes:" + TimeWarpProbe.SaturationWrites +
            ",render_saturation:" + TimeWarpProbe.LastSaturation.ToString("R");
        return new ScenarioResult(
            passed,
            actual,
            "current_multiplier:1,stale_multiplier:0.5,render_writes:1," +
                "render_saturation:0.75");
    }

    private object CreatePlayState(
        float saturation,
        float timeModifier,
        float timeMultiplier)
    {
        object scene = NewUninitialized(sceneType);
        WriteFloat(scene, "mSaturation", saturation);
        object level = NewUninitialized(levelType);
        levelSceneField.SetValue(level, scene);
        object state = NewUninitialized(playStateType);
        playStateLevelField.SetValue(state, level);
        WriteFloat(state, "mTimeModifier", timeModifier);
        WriteFloat(state, "mTimeMultiplier", timeMultiplier);
        return state;
    }

    private static FieldInfo LegacyStateField(Type effectType)
    {
        return effectType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
    }

    private static float ReadFloat(object target, string name)
    {
        return Convert.ToSingle(RuntimeReflection.ReadField(target, name));
    }

    private static void WriteFloat(object target, string name, float value)
    {
        RuntimeReflection.WriteField(target, name, value);
    }

    private static bool Close(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
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

    internal static void InstallProbesEarly(Assembly magicka)
    {
        Type spellManager = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type abilityEffect = magicka.GetType(
            "Magicka.GameLogic.IAbilityEffect",
            true);
        Type audioManager = magicka.GetType("Magicka.Audio.AudioManager", true);
        Type banks = magicka.GetType("Magicka.Audio.Banks", true);
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type visualReference = magicka.GetType(
            "Magicka.Graphics.VisualEffectReference",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type matrix = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Matrix");
        Type cue = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.Cue");
        Type renderManager = RuntimeReflection.FindLoadedType(
            "PolygonHead.RenderManager");

        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);
        PatchGetter(harmony, spellManager, "Instance", "SpellManagerPrefix");
        PatchGetter(harmony, audioManager, "Instance", "AudioManagerPrefix");
        PatchGetter(harmony, effectManager, "Instance", "EffectManagerPrefix");
        PatchGetter(harmony, renderManager, "Instance", "RenderManagerPrefix");
        Patch(harmony, spellManager.GetMethod(
            "IsEffectActive",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { typeof(Type) },
            null), "IsEffectActivePrefix");
        Patch(harmony, spellManager.GetMethod(
            "AddSpellEffect",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { abilityEffect },
            null), "AddSpellEffectPrefix");
        MethodInfo getCue = audioManager.GetMethod(
            "GetCue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { banks, typeof(int) },
            null);
        PatchGeneric(harmony, getCue, "GetCuePrefix", cue);
        Patch(harmony, cue.GetMethod("Play", Type.EmptyTypes), "CuePlayPrefix");

        MethodInfo startEffect = effectManager.GetMethod(
            "StartEffect",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] {
                typeof(int), vector.MakeByRefType(), vector.MakeByRefType(),
                visualReference.MakeByRefType()
            },
            null);
        PatchGeneric(
            harmony,
            startEffect,
            "StartEffectPrefix",
            visualReference);
        Patch(harmony, renderManager.GetProperty(
            "Saturation").GetSetMethod(), "SaturationPrefix");

        TimeWarpProbe.SpellManager = NewUninitialized(spellManager);
        TimeWarpProbe.AudioManager = NewUninitialized(audioManager);
        TimeWarpProbe.EffectManager = NewUninitialized(effectManager);
        TimeWarpProbe.RenderManager = NewUninitialized(renderManager);
        TimeWarpProbe.Cue = NewUninitialized(cue);
        TimeWarpProbe.CastSource = Activator.CreateInstance(matrix);
        TimeWarpProbe.Direction = Activator.CreateInstance(vector);
    }

    private static void PatchGetter(
        HarmonyInstance harmony,
        Type type,
        string propertyName,
        string prefixName)
    {
        MethodInfo target = type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        PatchGeneric(harmony, target, prefixName, target.ReturnType);
    }

    private static void Patch(
        HarmonyInstance harmony,
        MethodInfo target,
        string prefixName)
    {
        if (target == null)
            throw new MissingMethodException(prefixName);
        harmony.Patch(
            target,
            new HarmonyMethod(typeof(TimeWarpProbe).GetMethod(prefixName)),
            null,
            null);
    }

    private static void PatchGeneric(
        HarmonyInstance harmony,
        MethodInfo target,
        string prefixName,
        Type typeArgument)
    {
        if (target == null)
            throw new MissingMethodException(prefixName);
        MethodInfo prefix = typeof(TimeWarpProbe).GetMethod(
            prefixName).MakeGenericMethod(typeArgument);
        harmony.Patch(target, new HarmonyMethod(prefix), null, null);
    }
}

internal sealed class SpellCasterProxy : RealProxy
{
    internal SpellCasterProxy(Type interfaceType)
        : base(interfaceType)
    {
    }

    public override IMessage Invoke(IMessage message)
    {
        IMethodCallMessage call = (IMethodCallMessage)message;
        MethodInfo method = (MethodInfo)call.MethodBase;
        object result = null;
        if (method.Name == "get_CastSource")
            result = TimeWarpProbe.CastSource;
        else if (method.Name == "get_Direction")
            result = TimeWarpProbe.Direction;
        else if (method.ReturnType.IsValueType)
            result = Activator.CreateInstance(method.ReturnType);
        return new ReturnMessage(
            result,
            new object[0],
            0,
            call.LogicalCallContext,
            call);
    }
}

public static class TimeWarpProbe
{
    public static bool Enabled;
    public static object SpellManager;
    public static object AudioManager;
    public static object EffectManager;
    public static object RenderManager;
    public static object Cue;
    public static object CastSource;
    public static object Direction;
    public static int AddEffectCalls;
    public static int GetCueCalls;
    public static int CuePlayCalls;
    public static int StartVisualEffectCalls;
    public static int SaturationWrites;
    public static float LastSaturation;

    public static void Reset()
    {
        AddEffectCalls = 0;
        GetCueCalls = 0;
        CuePlayCalls = 0;
        StartVisualEffectCalls = 0;
        SaturationWrites = 0;
        LastSaturation = 0f;
    }

    public static bool SpellManagerPrefix<T>(ref T __result)
    {
        if (!Enabled)
            return true;
        __result = (T)SpellManager;
        return false;
    }

    public static bool AudioManagerPrefix<T>(ref T __result)
    {
        if (!Enabled)
            return true;
        __result = (T)AudioManager;
        return false;
    }

    public static bool EffectManagerPrefix<T>(ref T __result)
    {
        if (!Enabled)
            return true;
        __result = (T)EffectManager;
        return false;
    }

    public static bool RenderManagerPrefix<T>(ref T __result)
    {
        if (!Enabled)
            return true;
        __result = (T)RenderManager;
        return false;
    }

    public static bool IsEffectActivePrefix(ref bool __result)
    {
        if (!Enabled)
            return true;
        __result = false;
        return false;
    }

    public static bool AddSpellEffectPrefix()
    {
        if (!Enabled)
            return true;
        AddEffectCalls++;
        return false;
    }

    public static bool GetCuePrefix<T>(ref T __result)
    {
        if (!Enabled)
            return true;
        GetCueCalls++;
        __result = (T)Cue;
        return false;
    }

    public static bool CuePlayPrefix()
    {
        if (!Enabled)
            return true;
        CuePlayCalls++;
        return false;
    }

    public static bool StartEffectPrefix<TReference>(
        ref bool __result,
        ref TReference __3)
    {
        if (!Enabled)
            return true;
        StartVisualEffectCalls++;
        __3 = default(TReference);
        __result = true;
        return false;
    }

    public static bool SaturationPrefix(float __0)
    {
        if (!Enabled)
            return true;
        SaturationWrites++;
        LastSaturation = __0;
        return false;
    }
}
