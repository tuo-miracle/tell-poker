public static class CoreTestMain
{
    public static int Main(string[] args) => new NUnitLite.AutoRun(typeof(CoreTestMain).Assembly).Execute(args);
}
