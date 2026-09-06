namespace Magicka.CommunityPatch
{
	internal static class WidescreenSafeArea
	{
		private const int ReferenceWidth = 16;
		private const int ReferenceHeight = 9;

		internal static float GetHorizontalInset(int screenWidth, int screenHeight)
		{
			int safeWidth = screenHeight * ReferenceWidth / ReferenceHeight;
			if (screenWidth <= safeWidth)
			{
				return 0f;
			}
			return (screenWidth - safeWidth) * 0.5f;
		}

		internal static float GetRightAlignedCentre(int screenWidth, int screenHeight, float contentWidth)
		{
			int safeWidth = screenHeight * ReferenceWidth / ReferenceHeight;
			if (safeWidth > screenWidth)
			{
				safeWidth = screenWidth;
			}
			float safeLeft = (screenWidth - safeWidth) * 0.5f;
			return safeLeft + safeWidth * 0.95f - contentWidth * 0.5f;
		}
	}
}
