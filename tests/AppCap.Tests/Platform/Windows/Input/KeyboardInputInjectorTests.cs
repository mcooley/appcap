using AppCap.Windows;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AppCap.Tests;

public sealed class KeyboardInputInjectorTests
{
    [Fact]
    public void UnicodeInputsPreserveEveryCharacter()
    {
        const string Text = "AppCap test";

        INPUT[] inputs = KeyboardInputInjector.CreateUnicodeInputs(Text);

        Assert.Equal(Text.Length * 2, inputs.Length);
        for (int index = 0; index < Text.Length; index++)
        {
            KEYBDINPUT keyDown = inputs[index * 2].Anonymous.ki;
            KEYBDINPUT keyUp = inputs[(index * 2) + 1].Anonymous.ki;
            Assert.Equal(Text[index], keyDown.wScan);
            Assert.Equal(KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE, keyDown.dwFlags);
            Assert.Equal(Text[index], keyUp.wScan);
            Assert.Equal(KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, keyUp.dwFlags);
        }
    }
}