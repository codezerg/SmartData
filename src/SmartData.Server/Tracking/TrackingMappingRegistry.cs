using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using LinqToDB.Mapping;

namespace SmartData.Server.Tracking;

/// <summary>
/// Singleton that holds the fluent mappings for every tracked
/// <c>HistoryEntity&lt;T&gt;</c>. The mapping is lazily registered on first
/// write/read for each <typeparamref name="T"/>; the single shared
/// <see cref="MappingSchema"/> is attached to every <c>DataConnection</c> in
/// <c>DatabaseContext.GetOrCreateConnection</c>.
///
/// <para>
/// Design ported from <c>SmartApp.TrackingSpike/Program.cs</c>'s
/// <c>HistoryMappingRegistrar</c> — validated against linq2db 5.4.1 (see
/// <c>docs/SmartData.Server.Tracking.md</c> § Pre-implementation Spikes #3).
/// </para>
/// </summary>
public sealed class TrackingMappingRegistry
{
    private readonly MappingSchema _schema = new();
    private readonly FluentMappingBuilder _builder;
    private readonly ConcurrentDictionary<Type, Lazy<bool>> _registered = new();

    public TrackingMappingRegistry()
    {
        _builder = new FluentMappingBuilder(_schema);
    }

    public MappingSchema Schema => _schema;

