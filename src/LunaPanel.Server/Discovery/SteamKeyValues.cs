namespace LunaPanel.Server.Discovery;

/// <summary>
/// A minimal parser for Valve's KeyValues text format (the shape of
/// <c>libraryfolders.vdf</c> - not JSON, despite the similar bracing).
/// Confirmed against a real <c>libraryfolders.vdf</c> on the authoring
/// machine before this was written - see <c>ref/docs/discovery.md</c> for
/// what that file actually looked like structurally.
///
/// The grammar handled here: a sequence of "key" "value" pairs, where a
/// value is either a quoted string or a brace-delimited nested block of
/// more pairs. Line comments starting with <c>//</c> are skipped outside of
/// quotes. This is enough to read <c>libraryfolders.vdf</c>; it is not a
/// complete implementation of Valve's format (no <c>#include</c>/
/// <c>#base</c> macros, no conditional blocks).
/// </summary>
public static class SteamKeyValues
{
    /// <summary>
    /// One parsed KeyValues block. Each child is either a nested
    /// <see cref="KvNode"/> or a <see cref="string"/> leaf value. Duplicate
    /// keys at the same level keep the last occurrence, matching how a
    /// simple last-write-wins reader would behave - real
    /// <c>libraryfolders.vdf</c> files never repeat a library index, so this
    /// never matters in practice for this file.
    /// </summary>
    public sealed class KvNode
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private readonly List<string> _order = new();

        internal void Set(string key, object value)
        {
            if (!_values.ContainsKey(key))
            {
                _order.Add(key);
            }

            _values[key] = value;
        }

        /// <summary>Child keys in the order first seen.</summary>
        public IReadOnlyList<string> Keys => _order;

        public KvNode? GetNode(string key) => _values.TryGetValue(key, out var value) ? value as KvNode : null;

        public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value as string : null;
    }

    public static KvNode Parse(string text)
    {
        var tokens = Tokenize(text);
        var position = 0;
        return ParseBlock(tokens, ref position);
    }

    private static KvNode ParseBlock(IReadOnlyList<Token> tokens, ref int position)
    {
        var node = new KvNode();

        while (position < tokens.Count && tokens[position].Kind != TokenKind.CloseBrace)
        {
            var keyToken = tokens[position];
            if (keyToken.Kind != TokenKind.QuotedString)
            {
                // Not a well-formed "key" position - skip defensively rather
                // than throwing, since a malformed file should degrade, not
                // crash the whole discovery pass.
                position++;
                continue;
            }

            position++;

            if (position >= tokens.Count)
            {
                break;
            }

            if (tokens[position].Kind == TokenKind.OpenBrace)
            {
                position++; // consume '{'
                var child = ParseBlock(tokens, ref position);
                if (position < tokens.Count && tokens[position].Kind == TokenKind.CloseBrace)
                {
                    position++; // consume '}'
                }

                node.Set(keyToken.Value, child);
            }
            else if (tokens[position].Kind == TokenKind.QuotedString)
            {
                node.Set(keyToken.Value, tokens[position].Value);
                position++;
            }
            else
            {
                // A key with neither a quoted value nor a nested block -
                // malformed; skip just the key and keep going.
            }
        }

        return node;
    }

    private enum TokenKind
    {
        QuotedString,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                // Line comment - skip to end of line.
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '{')
            {
                tokens.Add(new Token(TokenKind.OpenBrace, "{"));
                i++;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new Token(TokenKind.CloseBrace, "}"));
                i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        // Valve's format escapes with a backslash - the
                        // character immediately after is taken literally
                        // (this is what turns the file's own "\\" into a
                        // single "\" in a Windows path).
                        sb.Append(text[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                }

                i++; // consume closing quote
                tokens.Add(new Token(TokenKind.QuotedString, sb.ToString()));
                continue;
            }

            // Any other stray character (shouldn't occur in a well-formed
            // file) - skip it rather than throwing.
            i++;
        }

        return tokens;
    }
}
