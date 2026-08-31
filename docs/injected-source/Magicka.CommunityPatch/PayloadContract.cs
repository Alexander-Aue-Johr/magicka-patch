using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Magicka.CommunityPatch
{
	public static class PayloadContract
	{
		public const string Id = "magicka-community-patch-payload-0.0.55-r1";

		public static bool IsPolygonHeadCompatible()
		{
			Type contract = Type.GetType(
				"PolygonHead.CommunityPatch.PayloadContract, PolygonHead",
				false);
			if (contract == null)
			{
				return false;
			}

			FieldInfo field = contract.GetField("Id");
			if (field == null || Id != field.GetRawConstantValue() as string)
			{
				return false;
			}

			Type renderScale = Type.GetType(
				"PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead",
				false);
			if (renderScale == null)
			{
				return false;
			}

			MethodInfo begin = renderScale.GetMethod(
				"Begin",
				new Type[]
				{
					typeof(GraphicsDevice),
					typeof(RenderTarget2D),
					typeof(Point)
				});
			MethodInfo end = renderScale.GetMethod(
				"End",
				new Type[]
				{
					typeof(GraphicsDevice),
					typeof(RenderTarget2D),
					typeof(DepthStencilBuffer),
					typeof(Point)
				});
			return begin != null
				&& begin.IsStatic
				&& begin.ReturnType == typeof(Point)
				&& end != null
				&& end.IsStatic
				&& end.ReturnType == typeof(void);
		}
	}
}
