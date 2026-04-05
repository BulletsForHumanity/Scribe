// Polyfill stubs for modern C# language features on netstandard2.0.
//
// These types are recognised by the compiler by their well-known fully-qualified names.
// Marking them internal prevents conflicts with the real BCL types when the binary is
// loaded in a .NET 5+ host (which already carries the real definitions).
//
// Feature map:
//   C# 9   init setters / record types    → IsExternalInit
//   C# 9   [ModuleInitializer]            → ModuleInitializerAttribute
//   C# 9   [SkipLocalsInit]               → SkipLocalsInitAttribute
//   C# 10  CallerArgumentExpression       → CallerArgumentExpressionAttribute
//   C# 10  Interpolated string handlers   → InterpolatedStringHandlerAttribute
//                                           InterpolatedStringHandlerArgumentAttribute
//   C# 11  required members               → RequiredMemberAttribute
//                                           CompilerFeatureRequiredAttribute
//   C# 11  scoped ref parameters          → ScopedRefAttribute
//   Nullable flow analysis attributes     → System.Diagnostics.

// ── System.Runtime.CompilerServices ──────────────────────────────────────────

#if !NET5_0_OR_GREATER
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    /// <summary>
    ///     Marks the <c>init</c> accessor and is required for <c>record</c> types.
    ///     The compiler looks for this class by its fully-qualified name.
    /// </summary>
    internal static class IsExternalInit { }

    /// <summary>Enables the <c>required</c> modifier on members (C# 11).</summary>
    [AttributeUsage(
        AttributeTargets.Class
            | AttributeTargets.Struct
            | AttributeTargets.Field
            | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = false
    )]
    internal sealed class RequiredMemberAttribute : Attribute { }

    /// <summary>
    ///     Companion to <see cref="RequiredMemberAttribute" />. The compiler emits this on
    ///     every constructor of a type that has required members (C# 11).
    /// </summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }

        /// <summary>
        ///     When <see langword="true" />, a compiler that does not understand this feature
        ///     is permitted to ignore it. When <see langword="false" /> (the default), the
        ///     compiler must reject the construct.
        /// </summary>
        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }

    /// <summary>Enables <c>[ModuleInitializer]</c> on a static void method (C# 9).</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }

    /// <summary>
    ///     Suppresses zero-initialisation of locals in the decorated method or type (C# 9).
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Module
            | AttributeTargets.Class
            | AttributeTargets.Struct
            | AttributeTargets.Interface
            | AttributeTargets.Constructor
            | AttributeTargets.Method
            | AttributeTargets.Property
            | AttributeTargets.Event,
        Inherited = false
    )]
    internal sealed class SkipLocalsInitAttribute : Attribute { }

    /// <summary>
    ///     Captures the source-text of the argument passed to a decorated parameter (C# 10).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) =>
            ParameterName = parameterName;

        public string ParameterName { get; }
    }

    /// <summary>Marks a type as a custom interpolated string handler (C# 10).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class InterpolatedStringHandlerAttribute : Attribute { }

    /// <summary>
    ///     Specifies which arguments of a method call are passed to a custom interpolated
    ///     string handler (C# 10).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
    {
        public InterpolatedStringHandlerArgumentAttribute(string argument) =>
            Arguments = [argument];

        public InterpolatedStringHandlerArgumentAttribute(params string[] arguments) =>
            Arguments = arguments;

        public string[] Arguments { get; }
    }

    /// <summary>
    ///     Indicates that a parameter is <c>scoped</c> — its ref-safety scope does not
    ///     extend beyond the method boundary (C# 11).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class ScopedRefAttribute : Attribute { }
}

// ── System.Diagnostics.CodeAnalysis — nullable flow analysis ─────────────────

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    ///     Specifies that an output will not be <see langword="null" /> even if the
    ///     corresponding type allows it.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Field
            | AttributeTargets.Parameter
            | AttributeTargets.Property
            | AttributeTargets.ReturnValue,
        Inherited = false
    )]
    internal sealed class NotNullAttribute : Attribute { }

    /// <summary>
    ///     Specifies that an output may be <see langword="null" /> even if the
    ///     corresponding type does not allow it.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Field
            | AttributeTargets.Parameter
            | AttributeTargets.Property
            | AttributeTargets.ReturnValue,
        Inherited = false
    )]
    internal sealed class MaybeNullAttribute : Attribute { }

    /// <summary>Specifies that <see langword="null" /> is allowed as an input even if the type does not allow it.</summary>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property,
        Inherited = false
    )]
    internal sealed class AllowNullAttribute : Attribute { }

    /// <summary>Specifies that <see langword="null" /> is disallowed as an input even if the type allows it.</summary>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property,
        Inherited = false
    )]
    internal sealed class DisallowNullAttribute : Attribute { }

    /// <summary>
    ///     Specifies that when the method returns <see cref="ReturnValue" />, the
    ///     decorated parameter will not be <see langword="null" />.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }

    /// <summary>
    ///     Specifies that when the method returns <see cref="ReturnValue" />, the
    ///     decorated parameter or return value may be <see langword="null" />.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }

    /// <summary>
    ///     Specifies that the return value of the decorated member is non-null when the
    ///     named parameter is non-null.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class NotNullIfNotNullAttribute : Attribute
    {
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

        public string ParameterName { get; }
    }

    /// <summary>Applied to a method that will never return under any circumstance.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute { }

    /// <summary>
    ///     Specifies that the method will not return if the decorated boolean parameter
    ///     has the specified value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class DoesNotReturnIfAttribute : Attribute
    {
        public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;

        public bool ParameterValue { get; }
    }

    /// <summary>
    ///     Specifies that the method or property ensures that the listed members are not
    ///     <see langword="null" />.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = true
    )]
    internal sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member) => Members = [member];

        public MemberNotNullAttribute(params string[] members) => Members = members;

        public string[] Members { get; }
    }

    /// <summary>
    ///     Specifies that the method or property ensures that the listed members are not
    ///     <see langword="null" /> when returning the specified value.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = true
    )]
    internal sealed class MemberNotNullWhenAttribute : Attribute
    {
        public MemberNotNullWhenAttribute(bool returnValue, string member)
        {
            ReturnValue = returnValue;
            Members = [member];
        }

        public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
        {
            ReturnValue = returnValue;
            Members = members;
        }

        public bool ReturnValue { get; }
        public string[] Members { get; }
    }
}
#endif
