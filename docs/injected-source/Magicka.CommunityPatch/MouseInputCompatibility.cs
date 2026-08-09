using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Magicka.CommunityPatch
{
	internal static class MouseInputCompatibility
	{
		internal static MouseState ScaleToLogicalResolution(MouseState state, Form form, GraphicsDevice graphicsDevice)
		{
			try
			{
				if (form == null || graphicsDevice == null || form.FormBorderStyle != FormBorderStyle.None)
				{
					return state;
				}

				PresentationParameters presentation = graphicsDevice.PresentationParameters;
				if (presentation == null || presentation.IsFullScreen)
				{
					return state;
				}

				Size clientSize = form.ClientSize;
				int logicalWidth = presentation.BackBufferWidth;
				int logicalHeight = presentation.BackBufferHeight;
				if (clientSize.Width <= 0 || clientSize.Height <= 0 ||
					logicalWidth <= 0 || logicalHeight <= 0 ||
					(clientSize.Width == logicalWidth && clientSize.Height == logicalHeight))
				{
					return state;
				}

				int x = ScaleCoordinate(state.X, clientSize.Width, logicalWidth);
				int y = ScaleCoordinate(state.Y, clientSize.Height, logicalHeight);
				return new MouseState(
					x,
					y,
					state.ScrollWheelValue,
					state.LeftButton,
					state.MiddleButton,
					state.RightButton,
					state.XButton1,
					state.XButton2);
			}
			catch
			{
				return state;
			}
		}

		private static int ScaleCoordinate(int coordinate, int physicalSize, int logicalSize)
		{
			long scaled = (long)coordinate * logicalSize / physicalSize;
			if (scaled < 0L)
			{
				return 0;
			}
			if (scaled >= logicalSize)
			{
				return logicalSize - 1;
			}
			return (int)scaled;
		}
	}
}
