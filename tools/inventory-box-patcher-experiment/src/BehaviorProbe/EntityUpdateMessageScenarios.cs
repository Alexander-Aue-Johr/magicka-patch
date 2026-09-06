using System;
using System.IO;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class EntityUpdateMessageScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        EntityUpdateMessageHarness harness = new EntityUpdateMessageHarness(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "entity_update.character_only",
            harness.CharacterOnly());
        report.Add(
            "entity_update.character_damageable",
            harness.CharacterAndDamageable());
        report.Add(
            "entity_update.no_features",
            harness.NoFeatures());
    }
}

internal sealed class EntityUpdateMessageHarness
{
    private const ushort ExpectedHandle = 0x1234;
    private const ushort ExpectedStamp = 0x5678;

    private readonly Type messageType;
    private readonly Type featuresType;
    private readonly byte packetType;
    private readonly bool maskCharacterMarker;
    private readonly MethodInfo prepareReader;
    private readonly MethodInfo read;
    private readonly FieldInfo handleField;
    private readonly FieldInfo stampField;
    private readonly FieldInfo featuresField;
    private readonly FieldInfo hitPointsField;
    private readonly ushort character;
    private readonly ushort damageable;

    internal EntityUpdateMessageHarness(Assembly magicka, bool runtimePatchEnabled)
    {
        messageType = magicka.GetType("Magicka.Network.EntityUpdateMessage", true);
        featuresType = magicka.GetType("Magicka.EntityFeatures", true);
        Type packetTypeType = magicka.GetType("Magicka.Network.PacketType", true);
        packetType = Convert.ToByte(Enum.Parse(packetTypeType, "EntityUpdate"));
        maskCharacterMarker = runtimePatchEnabled;
        Type patchType = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.EntityUpdateMessageReadPatch",
            true);
        prepareReader = patchType.GetMethod(
            "PrepareReader",
            BindingFlags.Static | BindingFlags.Public);
        read = messageType.GetMethod(
            "Read",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(BinaryReader) },
            null);
        handleField = RequireField("Handle");
        stampField = RequireField("UDPStamp");
        featuresField = RequireField("Features");
        hitPointsField = RequireField("HitPoints");
        character = Convert.ToUInt16(Enum.Parse(featuresType, "Character"));
        damageable = Convert.ToUInt16(Enum.Parse(featuresType, "Damageable"));
        if (read == null || read.ReturnType != typeof(void) || prepareReader == null)
            throw new MissingMethodException(messageType.FullName, "Read");
    }

    internal ScenarioResult CharacterOnly()
    {
        EntityUpdateReadResult result = Read(character, null);
        ushort expectedFeatures = maskCharacterMarker ? (ushort)0 : character;
        bool passed = result.Failure == null && result.Handle == ExpectedHandle &&
            result.Stamp == ExpectedStamp && result.Features == expectedFeatures &&
            result.Position == 6;
        return new ScenarioResult(
            passed,
            result.Describe(),
            "exception:none,handle:4660,stamp:22136,features:" + expectedFeatures +
                ",position:6,hit_points:0");
    }

    internal ScenarioResult CharacterAndDamageable()
    {
        ushort features = (ushort)(character | damageable);
        ushort expectedFeatures = maskCharacterMarker ? damageable : features;
        EntityUpdateReadResult result = Read(features, 123.5f);
        bool passed = result.Failure == null && result.Handle == ExpectedHandle &&
            result.Stamp == ExpectedStamp && result.Features == expectedFeatures &&
            result.Position == 10 && Math.Abs(result.HitPoints - 123.5f) < 0.0001f;
        return new ScenarioResult(
            passed,
            result.Describe(),
            "exception:none,handle:4660,stamp:22136,features:" + expectedFeatures +
                ",position:10,hit_points:123.5");
    }

    internal ScenarioResult NoFeatures()
    {
        EntityUpdateReadResult result = Read(0, null);
        bool passed = result.Failure == null && result.Handle == ExpectedHandle &&
            result.Stamp == ExpectedStamp && result.Features == 0 &&
            result.Position == 6;
        return new ScenarioResult(
            passed,
            result.Describe(),
            "exception:none,handle:4660,stamp:22136,features:0," +
                "position:6,hit_points:0");
    }

    private EntityUpdateReadResult Read(ushort features, float? hitPoints)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8);
        writer.Write(packetType);
        writer.Write(ExpectedHandle);
        writer.Write(ExpectedStamp);
        writer.Write(features);
        if (hitPoints.HasValue)
            writer.Write(hitPoints.Value);
        writer.Flush();
        stream.Position = 0;

        BinaryReader reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        if (maskCharacterMarker)
            prepareReader.Invoke(null, new object[] { reader });
        stream.Position = 1;

        object message = Activator.CreateInstance(messageType);
        Exception failure = null;
        try
        {
            read.Invoke(message, new object[] { reader });
        }
        catch (TargetInvocationException exception)
        {
            failure = exception.InnerException ?? exception;
        }
        EntityUpdateReadResult result = new EntityUpdateReadResult(
            failure,
            Convert.ToUInt16(handleField.GetValue(message)),
            Convert.ToUInt16(stampField.GetValue(message)),
            Convert.ToUInt16(featuresField.GetValue(message)),
            Convert.ToSingle(hitPointsField.GetValue(message)),
            stream.Position - 1);
        reader.Close();
        return result;
    }

    private FieldInfo RequireField(string name)
    {
        FieldInfo field = messageType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(messageType.FullName, name);
        return field;
    }
}

internal sealed class EntityUpdateReadResult
{
    internal Exception Failure { get; private set; }
    internal ushort Handle { get; private set; }
    internal ushort Stamp { get; private set; }
    internal ushort Features { get; private set; }
    internal float HitPoints { get; private set; }
    internal long Position { get; private set; }

    internal EntityUpdateReadResult(
        Exception failure,
        ushort handle,
        ushort stamp,
        ushort features,
        float hitPoints,
        long position)
    {
        Failure = failure;
        Handle = handle;
        Stamp = stamp;
        Features = features;
        HitPoints = hitPoints;
        Position = position;
    }

    internal string Describe()
    {
        return "exception:" +
            (Failure == null ? "none" : Failure.GetType().FullName) +
            ",handle:" + Handle +
            ",stamp:" + Stamp +
            ",features:" + Features +
            ",position:" + Position +
            ",hit_points:" + HitPoints;
    }
}
