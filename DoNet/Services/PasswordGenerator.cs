using System;
using System.Security.Cryptography;

namespace DoNet.Services;

/// <summary>
/// AUTO GENERATE, on the two password fields.
/// </summary>
/// <remarks>
/// Twenty characters drawn from all four classes with a cryptographic RNG. These guard
/// real email accounts, so the generator uses <see cref="RandomNumberGenerator"/> rather
/// than <c>Random</c>, and rejection-samples rather than taking a modulus - a plain
/// <c>% alphabet.Length</c> makes the first few characters of the alphabet measurably
/// more likely, which is a real bias even if a small one.
/// </remarks>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";

    private const int Length = 20;

    public static string Generate()
    {
        string all = Lower + Upper + Digits + Symbols;
        char[] result = new char[Length];

        // One guaranteed character from each class, so the result always satisfies a
        // "must contain" rule, then fill the rest from the union.
        result[0] = Pick(Lower);
        result[1] = Pick(Upper);
        result[2] = Pick(Digits);
        result[3] = Pick(Symbols);

        for (int i = 4; i < Length; i++)
        {
            result[i] = Pick(all);
        }

        Shuffle(result);
        return new string(result);
    }

    private static char Pick(string alphabet) => alphabet[NextIndex(alphabet.Length)];

    /// <summary>Uniform in [0, bound) by rejection sampling.</summary>
    private static int NextIndex(int bound)
    {
        int limit = int.MaxValue - (int.MaxValue % bound);
        while (true)
        {
            int value = RandomNumberGenerator.GetInt32(0, int.MaxValue);
            if (value < limit)
            {
                return value % bound;
            }
        }
    }

    /// <summary>Fisher-Yates, so the guaranteed characters are not always first.</summary>
    private static void Shuffle(char[] buffer)
    {
        for (int i = buffer.Length - 1; i > 0; i--)
        {
            int j = NextIndex(i + 1);
            (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
    }
}
