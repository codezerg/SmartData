# SmartData.Core

Shared library containing the binary serialization protocol and API models used by both SmartData.Client and SmartData.Server.

- **Target:** .NET 10
- **No external dependencies**
- **Used by:** SmartData.Client, SmartData.Server, SmartData.Console

## Project Structure

```
SmartData.Core/
├── Api/
│   ├── CommandRequest.cs
│   └── CommandResponse.cs
├── BinarySerialization/
│   ├── Attributes.cs
│   ├── BinarySerializer.cs
│   ├── BinarySerializationReader.cs
│   ├── BinarySerializationWriter.cs
│   ├── BinaryDataReader.cs
│   ├── BinaryToJson.cs
│   ├── ReadAheadStream.cs
│   └── TypeMarkers.cs
├── IdGenerator.cs
└── VoidResult.cs              Shared return type for procedures with no payload
```

**`VoidResult`** — framework-level convention for procedures whose outcome lives on an entity (e.g. `SysScheduleRun.Outcome`) rather than in the return value. Single static `Instance` property. Used by system procedures like `sp_schedule_execute`, `sp_scheduler_tick`, `sp_schedule_cancel`, `sp_schedule_run_retention`. Application-layer procedures may opt in the same way.

## API Models

### CommandRequest

Request message sent from client to server via `/rpc`.

| Property | Type | Description |
|----------|------|-------------|
| `Command` | `string` | Procedure name (e.g. `sp_customer_list`) |
| `Token` | `string?` | Authentication token |
| `Database` | `string?` | Target database name |
| `Args` | `byte[]?` | Binary-serialized argument dictionary |

### CommandResponse

Response message returned by the server.

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Whether the call succeeded |
| `Data` | `byte[]?` | Binary-serialized result payload |
| `Error` | `string?` | Error message on failure |

Helper methods:
- `Ok(object?)` / `Fail(string)` — static factory methods
- `GetData<T>()` — deserializes `Data` into a typed result
- `GetDataAsJson()` — converts `Data` to a JSON string

## Binary Serialization Protocol

Custom compact binary format (similar to MessagePack) for efficient client-server data transfer. Uses single-byte type markers, big-endian byte order, and variable-length integer encoding.

### BinarySerializer (High-Level API)

```csharp
// Serialize
byte[] bytes = BinarySerializer.Serialize(myObject);
BinarySerializer.Serialize(stream, myObject);

// Deserialize
var obj = BinarySerializer.Deserialize<MyType>(bytes);
var obj = BinarySerializer.Deserialize<MyType>(stream);
```

Internal helpers `ReadDynamicValue`, `ReadNil`, `ReadDynamicArray`, and `ReadDynamicMap` are `internal` (used by `BinaryDataReader`).

#### Supported Types

- **Primitives:** bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, decimal, string
- **Special:** byte[], DateTime, DateTimeOffset, TimeSpan, Guid
- **Collections:** arrays, `List<T>`, `Dictionary<K,V>`, IEnumerable
- **Data:** DataTable, DataSet, IDataReader
- **Enums:** serialized as int64
- **Custom objects:** via reflection on public properties

### SerializerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseKeyInterning` | `bool` | `true` | Intern repeated property name keys |
| `IncludeFields` | `bool` | `false` | Serialize public fields in addition to properties |

### Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[BinarySerializable]` | Class/Struct | Marks type as serializable. Options: `UseKeyInterning`, `IncludeFields` |
| `[BinaryProperty]` | Property/Field | Custom serialized `Name` and `Order` (lower = first) |
| `[BinaryIgnore]` | Property/Field | Excludes member from serialization |

### BinarySerializationWriter (Low-Level API)

For manual binary construction:

```csharp
using var writer = new BinarySerializationWriter(stream);
writer.WriteMapHeader(2);
writer.WriteKey("name");
writer.Write("test");
writer.WriteKey("id");
writer.Write(123);
writer.Flush();
```

Key methods:
- **Primitives:** `Write(bool)`, `Write(int)`, `Write(string)`, `Write(ReadOnlySpan<byte>)`, etc.
- **Collections:** `WriteArrayHeader(count)`, `WriteMapHeader(count)`
- **Unbounded:** `BeginArray()`, `BeginMap()`, `WriteEnd()`
- **Key interning:** `SetKey(string)`, `UseKey(int)`, `WriteKey(string)`
- **Struct templates:** `DefineStruct(params string[])`, `UseStruct(int)`
- **Cache:** `ClearKeys()`, `ClearStructs()`, `ClearAll()`

### BinarySerializationReader (Low-Level API)

For manual binary parsing:

```csharp
using var reader = new BinarySerializationReader(stream);
var type = reader.PeekType();
int count = reader.ReadMapHeader();
string key = reader.ReadKey();
int value = reader.ReadInt32();
```