    /// <summary>
    /// Eagerly register every <c>[Tracked]</c> / <c>[Ledger]</c> type found in
    /// the given assemblies, in a single <c>Build()</c> call. Call this at
    /// startup (from <c>UseSmartData</c>) so the fluent mapping is fully
    /// published before any request can race a first-write registration.
    /// LinqToDB caches compiled insert/select expressions statically; if the
    /// first cached expression for <c>HistoryEntity&lt;T&gt;</c> is built
    /// against a partial schema, every subsequent invocation reuses that bad
    /// descriptor — causing the exact "no such table: HistoryEntity`1" /
    /// "UNIQUE constraint failed: *.HistoryId" cascade we saw under stress.
    /// </summary>
    public void RegisterAll(IEnumerable<Type> trackedTypes)
    {
        lock (_builder)
        {
            var registered = false;
            foreach (var t in trackedTypes)
            {
                if (!IsTracked(t)) continue;
                if (!_registered.TryAdd(t, new Lazy<bool>(() => true))) continue;

                var register = typeof(TrackingMappingRegistry)
                    .GetMethod(nameof(RegisterHistoryShape), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(t);
                register.Invoke(this, null);

                var mode = (TrackingMode)typeof(TrackedEntityInfo<>)
                    .MakeGenericType(t).GetProperty(nameof(TrackedEntityInfo<object>.DeclaredMode))!
                    .GetValue(null)!;
                if (mode == TrackingMode.Ledger)
                {
                    var rl = typeof(TrackingMappingRegistry)
                        .GetMethod(nameof(RegisterLedgerShape), BindingFlags.NonPublic | BindingFlags.Instance)!
                        .MakeGenericMethod(t);
                    rl.Invoke(this, null);
                }
                registered = true;
            }
            if (registered) _builder.Build();
        }
    }

    private static bool IsTracked(Type t)
    {
        if (!t.IsClass || t.IsAbstract) return false;
        if (t.GetConstructor(Type.EmptyTypes) is null) return false;
        var attrs = t.GetCustomAttributes(inherit: false);
        return attrs.Any(a =>
            a.GetType().Name is "TrackedAttribute" or "LedgerAttribute");
    }

    /// <summary>
    /// Register history and (if applicable) ledger mapping for
    /// <typeparamref name="T"/>. Idempotent per T. Safe to call concurrently —
    /// the <see cref="Lazy{T}"/> barrier makes every concurrent caller block
    /// until the first one finishes <c>Build()</c>. A previous "TryAdd and
    /// bail" guard let racing threads proceed to insert against a
    /// half-registered schema, which caused LinqToDB to cache a fallback
    /// descriptor (no fluent mapping, no identity) for
    /// <c>HistoryEntity&lt;T&gt;</c> and corrupt every subsequent write.
    /// </summary>
    public void RegisterHistory<T>() where T : class, new()
    {
        var mode = TrackedEntityInfo<T>.DeclaredMode;
        if (mode == TrackingMode.None) return;

        _ = _registered.GetOrAdd(typeof(T), _ => new Lazy<bool>(() =>
        {
            lock (_builder)
            {
                RegisterHistoryShape<T>();
                if (mode == TrackingMode.Ledger) RegisterLedgerShape<T>();
                _builder.Build();
            }
            return true;
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private void RegisterHistoryShape<T>() where T : class, new()
    {
        var eb = _builder.Entity<HistoryEntity<T>>()
                         .HasTableName(TrackedEntityInfo<T>.HistoryTableName);

        eb.Property(h => h.HistoryId).HasColumnName("HistoryId").IsPrimaryKey().IsIdentity();
        eb.Property(h => h.Operation).HasColumnName("Operation");
        eb.Property(h => h.ChangedOn).HasColumnName("ChangedOn");
        eb.Property(h => h.ChangedBy).HasColumnName("ChangedBy");

        foreach (var p in TrackedEntityInfo<T>.MirroredProperties)
            MapDataColumn<T>(eb, p);
    }

    private void RegisterLedgerShape<T>() where T : class, new()
    {
        var eb = _builder.Entity<LedgerEntity<T>>()
                         .HasTableName(TrackedEntityInfo<T>.LedgerTableName);

        eb.Property(l => l.LedgerId).HasColumnName("LedgerId").IsPrimaryKey().IsIdentity();
        eb.Property(l => l.HistoryId).HasColumnName("HistoryId").IsNullable();
        eb.Property(l => l.FormatVersion).HasColumnName("FormatVersion");
        eb.Property(l => l.CanonicalBytes).HasColumnName("CanonicalBytes");
        eb.Property(l => l.PrevHash).HasColumnName("PrevHash");
        eb.Property(l => l.RowHash).HasColumnName("RowHash");
        eb.Member(l => l.TableName).IsNotColumn();
    }

    private static void MapDataColumn<T>(EntityMappingBuilder<HistoryEntity<T>> eb, PropertyInfo dataProp)
        where T : class, new()
    {
        var method = typeof(TrackingMappingRegistry)
            .GetMethod(nameof(MapDataColumnTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), dataProp.PropertyType);

        method.Invoke(null, new object[] { eb, dataProp });
    }

    private static void MapDataColumnTyped<T, TProp>(
        EntityMappingBuilder<HistoryEntity<T>> eb, PropertyInfo dataProp) where T : class, new()
    {
        // h => h.Data.<dataProp>
        var param = Expression.Parameter(typeof(HistoryEntity<T>), "h");
        var dataAccess = Expression.Property(param, nameof(HistoryEntity<T>.Data));
        var colAccess = Expression.Property(dataAccess, dataProp);
        var lambda = Expression.Lambda<Func<HistoryEntity<T>, TProp>>(colAccess, param);

        var pb = eb.Property(lambda).HasColumnName(dataProp.Name);
        if (IsNullableMember(dataProp)) pb.IsNullable();
    }

    /// <summary>
    /// True if a property should be treated as nullable in the history table.
    /// Handles both value-type nullable (<c>int?</c>) and reference-type
    /// nullability annotations (<c>string?</c> with NRT).
    /// </summary>
    private static bool IsNullableMember(PropertyInfo p)
    {
        if (Nullable.GetUnderlyingType(p.PropertyType) is not null) return true;
        if (p.PropertyType.IsValueType) return false;

        // Reference type — read the compiler-generated NullableAttribute or NullableContextAttribute.
        // Byte 1 in the flags = nullable, 2 = non-nullable. Missing = ambiguous (treat as nullable,
        // matches spike finding that SQL Server will tighten NOT NULL later via schema config).
        var nullableAttr = p.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
        if (nullableAttr is not null && nullableAttr.ConstructorArguments.Count == 1)
        {
            var arg = nullableAttr.ConstructorArguments[0];
            if (arg.Value is byte b) return b == 1;
            if (arg.Value is System.Collections.ObjectModel.ReadOnlyCollection<CustomAttributeTypedArgument> arr
                && arr.Count > 0 && arr[0].Value is byte first)
                return first == 1;
        }

        // Fall back to the declaring type's NullableContextAttribute.
        var ctxAttr = p.DeclaringType?.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        if (ctxAttr is not null && ctxAttr.ConstructorArguments.Count == 1
            && ctxAttr.ConstructorArguments[0].Value is byte ctxByte)
            return ctxByte == 1;

        return true; // conservative
    }
}
