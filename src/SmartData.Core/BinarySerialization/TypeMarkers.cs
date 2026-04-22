namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Type markers and ranges for the binary serialization format.
/// </summary>
public static class TypeMarkers
{
    #region Fixint Ranges

    /// <summary>Minimum value for positive fixint range (0x00).</summary>
    public const byte PositiveFixintMin = 0x00;

    /// <summary>Maximum value for positive fixint range (0x7F). Values 0-127 are encoded directly.</summary>
    public const byte PositiveFixintMax = 0x7F;

    /// <summary>Minimum value for negative fixint range (0xE0).</summary>
    public const byte NegativeFixintMin = 0xE0;

    /// <summary>Maximum value for negative fixint range (0xEF). Encodes values -16 to -1.</summary>
    public const byte NegativeFixintMax = 0xEF;

    #endregion

    #region Fixed-size Collection Ranges

    /// <summary>Minimum marker for fixmap (0x80). Maps with 0-15 pairs.</summary>
    public const byte FixmapMin = 0x80;

    /// <summary>Maximum marker for fixmap (0x8F).</summary>
    public const byte FixmapMax = 0x8F;

    /// <summary>Minimum marker for fixarray (0x90). Arrays with 0-15 elements.</summary>
    public const byte FixarrayMin = 0x90;

    /// <summary>Maximum marker for fixarray (0x9F).</summary>
    public const byte FixarrayMax = 0x9F;

    /// <summary>Minimum marker for fixstr (0xA0). Strings with 0-31 bytes.</summary>
    public const byte FixstrMin = 0xA0;

    /// <summary>Maximum marker for fixstr (0xBF).</summary>
    public const byte FixstrMax = 0xBF;

    #endregion

    #region Nil and Boolean

    /// <summary>Nil/null marker (0xC0).</summary>
    public const byte Nil = 0xC0;

    /// <summary>Boolean false marker (0xC1).</summary>
    public const byte False = 0xC1;

    /// <summary>Boolean true marker (0xC2).</summary>
    public const byte True = 0xC2;

    #endregion

    #region Binary Data

    /// <summary>Binary data with 8-bit length prefix (0xC3).</summary>
    public const byte Bin8 = 0xC3;

    /// <summary>Binary data with 16-bit length prefix (0xC4).</summary>
    public const byte Bin16 = 0xC4;

    /// <summary>Binary data with 32-bit length prefix (0xC5).</summary>
    public const byte Bin32 = 0xC5;

    #endregion

    #region Floating Point

    /// <summary>32-bit IEEE 754 float marker (0xC6).</summary>
    public const byte Float32 = 0xC6;

    /// <summary>64-bit IEEE 754 double marker (0xC7).</summary>
    public const byte Float64 = 0xC7;

    #endregion

    #region Unsigned Integers

    /// <summary>8-bit unsigned integer marker (0xC8).</summary>
    public const byte UInt8 = 0xC8;

    /// <summary>16-bit unsigned integer marker (0xC9).</summary>
    public const byte UInt16 = 0xC9;

    /// <summary>32-bit unsigned integer marker (0xCA).</summary>
    public const byte UInt32 = 0xCA;

    /// <summary>64-bit unsigned integer marker (0xCB).</summary>
    public const byte UInt64 = 0xCB;

    #endregion

    #region Signed Integers

    /// <summary>8-bit signed integer marker (0xCC).</summary>
    public const byte Int8 = 0xCC;

    /// <summary>16-bit signed integer marker (0xCD).</summary>
    public const byte Int16 = 0xCD;

    /// <summary>32-bit signed integer marker (0xCE).</summary>
    public const byte Int32 = 0xCE;

    /// <summary>64-bit signed integer marker (0xCF).</summary>
    public const byte Int64 = 0xCF;

    #endregion

    #region Strings

    /// <summary>String with 8-bit length prefix (0xD0).</summary>
    public const byte Str8 = 0xD0;

    /// <summary>String with 16-bit length prefix (0xD1).</summary>
    public const byte Str16 = 0xD1;

    /// <summary>String with 32-bit length prefix (0xD2).</summary>
    public const byte Str32 = 0xD2;

    #endregion

    #region Arrays

    /// <summary>Array with 16-bit element count (0xD3).</summary>
    public const byte Array16 = 0xD3;

    /// <summary>Array with 32-bit element count (0xD4).</summary>
    public const byte Array32 = 0xD4;

    #endregion

    #region Maps

    /// <summary>Map with 16-bit pair count (0xD5).</summary>
    public const byte Map16 = 0xD5;

    /// <summary>Map with 32-bit pair count (0xD6).</summary>
    public const byte Map32 = 0xD6;

    #endregion

    #region Commands

    /// <summary>SET_KEY command (0xF0). Defines a key for interning.</summary>
    public const byte SetKey = 0xF0;

    /// <summary>USE_KEY command (0xF1). References a previously defined key.</summary>
    public const byte UseKey = 0xF1;

    /// <summary>DEFINE_STRUCT command (0xF2). Defines a struct template.</summary>
    public const byte DefineStruct = 0xF2;

    /// <summary>USE_STRUCT command (0xF3). Uses a previously defined struct template.</summary>
    public const byte UseStruct = 0xF3;

    /// <summary>CLEAR_KEYS command (0xF4). Resets the key table.</summary>
    public const byte ClearKeys = 0xF4;

    /// <summary>CLEAR_STRUCTS command (0xF5). Resets the struct table.</summary>
    public const byte ClearStructs = 0xF5;

    /// <summary>CLEAR_ALL command (0xF6). Resets all tables.</summary>
    public const byte ClearAll = 0xF6;

    /// <summary>BEGIN_ARRAY command (0xF7). Starts an unbounded array.</summary>
    public const byte BeginArray = 0xF7;

    /// <summary>END command (0xF8). Ends an unbounded array or map.</summary>
    public const byte End = 0xF8;

    /// <summary>BEGIN_MAP command (0xF9). Starts an unbounded map.</summary>
    public const byte BeginMap = 0xF9;

    #endregion
}
