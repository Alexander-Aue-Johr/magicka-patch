using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class CharacterTemplateLookupScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "Named CharacterTemplate fallback is a current-version patch.";
            report.AddNotApplicable("character_template_lookup.named_fallback", reason);
            report.AddNotApplicable("character_template_lookup.missing_owner", reason);
            report.AddNotApplicable("character_template_lookup.cached_hit", reason);
            report.AddNotApplicable("character_template_lookup.unknown_id", reason);
            return;
        }
        Harness harness = new Harness(magicka, runtimePatchEnabled);
        report.Add("character_template_lookup.named_fallback", harness.NamedFallback());
        report.Add("character_template_lookup.missing_owner", harness.MissingOwner());
        report.Add("character_template_lookup.cached_hit", harness.CachedHit());
        report.Add("character_template_lookup.unknown_id", harness.UnknownId());
    }

    private sealed class Harness
    {
        private readonly bool runtime;
        private readonly Type templateType;
        private readonly MethodInfo get;
        private readonly FieldInfo cacheField;
        private readonly FieldInfo manualNames;
        private readonly FieldInfo recentPlayState;
        private readonly IDictionary runtimeNames;

        internal Harness(Assembly magicka, bool runtimePatchEnabled)
        {
            runtime = runtimePatchEnabled;
            templateType = magicka.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate", true);
            get = templateType.GetMethod(
                "GetCachedTemplate", BindingFlags.Static | BindingFlags.Public,
                null, new Type[] { typeof(int) }, null);
            cacheField = templateType.GetField(
                "mCachedTemplates", BindingFlags.Static |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            manualNames = templateType.GetField(
                "mCachedTemplateNames", BindingFlags.Static |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Type playState = magicka.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            recentPlayState = playState.GetField(
                "sRecentPlayState", BindingFlags.Static |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (runtime)
            {
                Type patch = typeof(
                    Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                        "Magicka.CommunityPatch.Runtime.CharacterTemplateLookupPatch",
                        true);
                runtimeNames = (IDictionary)patch.GetField(
                    "Names", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            }
        }

        internal ScenarioResult NamedFallback()
        {
            bool available = manualNames != null || runtimeNames != null;
            return Result(available,
                available ? "available" : "missing", "available");
        }

        internal ScenarioResult MissingOwner()
        {
            if (manualNames == null && runtimeNames == null)
                return Result(false, "missing_fallback", "returned_null");
            SetEmptyCache();
            IDictionary names = Names();
            names.Clear();
            names.Add(912345, "Data/Characters/test_missing_owner");
            object previous = recentPlayState.GetValue(null);
            recentPlayState.SetValue(null, null);
            try
            {
                object value = get.Invoke(null, new object[] { 912345 });
                return Result(value == null,
                    value == null ? "returned_null" : "returned_value",
                    "returned_null");
            }
            catch (TargetInvocationException exception)
            {
                return Result(false,
                    exception.InnerException.GetType().Name, "returned_null");
            }
            finally
            {
                recentPlayState.SetValue(null, previous);
                names.Clear();
            }
        }

        internal ScenarioResult CachedHit()
        {
            IDictionary cache = SetEmptyCache();
            object template = FormatterServices.GetUninitializedObject(templateType);
            GC.SuppressFinalize(template);
            cache.Add(44, template);
            object value = get.Invoke(null, new object[] { 44 });
            return Result(Object.ReferenceEquals(value, template),
                Object.ReferenceEquals(value, template) ? "same" : "different",
                "same");
        }

        internal ScenarioResult UnknownId()
        {
            SetEmptyCache();
            IDictionary names = Names();
            if (names != null)
                names.Clear();
            object value = get.Invoke(null, new object[] { 778899 });
            return Result(value == null,
                value == null ? "null" : "value", "null");
        }

        private IDictionary SetEmptyCache()
        {
            IDictionary cache = (IDictionary)Activator.CreateInstance(
                cacheField.FieldType);
            cacheField.SetValue(null, cache);
            return cache;
        }

        private IDictionary Names()
        {
            if (runtimeNames != null)
                return runtimeNames;
            return manualNames == null
                ? null : (IDictionary)manualNames.GetValue(null);
        }

        private static ScenarioResult Result(
            bool passed, string actual, string expected)
        {
            return new ScenarioResult(passed, actual, expected);
        }
    }
}
