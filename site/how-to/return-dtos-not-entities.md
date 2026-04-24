---
title: Return DTOs, not entities
description: Procedures should return shaped contracts — not raw entity classes.
---

A procedure's `TResult` should be a **DTO in the `Contracts/` folder**, not the entity class.

```csharp
// ❌ leaks the whole table
public class CustomerList : StoredProcedure<List<Customer>> { /* ... */ }

// ✅ typed result with only the columns callers need
public class CustomerList : StoredProcedure<CustomerListResult> { /* ... */ }
```

## Why

1. **Columns you don't intend to send.** Entities include audit fields, soft-delete flags, internal notes. DTOs send only what the caller needs.
2. **Shape stability.** Renaming an entity column shouldn't break every caller. DTOs decouple the wire from storage.
3. **Pagination and metadata.** A list result needs `Total`, `Page`, `PageSize` alongside `Items` — not a bare list.
4. **Denormalised joins.** Related data (contacts, notes) belongs nested in the result, not scattered across entities.

## Folder layout

One folder per procedure, named after the procedure class. Shared result types in `Common/`:

```
Contracts/
├── Common/
│   ├── SaveResult.cs
│   └── DeleteResult.cs
├── CustomerList/
│   ├── CustomerListResult.cs
│   └── CustomerItem.cs
└── CustomerGet/
    └── CustomerGetResult.cs        # includes nested contact/note items
```

## List result

```csharp
namespace MyApp.Contracts.CustomerList;

public class CustomerListResult
{
    public List<CustomerItem> Items    { get; set; } = new();
    public int                Total    { get; set; }
    public int                Page     { get; set; }
    public int                PageSize { get; set; }
}

public class CustomerItem
{
    public int    Id           { get; set; }
    public string CompanyName  { get; set; } = "";
    public string Industry     { get; set; } = "";
    public string Status       { get; set; } = "";
}
```

## Detail result with nested data

```csharp
namespace MyApp.Contracts.CustomerGet;

public class CustomerGetResult
{
    public int      Id          { get; set; }
    public string   CompanyName { get; set; } = "";
    public DateTime CreatedOn   { get; set; }
    public List<CustomerContactItem> Contacts { get; set; } = new();
    public List<CustomerNoteItem>    Notes    { get; set; } = new();
}

public class CustomerContactItem
{
    public int    Id      { get; set; }
    public string Name    { get; set; } = "";
    public string Email   { get; set; } = "";
    public string Role    { get; set; } = "";
}
```

## Shared CRUD result types

```csharp
namespace MyApp.Contracts.Common;

public class SaveResult
{
    public string Message { get; set; } = "";
    public int    Id      { get; set; }
}

public class DeleteResult
{
    public string Message { get; set; } = "";
}
```

## Conventions

- Initialise non-nullable strings to `""`.
- Initialise collections to `new()`.
- Nullable fields → `?`.
- Flat — no methods, no computed properties, no logic.

## How the binary serializer maps this

Properties are matched **by name, case-insensitive**. The procedure result type and the caller's contract type don't need to be the same type — they just need matching property names. This is what makes contracts work across project boundaries without a shared library.

## Related

- [Procedures](/fundamentals/procedures/) — what `TResult` is
- [Binary RPC](/fundamentals/binary-rpc/) — how the DTO gets across the wire
- [Call procedures from a client](/how-to/call-procedures-from-a-client/) — consumer side
