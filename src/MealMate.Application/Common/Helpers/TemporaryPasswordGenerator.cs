namespace MealMate.Application.Common.Helpers;

public static class TemporaryPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Specials = "!@#$%&*";

    public static string Generate(int length = 10)
    {
        var random = Random.Shared;
        var chars = new char[length];

        chars[0] = Uppercase[random.Next(Uppercase.Length)];
        chars[1] = Lowercase[random.Next(Lowercase.Length)];
        chars[2] = Digits[random.Next(Digits.Length)];
        chars[3] = Specials[random.Next(Specials.Length)];

        const string allChars = Uppercase + Lowercase + Digits + Specials;
        for (int i = 4; i < length; i++)
        {
            chars[i] = allChars[random.Next(allChars.Length)];
        }

        return new string(chars.OrderBy(_ => random.Next()).ToArray());
    }
}
