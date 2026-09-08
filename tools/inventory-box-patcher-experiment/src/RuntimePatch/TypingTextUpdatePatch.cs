using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TypingTextUpdatePatch
    {
        private static FieldInfo nextCharField;
        private static FieldInfo visibleCharactersField;
        private static FieldInfo charIndexField;
        private static FieldInfo typeSpeedField;
        private static FieldInfo textField;
        private static FieldInfo primitiveCountField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "TypingText malformed-state recovery",
                "org.magickacommunitypatch.typing-text-update",
                FindUpdate,
                target => typeof(TypingTextUpdatePatch).GetMethod("Prefix"));

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type typingText = targetAssembly.GetType(
                "Magicka.Graphics.TypingText",
                true);
            nextCharField = RequireField(typingText, "mNextChar", typeof(float));
            visibleCharactersField = RequireField(
                typingText,
                "mVisibleCharacters",
                typeof(int));
            charIndexField = RequireField(typingText, "mCharIndex", typeof(int));
            typeSpeedField = RequireField(typingText, "mTypeSpeed", typeof(float));
            textField = RequireFieldInHierarchy(typingText, "mText", typeof(char[]));
            primitiveCountField = RequireFieldInHierarchy(
                typingText,
                "mPrimitiveCount",
                typeof(int));

            MethodInfo method = typingText.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(typingText.FullName, "Update");
            return method;
        }

        public static bool Prefix(object __instance, float iDeltaTime)
        {
            float nextChar = (float)nextCharField.GetValue(__instance);
            int visibleCharacters =
                (int)visibleCharactersField.GetValue(__instance);
            int charIndex = (int)charIndexField.GetValue(__instance);
            float typeSpeed = (float)typeSpeedField.GetValue(__instance);
            char[] text = (char[])textField.GetValue(__instance);
            int primitiveCount = (int)primitiveCountField.GetValue(__instance);

            try
            {
                nextChar -= iDeltaTime;
                while (nextChar < 0f &&
                    visibleCharacters < primitiveCount / 2)
                {
                    charIndex++;
                    char character = text[charIndex];
                    if (character == '[')
                    {
                        charIndex++;
                        character = text[charIndex];
                        if (character != '[')
                        {
                            if (Char.ToLowerInvariant(character) == 'p')
                            {
                                charIndex++;
                                character = text[charIndex];
                                if (character != '=')
                                    throw new Exception("Syntax Error!");

                                float pause = 0f;
                                while (true)
                                {
                                    charIndex++;
                                    character = text[charIndex];
                                    if (!Char.IsDigit(character))
                                        break;
                                    pause *= 10f;
                                    pause += character - '0';
                                }

                                if (character == '.')
                                {
                                    float fraction = 0.1f;
                                    while (true)
                                    {
                                        charIndex++;
                                        character = text[charIndex];
                                        if (!Char.IsDigit(character))
                                            break;
                                        pause += (character - '0') * fraction;
                                        fraction *= 0.1f;
                                    }
                                    if (character != ']')
                                        throw new Exception("Syntax Error!");
                                }
                                else if (character != ']')
                                {
                                    throw new Exception("Syntax Error!");
                                }
                                nextChar += pause;
                            }
                            else
                            {
                                while (character != ']')
                                {
                                    charIndex++;
                                    character = text[charIndex];
                                }
                            }
                        }
                        else
                        {
                            visibleCharacters++;
                            nextChar += 1f / typeSpeed;
                        }
                    }
                    else if (IsPunctuation(character) &
                        (charIndex + 1 < text.Length &&
                            Char.IsWhiteSpace(text[charIndex + 1])))
                    {
                        visibleCharacters++;
                        nextChar += 0.25f;
                    }
                    else if (character != '\n')
                    {
                        visibleCharacters++;
                        nextChar += 1f / typeSpeed;
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                if (primitiveCount > 0)
                    visibleCharacters = primitiveCount / 2;
                charIndex = text != null && text.Length != 0
                    ? text.Length - 1
                    : -1;
                nextChar = Single.PositiveInfinity;
            }

            nextCharField.SetValue(__instance, nextChar);
            visibleCharactersField.SetValue(__instance, visibleCharacters);
            charIndexField.SetValue(__instance, charIndex);
            return false;
        }

        private static bool IsPunctuation(char character)
        {
            return character == '!' || character == ',' || character == '.' ||
                character == ':' || character == ';' || character == '?' ||
                character == '\u2026' || character == '\u203c';
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
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static FieldInfo RequireFieldInHierarchy(
            Type type,
            string name,
            Type fieldType)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == fieldType)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }
}
