using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class LevelSceneTransitionPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        private static FieldInfo busyField;
        private static FieldInfo currentSceneField;
        private static FieldInfo nextSceneField;
        private static MethodInfo changeSceneMethod;
        private static MethodInfo loadLevelMethod;
        private static MethodInfo gameInstanceGetter;
        private static MethodInfo addLoadTaskMethod;
        private static MethodInfo disableRenderingMethod;
        private static MethodInfo enableRenderingMethod;
        private static MethodInfo renderingEnabledGetter;
        private static MethodInfo recentPlayStateGetter;
        private static PropertyInfo playStateBusyProperty;

        internal static readonly RuntimePatchDefinition GoToDefinition =
            RuntimePatchDefinition.Transpile(
                "Level serialized scene transition",
                "org.magickacommunitypatch.level-serialized-scene-transition",
                FindGoToScene,
                typeof(LevelSceneTransitionPatch).GetMethod("GoToTranspiler"));

        internal static readonly RuntimePatchDefinition FinishDefinition =
            RuntimePatchDefinition.Transpile(
                "Level serialized transition finish",
                "org.magickacommunitypatch.level-serialized-transition-finish",
                FindTransitionFinish,
                typeof(LevelSceneTransitionPatch).GetMethod("FinishTranspiler"));

        internal static readonly RuntimePatchDefinition ChangeDefinition =
            RuntimePatchDefinition.Transpile(
                "Level load before scene publication",
                "org.magickacommunitypatch.level-load-before-publication",
                FindChangeScene,
                typeof(LevelSceneTransitionPatch).GetMethod("ChangeTranspiler"));

        private static Type Configure(Assembly assembly)
        {
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            busyField = RequireField(level, "mBusy");
            currentSceneField = RequireField(level, "mCurrentScene");
            nextSceneField = RequireField(level, "mNextScene");
            changeSceneMethod = RequireMethod(level, "ChangeScene", Type.EmptyTypes);
            loadLevelMethod = RequireMethod(
                nextSceneField.FieldType, "LoadLevel", Type.EmptyTypes);

            Type game = assembly.GetType("Magicka.Game", true);
            gameInstanceGetter = game.GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public)
                .GetGetMethod();
            addLoadTaskMethod = RequireMethod(
                game, "AddLoadTask", new Type[] { typeof(Action) });
            disableRenderingMethod = RequireMethod(
                game, "DisableRendering", Type.EmptyTypes);
            enableRenderingMethod = RequireMethod(
                game, "EnableRendering", Type.EmptyTypes);
            renderingEnabledGetter = game.GetProperty(
                "RenderingEnabled", BindingFlags.Instance | BindingFlags.Public)
                .GetGetMethod();

            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            recentPlayStateGetter = playState.GetProperty(
                "RecentPlayState", BindingFlags.Static | BindingFlags.Public)
                .GetGetMethod();
            playStateBusyProperty = playState.GetProperty(
                "Busy", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (gameInstanceGetter == null || renderingEnabledGetter == null ||
                recentPlayStateGetter == null || playStateBusyProperty == null)
                throw new MissingMemberException("Scene transition contract");
            return level;
        }

        private static MethodInfo FindGoToScene(Assembly assembly)
        {
            Type level = Configure(assembly);
            MethodInfo[] methods = level.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "GoToScene" &&
                    methods[index].GetParameters().Length == 6)
                    return methods[index];
            throw new MissingMethodException(level.FullName, "GoToScene/6");
        }

        private static MethodInfo FindTransitionFinish(Assembly assembly)
        {
            Type level = Configure(assembly);
            MethodInfo[] methods = level.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "TransitionFinish" &&
                    methods[index].GetParameters().Length == 1)
                    return methods[index];
            throw new MissingMethodException(level.FullName, "TransitionFinish");
        }

        private static MethodInfo FindChangeScene(Assembly assembly)
        {
            Configure(assembly);
            return changeSceneMethod;
        }

        public static IEnumerable<CodeInstruction> GoToTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int firstBusy = FindFieldStore(result, busyField, 0);
            InsertCall(result, firstBusy + 1, "WaitForCurrentPlayStateBusy");

            int directChange = FindCall(result, changeSceneMethod, 0);
            InsertCall(result, directChange, "AwaitDisabledRendering");

            int queuedChange = FindCall(result, addLoadTaskMethod, directChange + 1);
            InsertCall(result, queuedChange - 4, "QueueAwaitDisabledRendering");
            queuedChange = FindCall(result, addLoadTaskMethod, queuedChange + 1);
            InsertCall(result, queuedChange + 1, "QueueEnableRendering");
            return result;
        }

        public static IEnumerable<CodeInstruction> FinishTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int queue = FindCall(result, addLoadTaskMethod, 0);
            InsertCall(result, queue - 4, "QueueAwaitDisabledRendering");
            queue = FindCall(result, addLoadTaskMethod, queue + 1);
            InsertCall(result, queue + 1, "QueueEnableRendering");
            return result;
        }

        public static IEnumerable<CodeInstruction> ChangeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int publish = FindFieldStore(result, currentSceneField, 0);
            result.Insert(publish - 2, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(publish - 1,
                new CodeInstruction(OpCodes.Ldfld, nextSceneField));
            result.Insert(publish,
                new CodeInstruction(OpCodes.Callvirt, loadLevelMethod));

            int oldLoad = FindCall(result, loadLevelMethod, publish + 3);
            if (oldLoad < 2 || result[oldLoad - 2].opcode != OpCodes.Ldarg_0 ||
                result[oldLoad - 1].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[oldLoad - 1].operand, nextSceneField))
                throw new InvalidOperationException(
                    "Level.ChangeScene load sequence changed.");
            result[oldLoad - 2].opcode = OpCodes.Nop;
            result[oldLoad - 2].operand = null;
            result[oldLoad - 1].opcode = OpCodes.Nop;
            result[oldLoad - 1].operand = null;
            result[oldLoad].opcode = OpCodes.Nop;
            result[oldLoad].operand = null;
            return result;
        }

        public static void WaitForCurrentPlayStateBusy()
        {
            while (true)
            {
                object state = recentPlayStateGetter.Invoke(null, null);
                if (state == null ||
                    (bool)playStateBusyProperty.GetValue(state, null))
                    return;
                Thread.Sleep(100);
            }
        }

        public static void AwaitDisabledRendering()
        {
            object game = gameInstanceGetter.Invoke(null, null);
            disableRenderingMethod.Invoke(game, null);
            while ((bool)renderingEnabledGetter.Invoke(game, null))
                Thread.Sleep(16);
        }

        public static void QueueAwaitDisabledRendering()
        {
            Queue(new Action(AwaitDisabledRendering));
        }

        public static void QueueEnableRendering()
        {
            Queue(new Action(EnableRendering));
        }

        private static void EnableRendering()
        {
            object game = gameInstanceGetter.Invoke(null, null);
            enableRenderingMethod.Invoke(game, null);
        }

        private static void Queue(Action action)
        {
            object game = gameInstanceGetter.Invoke(null, null);
            addLoadTaskMethod.Invoke(game, new object[] { action });
        }

        private static void InsertCall(
            List<CodeInstruction> body, int index, string name)
        {
            CodeInstruction call = new CodeInstruction(
                OpCodes.Call,
                typeof(LevelSceneTransitionPatch).GetMethod(name));
            if (index >= 0 && index < body.Count)
            {
                call.labels.AddRange(body[index].labels);
                call.blocks.AddRange(body[index].blocks);
                body[index].labels.Clear();
                body[index].blocks.Clear();
            }
            body.Insert(index, call);
        }

        private static int FindCall(
            List<CodeInstruction> body, MethodInfo method, int start)
        {
            for (int index = start; index < body.Count; index++)
                if ((body[index].opcode == OpCodes.Call ||
                        body[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(body[index].operand, method))
                    return index;
            throw new InvalidOperationException(
                "Expected call was not found: " + method.Name);
        }

        private static int FindFieldStore(
            List<CodeInstruction> body, FieldInfo field, int start)
        {
            for (int index = start; index < body.Count; index++)
                if (body[index].opcode == OpCodes.Stfld &&
                    Object.Equals(body[index].operand, field))
                    return index;
            throw new InvalidOperationException(
                "Expected field store was not found: " + field.Name);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type, string name, Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name, Members, null, parameters, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
