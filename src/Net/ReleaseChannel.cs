namespace BadNorthMP.Net
{
    /// <summary>
    /// Where this build gets its updates from, and where tools/package.ps1 publishes it. Each git branch sets its own:
    /// main builds update from the public mod's releases, the battleship branch's builds from its test repo, so neither
    /// is ever offered the other. This is the only file the two branches keep different, so merging main into battleship
    /// leaves it alone.
    /// </summary>
    internal static class ReleaseChannel
    {
        public const string Name = "public";
        public const string Repo = "BananaSplit101/BadNorthMastermind";
    }
}
