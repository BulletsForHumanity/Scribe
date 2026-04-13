using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     A single declarative check registered on a <see cref="FocusShape{TFocus}"/>.
///     Pairs a positive predicate (<see langword="true"/> = satisfied) with the
///     metadata needed to emit a diagnostic when violated. The focus itself is the
///     input — its cache-safe breadcrumb carries identity, and its embedded Roslyn
///     payload (symbol, attribute data, typed constant) is read from inside the
///     predicate body.
/// </summary>
internal sealed record FocusCheck<TFocus>(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity,
    Func<TFocus, Compilation, CancellationToken, bool> Predicate,
    Func<TFocus, EquatableArray<string>> MessageArgs)
    where TFocus : IEquatable<TFocus>;
