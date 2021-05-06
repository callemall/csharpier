using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpier.SyntaxPrinter;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;

namespace CSharpier
{
    public class CodeFormatter
    {
        public CSharpierResult Format(string code, PrinterOptions printerOptions)
        {
            return this.FormatAsync(code, printerOptions, CancellationToken.None).Result;
        }

        public async Task<CSharpierResult> FormatAsync(
            string code,
            PrinterOptions printerOptions,
            CancellationToken cancellationToken
        ) {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                code,
                new CSharpParseOptions(LanguageVersion.CSharp9, DocumentationMode.Diagnose),
                cancellationToken: cancellationToken
            );
            var syntaxNode = await syntaxTree.GetRootAsync(cancellationToken);
            if (syntaxNode is not CompilationUnitSyntax rootNode)
            {
                throw new Exception(
                    "Root was not CompilationUnitSyntax, it was " + syntaxNode.GetType()
                );
            }

            if (GeneratedCodeUtilities.BeginsWithAutoGeneratedComment(rootNode))
            {
                return new CSharpierResult { Code = code };
            }

            var diagnostics = syntaxTree.GetDiagnostics(cancellationToken)
                .Where(o => o.Severity == DiagnosticSeverity.Error && o.Id != "CS1029")
                .ToList();
            if (diagnostics.Any())
            {
                return new CSharpierResult
                {
                    Code = code,
                    Errors = diagnostics,
                    AST = printerOptions.IncludeAST ? this.PrintAST(rootNode) : string.Empty
                };
            }

            try
            {
                var document = Node.Print(rootNode);
                var lineEnding = GetLineEnding(code, printerOptions);
                var formattedCode = DocPrinter.DocPrinter.Print(
                    document,
                    printerOptions,
                    lineEnding
                );
                return new CSharpierResult
                {
                    Code = formattedCode,
                    DocTree = printerOptions.IncludeDocTree
                        ? DocSerializer.Serialize(document)
                        : string.Empty,
                    AST = printerOptions.IncludeAST ? this.PrintAST(rootNode) : string.Empty
                };
            }
            catch (InTooDeepException)
            {
                return new CSharpierResult
                {
                    FailureMessage = "We can't handle this deep of recursion yet."
                };
            }
        }

        public static string GetLineEnding(string code, PrinterOptions printerOptions)
        {
            if (printerOptions.EndOfLine == EndOfLine.Auto)
            {
                var lineIndex = code.IndexOf('\n');
                if (lineIndex <= 0)
                {
                    return "\n";
                }
                if (code[lineIndex - 1] == '\r')
                {
                    return "\r\n";
                }

                return "\n";
            }

            return printerOptions.EndOfLine == EndOfLine.CRLF ? "\r\n" : "\n";
        }

        private string PrintAST(CompilationUnitSyntax rootNode)
        {
            var stringBuilder = new StringBuilder();
            SyntaxNodeJsonWriter.WriteCompilationUnitSyntax(stringBuilder, rootNode);
            return JsonConvert.SerializeObject(
                JsonConvert.DeserializeObject(stringBuilder.ToString()),
                Formatting.Indented
            );
        }
    }

    public class CSharpierResult
    {
        public string Code { get; set; } = string.Empty;
        public string DocTree { get; set; } = string.Empty;
        public string AST { get; set; } = string.Empty;
        public IEnumerable<Diagnostic> Errors { get; set; } = Enumerable.Empty<Diagnostic>();

        public string FailureMessage { get; set; } = string.Empty;
    }
}
