namespace IL2FF.Support;

public static class Extensions
{
    public static int MinZero(this int value)
    {
        return value < 0 ? 0 : value;
    }

    public static void AddSafe<T>(this List<T> list, T? thing) where T : class
    {
        if (thing != null && !list.Contains(thing))
            list.Add(thing);
    }
}
