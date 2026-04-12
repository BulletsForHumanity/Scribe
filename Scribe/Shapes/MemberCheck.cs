using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Scribe.Cache;

namespace Scribe.Shapes;

/// <summary>
///     Internal representation of a member-level check on a <see cref="ShapeBuilder"/>.
///     Runs per declared member of each matched type; emits one diagnostic per
///     member that satisfies <see cref="Match"/>.
/// </summary>
/// <remarks>
///     Distinct from <see cref="ShapeCheck"/> (type-level) in execution model:
///     a type-level check yields zero or one diagnostic; a member-level check
///     yields zero to <c>N</c> diagnostics — one per offending member.
/// </remarks>
internal sealed record MemberCheck(
    string Id,
    string Title,
    string MessageFormat,
    DiagnosticSeverity Severity,
    MemberSquiggleAt SquiggleAt,
    FixKind FixKind,
    string? CustomFixTag,
    Func<ISymbol, bool> Match,
    Func<INamedTypeSymbol, ISymbol, EquatableArray<string>> MessageArgs,
    Func<INamedTypeSymbol, ISymbol, ImmutableDictionary<string, string?>>? FixProperties = null);
