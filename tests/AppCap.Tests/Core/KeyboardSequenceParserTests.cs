namespace AppCap.Tests;

public sealed class KeyboardSequenceParserTests
{
    [Fact]
    public void ParsesLiteralText()
    {
        Assert.True(KeyboardSequenceParser.TryParse("hello world", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        TextKeyboardAction text = Assert.IsType<TextKeyboardAction>(Assert.Single(actions));
        Assert.Equal("hello world", text.Text);
    }

    [Fact]
    public void ParsesMixedTextAndKeyTokens()
    {
        Assert.True(KeyboardSequenceParser.TryParse("hello[Enter][Shift+F2]", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        Assert.Collection(
            actions,
            action => Assert.Equal("hello", Assert.IsType<TextKeyboardAction>(action).Text),
            action => Assert.Equal(KeyboardKey.Enter, Assert.IsType<KeyPressKeyboardAction>(action).Key),
            action =>
            {
                KeyPressKeyboardAction key = Assert.IsType<KeyPressKeyboardAction>(action);
                Assert.Equal([KeyboardModifier.Shift], key.Modifiers);
                Assert.Equal(KeyboardKey.F2, key.Key);
            });
    }

    [Fact]
    public void ParsesModifierAndKeyAliases()
    {
        Assert.True(KeyboardSequenceParser.TryParse("[Ctrl+A][Win+1]", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        Assert.Collection(
            actions,
            action =>
            {
                KeyPressKeyboardAction key = Assert.IsType<KeyPressKeyboardAction>(action);
                Assert.Equal([KeyboardModifier.Control], key.Modifiers);
                Assert.Equal(KeyboardKey.A, key.Key);
            },
            action =>
            {
                KeyPressKeyboardAction key = Assert.IsType<KeyPressKeyboardAction>(action);
                Assert.Equal([KeyboardModifier.Windows], key.Modifiers);
                Assert.Equal(KeyboardKey.Digit1, key.Key);
            });
    }

    [Theory]
    [InlineData("[Shift]", KeyboardKey.Shift)]
    [InlineData("[Control]", KeyboardKey.Control)]
    [InlineData("[Ctrl]", KeyboardKey.Control)]
    [InlineData("[Alt]", KeyboardKey.Alt)]
    [InlineData("[Windows]", KeyboardKey.Windows)]
    [InlineData("[Win]", KeyboardKey.Windows)]
    public void ParsesStandaloneModifierKey(string sequence, KeyboardKey expectedKey)
    {
        Assert.True(KeyboardSequenceParser.TryParse(sequence, out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        KeyPressKeyboardAction key = Assert.IsType<KeyPressKeyboardAction>(Assert.Single(actions));
        Assert.Empty(key.Modifiers);
        Assert.Equal(expectedKey, key.Key);
    }

    [Theory]
    [InlineData("[Control+,]", KeyboardKey.Comma)]
    [InlineData("[Control+Comma]", KeyboardKey.Comma)]
    [InlineData("[Control+Period]", KeyboardKey.Period)]
    [InlineData("[Control+Slash]", KeyboardKey.Slash)]
    [InlineData("[Control+Semicolon]", KeyboardKey.Semicolon)]
    [InlineData("[Control++]", KeyboardKey.Equal)]
    public void ParsesPhysicalPunctuationKeys(string sequence, KeyboardKey expectedKey)
    {
        Assert.True(KeyboardSequenceParser.TryParse(sequence, out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        KeyPressKeyboardAction key = Assert.IsType<KeyPressKeyboardAction>(Assert.Single(actions));
        Assert.Equal([KeyboardModifier.Control], key.Modifiers);
        Assert.Equal(expectedKey, key.Key);
    }

    [Fact]
    public void ParsesEscapedBrackets()
    {
        Assert.True(KeyboardSequenceParser.TryParse("open [[bracket]]", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Null(error);
        TextKeyboardAction text = Assert.IsType<TextKeyboardAction>(Assert.Single(actions));
        Assert.Equal("open [bracket]", text.Text);
    }

    [Fact]
    public void RejectsUnmatchedOpenBracket()
    {
        Assert.False(KeyboardSequenceParser.TryParse("hello[Enter", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Empty(actions);
        Assert.Equal("Keyboard sequence has an unmatched '['.", error);
    }

    [Fact]
    public void RejectsUnknownKey()
    {
        Assert.False(KeyboardSequenceParser.TryParse("[Shift+Nope]", out IReadOnlyList<KeyboardAction> actions, out string? error));

        Assert.Empty(actions);
        Assert.Equal("Unknown keyboard key 'Nope'.", error);
    }
}