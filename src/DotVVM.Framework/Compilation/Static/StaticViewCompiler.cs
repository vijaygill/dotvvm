#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotVVM.Framework.Compilation.ControlTree;
using DotVVM.Framework.Compilation.ControlTree.Resolved;
using DotVVM.Framework.Compilation.Parser.Dothtml.Parser;
using DotVVM.Framework.Compilation.Parser.Dothtml.Tokenizer;
using DotVVM.Framework.Compilation.Styles;
using DotVVM.Framework.Compilation.Validation;
using DotVVM.Framework.Configuration;
using DotVVM.Framework.Controls;
using DotVVM.Framework.Controls.Infrastructure;
using DotVVM.Framework.Hosting;
using DotVVM.Framework.Security;
using DotVVM.Framework.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotVVM.Framework.Compilation.Static
{
    internal class StaticViewCompiler
    {
        public const string ObjectsClassName = "SerializedObjects";
        private const string IDotvvmCacheAdapterName
            = "DotVVM.Framework.Runtime.Caching.IDotvvmCacheAdapter, DotVVM.Framework";
        private const string SimpleDictionaryCacheAdapterName
            = "DotVVM.Framework.Testing.SimpleDictionaryCacheAdapter, DotVVM.Framework";
        private readonly ImmutableArray<MetadataReference> baseReferences;

        private readonly DotvvmConfiguration configuration;
        private readonly Assembly dotvvmProjectAssembly;

        // NB: Currently, an Assembly must be built for each view/markup control (i.e. IControlBuilder) and then merged
        //     into one assembly. It's horrible, I know, but the compiler is riddled with references to System.Type
        //     that make it presently impossible to compile it all in one go.
        private readonly ConcurrentDictionary<string, StaticView> viewCache
            = new ConcurrentDictionary<string, StaticView>();

        public StaticViewCompiler(DotvvmConfiguration configuration, Assembly dotvvmProjectAssembly)
        {
            this.configuration = configuration;
            this.dotvvmProjectAssembly = dotvvmProjectAssembly;
            baseReferences = GetBaseReferences(configuration);
        }

        public static DotvvmConfiguration CreateConfiguration(
            Assembly dotvvmProjectAssembly,
            string dotvvmProjectDir)
        {
            return ConfigurationInitializer.GetConfiguration(dotvvmProjectAssembly, dotvvmProjectDir, services =>
            {
                services.AddSingleton<IControlResolver, StaticViewControlResolver>();
                services.TryAddSingleton<IViewModelProtector, FakeViewModelProtector>();
                services.AddSingleton(new RefObjectSerializer());
                // Yes, this is here so that there can be a circular dependency in StaticViewControlResolver.
                // I'm not happy about it, no, but the alternative is a more-or-less complete rewrite.
                services.AddSingleton(p => new StaticViewCompiler(
                    p.GetRequiredService<DotvvmConfiguration>(),
                    dotvvmProjectAssembly));

                // HACK: IDotvvmCacheAdapter is not in v2.0.0 that's why it's hacked this way.
                var iCacheAdapter = Type.GetType(IDotvvmCacheAdapterName);
                if (iCacheAdapter is object)
                {
                    services.AddSingleton(iCacheAdapter, Type.GetType(SimpleDictionaryCacheAdapterName));
                }

                // TODO: Uncomment when the views can actually be compiled into one assembly.
                // var bindingCompiler = new AssemblyBindingCompiler(
                //     assemblyName: null,
                //     className: null,
                //     outputFileName: null,
                //     configuration: null);
                // services.AddSingleton<IBindingCompiler>(bindingCompiler);
                // services.AddSingleton<IExpressionToDelegateCompiler>(bindingCompiler.GetExpressionToDelegateCompiler());
            });
        }

        public static ImmutableArray<CompilationReport> CompileAll(
            Assembly dotvvmProjectAssembly,
            string dotvvmProjectDir)
        {
            var configuration = CreateConfiguration(dotvvmProjectAssembly, dotvvmProjectDir);
            var reportsBuilder = ImmutableArray.CreateBuilder<CompilationReport>();

            var markupControls = configuration.Markup.Controls.Select(c => c.Src)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToImmutableArray();
            foreach (var markupControl in markupControls)
            {
                reportsBuilder.AddRange(CompileNoThrow(configuration, markupControl!));
            }

            var views = configuration.RouteTable.Select(r => r.VirtualPath).ToImmutableArray();
            foreach(var view in views)
            {
                reportsBuilder.AddRange(CompileNoThrow(configuration, view));
            }

            return reportsBuilder.Distinct().ToImmutableArray();
        }

        private static ImmutableArray<CompilationReport> CompileNoThrow(
            DotvvmConfiguration configuration,
            string viewPath)
        {
            var fileLoader = configuration.ServiceProvider.GetRequiredService<IMarkupFileLoader>();
            var file = fileLoader.GetMarkup(configuration, viewPath);
            if (file is null)
            {
                return ImmutableArray.Create<CompilationReport>();
            }

            var namespaceName = DefaultControlBuilderFactory.GetNamespaceFromFileName(
                file.FileName,
                file.LastWriteDateTimeUtc);
            var className = DefaultControlBuilderFactory.GetClassFromFileName(file.FileName) + "ControlBuilder";
            var fullClassName = namespaceName + "." + className;
            var sourceCode = file.ContentsReaderFactory();

            try
            {
                var compiler = configuration.ServiceProvider.GetRequiredService<IViewCompiler>();
                var (_, builderFactory) = compiler.CompileView(
                    sourceCode: sourceCode,
                    fileName: viewPath,
                    assemblyName: $"{fullClassName}.Compiled",
                    namespaceName: namespaceName,
                    className: className);
                _ = builderFactory();
                // TODO: Reporting compiler errors solely through exceptions is dumb. I have no way of getting all of
                //       the parser errors at once because they're reported through exceptions one at a time. We need
                //       to rewrite DefaultViewCompiler and its interface if the static linter/compiler is to be useful.
                return ImmutableArray.Create<CompilationReport>();
            }
            catch(DotvvmCompilationException e)
            {
                return ImmutableArray.Create(new CompilationReport(viewPath, e));
            }
        }

        public StaticView GetView(string viewPath)
        {
            if (viewCache.ContainsKey(viewPath))
            {
                return viewCache[viewPath];
            }

            var view = CompileView(viewPath);
            // NB: in the meantime, the view could have been compiled on another thread
            return viewCache.GetOrAdd(viewPath, view);
        }

        public IEnumerable<StaticView> CompileAllViews()
        {
            var markupControls = CompileViews(configuration.Markup.Controls.Select(c => c.Src));
            var views = CompileViews(configuration.RouteTable.Select(r => r.VirtualPath));
            return markupControls.Concat(views).ToImmutableArray();
        }

        private ImmutableArray<StaticView> CompileViews(IEnumerable<string?> paths)
        {
            return paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => GetView(p!))
                .ToImmutableArray();
        }

        private StaticView CompileView(string viewPath)
        {
            var fileLoader = configuration.ServiceProvider.GetRequiredService<IMarkupFileLoader>();
            var file = fileLoader.GetMarkup(configuration, viewPath);
            if (file is null)
            {
                throw new ArgumentException($"No view with path '{viewPath}' exists.");
            }

            // parse the document
            var tokenizer = new DothtmlTokenizer();
            tokenizer.Tokenize(file.ContentsReaderFactory());
            var parser = new DothtmlParser();
            var node = parser.Parse(tokenizer.Tokens);

            // analyze control types
            var controlTreeResolver = configuration.ServiceProvider.GetRequiredService<IControlTreeResolver>();
            var resolvedView = (ResolvedTreeRoot)controlTreeResolver.ResolveTree(node, viewPath);

            var view = new StaticView(viewPath);
            var reports = new List<CompilationReport>();

            try
            {
                var errorCheckingVisitor = new ErrorCheckingVisitor();
                resolvedView.Accept(errorCheckingVisitor);
            }
            catch (DotvvmCompilationException e)
            {
                reports.Add(new CompilationReport(viewPath, e));
                // the error is too severe for compilation to continue 
                return view.WithReports(reports);
            }

            foreach (var n in node.EnumerateNodes())
            {
                if (n.HasNodeErrors)
                {
                    var line = n.Tokens.FirstOrDefault()?.LineNumber ?? -1;
                    var column = n.Tokens.FirstOrDefault()?.ColumnNumber ?? -1;

                    reports.AddRange(n.NodeErrors.Select(e => new CompilationReport(viewPath, line, column, e)));
                    // these errors are once again too severe
                    return view.WithReports(reports);
                }
            }

            var contextSpaceVisitor = new DataContextPropertyAssigningVisitor();
            resolvedView.Accept(contextSpaceVisitor);

            var styleVisitor = new StylingVisitor(configuration);
            resolvedView.Accept(styleVisitor);

            var usageValidator = configuration.ServiceProvider.GetRequiredService<IControlUsageValidator>();
            var validationVisitor = new ControlUsageValidationVisitor(usageValidator);
            resolvedView.Accept(validationVisitor);
            foreach (var error in validationVisitor.Errors)
            {
                var line = error.Nodes.FirstOrDefault()?.Tokens?.FirstOrDefault()?.LineNumber ?? -1;
                var column = error.Nodes.FirstOrDefault()?.Tokens?.FirstOrDefault()?.ColumnNumber ?? -1;

                reports.Add(new CompilationReport(viewPath, line, column, error.ErrorMessage));
            }

            if (reports.Any())
            {
                return view.WithReports(reports);
            }

            // no dothtml compilation errors beyond this point

            // NOTE: Markup controls referenced in the view have already been compiled "thanks" to the circular
            //       dependency in StaticViewControlResolver.
            var namespaceName = DefaultControlBuilderFactory.GetNamespaceFromFileName(
                file.FileName,
                file.LastWriteDateTimeUtc);
            var className = DefaultControlBuilderFactory.GetClassFromFileName(file.FileName) + "ControlBuilder";
            string fullClassName = namespaceName + "." + className;
            var refObjectSerializer = configuration.ServiceProvider.GetRequiredService<RefObjectSerializer>();
            var emitter = new DefaultViewCompilerCodeEmitter();
            var assemblyCache = configuration.ServiceProvider.GetRequiredService<CompiledAssemblyCache>();
            var bindingCompiler = configuration.ServiceProvider.GetRequiredService<IBindingCompiler>();
            var compilingVisitor = new ViewCompilingVisitor(emitter, assemblyCache, bindingCompiler, className);
            resolvedView.Accept(compilingVisitor);

            // HACK: In 2.4.0, ReflectionUtils.FindType expects to find the controls in the current assembly,
            //       which is bollocks.
            if (resolvedView.Metadata.Type != typeof(DotvvmView))
            {
                emitter.ResultControlTypeSyntax = emitter.ParseTypeName(resolvedView.Metadata.Type);
            }

            if (resolvedView.Directives.ContainsKey("masterPage"))
            {
                // make sure that the masterpage chain is already compiled
                _ = GetView(resolvedView.Directives["masterPage"].Single().Value);
            }

            var syntaxTree = emitter.BuildTree(namespaceName, className, viewPath).Single();
            var references = emitter.UsedAssemblies.Select(a =>
                MetadataReference.CreateFromFile(a.Key.Location).WithAliases(new[] { a.Value, "global" }));

            var compilation = CSharpCompilation.Create(
                assemblyName: $"{fullClassName}.Compiled",
                syntaxTrees: new[] { syntaxTree },
                references: baseReferences.Concat(references),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var memoryStream = new MemoryStream();
            var result = compilation.Emit(memoryStream);
            if (!result.Success)
            {
                reports.Add(new CompilationReport(viewPath, -1, -1, "Compilation failed. This is likely a bug in the DotVVM compiler."));
                return view.WithReports(reports);
            }

            var assembly = Assembly.Load(memoryStream.ToArray());
            return view.WithAssembly(assembly)
                .WithViewType(resolvedView.Metadata.Type)
                .WithDataContextType(resolvedView.DataContextTypeStack.DataContextType);
        }

        private static ImmutableArray<MetadataReference> GetBaseReferences(DotvvmConfiguration configuration)
        {
            return CompiledAssemblyCache.BuildReferencedAssembliesCache(configuration)
                .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToImmutableArray();
        }
    }
}
