namespace SmartData.Server.Tracking;

/// <summary>
/// The unique-<c>PrevHash</c> retry loop exceeded its cap. Indicates catastrophic
/// contention on the target ledger table; surfaces to the caller with the source
/// mutation rolled back.
/// </summary>
public sealed class LedgerRetryExhaustedException : TrackingException
{
    public string TableName { get; }
    public int Attempts { get; }

    public LedgerRetryExhaustedException(string tableName, int attempts)
        : base($"Ledger insert on '{tableName}' exhausted {attempts} retry attempts on UNIQUE(PrevHash). " +
               $"Catastrophic contention — check for misbehaving concurrent writers.")
    {
        TableName = tableName;
        Attempts = attempts;
    }
}

/// <summary>
/// An <see cref="ITrackingErrorHandler"/> returned
/// <see cref="TrackingErrorDisposition.Suppress"/> for a <c>[Ledger]</c> table.
/// The integrity claim is all-or-nothing — suppression is rejected. If the
/// escape hatch is genuinely needed, downgrade the table with
/// <c>sp_ledger_drop(table, confirm)</c> first.
/// </summary>
public sealed class LedgerSuppressNotAllowedException : TrackingException
{
    public string TableName { get; }
    public LedgerSuppressNotAllowedException(string tableName)
        : base($"Suppress disposition rejected on ledgered table '{tableName}'. " +
               "Downgrade via sp_ledger_drop if a gap is genuinely acceptable.")
    {
        TableName = tableName;
    }
}

/// <summary>
/// <c>CanonicalBytes</c> on a ledger row could not be deserialized — truncated,
/// malformed, or incompatible with the declared <c>FormatVersion</c>. Raised by
/// <see cref="LedgerEntity{T}.Deserialize"/> and
/// <see cref="LedgerEntity{T}.VerifySelfHash"/>.
/// </summary>
public sealed class LedgerCorruptBytesException : TrackingException
{
    public string TableName { get; }
    public long LedgerId { get; }

    public LedgerCorruptBytesException(string tableName, long ledgerId, Exception? inner = null)
        : base(Msg(tableName, ledgerId), inner ?? new Exception("malformed"))
    {
        TableName = tableName;
        LedgerId = ledgerId;
    }

    private static string Msg(string t, long id) =>
        $"Ledger row {id} on '{t}' has malformed CanonicalBytes — cannot deserialize.";
}

/// <summary>
/// A ledger row's <c>FormatVersion</c> is newer than the current build can
/// parse. Bumped only across binary-incompatible <c>BinarySerializer</c> changes.
/// </summary>
public sealed class LedgerFormatVersionMismatchException : TrackingException
{
    public string TableName { get; }
    public long LedgerId { get; }
    public byte FormatVersion { get; }

    public LedgerFormatVersionMismatchException(string tableName, long ledgerId, byte formatVersion)
        : base($"Ledger row {ledgerId} on '{tableName}' has FormatVersion={formatVersion} — " +
               "this build does not know how to read that version.")
    {
        TableName = tableName;
        LedgerId = ledgerId;
        FormatVersion = formatVersion;
    }
}

/// <summary>
/// Verification detected a broken forward-link or self-hash mismatch. Carries
/// the first bad <c>LedgerId</c> and the failure kind for programmatic
/// recovery.
/// </summary>
public sealed class LedgerChainBrokenException : TrackingException
{
    public string TableName { get; }
    public long FirstBadLedgerId { get; }
    public string FailureKind { get; }

    public LedgerChainBrokenException(string tableName, long firstBadLedgerId, string failureKind)
        : base($"Ledger chain on '{tableName}' is broken at LedgerId {firstBadLedgerId}: {failureKind}.")
    {
        TableName = tableName;
        FirstBadLedgerId = firstBadLedgerId;
        FailureKind = failureKind;
    }
}

/// <summary>
/// Startup boot-time self-hash check on the chain head failed. Writes refused
/// for this table until an operator explicitly acknowledges (CLI command or
/// config flag).
/// </summary>
public sealed class LedgerChainHeadInvalidException : TrackingException
{
    public string TableName { get; }
    public long LedgerId { get; }
    public LedgerChainHeadInvalidException(string tableName, long ledgerId)
        : base($"Ledger '{tableName}' chain head at LedgerId {ledgerId} failed self-hash check. " +
               "Writes refused; operator action required.")
    {
        TableName = tableName;
        LedgerId = ledgerId;
    }
}
