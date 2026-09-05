using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Magicka.InventoryBoxRuntimePatch;

namespace Microsoft.Xna.Framework
{
    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}

namespace PolygonHead
{
    public sealed class RenderManager
    {
        private static readonly RenderManager instance = new RenderManager();

        public static RenderManager Instance
        {
            get { return instance; }
        }

        public Microsoft.Xna.Framework.Point ScreenSize { get; set; }
    }
}

namespace Magicka.Graphics.Effects
{
    public sealed class TextBoxEffect
    {
        public Microsoft.Xna.Framework.Vector2 ScreenSize { get; set; }
    }
}

namespace Magicka.GameLogic.UI
{
    public class InventoryBox
    {
        protected class RenderData
        {
            public Magicka.Graphics.Effects.TextBoxEffect mTextBoxEffect =
                new Magicka.Graphics.Effects.TextBoxEffect();

            private Microsoft.Xna.Framework.Vector2 mPosition;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Draw(float iDeltaTime)
            {
                Microsoft.Xna.Framework.Point screenSize = PolygonHead.RenderManager.Instance.ScreenSize;
                mPosition.X = (float)screenSize.X * 0.5f;
                mPosition.Y = (float)screenSize.Y * 0.5f;
                GC.KeepAlive(iDeltaTime);
            }
        }

        public static bool RunPatchedDraw()
        {
            RenderData renderData = new RenderData();
            renderData.Draw(0.016f);
            return renderData.mTextBoxEffect.ScreenSize.X == 1920f &&
                renderData.mTextBoxEffect.ScreenSize.Y == 1080f;
        }
    }
}

namespace Magicka.Network
{
    public enum TriggerActionType
    {
        SpawnNPC,
        Other
    }

    public struct TriggerActionMessage
    {
        public TriggerActionType ActionType;
        public ushort Handle;
    }

    public struct WorldSyncMessage
    {
        public enum WorldSyncMessageType
        {
            Begin,
            Message,
            End
        }

        public WorldSyncMessageType MessageType;
        public TriggerActionMessage TriggerMessage;
    }
}

namespace Magicka.GameLogic.Entities
{
    public class Entity
    {
        private static readonly Dictionary<int, Entity> handles =
            new Dictionary<int, Entity>();

        public bool IsDisposed { get; set; }
        public Magicka.GameLogic.GameStates.PlayState PlayState { get; set; }

        public static Entity GetFromHandle(int handle)
        {
            Entity entity;
            return handles.TryGetValue(handle, out entity) ? entity : null;
        }

        public static void Register(int handle, Entity entity)
        {
            handles[handle] = entity;
        }
    }

    public sealed class NonPlayerCharacter : Entity
    {
    }
}

namespace Magicka.GameLogic.GameStates
{
    public sealed class PlayState
    {
        private readonly Queue<Magicka.Network.WorldSyncMessage> mWorldSyncMessageQueue =
            new Queue<Magicka.Network.WorldSyncMessage>();

        public void AddWorldSyncMessage(Magicka.Network.WorldSyncMessage iMessage)
        {
            mWorldSyncMessageQueue.Enqueue(iMessage);
        }

        public int QueuedMessages
        {
            get { return mWorldSyncMessageQueue.Count; }
        }
    }
}

internal static class Program
{
    private static int Main()
    {
        PolygonHead.RenderManager.Instance.ScreenSize =
            new Microsoft.Xna.Framework.Point(1920, 1080);

        Bootstrap.Apply(Assembly.GetExecutingAssembly());

        bool inventoryBehaviorMatches = Magicka.GameLogic.UI.InventoryBox.RunPatchedDraw();
        bool playStateBehaviorMatches = RunPlayStateWorldSyncGuard();
        string auditPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "inventory-box-runtime-audit.txt");
        bool auditPassed = File.Exists(auditPath) &&
            File.ReadAllText(auditPath).Contains("result=PASS");

        bool behaviorMatches = inventoryBehaviorMatches && playStateBehaviorMatches;
        Console.WriteLine("inventory_behavior=" + (inventoryBehaviorMatches ? "PASS" : "FAIL"));
        Console.WriteLine("play_state_behavior=" + (playStateBehaviorMatches ? "PASS" : "FAIL"));
        Console.WriteLine("audit=" + (auditPassed ? "PASS" : "FAIL"));
        return behaviorMatches && auditPassed ? 0 : 1;
    }

    private static bool RunPlayStateWorldSyncGuard()
    {
        Magicka.GameLogic.GameStates.PlayState playState =
            new Magicka.GameLogic.GameStates.PlayState();
        Magicka.GameLogic.GameStates.PlayState otherPlayState =
            new Magicka.GameLogic.GameStates.PlayState();

        Magicka.Network.WorldSyncMessage ordinary = default(Magicka.Network.WorldSyncMessage);
        ordinary.MessageType = Magicka.Network.WorldSyncMessage.WorldSyncMessageType.Begin;
        playState.AddWorldSyncMessage(ordinary);

        Magicka.Network.WorldSyncMessage missingSpawn = SpawnNpcMessage(10);
        playState.AddWorldSyncMessage(missingSpawn);

        Magicka.GameLogic.Entities.Entity.Register(
            11,
            new Magicka.GameLogic.Entities.NonPlayerCharacter { PlayState = playState });
        playState.AddWorldSyncMessage(SpawnNpcMessage(11));

        Magicka.GameLogic.Entities.Entity.Register(
            12,
            new Magicka.GameLogic.Entities.NonPlayerCharacter { PlayState = otherPlayState });
        playState.AddWorldSyncMessage(SpawnNpcMessage(12));

        return playState.QueuedMessages == 2;
    }

    private static Magicka.Network.WorldSyncMessage SpawnNpcMessage(ushort handle)
    {
        Magicka.Network.WorldSyncMessage message = default(Magicka.Network.WorldSyncMessage);
        message.MessageType = Magicka.Network.WorldSyncMessage.WorldSyncMessageType.Message;
        message.TriggerMessage.ActionType = Magicka.Network.TriggerActionType.SpawnNPC;
        message.TriggerMessage.Handle = handle;
        return message;
    }
}
