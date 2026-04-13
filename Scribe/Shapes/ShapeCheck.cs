using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Internal, opaque representation of a single declarative check registered on a
///     <see cref="TypeShape"/>. A check pairs a positive <see cref="Predicate"/>
///     (<see langword="true"/> = shape satisfied) with the metadata needed to emit a
///     diagnostic when violated and the data needed to apply an automatic code fix.
/// </summary>
internal sealed record ShapeCheck(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity,
    SquiggleAt SquiggleAt,
    FixKind FixKind,
    Func<TypeFocus, Compilation, CancellationToken, bool> Predicate,
    Func<TypeFocus, EquatableArray<string>> MessageArgs,
    Func<TypeFocus, ImmutableDictionary<string, string?>>? FixProperties = null);
