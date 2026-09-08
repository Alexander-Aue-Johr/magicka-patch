using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class RailgunParentCycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        RailgunParentCycleHarness harness =
            new RailgunParentCycleHarness(magicka, runtimePatchEnabled);
        report.Add(
            "railgun.parent_cycle_candidate",
            harness.ParentCycleCandidate());
        report.Add(
            "railgun.acyclic_candidate",
            harness.AcyclicCandidate());
        report.Add(
            "railgun.parent_check_limit",
            harness.ParentCheckLimit());
        report.Add("railgun.lock_cycle", harness.LockCycle());
        report.Add("railgun.lock_acyclic", harness.LockAcyclic());
    }
}

internal sealed class RailgunParentCycleHarness
{
    private readonly Type railgunType;
    private readonly FieldInfo parentsField;
    private readonly FieldInfo lockedField;
    private readonly FieldInfo manualLockGuardField;
    private readonly MethodInfo manualCycleCheck;
    private readonly MethodInfo lockAll;
    private readonly bool runtimePatchEnabled;

    internal RailgunParentCycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        railgunType = magicka.GetType(
            "Magicka.GameLogic.Spells.Railgun",
            true);
        parentsField = RequireField("mParents");
        lockedField = RequireField("mLocked");
        manualLockGuardField = railgunType.GetField(
            "mCommunityPatchLockAllActive",
            BindingFlags.Instance | BindingFlags.NonPublic);
        manualCycleCheck = railgunType.GetMethod(
            "CommunityPatchWouldCreateParentCycle",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new Type[] { railgunType },
            null);
        lockAll = railgunType.GetMethod(
            "LockAll",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (lockAll == null)
            throw new MissingMethodException(railgunType.FullName, "LockAll");
    }

    internal ScenarioResult ParentCycleCandidate()
    {
        object current = NewRailgun();
        object parent = NewRailgun();
        object ancestor = NewRailgun();
        AddParent(current, parent);
        AddParent(parent, ancestor);
        bool rejected = WouldCreateParentCycle(current, ancestor);
        return Result(
            rejected,
            rejected ? "rejected" : "accepted",
            "rejected");
    }

    internal ScenarioResult AcyclicCandidate()
    {
        object current = NewRailgun();
        object parent = NewRailgun();
        object candidate = NewRailgun();
        AddParent(current, parent);
        bool rejected = WouldCreateParentCycle(current, candidate);
        return Result(!rejected, rejected ? "rejected" : "accepted", "accepted");
    }

    internal ScenarioResult ParentCheckLimit()
    {
        object current = NewRailgun();
        object node = current;
        for (int index = 0; index < 257; index++)
        {
            object parent = NewRailgun();
            AddParent(node, parent);
            node = parent;
        }
        object candidate = NewRailgun();
        bool rejected = WouldCreateParentCycle(current, candidate);
        return Result(
            rejected,
            rejected ? "rejected" : "accepted",
            "rejected");
    }

    internal ScenarioResult LockCycle()
    {
        if (!runtimePatchEnabled && manualLockGuardField == null)
            return new ScenarioResult(false, "unguarded", "locked:11");

        object first = NewRailgun();
        object second = NewRailgun();
        AddParent(first, second);
        AddParent(second, first);
        InvokeLockAll(first);
        string actual = "locked:" +
            (ReadLocked(first) ? 1 : 0) +
            (ReadLocked(second) ? 1 : 0);
        return Result(actual == "locked:11", actual, "locked:11");
    }

    internal ScenarioResult LockAcyclic()
    {
        object first = NewRailgun();
        object second = NewRailgun();
        AddParent(first, second);
        InvokeLockAll(first);
        string actual = "locked:" +
            (ReadLocked(first) ? 1 : 0) +
            (ReadLocked(second) ? 1 : 0);
        return Result(actual == "locked:11", actual, "locked:11");
    }

    private bool WouldCreateParentCycle(object current, object candidate)
    {
        if (runtimePatchEnabled)
        {
            return Magicka.CommunityPatch.Runtime.RailgunParentCyclePatch
                .WouldCreateParentCycle(current, candidate);
        }
        if (manualCycleCheck == null)
            return false;
        return (bool)manualCycleCheck.Invoke(current, new object[] { candidate });
    }

    private void InvokeLockAll(object railgun)
    {
        try
        {
            lockAll.Invoke(railgun, null);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private object NewRailgun()
    {
        object railgun = FormatterServices.GetUninitializedObject(railgunType);
        parentsField.SetValue(
            railgun,
            Activator.CreateInstance(parentsField.FieldType));
        return railgun;
    }

    private void AddParent(object railgun, object parent)
    {
        ((IList)parentsField.GetValue(railgun)).Add(parent);
    }

    private bool ReadLocked(object railgun)
    {
        return (bool)lockedField.GetValue(railgun);
    }

    private FieldInfo RequireField(string name)
    {
        FieldInfo field = railgunType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(railgunType.FullName, name);
        return field;
    }

    private static ScenarioResult Result(
        bool passed,
        string actual,
        string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
