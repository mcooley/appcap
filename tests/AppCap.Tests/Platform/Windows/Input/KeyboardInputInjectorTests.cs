using AppCap.Windows;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AppCap.Tests;

public sealed class KeyboardInputInjectorTests
{
    [Theory]
    [InlineData(KeyboardKey.Shift)]
    [InlineData(KeyboardKey.Control)]
    [InlineData(KeyboardKey.Alt)]
    [InlineData(KeyboardKey.Windows)]
    public void StandaloneModifierCreatesKeyPress(KeyboardKey key)
    {
        VIRTUAL_KEY expectedVirtualKey = key switch
        {
            KeyboardKey.Shift => VIRTUAL_KEY.VK_SHIFT,
            KeyboardKey.Control => VIRTUAL_KEY.VK_CONTROL,
            KeyboardKey.Alt => VIRTUAL_KEY.VK_MENU,
            KeyboardKey.Windows => VIRTUAL_KEY.VK_LWIN,
            _ => throw new InvalidOperationException(),
        };
        INPUT[] inputs = KeyboardInputInjector.CreateKeyPressInputs(new KeyPressKeyboardAction([], key));

        Assert.Collection(
            inputs,
            input =>
            {
                Assert.Equal(expectedVirtualKey, input.Anonymous.ki.wVk);
                KEYBD_EVENT_FLAGS expectedFlags = key == KeyboardKey.Windows
                    ? KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY
                    : 0;
                Assert.Equal(expectedFlags, input.Anonymous.ki.dwFlags);
            },
            input =>
            {
                Assert.Equal(expectedVirtualKey, input.Anonymous.ki.wVk);
                KEYBD_EVENT_FLAGS expectedFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
                if (key == KeyboardKey.Windows)
                {
                    expectedFlags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
                }

                Assert.Equal(expectedFlags, input.Anonymous.ki.dwFlags);
            });
    }

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

    [Fact]
    public void PhysicalCommaUsesOemCommaKey()
    {
        INPUT[] inputs = KeyboardInputInjector.CreateKeyPressInputs(
            new KeyPressKeyboardAction([KeyboardModifier.Control], KeyboardKey.Comma));

        Assert.Equal(VIRTUAL_KEY.VK_CONTROL, inputs[0].Anonymous.ki.wVk);
        Assert.Equal(VIRTUAL_KEY.VK_OEM_COMMA, inputs[1].Anonymous.ki.wVk);
        Assert.Equal(VIRTUAL_KEY.VK_OEM_COMMA, inputs[2].Anonymous.ki.wVk);
        Assert.Equal(VIRTUAL_KEY.VK_CONTROL, inputs[3].Anonymous.ki.wVk);
    }
}