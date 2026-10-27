namespace IOToolkit
{
    /// <summary>Output write policy used by native SetDO overloads.</summary>
    public enum OutputWriteMode
    {
        /// <summary>Apply Key.SlewRate when configured; otherwise write immediately.</summary>
        Configured = 0,
        /// <summary>Always bypass Key.SlewRate.</summary>
        Immediate = 1,
        /// <summary>Use each Key's SafeValue/SafeStopPolicy/SafeStopRate.</summary>
        SafeStop = 2
    }
}
