using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ChallengeScorePatch
    {
        private static readonly object CreditLock = new object();
        private static readonly List<WeakReference> CreditedEnemies =
            new List<WeakReference>();
        private static MethodInfo addScore;
        private static Action<object, int, int> callAddScore;

        internal static readonly RuntimePatchDefinition DamageDefinition =
            RuntimePatchDefinition.Transpile(
                "Challenge score direct-damage guard",
                "org.magickacommunitypatch.challenge-score-damage",
                assembly => FindStatisticsMethod(assembly, "InternalDamageEvent"),
                typeof(ChallengeScorePatch).GetMethod("Transpiler"));

        internal static readonly RuntimePatchDefinition KillDefinition =
            RuntimePatchDefinition.Transpile(
                "Challenge score kill-event guard",
                "org.magickacommunitypatch.challenge-score-kill",
                assembly => FindStatisticsMethod(assembly, "AddKillEvent"),
                typeof(ChallengeScorePatch).GetMethod("Transpiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Postfix(
                "Challenge score pooled-enemy reset",
                "org.magickacommunitypatch.challenge-score-initialize",
                FindNonPlayerCharacterInitialize,
                target => typeof(ChallengeScorePatch).GetMethod(
                    "InitializePostfix"));

        private static MethodInfo FindStatisticsMethod(
            Assembly targetAssembly,
            string methodName)
        {
            ConfigureAddScore(targetAssembly);
            Type statisticsType = targetAssembly.GetType(
                "Magicka.GameLogic.Statistics.StatisticsManager",
                true);
            MethodInfo[] matches = Array.FindAll(
                statisticsType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == methodName &&
                    method.ReturnType == typeof(void));
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one StatisticsManager." + methodName +
                    " method, found " + matches.Length + ".");
            return matches[0];
        }

        private static MethodInfo FindNonPlayerCharacterInitialize(
            Assembly targetAssembly)
        {
            Type npcType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            MethodInfo[] matches = Array.FindAll(
                npcType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.DeclaredOnly),
                IsNonPlayerCharacterInitialize);
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one NonPlayerCharacter.Initialize method, found " +
                    matches.Length + ".");
            return matches[0];
        }

        private static bool IsNonPlayerCharacterInitialize(MethodInfo method)
        {
            if (method.Name != "Initialize" || method.ReturnType != typeof(void))
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 4 &&
                parameters[0].ParameterType.FullName ==
                    "Magicka.GameLogic.Entities.CharacterTemplate" &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType.FullName ==
                    "Microsoft.Xna.Framework.Vector3" &&
                parameters[3].ParameterType == typeof(int);
        }

        private static void ConfigureAddScore(Assembly targetAssembly)
        {
            Type rulesetType = targetAssembly.GetType(
                "Magicka.Levels.SurvivalRuleset",
                true);
            addScore = rulesetType.GetMethod(
                "AddScore",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int), typeof(int) },
                null);
            if (addScore == null || addScore.ReturnType != typeof(void))
                throw new MissingMethodException(rulesetType.FullName, "AddScore");
            DynamicMethod adapter = new DynamicMethod(
                "ChallengeScoreAddScoreAdapter",
                typeof(void),
                new Type[] { typeof(object), typeof(int), typeof(int) },
                typeof(ChallengeScorePatch),
                true);
            ILGenerator il = adapter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, rulesetType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, addScore);
            il.Emit(OpCodes.Ret);
            callAddScore = (Action<object, int, int>)adapter.CreateDelegate(
                typeof(Action<object, int, int>));
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int callIndex = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, addScore))
                {
                    if (callIndex >= 0)
                        throw new InvalidOperationException(
                            "Multiple SurvivalRuleset.AddScore calls matched.");
                    callIndex = index;
                }
            }
            if (callIndex < 0)
                throw new InvalidOperationException(
                    "SurvivalRuleset.AddScore call was not found.");

            CodeInstruction enemyLoad = FindEnemyLoad(result, callIndex);
            result.Insert(callIndex, Clone(enemyLoad));
            result[callIndex + 1].opcode = OpCodes.Call;
            result[callIndex + 1].operand =
                typeof(ChallengeScorePatch).GetMethod("AddScoreOnce");
            return result;
        }

        public static void AddScoreOnce(
            object ruleset,
            int displayName,
            int score,
            object enemy)
        {
            if (TryCredit(enemy))
                callAddScore(ruleset, displayName, score);
        }

        public static bool TryCredit(object enemy)
        {
            if (enemy == null)
                return false;
            lock (CreditLock)
            {
                for (int index = CreditedEnemies.Count - 1; index >= 0; index--)
                {
                    object current = CreditedEnemies[index].Target;
                    if (current == null)
                    {
                        CreditedEnemies.RemoveAt(index);
                        continue;
                    }
                    if (Object.ReferenceEquals(current, enemy))
                        return false;
                }
                CreditedEnemies.Add(new WeakReference(enemy));
                return true;
            }
        }

        public static void InitializePostfix(object __instance)
        {
            Reset(__instance);
        }

        public static void Reset(object enemy)
        {
            if (enemy == null)
                return;
            lock (CreditLock)
            {
                for (int index = CreditedEnemies.Count - 1; index >= 0; index--)
                {
                    object current = CreditedEnemies[index].Target;
                    if (current == null || Object.ReferenceEquals(current, enemy))
                        CreditedEnemies.RemoveAt(index);
                }
            }
        }

        private static CodeInstruction FindEnemyLoad(
            IList<CodeInstruction> instructions,
            int addScoreCall)
        {
            int first = Math.Max(1, addScoreCall - 18);
            for (int index = addScoreCall - 1; index >= first; index--)
            {
                MethodInfo called = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    called != null &&
                    called.Name == "get_DisplayName" &&
                    IsLocalLoad(instructions[index - 1]))
                    return instructions[index - 1];
            }
            throw new InvalidOperationException(
                "The scored NonPlayerCharacter local was not found.");
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Ldloc_3;
        }

        private static CodeInstruction Clone(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }
    }
}
