using SmartData.Server.Tracking;

namespace SmartData.Console.Models;

public class TrackingPageViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
    public string ActiveTab { get; set; } = "";

    /// <summary><c>"none"</c>, <c>"tracked"</c>, or <c>"ledger"</c> based on observed tables.</summary>
    public string Mode { get; set; } = "none";

    public bool HistoryExists { get; set; }
    public bool LedgerExists { get; set; }
}

public class HistoryListViewModel : TrackingPageViewModel
{
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
    public long Total { get; set; }
}

public class LedgerListViewModel : TrackingPageViewModel
{
    public List<LedgerRowView> Rows { get; set; } = new();
    public long Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
}

public class LedgerRowView
{
    public long LedgerId { get; set; }
    public long? HistoryId { get; set; }
    public string Kind { get; set; } = ""; // "Entity", "Schema change", "Pruned"
    public string OperationLabel { get; set; } = "";
    public DateTime ChangedOn { get; set; }
    public string ChangedBy { get; set; } = "";
    public string PrevHashHex { get; set; } = "";
    public string RowHashHex { get; set; } = "";
    public int CanonicalBytesLength { get; set; }
}

public class VerifyViewModel : TrackingPageViewModel
{
    public VerificationResult? Result { get; set; }
}

public class SchemaHistoryViewModel : TrackingPageViewModel
{
    public SchemaHistoryResult? Timeline { get; set; }
}
