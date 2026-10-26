internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Bytes(byte[] expected, byte[] actual)
    {
        That(
            expected.SequenceEqual(actual),
            $"Expected {Convert.ToHexString(expected)}; received {Convert.ToHexString(actual)}."
        );
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }

        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
