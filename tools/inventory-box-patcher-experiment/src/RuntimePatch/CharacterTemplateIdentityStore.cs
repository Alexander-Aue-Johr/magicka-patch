using System;
using System.Collections.Generic;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterTemplateIdentityStore
    {
        private sealed class Entry
        {
            internal readonly WeakReference Character;
            internal int TemplateId;

            internal Entry(object character, int templateId)
            {
                Character = new WeakReference(character);
                TemplateId = templateId;
            }
        }

        private static readonly object Sync = new object();
        private static readonly List<Entry> Entries = new List<Entry>();

        internal static void Remember(object character, int templateId)
        {
            if (character == null)
                return;
            lock (Sync)
            {
                for (int index = Entries.Count - 1; index >= 0; index--)
                {
                    object target = Entries[index].Character.Target;
                    if (target == null)
                    {
                        Entries.RemoveAt(index);
                        continue;
                    }
                    if (Object.ReferenceEquals(target, character))
                    {
                        Entries[index].TemplateId = templateId;
                        return;
                    }
                }
                Entries.Add(new Entry(character, templateId));
            }
        }

        internal static bool TryGet(object character, out int templateId)
        {
            templateId = 0;
            if (character == null)
                return false;
            lock (Sync)
            {
                for (int index = Entries.Count - 1; index >= 0; index--)
                {
                    object target = Entries[index].Character.Target;
                    if (target == null)
                    {
                        Entries.RemoveAt(index);
                        continue;
                    }
                    if (Object.ReferenceEquals(target, character))
                    {
                        templateId = Entries[index].TemplateId;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
