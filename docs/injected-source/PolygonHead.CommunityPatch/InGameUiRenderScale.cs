using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;

namespace PolygonHead.CommunityPatch
{
	public static class InGameUiRenderScale
	{
		private static bool sEnabled;
		private static bool sActive;
		private static GraphicsDevice sDevice;
		private static RenderTarget2D sUiTarget;
		private static DepthStencilBuffer sUiDepth;
		private static SpriteBatch sSpriteBatch;
		private static int sRenderThreadId = -1;
		private static float sScaleFactor = 2f;

		public static bool Enabled
		{
			get { return sEnabled; }
		}

		public static bool Active
		{
			get { return sActive; }
		}

		public static void SetEnabled(bool enabled)
		{
			sEnabled = enabled;
		}

		public static float ScaleFactor
		{
			get { return sScaleFactor; }
		}

		public static float GetScaleFactor()
		{
			return sScaleFactor;
		}

		public static void SetScale(float scaleFactor)
		{
			sScaleFactor = MathHelper.Clamp(scaleFactor, 1f, 4f);
		}

		public static bool ShouldScale(int width, int height)
		{
			return sEnabled && sScaleFactor > 1.001f && width >= 2560 && height >= 1440;
		}

		public static void AdjustProjectedPosition(ref Vector2 position)
		{
			if (IsUiRenderThread())
			{
				position.X /= sScaleFactor;
				position.Y /= sScaleFactor;
			}
		}

		public static void AdjustProjectedPosition(ref Vector2 position, Vector2 layoutOffset)
		{
			if (IsUiRenderThread())
			{
				position.X = (position.X - layoutOffset.X) / sScaleFactor + layoutOffset.X;
				position.Y = (position.Y - layoutOffset.Y) / sScaleFactor + layoutOffset.Y;
			}
		}

		public static Point GetScreenSize(Point fullSize)
		{
			if (!IsUiRenderThread())
			{
				return fullSize;
			}
			return new Point(Math.Max(1, (int)(fullSize.X / sScaleFactor + 0.5f)), Math.Max(1, (int)(fullSize.Y / sScaleFactor + 0.5f)));
		}

		public static float GetGuiScale(float fullScale)
		{
			return IsUiRenderThread() ? fullScale / sScaleFactor : fullScale;
		}

		public static Point Begin(GraphicsDevice device, RenderTarget2D fullTarget, Point fullSize)
		{
			sActive = device != null && ShouldScale(fullSize.X, fullSize.Y);
			if (!sActive)
			{
				return fullSize;
			}
			sRenderThreadId = Thread.CurrentThread.ManagedThreadId;

			Point uiSize = GetScreenSize(fullSize);
			EnsureResources(device, fullSize);
			device.SetRenderTarget(0, null);
			Texture2D sceneTexture = fullTarget.GetTexture();
			device.DepthStencilBuffer = null;
			device.SetRenderTarget(0, sUiTarget);
			sSpriteBatch.Begin(SpriteBlendMode.None, SpriteSortMode.Immediate, SaveStateMode.SaveState);
			sSpriteBatch.Draw(sceneTexture, new Rectangle(0, 0, fullSize.X, fullSize.Y), Color.White);
			sSpriteBatch.End();
			device.DepthStencilBuffer = sUiDepth;
			device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.TransparentBlack, 1f, 0);
			return uiSize;
		}

		public static void End(GraphicsDevice device, RenderTarget2D fullTarget, DepthStencilBuffer fullDepth, Point fullSize)
		{
			if (!sActive)
			{
				return;
			}

			device.SetRenderTarget(0, null);
			Texture2D uiTexture = sUiTarget.GetTexture();
			device.SetRenderTarget(0, fullTarget);
			device.DepthStencilBuffer = fullDepth;
			sSpriteBatch.Begin(SpriteBlendMode.None, SpriteSortMode.Immediate, SaveStateMode.SaveState);
			sSpriteBatch.Draw(uiTexture, new Rectangle(0, 0, fullSize.X, fullSize.Y), Color.White);
			sSpriteBatch.End();
			sActive = false;
			sRenderThreadId = -1;
		}

		private static bool IsUiRenderThread()
		{
			return sActive && Thread.CurrentThread.ManagedThreadId == sRenderThreadId;
		}

		private static void EnsureResources(GraphicsDevice device, Point fullSize)
		{
			if (sDevice == device && sUiTarget != null && !sUiTarget.IsDisposed &&
				sUiTarget.Width == fullSize.X && sUiTarget.Height == fullSize.Y &&
				sUiDepth != null && !sUiDepth.IsDisposed && sSpriteBatch != null)
			{
				return;
			}

			DisposeResources();
			sDevice = device;
			sUiTarget = new RenderTarget2D(device, fullSize.X, fullSize.Y, 1, SurfaceFormat.Color, MultiSampleType.None, 0, RenderTargetUsage.DiscardContents);
			sUiDepth = new DepthStencilBuffer(device, fullSize.X, fullSize.Y, DepthFormat.Depth24Stencil8, MultiSampleType.None, 0);
			sSpriteBatch = new SpriteBatch(device);
		}

		private static void DisposeResources()
		{
			if (sSpriteBatch != null)
			{
				sSpriteBatch.Dispose();
				sSpriteBatch = null;
			}
			if (sUiDepth != null)
			{
				sUiDepth.Dispose();
				sUiDepth = null;
			}
			if (sUiTarget != null)
			{
				sUiTarget.Dispose();
				sUiTarget = null;
			}
			sDevice = null;
		}
	}
}
