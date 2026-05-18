using System.Text;

namespace ContextOS.Embeddings;

/// <summary>
/// Minimal BERT WordPiece tokenizer for all-MiniLM-L6-v2 (uncased).
/// Handles English text well; CJK characters are not split at character level.
/// </summary>
internal sealed class BertWordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _unkId;

    // WordPiece emits [UNK] for words longer than this.
    private const int MaxWordChars = 100;

    public BertWordPieceTokenizer(string vocabPath)
    {
        _vocab = new Dictionary<string, int>(35_000, StringComparer.Ordinal);
        int idx = 0;
        foreach (string line in File.ReadLines(vocabPath))
        {
            string t = line.Trim();
            if (t.Length > 0)
                _vocab[t] = idx++;
        }
        _clsId = Lookup("[CLS]");
        _sepId = Lookup("[SEP]");
        _unkId = Lookup("[UNK]");
    }

    private int Lookup(string token) =>
        _vocab.TryGetValue(token, out int id) ? id : 0;

    /// <summary>
    /// Encodes <paramref name="text"/> as BERT input tensors (int64).
    /// Total length is capped at <paramref name="maxLength"/> including [CLS] and [SEP].
    /// </summary>
    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Encode(
        string text, int maxLength = 256)
    {
        var ids = new List<int>(maxLength) { _clsId };

        foreach (string word in BasicTokenize(text))
        {
            foreach (int id in WordPieceEncode(word))
            {
                if (ids.Count >= maxLength - 1)
                    goto done;
                ids.Add(id);
            }
        }
        done:
        ids.Add(_sepId);

        long[] inputIds = ids.Select(x => (long)x).ToArray();
        long[] attMask = new long[inputIds.Length];
        long[] typeIds = new long[inputIds.Length];
        attMask.AsSpan().Fill(1L);

        return (inputIds, attMask, typeIds);
    }

    private IEnumerable<int> WordPieceEncode(string word)
    {
        if (word.Length > MaxWordChars)
        {
            yield return _unkId;
            yield break;
        }

        if (_vocab.TryGetValue(word, out int fullId))
        {
            yield return fullId;
            yield break;
        }

        // Greedy longest-match subword segmentation
        int start = 0;
        var subIds = new List<int>(4);

        while (start < word.Length)
        {
            int end = word.Length;
            int found = -1;

            while (start < end)
            {
                string sub = start == 0 ? word[start..end] : "##" + word[start..end];
                if (_vocab.TryGetValue(sub, out int subId))
                {
                    found = subId;
                    break;
                }
                end--;
            }

            if (found == -1)
            {
                yield return _unkId;
                yield break;
            }

            subIds.Add(found);
            start = end;
        }

        foreach (int id in subIds)
            yield return id;
    }

    private static IEnumerable<string> BasicTokenize(string text)
    {
        text = text.ToLowerInvariant();
        var buf = new StringBuilder(64);

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (buf.Length > 0) { yield return buf.ToString(); buf.Clear(); }
            }
            else if (IsAsciiPunctuation(c))
            {
                if (buf.Length > 0) { yield return buf.ToString(); buf.Clear(); }
                yield return c.ToString();
            }
            else
            {
                buf.Append(c);
            }
        }

        if (buf.Length > 0)
            yield return buf.ToString();
    }

    // ASCII punctuation ranges, matching the original BERT basic tokenizer.
    private static bool IsAsciiPunctuation(char c) =>
        (c >= '!' && c <= '/') ||
        (c >= ':' && c <= '@') ||
        (c >= '[' && c <= '`') ||
        (c >= '{' && c <= '~');
}