Key methods:
- **Inspect:** `PeekType()`, `ReadType()`, `IsEnd()`
- **Read:** `ReadBoolean()`, `ReadInt32()`, `ReadInt64()`, `ReadString()`, `ReadBinary()`, etc.
- **Collections:** `ReadArrayHeader()`, `ReadMapHeader()`
- **Navigation:** `Skip()`, `ProcessCommand()`, `ReadEnd()`

### BinaryDataReader (Streaming Row Reader)

Streaming row-by-row reader for binary-serialized tabular data. Reads data written by `BinarySerializer.Serialize()` for `IDataReader` or `DataTable`. Supports both bounded (fixed count) and unbounded (`BeginArray`/`End`) arrays. Non-seekable streams (e.g. zip entries) are automatically wrapped with `ReadAheadStream`.

```csharp
using var reader = new BinaryDataReader(stream);
while (reader.HasMore)
{
    var row = reader.Read();       // single row as Dictionary<string, object?>
    if (row == null) break;
}

// Or batch read:
var batch = reader.Read(1000);     // up to 1000 rows
```

| Member | Description |
|--------|-------------|
| `BinaryDataReader(Stream, SerializerOptions?)` | Constructor. Wraps non-seekable streams automatically. |
| `HasMore` | `true` if there may be more rows to read |
| `Read()` | Returns next row as `Dictionary<string, object?>`, or `null` when done |
| `Read(int count)` | Returns up to `count` rows. Fewer if stream ends early. |

### ReadAheadStream

Thin `Stream` wrapper with a 1-byte lookahead buffer that enables `BinarySerializationReader`'s `PeekType()`/`IsEnd()` to work on non-seekable streams (e.g. zip entry streams). Only the `Seek(-1, Current)` pattern used by the reader is supported; reports `CanSeek = true`.

```csharp
var wrapped = new ReadAheadStream(nonSeekableStream);
// Now PeekType() works via Seek(-1, Current) push-back
```

### SerializedType (Enum)

Values returned by `PeekType()`: `Nil`, `Boolean`, `Integer`, `Float`, `String`, `Binary`, `Array`, `Map`, `Command`, `Key`, `Struct`, `End`, `EndOfStream`, `Unknown`.

### BinaryToJson (Utility)

```csharp
string json = BinaryToJson.Convert(binaryBytes, maxValueLength: 100);
```

Converts binary-serialized data directly to JSON. Useful for debugging and logging.

## Type Markers (Wire Format)

| Range | Type | Encoding |
|-------|------|----------|
| `0x00-0x7F` | Positive fixint | Value encoded directly (0–127) |
| `0x80-0x8F` | Fixmap | Lower 4 bits = entry count (0–15) |
| `0x90-0x9F` | Fixarray | Lower 4 bits = element count (0–15) |
| `0xA0-0xBF` | Fixstr | Lower 5 bits = byte length (0–31) |
| `0xC0` | Nil | null |
| `0xC1-0xC2` | Boolean | false / true |
| `0xC3-0xC5` | Binary | Bin8 / Bin16 / Bin32 (length-prefixed) |
| `0xC6-0xC7` | Float | Float32 / Float64 |
| `0xC8-0xCF` | Integer | UInt8–UInt64, Int8–Int64 |
| `0xD0-0xD2` | String | Str8 / Str16 / Str32 (length-prefixed) |
| `0xD3-0xD6` | Collection | Array16 / Array32 / Map16 / Map32 |
| `0xE0-0xEF` | Negative fixint | -16 to -1 |
| `0xF0-0xF9` | Commands | Key interning, struct templates, cache control |

### Commands

| Marker | Name | Description |
|--------|------|-------------|
| `0xF0` | SET_KEY | Define an interned key (ID + string) |
| `0xF1` | USE_KEY | Reference an interned key by ID |
| `0xF2` | DEFINE_STRUCT | Define a reusable struct template |
| `0xF3` | USE_STRUCT | Instantiate a defined struct |
| `0xF4` | CLEAR_KEYS | Reset key intern table |
| `0xF5` | CLEAR_STRUCTS | Reset struct table |
| `0xF6` | CLEAR_ALL | Reset all tables |
| `0xF7` | BEGIN_ARRAY | Start unbounded array |
| `0xF8` | END | End unbounded collection |
| `0xF9` | BEGIN_MAP | Start unbounded map |

## Security

`ReaderLimits` prevents denial-of-service via oversized payloads:

| Limit | Default |
|-------|---------|
| `MaxStringLength` | 10 MB |
| `MaxBinaryLength` | 100 MB |
| `MaxKeyTableSize` | 10,000 |
| `MaxStructTableSize` | 1,000 |
| `MaxDepth` | 100 |

## Request/Response Flow

1. Client builds `CommandRequest` with command name, token, database, and binary-serialized args
2. `CommandRequest` itself is binary-serialized and POSTed to `/rpc`
3. Server deserializes request, executes the procedure
4. Server wraps result in `CommandResponse.Ok(result)` (or `.Fail(error)`)
5. `CommandResponse` is binary-serialized back to the client
6. Client deserializes and calls `response.GetData<T>()` for typed access
