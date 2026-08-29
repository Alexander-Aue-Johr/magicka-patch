// Readable source equivalent for the focused IL change in
// Magicka.GameLogic.Player.DeinitializeGame. The actual type remains in
// Magicka.exe.

namespace Magicka.GameLogic
{
	public partial class Player
	{
		public void DeinitializeGame()
		{
			if (mObtainedTextBox != null)
			{
				mObtainedTextBox.ReleaseLevelReferences();
			}
		}
	}
}
