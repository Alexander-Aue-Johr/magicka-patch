// Readable source equivalent for the helper injected into
// Magicka.Graphics.NotifierButton. The actual type remains in Magicka.exe.

namespace Magicka.Graphics
{
	public sealed partial class NotifierButton
	{
		internal void ReleaseLevelReferences()
		{
			mAlpha = 0f;
			mTargetAlpha = 0f;
			mOwner = null;
			mDialogAttach = null;
		}
	}
}
