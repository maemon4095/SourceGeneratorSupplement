﻿using Microsoft.CodeAnalysis;
using SourceGeneratorSupplement.Internal;
namespace SourceGeneratorSupplement.Generator;

[Generator]
public class TypeSourceGenerator : IIncrementalGenerator
{
    static string TypeSourceAttributeName { get; } = "TypeSourceAttribute";
    static string TypeSourceAttribute { get; } = $"{nameof(SourceGeneratorSupplement)}.{TypeSourceAttributeName}";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(this.ProductInitialSource);
        this.RegisterGenerator(context);
    }

    void ProductInitialSource(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("MemberSourceAttribute", @$"
namespace {nameof(SourceGeneratorSupplement)}
{{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
    internal class {TypeSourceAttributeName} : global::System.Attribute
    {{
        public {TypeSourceAttributeName}(global::System.Type type, global::System.Int32 depthLimit = -1)
        {{
            this.Type = type;
            this.DepthLimit = depthLimit;
        }}

        public global::System.Type Type {{ get; }}
        public global::System.Int32 DepthLimit {{ get; }}
    }}
}}
");
    }

    void RegisterGenerator(IncrementalGeneratorInitializationContext context)
    {
        var attributeSymbolProvider = context.CompilationProvider.Select(
            (compilation, token) =>
            {
                token.ThrowIfCancellationRequested();
                return compilation.GetTypeByMetadataName(TypeSourceAttribute) ?? throw new NullReferenceException($"{TypeSourceAttributeName} was not found.");
            });

        var provider = context.SyntaxProvider.CreateAttribute(attributeSymbolProvider)
                .Select((pair, token) =>
                {
                    var (_, _, symbol, attributeSymbol, attributes) = pair;
                    var attributeData = attributes.First();
                    var args = attributeData.ConstructorArguments;
                    if (args.Length < 1) return default;
                    if (args[0].Value is not INamedTypeSymbol type) return default;
                    var depth = args.Select(a => a.Value).ElementAtOrDefault(1) as int? ?? 0;
                    return (Symbol: (symbol as IMethodSymbol)!, Type: type, Depth: Math.Max(depth - 1, -1));
                })
                .Select((pair, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return new Bundle(pair.Symbol, pair.Type, pair.Depth);
                });

        context.RegisterSourceOutput(provider, this.ProductSource);
    }

    void ProductSource(SourceProductionContext context, Bundle bundle)
    {
        try
        {
            var writer = new IndentedWriter("    ");
            using (writer.DeclarationScope(bundle.Method))
            {
                var refs = bundle.Type.DeclaringSyntaxReferences;
                writer["return @\""].End();
                var indentLevel = writer.IndentLevel;
                writer.IndentLevel = 0;
                foreach (var r in refs)
                {
                    using (writer.DeclarationScope(bundle.Type.ContainingSymbol, bundle.DepthLimit))
                    {
                        //remove trailing trivia because it does not affect indent level
                        var str = r.GetSyntax().WithoutTrailingTrivia().ToFullString();
                        var minindent = -1;

                        foreach (var line in str.EnumerateLines())
                        {
                            var indent = line.Length - line.TrimStart().Length;
                            //skip whitespace line
                            if (indent == line.Length) continue;
                            if ((uint)indent < (uint)minindent) minindent = indent;
                            if (minindent == 0) break;
                        }
                        if (minindent < 0) minindent = 0;

                        //remove lines before declaration
                        var leading = true;
                        foreach (var line in str.EnumerateLines())
                        {
                            if (leading && line.IsWhiteSpace()) continue;
                            leading = false;
                            if (line.Length <= minindent)
                            {
                                writer.Line();
                            }
                            else
                            {
                                writer[line.Slice(minindent)].Line();
                            }
                        }
                    }
                }
                writer["\";"].Line();
                writer.IndentLevel = indentLevel;
            }

            context.AddSource($"Source.{bundle.Method.ContainingType}.{bundle.Method.Name}.g.cs", writer.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception($"{ex.GetType()} was thrown. Message: {ex.Message.Replace("\r\n", "").Replace("\r", "").Replace("\n", "")} StackTrace: {ex.StackTrace.Replace(Environment.NewLine, " - ")}");
        }
    }

    readonly struct Bundle
    {
        public Bundle(IMethodSymbol method, ITypeSymbol type, int depthLimit)
        {
            this.Method = method;
            this.Type = type;
            this.DepthLimit = depthLimit;
        }

        public IMethodSymbol Method { get; }
        public ITypeSymbol Type { get; }
        public int DepthLimit { get; }
    }
}