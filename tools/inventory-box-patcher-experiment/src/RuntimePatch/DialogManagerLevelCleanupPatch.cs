using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DialogManagerLevelCleanupPatch
    {
        private static readonly object[] EmptyArguments = new object[0];

        private static MethodInfo setDialogs;
        private static MethodInfo endAll;
        private static FieldInfo textBoxesField;
        private static FieldInfo cutsceneTextField;
        private static FieldInfo additionalTextBoxesField;
        private static FieldInfo dialogsField;
        private static FieldInfo drawCutsceneTextField;
        private static FieldInfo cutsceneInteractField;
        private static FieldInfo drawSubtitlesField;
        private static FieldInfo subtitleHeightField;
        private static FieldInfo awaitingInputField;
        private static FieldInfo holdoffInputTimerField;
        private static FieldInfo ownerField;
        private static FieldInfo sceneField;
        private static FieldInfo automaticAdvanceField;
        private static FieldInfo timeToLiveField;
        private static FieldInfo growField;
        private static FieldInfo scaleField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "DialogManager level reference cleanup",
                "org.magickacommunitypatch.dialog-manager-level-cleanup",
                FindPlayStateDispose,
                typeof(DialogManagerLevelCleanupPatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type manager = targetAssembly.GetType(
                "Magicka.GameLogic.UI.DialogManager",
                true);
            Type textBox = targetAssembly.GetType(
                "Magicka.Graphics.TextBox",
                true);
            Type cutsceneText = targetAssembly.GetType(
                "Magicka.Graphics.CutsceneText",
                true);
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);

            textBoxesField = RequireField(
                manager,
                "mTextBoxes",
                textBox.MakeArrayType());
            cutsceneTextField = RequireField(
                manager,
                "mCutsceneText",
                cutsceneText);
            additionalTextBoxesField = RequireField(
                manager,
                "mAdditionalTextBoxes",
                null);
            if (!typeof(IList).IsAssignableFrom(
                additionalTextBoxesField.FieldType))
                throw new InvalidOperationException(
                    manager.FullName + ".mAdditionalTextBoxes is not an IList.");
            dialogsField = RequireReferenceField(manager, "mDialogs");
            drawCutsceneTextField = RequireField(
                manager,
                "mDrawCutsceneText",
                typeof(bool));
            cutsceneInteractField = RequireField(
                manager,
                "mCutsceneInteract",
                typeof(int));
            drawSubtitlesField = RequireField(
                manager,
                "mDrawSubtitles",
                typeof(bool));
            subtitleHeightField = RequireField(
                manager,
                "mSubtitleHeight",
                typeof(float));
            awaitingInputField = RequireField(
                manager,
                "mAwaitingInput",
                typeof(bool));
            holdoffInputTimerField = RequireField(
                manager,
                "mHoldoffInputTimer",
                typeof(float));

            ownerField = RequireField(textBox, "mOwner", entity);
            sceneField = RequireReferenceField(textBox, "mScene");
            automaticAdvanceField = RequireField(
                textBox,
                "mAutomaticAdvance",
                typeof(bool));
            timeToLiveField = RequireField(textBox, "mTTL", typeof(float));
            growField = RequireField(textBox, "mGrow", typeof(bool));
            scaleField = RequireField(textBox, "mScale", typeof(float));

            endAll = manager.GetMethod(
                "EndAll",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (endAll == null || endAll.ReturnType != typeof(void))
                throw new MissingMethodException(manager.FullName, "EndAll");

            setDialogs = FindSetDialogs(manager);
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
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

        private static MethodInfo FindSetDialogs(Type manager)
        {
            MethodInfo result = null;
            MethodInfo[] methods = manager.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "SetDialogs" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 1 ||
                    parameters[0].ParameterType.IsValueType)
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple DialogManager.SetDialogs methods matched.");
                result = methods[index];
            }
            if (result == null)
                throw new MissingMethodException(manager.FullName, "SetDialogs");
            return result;
        }

        private static FieldInfo RequireReferenceField(Type type, string name)
        {
            FieldInfo field = RequireField(type, name, null);
            if (field.FieldType.IsValueType)
                throw new InvalidOperationException(
                    type.FullName + "." + name + " is not a reference field.");
            return field;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null ||
                (fieldType != null && field.FieldType != fieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            MethodInfo replacement =
                typeof(DialogManagerLevelCleanupPatch).GetMethod(
                    "ResetForLevelUnload");
            for (int index = 0; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, setDialogs))
                    continue;
                result[index].opcode = OpCodes.Call;
                result[index].operand = replacement;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one DialogManager.SetDialogs call in " +
                    "PlayState.Dispose, found " + matches + ".");
            return result;
        }

        public static void ResetForLevelUnload(
            object manager,
            object ignoredDialogs)
        {
            if (manager == null)
                return;
            endAll.Invoke(manager, EmptyArguments);

            Array textBoxes = (Array)textBoxesField.GetValue(manager);
            if (textBoxes != null)
            {
                for (int index = 0; index < textBoxes.Length; index++)
                    ReleaseTextBox(textBoxes.GetValue(index));
            }
            ReleaseTextBox(cutsceneTextField.GetValue(manager));

            IList additional =
                (IList)additionalTextBoxesField.GetValue(manager);
            if (additional != null)
            {
                for (int index = 0; index < additional.Count; index++)
                    ReleaseTextBox(additional[index]);
                additional.Clear();
            }

            dialogsField.SetValue(manager, null);
            drawCutsceneTextField.SetValue(manager, false);
            cutsceneInteractField.SetValue(manager, 0);
            drawSubtitlesField.SetValue(manager, false);
            subtitleHeightField.SetValue(manager, 0f);
            awaitingInputField.SetValue(manager, false);
            holdoffInputTimerField.SetValue(manager, 0f);
        }

        private static void ReleaseTextBox(object textBox)
        {
            if (textBox == null)
                return;
            ownerField.SetValue(textBox, null);
            sceneField.SetValue(textBox, null);
            automaticAdvanceField.SetValue(textBox, false);
            timeToLiveField.SetValue(textBox, 0f);
            growField.SetValue(textBox, false);
            scaleField.SetValue(textBox, 0f);
        }
    }
}
