using System.Runtime.Loader;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(Scribe.Tests.OutputDirectoryAssemblyResolver))]

namespace Scribe.Tests;

/// <summary>
///     Microsoft.Testing.Platform runs this test project as a self-hosted executable that
///     resolves runtime assemblies strictly from its <c>.deps.json</c>. Scribe is consumed
///     here via a <c>ProjectReference</c> to a Scribe.Sdk <c>Library</c> project, whose output
///     is intentionally kept out of consumers' runtime dependency graph — in production it
///     ships embedded as an analyzer, loaded by the Roslyn host, never as an application
///     runtime dependency. VSTest's <c>testhost</c> masked this by probing the output
///     directory; MTP does not, so a test that instantiates a generator touching Scribe throws
///     <see cref="System.IO.FileNotFoundException"/>.
///
///     Installed via xUnit's pipeline-startup hook rather than a <c>[ModuleInitializer]</c>:
///     the Scribe.Sdk injects polyfilled <c>ModuleInitializerAttribute</c> types into both
///     <c>Scribe</c> and <c>Scribe.Ink</c>, which collide here via <c>InternalsVisibleTo</c>.
///     The assemblies sit in the output directory regardless; this teaches the default load
///     context to probe there for anything missing from the deps.json graph.
/// </summary>
public sealed class OutputDirectoryAssemblyResolver : ITestPipelineStartup
{
    public ValueTask StartAsync(Xunit.Sdk.IMessageSink diagnosticMessageSink)
    {
        AssemblyLoadContext.Default.Resolving += static (context, assemblyName) =>
        {
            if (assemblyName.Name is not { } name)
            {
                return null;
            }

            var candidate = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            return File.Exists(candidate)
                ? context.LoadFromAssemblyPath(candidate)
                : null;
        };

        return default;
    }

    public ValueTask StopAsync() => default;
}
